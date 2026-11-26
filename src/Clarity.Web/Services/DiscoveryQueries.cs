using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Clarity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Web.Services;

public sealed record RunView(Guid Id, string ProducerId, string ProducerVersion, string ScopeJson, DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc, CollectionRunState State, DateTimeOffset? ConfirmedAtUtc, string ContentHash, Guid ExternalRunId);
public sealed record RequestView(Guid Id, string ScopeJson, string ScriptVersion, CollectionRequestState State, DateTimeOffset CreatedAtUtc);
public sealed record JobView(Guid Id, CollectionJobKind Kind, CollectionRunState State, int Attempt, int Progress,
    bool CancellationRequested, string? ErrorCode, string CorrelationId, DateTimeOffset CreatedAtUtc);
public sealed record CoverageView(Guid RunId, string CapabilityId, string ScopeJson, CoverageStatus? Coverage,
    long? ObjectCount, long? RelationshipCount, DateTimeOffset? LastValidAtUtc, Guid? LastValidRunId, Guid SectionRunId);
public sealed record ConnectionView(Guid Id, string Name, Guid TenantId, DateTimeOffset? TestedAtUtc, IReadOnlyList<SectionAccess> Checks);
public sealed record PreviewView(RunView Run, IReadOnlyList<SectionResult> Sections, IReadOnlyList<ObjectRecord> Objects,
    IReadOnlyList<RelationshipRecord> Relationships, int NewObjects, int ExistingObjects, ReportDiagnostics? Report = null);

public sealed class DiscoveryQueries(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, DiscoveryCatalog catalog)
{
    public async Task<PageResult<RunView>> RunsAsync(Guid projectId, Guid environmentId, PageRequest page, CancellationToken ct = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = from run in db.CollectionRuns.AsNoTracking()
                    where run.WorkspaceId == project.WorkspaceId && run.ProjectId == projectId && run.EnvironmentId == environmentId
                        && run.ProducerId.Contains(page.Search)
                    join job in db.CollectionJobs on new { run.WorkspaceId, run.Id } equals new { job.WorkspaceId, job.Id } into jobs
                    from job in jobs.DefaultIfEmpty()
                    select new RunView(run.Id, run.ProducerId, run.ProducerVersion, run.ScopeJson, run.StartedAtUtc, run.CompletedAtUtc,
                        job == null ? run.State : job.State, run.ConfirmedAtUtc, run.ContentHash, run.ExternalRunId);
        var count = await query.CountAsync(ct);
        var ordered = page.Descending ? query.OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id) : query.OrderBy(x => x.StartedAtUtc).ThenBy(x => x.Id);
        return new(await ordered.Skip(page.Start).Take(page.Count).ToListAsync(ct), count);
    }

    public async Task<PageResult<RequestView>> RequestsAsync(Guid projectId, Guid environmentId, PageRequest page, CancellationToken ct = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.CollectionRequests.AsNoTracking().Where(x => x.WorkspaceId == project.WorkspaceId
            && x.ProjectId == projectId && x.EnvironmentId == environmentId && x.ScopeJson.Contains(page.Search))
            .Select(x => new RequestView(x.Id, x.ScopeJson, x.ScriptVersion, x.State, x.CreatedAtUtc));
        var count = await query.CountAsync(ct);
        var ordered = page.Descending ? query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id) : query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id);
        return new(await ordered.Skip(page.Start).Take(page.Count).ToListAsync(ct), count);
    }

    public async Task<PageResult<JobView>> JobsAsync(Guid projectId, PageRequest page, CancellationToken ct = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.CollectionJobs.AsNoTracking().Where(x => x.WorkspaceId == project.WorkspaceId && x.ProjectId == projectId)
            .Select(x => new JobView(x.Id, x.Kind, x.State, x.Attempt, x.Progress, x.CancellationRequested, x.ErrorCode, x.CorrelationId, x.CreatedAtUtc));
        var count = await query.CountAsync(ct);
        var ordered = page.Descending ? query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id) : query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id);
        return new(await ordered.Skip(page.Start).Take(page.Count).ToListAsync(ct), count);
    }

    public async Task<PageResult<CoverageView>> CoverageAsync(Guid projectId, Guid environmentId, PageRequest page, CancellationToken ct = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var sections = from section in db.SectionRuns.AsNoTracking()
                       join run in db.CollectionRuns on new { section.WorkspaceId, Id = section.RunId } equals new { run.WorkspaceId, run.Id }
                       where run.WorkspaceId == project.WorkspaceId && run.ProjectId == projectId && run.EnvironmentId == environmentId
                       select new { Section = section, Run = run };
        var units = sections.Select(x => new { x.Section.CapabilityId, x.Section.ScopeHash }).Distinct()
            .Where(x => x.CapabilityId.Contains(page.Search));
        var count = await units.CountAsync(ct);
        var ordered = page.Descending ? units.OrderByDescending(x => x.CapabilityId).ThenBy(x => x.ScopeHash)
            : units.OrderBy(x => x.CapabilityId).ThenBy(x => x.ScopeHash);
        var keys = await ordered.Skip(page.Start).Take(page.Count).ToListAsync(ct);
        var rows = new List<CoverageView>();
        foreach (var key in keys)
        {
            var unit = sections.Where(x => x.Section.CapabilityId == key.CapabilityId && x.Section.ScopeHash == key.ScopeHash);
            var latest = await unit.OrderByDescending(x => x.Run.StartedAtUtc).ThenByDescending(x => x.Run.UploadedAtUtc).FirstAsync(ct);
            var valid = await unit.Where(x => x.Section.PublishedAtUtc != null
                && (x.Section.Coverage == CoverageStatus.Complete || x.Section.Coverage == CoverageStatus.Partial))
                .OrderByDescending(x => x.Run.CompletedAtUtc).Select(x => new { x.Run.CompletedAtUtc, x.Run.Id }).FirstOrDefaultAsync(ct);
            var published = latest.Section.PublishedAtUtc is not null;
            rows.Add(new(latest.Run.Id, key.CapabilityId, latest.Section.ScopeJson, published ? latest.Section.Coverage : null,
                published ? latest.Section.ObjectCount : null, published ? latest.Section.RelationshipCount : null, valid?.CompletedAtUtc, valid?.Id, latest.Section.Id));
        }
        return new(rows, count);
    }

    public async Task<IReadOnlyList<string>> UncollectedAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var environment = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.ProjectId == projectId && x.Id == environmentId, ct) ?? throw new AccessDeniedException();
        var collected = await (from section in db.SectionRuns
                               join run in db.CollectionRuns on new { section.WorkspaceId, Id = section.RunId } equals new { run.WorkspaceId, run.Id }
                               where run.ProjectId == projectId && run.WorkspaceId == project.WorkspaceId && run.EnvironmentId == environmentId
                               select section.CapabilityId).Distinct().ToListAsync(ct);
        return catalog.Families.Where(x => x.EnvironmentKind == environment.Kind.ToString())
            .SelectMany(x => x.Sections).Where(x => x.ProducerIds.Any(id => catalog.Producers[id].Readiness == "SyntheticValidated"))
            .Select(x => x.Id).Except(collected, StringComparer.Ordinal).ToArray();
    }

    public async Task<ConnectionView?> ConnectionAsync(Guid projectId, Guid environmentId, CancellationToken ct = default,
        string producerId = GraphDiscoveryService.EntraProducerId)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var environment = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.ProjectId == projectId && x.Id == environmentId, ct) ?? throw new AccessDeniedException();
        if (environment.Kind != EnvironmentKind.M365Tenant || !Guid.TryParse(environment.ExternalId, out var tenant)) { return null; }
        var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.TenantId == tenant && x.ProducerId == producerId, ct);
        return connection is null ? null : new(connection.Id, connection.Name, tenant, connection.TestedAtUtc,
            JsonSerializer.Deserialize<SectionAccess[]>(connection.AccessResultsJson, ContractJson.Options)!);
    }

    public static PreviewView Present(ImportPreview preview) => new(new(preview.Run.Id, preview.Run.ProducerId, preview.Run.ProducerVersion,
        preview.Run.ScopeJson, preview.Run.StartedAtUtc, preview.Run.CompletedAtUtc, preview.Run.State, preview.Run.ConfirmedAtUtc,
        preview.Run.ContentHash, preview.Run.ExternalRunId), preview.Sections, preview.Objects, preview.Relationships, preview.NewObjects, preview.ExistingObjects,
        preview.Report);
}
