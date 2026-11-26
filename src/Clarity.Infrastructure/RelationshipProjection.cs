using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

internal static class RelationshipProjection
{
    private sealed record Endpoint(Guid? Id, EndpointResolution Method, string Reference)
    {
        public string Identity => Id?.ToString("N") ?? Reference;
    }
    private sealed record Claim(RelationshipObservation Row, ProjectedSection Source, Endpoint From, Endpoint To);

    internal static async Task RefreshAsync(ClarityDbContext db, DiscoveryCatalog catalog, Guid workspace, Guid environment,
        CancellationToken token)
    {
        var observations = await (from o in db.RelationshipObservations.AsNoTracking()
                                  join s in db.SectionRuns on new { o.WorkspaceId, Id = o.SectionRunId } equals new { s.WorkspaceId, s.Id }
                                  join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                                  where o.WorkspaceId == workspace && r.EnvironmentId == environment && s.PublishedAtUtc != null
                                  select o).ToListAsync(token);
        if (observations.Count == 0) { return; }
        var keys = (await db.ExternalKeys.AsNoTracking().Where(x => x.WorkspaceId == workspace && x.EnvironmentId == environment && x.IsPrimary)
            .ToListAsync(token)).ToLookup(x => (x.Namespace, x.ExternalId));
        var aliases = (await db.AlternateKeyPeriods.AsNoTracking().Where(x => x.WorkspaceId == workspace && x.EnvironmentId == environment)
            .ToListAsync(token)).ToLookup(x => (x.Namespace, AlternateKeyProjection.Normalize(x.Namespace, x.OriginalValue)));
        var sections = (await ProjectedSection.LoadAsync(db, workspace, environment, token)).ToDictionary(x => x.Section.Id);
        var exhaustive = sections.Values.Where(x => x.IsExhaustive(catalog)).ToLookup(x => x.Stream);
        var edges = db.InventoryRelationships.Where(x => x.WorkspaceId == workspace && x.EnvironmentId == environment);
        await db.RelationshipSupports.Where(x => x.WorkspaceId == workspace && edges.Any(e => e.Id == x.RelationshipId))
            .ExecuteDeleteAsync(token);
        await edges.ExecuteDeleteAsync(token);

        var claims = observations.Select(o => new Claim(o, sections[o.SectionRunId], Resolve(o.FromJson, o.ObservedAtUtc),
            Resolve(o.ToJson, o.ObservedAtUtc)));
        foreach (var group in claims.GroupBy(c => (c.Row.RelationType, c.From.Identity, c.To.Identity)))
        {
            var sorted = group.OrderByDescending(c => c.Row.ObservedAtUtc).ThenByDescending(c => c.Source.ExternalRunId)
                .ThenBy(c => c.Source.Section.SectionId, StringComparer.Ordinal).ThenBy(c => c.Row.IdentityHash, StringComparer.Ordinal).ToList();
            var latest = sorted[0];
            var current = sorted.GroupBy(c => c.Source.Stream).Any(stream =>
            {
                var last = stream.First();
                using var properties = JsonDocument.Parse(last.Row.PropertiesJson);
                return !(properties.RootElement.TryGetProperty("deleted", out var removed) && removed.ValueKind == JsonValueKind.True)
                    && !(last.Source.IsExhaustive(catalog) && exhaustive[last.Source.Stream].Any(s => s.Started > last.Source.Completed));
            });
            var edge = new InventoryRelationship
            {
                Id = sorted[^1].Row.Id,
                WorkspaceId = workspace,
                EnvironmentId = environment,
                IsCurrent = current,
                FromObjectId = latest.From.Id,
                ToObjectId = latest.To.Id,
                FromMethod = latest.From.Method,
                ToMethod = latest.To.Method,
                FromJson = latest.Row.FromJson,
                ToJson = latest.Row.ToJson,
                RelationType = latest.Row.RelationType,
                Origin = latest.Row.RelationType switch
                {
                    "memberOf" or "primaryMemberOf" => RelationshipOrigin.Membership,
                    "delegatedTo" or "delegation" => RelationshipOrigin.ObservedDelegation,
                    _ => RelationshipOrigin.Observed
                }
            };
            db.InventoryRelationships.Add(edge);
            db.RelationshipSupports.AddRange(sorted.Select(c => new RelationshipSupport
            { WorkspaceId = workspace, RelationshipId = edge.Id, ObservationId = c.Row.Id }));
        }

        Endpoint Resolve(string json, DateTimeOffset at)
        {
            var reference = JsonSerializer.Deserialize<ExternalReference>(json, ContractJson.Options)!;
            bool TypeMatches(string type) => type == reference.Type || reference.Type == "directoryObject";
            var primary = keys[(reference.Namespace, reference.ExternalId)].Where(x => TypeMatches(x.Type)).Select(x => x.ObjectId).Distinct().ToArray();
            if (primary.Length != 0) { return Result(primary, EndpointResolution.Primary); }
            var history = aliases[(reference.Namespace, AlternateKeyProjection.Normalize(reference.Namespace, reference.ExternalId))]
                .Where(x => TypeMatches(x.Type)).ToArray();
            var candidates = history.Where(x => x.ValidFromUtc <= at && (x.ValidUntilUtc == null || at < x.ValidUntilUtc))
                .Select(x => x.ObjectId).Distinct().ToArray();
            // A stable technical alternate may arrive after the edge. Mutable names cannot be extrapolated backwards.
            if (candidates.Length == 0 && reference.Namespace is "ad.objectSid" or "microsoft.graph.skuId" or "deviceId" or "appId")
            {
                candidates = history.Select(x => x.ObjectId).Distinct().ToArray();
            }
            return Result(candidates, EndpointResolution.Alternate);

            Endpoint Result(Guid[] ids, EndpointResolution method) => ids.Length switch
            {
                0 => new(null, EndpointResolution.Pending, json),
                1 => new(ids[0], method, json),
                _ => new(null, EndpointResolution.Ambiguous, json)
            };
        }
    }
}
