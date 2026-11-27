using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

/// <summary>
/// Generates an export in the background: re-authorizes the author, verifies the dated selection captured at launch is still current before and
/// after reading, writes the workbook and CSV files to private storage, then records artifacts, reconciled counters and the audit in one save.
/// </summary>
public sealed class ExportJobHandler(IExportStore store, IExportText text, DiscoveryCatalog catalog, TimeProvider? clock = null) : ICollectionJobHandler
{
    public CollectionJobKind Kind => CollectionJobKind.Export;

    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        var job = execution.Job;
        var payload = JsonSerializer.Deserialize<ExportJobPayload>(job.PayloadJson, ContractJson.Options);
        if (payload is null || payload.ExportId != job.Id) { throw new ContractException("JobPayload", "job"); }
        var project = await execution.Access.RequireAsync(job.ProjectId, WorkspaceOperation.Export, cancellationToken);
        var ws = project.WorkspaceId;
        ExportJob export;
        ExportDocument document;
        await using (var db = await execution.Factory.CreateDbContextAsync(cancellationToken))
        {
            export = await db.ExportJobs.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == ws && x.Id == job.Id && x.ProjectId == project.Id,
                cancellationToken) ?? throw new AccessDeniedException();
            if (export.GeneratedAtUtc is not null) { return; }
            var filter = (JsonSerializer.Deserialize<ExportFilter>(export.FiltersJson, ContractJson.Options)
                ?? throw new ContractException("JobPayload", "export")).Normalize();
            await DemandCurrentAsync(db, project, filter, export, cancellationToken);
            await execution.ReportProgressAsync(1, cancellationToken);
            var generated = (clock ?? TimeProvider.System).GetUtcNow();
            document = await ExportReader.ReadAsync(db, project, filter, ExportSelection.Read(export.SelectionJson), export, text, generated, cancellationToken);
            await DemandCurrentAsync(db, project, filter, export, cancellationToken);
        }
        await execution.ReportProgressAsync(2, cancellationToken);

        var stamp = export.AsOfUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var prefix = $"clarity-{export.Side.ToString().ToLowerInvariant()}-{stamp}";
        var files = new (ExportArtifactKind Kind, string Extension, string ContentType, string FileName, int Rows, Action<Stream> Write)[]
        {
            (ExportArtifactKind.Workbook, ".xlsx", ExportWriter.WorkbookContentType, $"{prefix}-summary.xlsx", document.Inventory.Count,
                s => ExportWriter.WriteWorkbook(document, text, s)),
            (ExportArtifactKind.InventoryCsv, ".csv", ExportWriter.CsvContentType, $"{prefix}-inventory.csv", document.Inventory.Count,
                s => ExportWriter.WriteInventoryCsv(document, text, s)),
            (ExportArtifactKind.MappingsCsv, ".csv", ExportWriter.CsvContentType, $"{prefix}-mappings.csv", document.Mappings.Count,
                s => ExportWriter.WriteMappingsCsv(document, text, s))
        };
        var saved = new List<ExportArtifact>();
        try
        {
            foreach (var file in files)
            {
                using var buffer = new MemoryStream();
                file.Write(buffer);
                buffer.Position = 0;
                var stored = await store.SaveAsync(ws, export.Id, file.Extension, file.ContentType, buffer, cancellationToken);
                saved.Add(new ExportArtifact
                {
                    WorkspaceId = ws,
                    ExportJobId = export.Id,
                    Kind = file.Kind,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    BlobName = stored.BlobName,
                    ContentHash = stored.ContentHash,
                    Bytes = stored.Bytes,
                    RowCount = file.Rows
                });
            }
            await execution.ReportProgressAsync(3, cancellationToken);
            await execution.Access.RequireAsync(job.ProjectId, WorkspaceOperation.Export, cancellationToken);
            await using var db = await execution.Factory.CreateDbContextAsync(cancellationToken);
            var tracked = await db.ExportJobs.SingleAsync(x => x.WorkspaceId == ws && x.Id == export.Id, cancellationToken);
            if (tracked.GeneratedAtUtc is not null) { throw new ConflictException(); }
            tracked.GeneratedAtUtc = document.Header.GeneratedAtUtc;
            tracked.ObjectCount = document.Inventory.Count;
            tracked.ExceptionCount = document.Exceptions.Count;
            tracked.MappingCount = document.Mappings.Count;
            tracked.CoverageGaps = document.CoverageGaps;
            db.ExportArtifacts.AddRange(saved);
            db.AuditEvents.Add(new AuditEvent
            {
                WorkspaceId = ws,
                ResourceId = export.Id,
                Action = "ExportGenerated",
                ActorTenantId = job.ActorTenantId,
                ActorObjectId = job.ActorObjectId,
                CorrelationId = job.CorrelationId
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            foreach (var artifact in saved)
            {
                await store.DeleteAsync(ws, artifact.BlobName, CancellationToken.None);
            }
            throw;
        }
        await execution.ReportProgressAsync(4, cancellationToken);
    }

    private async Task DemandCurrentAsync(ClarityDbContext db, Project project, ExportFilter filter, ExportJob export, CancellationToken token)
    {
        var current = await ExportSelection.CaptureAsync(db, project, filter, catalog, token);
        if (ExportSelection.Hash(current.Serialize()) != export.SelectionHash)
        {
            throw new ContractException("SelectionSuperseded", "export");
        }
    }
}

internal static class ExportReader
{
    private static readonly TargetOutcome[] TargetExceptions = [TargetOutcome.Ambiguous, TargetOutcome.Absent, TargetOutcome.Unknown];

    /// <summary>The objects of the export side matching every recorded filter, as one composable query.</summary>
    internal static IQueryable<InventoryObject> Objects(ClarityDbContext db, Project project, ExportFilter filter)
    {
        var ws = project.WorkspaceId;
        var objects = InventoryQueries.Filter(db, project, new InventoryFilter(filter.Side, filter.EnvironmentId, Type: filter.Type, Status: filter.Status));
        var decisions = db.ScopeDecisions.Where(d => d.WorkspaceId == ws && d.ProjectId == project.Id);
        objects = filter.Decision switch
        {
            ExportDecisionFilter.All => objects,
            ExportDecisionFilter.Undecided => objects.Where(i => !decisions.Any(d => d.ObjectId == i.Id)),
            var value => objects.Where(i => decisions.Any(d => d.ObjectId == i.Id && d.Value == Value(value)))
        };
        if (filter.Wave is { } wave) { objects = objects.Where(i => decisions.Any(d => d.ObjectId == i.Id && d.Wave == wave)); }
        if (filter.PopulationId is { } population)
        {
            objects = objects.Where(i => db.PopulationMembers.Any(m => m.WorkspaceId == ws && m.PopulationId == population && m.ObjectId == i.Id));
        }
        return objects;
    }

    private static ScopeValue Value(ExportDecisionFilter filter) => filter switch
    {
        ExportDecisionFilter.Include => ScopeValue.Include,
        ExportDecisionFilter.Exclude => ScopeValue.Exclude,
        ExportDecisionFilter.ToArbitrate => ScopeValue.ToArbitrate,
        _ => throw new ArgumentOutOfRangeException(nameof(filter))
    };

    internal static async Task<ExportDocument> ReadAsync(ClarityDbContext db, Project project, ExportFilter filter, ExportSelection selection,
        ExportJob export, IExportText text, DateTimeOffset generatedAt, CancellationToken token)
    {
        var ws = project.WorkspaceId;
        var pid = project.Id;
        var objects = Objects(db, project, filter);
        if (await objects.CountAsync(token) > ExportRules.MaxRows) { throw new ContractException("ExportTooLarge", "export"); }
        var environments = await db.Environments.AsNoTracking().Where(e => e.WorkspaceId == ws && e.ProjectId == pid)
            .ToDictionaryAsync(e => e.Id, e => e.ExternalId, token);
        var decisions = db.ScopeDecisions.Where(d => d.WorkspaceId == ws && d.ProjectId == pid);
        var loaded = await (from i in objects
                            join decision in decisions on i.Id equals decision.ObjectId into joined
                            from d in joined.DefaultIfEmpty()
                            select new
                            {
                                i.Id,
                                i.EnvironmentId,
                                i.Type,
                                i.DisplayName,
                                i.UserPrincipalName,
                                i.Smtp,
                                i.Domain,
                                i.Site,
                                i.Enabled,
                                i.Presence,
                                i.LastObservedAtUtc,
                                Capability = db.SectionRuns.Where(s => s.WorkspaceId == ws && s.Id == i.PresenceSectionId).Select(s => s.CapabilityId).FirstOrDefault(),
                                SourceDate = (from s in db.SectionRuns
                                              join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                                              where s.WorkspaceId == ws && s.Id == i.PresenceSectionId
                                              select (DateTimeOffset?)r.CompletedAtUtc).FirstOrDefault(),
                                Decision = d == null ? (ScopeValue?)null : d.Value,
                                Wave = d == null ? null : d.Wave,
                                Population = d == null ? null
                                    : db.Populations.Where(p => p.WorkspaceId == ws && p.Id == d.PopulationId).Select(p => p.Name).FirstOrDefault(),
                                Reason = d == null ? null : db.ScopeDecisionRevisions.Where(r => r.WorkspaceId == ws && r.DecisionId == d.Id && r.Number == d.Revision)
                                    .Select(r => r.Reason).FirstOrDefault(),
                                DecidedAt = d == null ? (DateTimeOffset?)null : d.UpdatedAtUtc
                            }).ToListAsync(token);
        var flags = await db.ReviewFlags.AsNoTracking().Where(f => f.WorkspaceId == ws && f.ProjectId == pid && objects.Any(i => i.Id == f.ObjectId))
            .Select(f => new { f.ObjectId, f.Kind, f.FlaggedAtUtc }).ToListAsync(token);
        var conflicts = await db.CurrentValues.AsNoTracking().Where(v => v.WorkspaceId == ws && v.Conflicting && objects.Any(i => i.Id == v.ObjectId))
            .Select(v => new { v.ObjectId, v.Field }).ToListAsync(token);
        var mappings = await db.MappingDecisions.AsNoTracking()
            .Where(m => m.WorkspaceId == ws && m.ProjectId == pid && (objects.Any(i => i.Id == m.AdObjectId) || objects.Any(i => i.Id == m.EntraObjectId)))
            .Select(m => new
            {
                m.Id,
                m.Kind,
                m.State,
                m.Rule,
                m.AdEnvironmentId,
                m.AdObjectId,
                m.EntraEnvironmentId,
                m.EntraObjectId,
                m.Revision,
                m.ConsultantQualified,
                Left = db.InventoryObjects.Where(o => o.WorkspaceId == ws && o.Id == m.AdObjectId)
                    .Select(o => o.DisplayName ?? o.UserPrincipalName ?? o.Smtp).FirstOrDefault(),
                Right = db.InventoryObjects.Where(o => o.WorkspaceId == ws && o.Id == m.EntraObjectId)
                    .Select(o => o.DisplayName ?? o.UserPrincipalName ?? o.Smtp).FirstOrDefault(),
                Reason = db.MappingRevisions.Where(r => r.WorkspaceId == ws && r.DecisionId == m.Id).OrderByDescending(r => r.Number)
                    .Select(r => r.Reason).FirstOrDefault(),
                Automatic = db.MappingRevisions.Where(r => r.WorkspaceId == ws && r.DecisionId == m.Id).OrderByDescending(r => r.Number)
                    .Select(r => r.ActorObjectId == null).FirstOrDefault(),
                Date = db.MappingRevisions.Where(r => r.WorkspaceId == ws && r.DecisionId == m.Id).OrderByDescending(r => r.Number)
                    .Select(r => (DateTimeOffset?)r.OccurredAtUtc).FirstOrDefault()
            }).ToListAsync(token);
        var comparisons = db.TargetComparisons.Where(c => c.WorkspaceId == ws && c.ProjectId == pid);
        var targets = await db.TargetComparisonItems.AsNoTracking()
            .Where(t => t.WorkspaceId == ws && comparisons.Any(c => c.Id == t.ComparisonId)
                && (t.SourceObjectId != null && objects.Any(i => i.Id == t.SourceObjectId) || t.TargetObjectId != null && objects.Any(i => i.Id == t.TargetObjectId))
                && (t.Collision != null || t.Outcome != null && TargetExceptions.Contains(t.Outcome.Value)))
            .Select(t => new
            {
                t.Id,
                t.SourceObjectId,
                t.TargetObjectId,
                t.Outcome,
                t.Collision,
                t.Key,
                t.Reason,
                t.OtherLabel,
                Date = comparisons.Where(c => c.Id == t.ComparisonId).Select(c => (DateTimeOffset?)c.ComputedAtUtc).FirstOrDefault()
            }).ToListAsync(token);
        var shared = await (from r in db.DependencyResources.AsNoTracking()
                            join a in db.DependencyAnalyses on new { r.WorkspaceId, Id = r.AnalysisId } equals new { a.WorkspaceId, a.Id }
                            where r.WorkspaceId == ws && a.ProjectId == pid && r.Classification != DependencyClassification.MigratingOnly
                                && objects.Any(i => i.Id == r.ObjectId)
                            select new { r.ObjectId, r.Family, r.Classification, a.Wave, a.ComputedAtUtc, r.MigratingCount, r.RemainingCount }).ToListAsync(token);

        var flagged = flags.Select(f => f.ObjectId).ToHashSet();
        var conflicting = conflicts.Select(c => c.ObjectId).ToHashSet();
        var inventory = loaded.Select(i => new ExportInventoryRow(i.Id, environments.GetValueOrDefault(i.EnvironmentId, ""), i.Type, i.DisplayName,
                i.UserPrincipalName, i.Smtp, i.Domain, i.Site, i.Enabled, i.Presence, i.LastObservedAtUtc, i.Capability, i.SourceDate, i.Decision, i.Wave,
                i.Population, i.Reason, i.DecidedAt, flagged.Contains(i.Id), conflicting.Contains(i.Id)))
            .OrderBy(r => r.Environment, StringComparer.Ordinal).ThenBy(r => r.Type, StringComparer.Ordinal)
            .ThenBy(r => r.DisplayName ?? r.Upn ?? "", StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
        var byId = inventory.ToDictionary(r => r.Id);
        // Rows published between the reads above are not in the inventory; they are skipped here and the post-read selection check fails the export.
        flags.RemoveAll(f => !byId.ContainsKey(f.ObjectId));
        conflicts.RemoveAll(c => !byId.ContainsKey(c.ObjectId));
        mappings.RemoveAll(m => !byId.ContainsKey(m.AdObjectId) && !byId.ContainsKey(m.EntraObjectId));
        targets.RemoveAll(t => !(t.SourceObjectId is { } s && byId.ContainsKey(s)) && !(t.TargetObjectId is { } o && byId.ContainsKey(o)));
        shared.RemoveAll(r => !byId.ContainsKey(r.ObjectId));

        var exceptions = new List<ExportExceptionRow>();
        ExportExceptionRow For(ExportExceptionCategory category, Guid id, string detail, DateTimeOffset? date)
        {
            var row = byId[id];
            return new(category, row.Environment, row.Type, row.DisplayName ?? row.Upn ?? row.Smtp, id, detail, date);
        }
        Guid Selected(Guid? first, Guid? second) => first is { } id && byId.ContainsKey(id) ? id : second!.Value;

        exceptions.AddRange(selection.Units.Where(u => u.Status is CoverageStatus.Partial or CoverageStatus.Failed)
            .Select(u => new ExportExceptionRow(ExportExceptionCategory.CoverageGap, u.Environment, null, null, null,
                $"{u.Capability} — {text[u.Status.ToString()]}", u.Date)));
        exceptions.AddRange(selection.Uncollected.Select(g => new ExportExceptionRow(ExportExceptionCategory.CoverageGap, g.Environment, null, null, null,
            $"{g.Capability} — {text["Export_NotCollected"]}", null)));
        exceptions.AddRange(conflicts.GroupBy(c => c.ObjectId).Select(g => For(ExportExceptionCategory.ConflictingValue, g.Key,
            string.Join(", ", g.Select(c => c.Field).Distinct().Order(StringComparer.Ordinal)), null)));
        exceptions.AddRange(flags.Select(f => For(ExportExceptionCategory.ReviewFlag, f.ObjectId, text["DecisionKind_" + f.Kind], f.FlaggedAtUtc)));
        exceptions.AddRange(inventory.Where(r => r.Decision == ScopeValue.ToArbitrate)
            .Select(r => For(ExportExceptionCategory.ToArbitrate, r.Id, Join(r.Wave, r.Reason), r.DecidedAtUtc)));
        exceptions.AddRange(inventory.Where(r => r.Decision == ScopeValue.Include && r.Presence != InventoryPresence.Observed)
            .Select(r => For(ExportExceptionCategory.IncludedNotObserved, r.Id, text[r.Presence.ToString()], r.LastObservedAtUtc)));
        exceptions.AddRange(mappings.Where(m => m.State is MappingState.Proposed or MappingState.ToReview)
            .Select(m => For(ExportExceptionCategory.MappingPending, Selected(m.AdObjectId, m.EntraObjectId),
                Join(text["Export_MappingKind_" + m.Kind], text[m.State.ToString()], text[m.Rule.ToString()]), m.Date)));
        exceptions.AddRange(targets.Where(t => t.Outcome is not null && TargetExceptions.Contains(t.Outcome.Value)
                && t.SourceObjectId is { } s && byId.ContainsKey(s))
            .Select(t => For(ExportExceptionCategory.TargetComparison, t.SourceObjectId!.Value,
                Join(text["TargetOutcome_" + t.Outcome], text["TargetReason_" + t.Reason]), t.Date)));
        exceptions.AddRange(targets.Where(t => t.Collision is not null)
            .Select(t => For(ExportExceptionCategory.Collision, Selected(t.SourceObjectId, t.TargetObjectId),
                Join(text["CollisionKind_" + t.Collision], t.Key, t.OtherLabel), t.Date)));
        exceptions.AddRange(shared.Select(r => For(ExportExceptionCategory.SharedDependency, r.ObjectId,
            Join(text["DependencyClassification_" + r.Classification], text["DependencyFamily_" + r.Family], r.Wave ?? text["NoWave"]), r.ComputedAtUtc)));
        var orderedExceptions = exceptions.OrderBy(e => e.Category).ThenBy(e => e.Environment, StringComparer.Ordinal)
            .ThenBy(e => e.Label ?? "", StringComparer.Ordinal).ThenBy(e => e.ObjectId).ThenBy(e => e.Detail, StringComparer.Ordinal).ToList();

        var mappingRows = mappings.Select(m => new ExportMappingRow(m.Id, m.Kind, m.State, m.Rule, environments.GetValueOrDefault(m.AdEnvironmentId, ""),
                m.AdObjectId, m.Left, environments.GetValueOrDefault(m.EntraEnvironmentId, ""), m.EntraObjectId, m.Right, m.Revision, m.ConsultantQualified,
                m.Reason is null ? null : m.Automatic ? text[m.Reason] : m.Reason, m.Date))
            .OrderBy(m => m.Kind).ThenBy(m => m.LeftEnvironment, StringComparer.Ordinal).ThenBy(m => m.LeftLabel ?? "", StringComparer.Ordinal)
            .ThenBy(m => m.Id).ToList();

        var all = text["All"];
        var population = filter.PopulationId is { } populationId
            ? await db.Populations.Where(p => p.WorkspaceId == ws && p.Id == populationId).Select(p => p.Name).SingleAsync(token)
            : all;
        IReadOnlyList<(string, string)> filters =
        [
            (text["Export_Col_Environment"], filter.EnvironmentId is { } environment ? environments.GetValueOrDefault(environment, "") : all),
            (text["Export_Col_ObjectType"], filter.Type is { } type ? (text["ObjectType_" + type] is var label && label != "ObjectType_" + type ? label : type) : all),
            (text["Status"], filter.Status is { } status ? text[status] : all),
            (text["Export_Col_Decision"], filter.Decision switch
            {
                ExportDecisionFilter.All => all,
                ExportDecisionFilter.Undecided => text["Export_Undecided"],
                var value => text["ScopeDecision_" + Value(value)]
            }),
            (text["Export_Col_Wave"], filter.Wave ?? all),
            (text["Export_Col_Population"], population)
        ];
        var header = new ExportHeader(project.Name, project.Scenario, export.Side, filters, $"{export.AuthorTenantId}/{export.AuthorObjectId}",
            export.AsOfUtc, generatedAt, export.SelectionHash);
        return new(header, selection.Units, selection.Uncollected, inventory, orderedExceptions, mappingRows);
    }

    private static string Join(params string?[] parts) => string.Join(" — ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
