using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public enum ProofKind { Section, Observation, Relationship, Mapping }
public sealed record ProofRef(ProofKind Kind, Guid Id);
public sealed record SourceProof(Guid SectionId, string Capability, string Channel, string Producer, string Version, string Source,
    IReadOnlyDictionary<string, string> Modules, string Environment, EnvironmentRole Side, DateTimeOffset Observed, CollectionScope Scope,
    CoverageStatus Result, IReadOnlyList<string> Fields, IReadOnlyList<string> Relations, string? FileName, DateTimeOffset? Uploaded, UserIdentity? Uploader);
public sealed record DecisionProof(long Revision, MappingState State, AssociationRule Rule, DateTimeOffset Date, string Reason, UserIdentity? Author);
public sealed record ProofDetail(IReadOnlyList<SourceProof> Sources, IReadOnlyList<DecisionProof> Decisions,
    string? Relation, EndpointResolution? From, EndpointResolution? To);

public sealed class InventoryProof(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access)
{
    public async Task<ProofDetail> ReadAsync(Guid projectId, ProofRef target, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var runs = db.CollectionRuns.Where(r => r.ProjectId == project.Id && r.WorkspaceId == project.WorkspaceId);
        var sections = from s in db.SectionRuns
                       join r in runs on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                       where s.PublishedAtUtc != null
                       select new
                       {
                           s.Id,
                           s.WorkspaceId,
                           s.CapabilityId,
                           s.ResultJson,
                           r.EnvironmentId,
                           r.ManifestJson,
                           r.ConnectionId,
                           r.Channel,
                           r.UploadedAtUtc,
                           r.ActorObjectId,
                           r.ActorTenantId,
                           RunId = r.Id,
                           r.CompletedAtUtc
                       };
        var objects = from o in db.ObjectObservations
                      join s in sections on new { o.WorkspaceId, Id = o.SectionRunId } equals new { s.WorkspaceId, s.Id }
                      select new { o.Id, o.SectionRunId, o.ObservedAtUtc };
        var dates = new List<(Guid Section, DateTimeOffset Date)>();
        var decisions = new List<DecisionProof>();
        InventoryRelationship? edge = null;
        switch (target.Kind)
        {
            case ProofKind.Section:
                var section = await sections.Where(s => s.Id == target.Id).Select(s => new { s.Id, s.CompletedAtUtc }).SingleOrDefaultAsync(token)
                    ?? throw new AccessDeniedException();
                dates.Add((section.Id, section.CompletedAtUtc));
                break;
            case ProofKind.Observation:
                var value = await objects.SingleOrDefaultAsync(o => o.Id == target.Id, token) ?? throw new AccessDeniedException();
                dates.Add((value.SectionRunId, value.ObservedAtUtc));
                break;
            case ProofKind.Relationship:
                edge = await db.InventoryRelationships.AsNoTracking().SingleOrDefaultAsync(r => r.WorkspaceId == project.WorkspaceId && r.Id == target.Id
                    && db.Environments.Any(e => e.WorkspaceId == r.WorkspaceId && e.ProjectId == projectId && e.Id == r.EnvironmentId), token)
                    ?? throw new AccessDeniedException();
                var observations = await (from p in db.RelationshipSupports
                                          join o in db.RelationshipObservations on new { p.WorkspaceId, Id = p.ObservationId } equals new { o.WorkspaceId, o.Id }
                                          join s in sections on new { o.WorkspaceId, Id = o.SectionRunId } equals new { s.WorkspaceId, s.Id }
                                          where p.WorkspaceId == project.WorkspaceId && p.RelationshipId == target.Id
                                          select new { o.SectionRunId, o.ObservedAtUtc }).ToListAsync(token);
                dates.AddRange(observations.Select(o => (o.SectionRunId, o.ObservedAtUtc)));
                break;
            case ProofKind.Mapping:
                var revisions = await (from h in db.MappingRevisions.AsNoTracking()
                                       join m in db.MappingDecisions on new { h.WorkspaceId, Id = h.DecisionId } equals new { m.WorkspaceId, m.Id }
                                       where m.ProjectId == projectId && m.WorkspaceId == project.WorkspaceId && m.Id == target.Id
                                       orderby h.Number descending
                                       select h).ToListAsync(token);
                if (revisions.Count == 0) { throw new AccessDeniedException(); }
                var ids = new HashSet<Guid>();
                foreach (var revision in revisions)
                {
                    decisions.Add(new(revision.Number, revision.State, revision.Rule, revision.OccurredAtUtc, revision.Reason,
                        revision.ActorObjectId.HasValue ? new UserIdentity(revision.ActorTenantId!.Value, revision.ActorObjectId.Value) : null));
                    using var evidence = JsonDocument.Parse(revision.EvidenceJson);
                    ids.UnionWith(evidence.RootElement.GetProperty("observations").EnumerateArray().Select(o => o.GetGuid()));
                }
                dates.AddRange((await objects.Where(o => ids.Contains(o.Id)).ToListAsync(token)).Select(o => (o.SectionRunId, o.ObservedAtUtc)));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(target));
        }
        var sectionIds = dates.Select(d => d.Section).Distinct().ToArray();
        var metadata = await (from s in sections
                              join e in db.Environments on new { s.WorkspaceId, Id = s.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                              join a in db.RawArtifacts on new { s.WorkspaceId, s.RunId } equals new { a.WorkspaceId, a.RunId } into files
                              from file in files.DefaultIfEmpty()
                              where sectionIds.Contains(s.Id) && e.ProjectId == projectId
                              select new { Section = s, e.Role, File = file == null ? null : file.FileName }).ToDictionaryAsync(s => s.Section.Id, token);
        var sources = dates.Distinct().OrderByDescending(d => d.Date).ThenBy(d => d.Section).Select(d =>
        {
            var data = metadata[d.Section];
            var s = data.Section;
            var manifest = JsonSerializer.Deserialize<PackageManifest>(s.ManifestJson, ContractJson.Options)!;
            var result = JsonSerializer.Deserialize<SectionResult>(s.ResultJson, ContractJson.Options)!;
            var import = s.ConnectionId is null;
            // A profiled report upload is labelled as such and is never presented as Microsoft Graph connection evidence.
            var channel = !import ? "Microsoft Graph" : s.Channel == CollectionChannel.Report ? "ReportImport" : "ZipImport";
            return new SourceProof(s.Id, result.CapabilityId, channel, manifest.ProducerId, manifest.ProducerVersion,
                manifest.SourceId, manifest.ModuleVersions, manifest.EnvironmentExternalId, data.Role, d.Date, result.Scope, result.Status,
                result.CoveredFields, result.CoveredRelations, import ? data.File : null, import ? s.UploadedAtUtc : null,
                import ? new UserIdentity(s.ActorTenantId, s.ActorObjectId) : null);
        }).ToList();
        return new(sources, decisions, edge?.RelationType, edge?.FromMethod, edge?.ToMethod);
    }
}
