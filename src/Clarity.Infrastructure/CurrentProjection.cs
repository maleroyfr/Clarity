using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

internal sealed record ProjectedSection(SectionRun Section, Guid ExternalRunId, string Producer, string Version,
    DateTimeOffset Started, DateTimeOffset Completed, Guid? Connection, CollectionChannel Channel)
{
    public (string, string, string, string, Guid?) Stream =>
        (Producer, Version, Section.CapabilityId, Section.ScopeHash, Connection);

    public bool IsExhaustive(DiscoveryCatalog catalog)
    {
        if (Section.Coverage != CoverageStatus.Complete || Section.CapabilityId == "entra.users"
            || !catalog.Sections.TryGetValue(Section.CapabilityId, out var capability)) { return false; }
        var result = JsonSerializer.Deserialize<SectionResult>(Section.ResultJson, ContractJson.Options)!;
        return result.Warnings.Count == 0 && result.Errors.Count == 0
            && capability.Attributes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(result.CoveredFields)
            && capability.Relations.ToHashSet(StringComparer.Ordinal).SetEquals(result.CoveredRelations);
    }

    internal static Task<List<ProjectedSection>> LoadAsync(ClarityDbContext db, Guid workspace, Guid environment, CancellationToken token) =>
        (from s in db.SectionRuns.AsNoTracking()
         join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
         where s.WorkspaceId == workspace && r.EnvironmentId == environment && s.PublishedAtUtc != null
         select new ProjectedSection(s, r.ExternalRunId, r.ProducerId, r.ProducerVersion, r.StartedAtUtc, r.CompletedAtUtc, r.ConnectionId, r.Channel))
        .ToListAsync(token);
}

internal static class CurrentProjection
{
    private sealed record Evidence(ObjectObservation Observation, ProjectedSection Source, JsonElement Attributes);
    private sealed record Candidate(Evidence Evidence, JsonElement Value, bool Reference);

    internal static async Task RefreshAsync(ClarityDbContext db, DiscoveryCatalog catalog, Guid workspace, Guid environment,
        SectionRun? published, IReadOnlyList<Guid> changed, CancellationToken token)
    {
        var sections = (await ProjectedSection.LoadAsync(db, workspace, environment, token)).ToDictionary(s => s.Section.Id);
        var comparableFields = sections.Values.Where(s =>
        {
            if (s.Section.Coverage == CoverageStatus.Complete) { return true; }
            if (s.Section.Coverage != CoverageStatus.Partial) { return false; }
            var result = JsonSerializer.Deserialize<SectionResult>(s.Section.ResultJson, ContractJson.Options)!;
            // A profiled report converts each row whole or rejects it whole: values of converted rows stay comparable even when
            // other rows were rejected or concealed. Such sections still never prove absence.
            if (s.Channel == CollectionChannel.Report)
            {
                return result.Errors.All(x => x.Code == "RejectedRows");
            }
            if (s.Connection == null || s.Section.CapabilityId != "entra.users") { return false; }
            return result.Errors.Count == 0 && result.Warnings.Count == 1 && result.Warnings[0].Code == "IncrementalChanges";
        }).Select(s => s.Section.Id).ToHashSet();
        var exhaustive = sections.Values.Where(x => x.IsExhaustive(catalog)).ToLookup(x => x.Stream);
        var inventory = db.InventoryObjects.Where(x => x.WorkspaceId == workspace && x.EnvironmentId == environment);
        var affected = changed.ToHashSet();
        // Include objects from earlier publications on upgrade, and omissions only in the same verified stream.
        affected.UnionWith(await inventory.Where(x => published == null || x.LastObservedAtUtc == null)
            .Select(x => x.Id).ToListAsync(token));
        if (published is not null && sections[published.Id].IsExhaustive(catalog))
        {
            var comparable = exhaustive[sections[published.Id].Stream].Select(x => x.Section.Id).ToArray();
            affected.UnionWith(await db.ObjectObservations.Where(x => x.WorkspaceId == workspace && comparable.Contains(x.SectionRunId))
                .Select(x => x.ObjectId).Distinct().ToListAsync(token));
        }
        foreach (var ids in affected.Chunk(500))
        {
            var items = await inventory.Where(x => ids.Contains(x.Id)).ToListAsync(token);
            var observations = await db.ObjectObservations.AsNoTracking().Where(x => x.WorkspaceId == workspace && ids.Contains(x.ObjectId))
                .ToListAsync(token);
            var history = observations.Where(x => sections.ContainsKey(x.SectionRunId)).ToLookup(x => x.ObjectId);
            await db.CurrentValues.Where(x => x.WorkspaceId == workspace && ids.Contains(x.ObjectId)).ExecuteDeleteAsync(token);
            foreach (var item in items)
            {
                var evidence = history[item.Id].Select(o => new Evidence(o, sections[o.SectionRunId], JsonSerializer.Deserialize<JsonElement>(o.AttributesJson)))
                    .OrderByDescending(e => e.Observation.ObservedAtUtc).ThenByDescending(e => e.Source.ExternalRunId)
                    .ThenBy(e => e.Source.Section.SectionId, StringComparer.Ordinal).ToList();
                if (evidence.Count == 0) { continue; }
                item.LastObservedAtUtc = evidence[0].Observation.ObservedAtUtc;
                item.LastChangedAtUtc = evidence[^1].Observation.ObservedAtUtc;
                item.PresenceSectionId = evidence[0].Source.Section.Id;
                item.Presence = InventoryPresence.Observed;
                if (evidence[0].Attributes.TryGetProperty("deleted", out var tombstone) && tombstone.ValueKind == JsonValueKind.True
                    && Reference(evidence[0].Source, "deleted"))
                {
                    item.Presence = InventoryPresence.Deleted;
                }
                else
                {
                    var absences = evidence.GroupBy(e => e.Source.Stream).Select(stream =>
                    {
                        var last = stream.First();
                        return last.Source.IsExhaustive(catalog) ? exhaustive[last.Source.Stream]
                            .Where(s => s.Started > last.Source.Completed).MaxBy(s => (s.Completed, s.ExternalRunId)) : null;
                    }).ToList();
                    if (absences.TrueForAll(s => s is not null))
                    {
                        item.Presence = InventoryPresence.AbsentFromScope;
                        var absence = absences.MaxBy(s => (s!.Completed, s.ExternalRunId))!;
                        item.PresenceSectionId = absence.Section.Id;
                        item.LastChangedAtUtc = absence.Completed;
                    }
                }
                var values = evidence.SelectMany(e => e.Attributes.EnumerateObject().Select(f =>
                    (f.Name, Candidate: new Candidate(e, f.Value, Reference(e.Source, f.Name)))))
                    .ToLookup(x => x.Name, x => x.Candidate, StringComparer.Ordinal);
                foreach (var field in values)
                {
                    var winner = field.OrderByDescending(c => c.Reference)
                        .ThenByDescending(c => c.Reference && c.Evidence.Source.Connection != null)
                        .ThenByDescending(c => comparableFields.Contains(c.Evidence.Source.Section.Id)).First();
                    var since = field.Where(c => c.Evidence.Source.Stream == winner.Evidence.Source.Stream
                            && c.Evidence.Observation.ObservedAtUtc <= winner.Evidence.Observation.ObservedAtUtc)
                        .SkipWhile(c => c != winner).TakeWhile(c => JsonElement.DeepEquals(c.Value, winner.Value)).Last().Evidence.Observation.ObservedAtUtc;
                    if (since > item.LastChangedAtUtc) { item.LastChangedAtUtc = since; }
                    var conflicts = field.Where(c => c.Evidence.Source.Section.ScopeHash == winner.Evidence.Source.Section.ScopeHash
                            && c.Evidence.Source.Stream != winner.Evidence.Source.Stream)
                        .GroupBy(c => c.Evidence.Source.Stream)
                        .Any(g => !JsonElement.DeepEquals(winner.Value, g.First().Value));
                    db.CurrentValues.Add(new CurrentValue
                    {
                        WorkspaceId = workspace,
                        ObjectId = item.Id,
                        ObservationId = winner.Evidence.Observation.Id,
                        Field = field.Key,
                        ValueJson = winner.Value.GetRawText(),
                        Conflicting = conflicts
                    });
                    var text = winner.Value.ValueKind == JsonValueKind.String ? winner.Value.GetString() : null;
                    switch (field.Key)
                    {
                        case "displayName": item.DisplayName = text; break;
                        case "userPrincipalName": item.UserPrincipalName = text; break;
                        case "mail": item.Smtp = text; break;
                        case "siteId" or "siteUrl": item.Site = text; break;
                        case "enabled" or "accountEnabled":
                            item.Enabled = winner.Value.ValueKind switch
                            { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }; break;
                    }
                }
                item.Domain = evidence.Select(e => JsonSerializer.Deserialize<CollectionScope>(e.Source.Section.ScopeJson, ContractJson.Options)!)
                    .FirstOrDefault(s => s.Kind == "domain")?.Value;
                if (item.Domain == null && item.UserPrincipalName?.LastIndexOf('@') is > 0 and var separator)
                { item.Domain = item.UserPrincipalName[(separator + 1)..]; }
            }
        }
        bool Reference(ProjectedSection s, string field) => catalog.Sections.TryGetValue(s.Section.CapabilityId, out var capability)
            && capability.Attributes.TryGetValue(field, out var definition) && definition.ReferenceProducer == s.Producer;
    }
}
