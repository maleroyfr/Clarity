using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

internal static class AlternateKeyProjection
{
    internal static async Task RebuildAsync(ClarityDbContext db, Guid workspaceId, Guid environmentId,
        IReadOnlyList<Guid> objectIds, CancellationToken token)
    {
        foreach (var ids in objectIds.Chunk(500))
        {
            await db.AlternateKeyPeriods.Where(x => x.WorkspaceId == workspaceId && x.EnvironmentId == environmentId
                && ids.Contains(x.ObjectId)).ExecuteDeleteAsync(token);
            var history = await (from o in db.ObjectObservations.AsNoTracking()
                                 join i in db.InventoryObjects on new { o.WorkspaceId, Id = o.ObjectId } equals new { i.WorkspaceId, i.Id }
                                 join s in db.SectionRuns on new { o.WorkspaceId, Id = o.SectionRunId } equals new { s.WorkspaceId, s.Id }
                                 join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                                 where i.WorkspaceId == workspaceId && i.EnvironmentId == environmentId && ids.Contains(i.Id)
                                    && s.PublishedAtUtc != null
                                 orderby o.ObservedAtUtc, r.ExternalRunId, s.SectionId
                                 select new { Observation = o, i.Type, r.ProducerId, s.CapabilityId, s.ScopeHash }).ToListAsync(token);
            foreach (var stream in history.GroupBy(x => new { x.Observation.ObjectId, x.ProducerId, x.CapabilityId, x.ScopeHash }))
            {
                var previous = new Dictionary<(string Field, string Type, string Namespace), List<AlternateKeyPeriod>>();
                foreach (var row in stream)
                {
                    var o = row.Observation;
                    using var attributes = JsonDocument.Parse(o.AttributesJson);
                    foreach (var field in attributes.RootElement.EnumerateObject())
                    {
                        var ns = field.Name switch
                        {
                            "userPrincipalName" => "upn",
                            "mail" or "proxyAddresses" => "smtp",
                            "url" or "webUrl" => "url",
                            "distinguishedName" => "ad.distinguishedName",
                            "objectSid" => "ad.objectSid",
                            "skuId" => "microsoft.graph.skuId",
                            "msDSConsistencyGuid" or "onPremisesSecurityIdentifier" or "deviceId" or "appId"
                                or "onPremisesImmutableId" or "sAMAccountName" or "dnsHostName" => field.Name,
                            _ => null
                        };
                        if (ns is null) { continue; }
                        IEnumerable<string> values = field.Value.ValueKind switch
                        {
                            JsonValueKind.String => [field.Value.GetString()!],
                            JsonValueKind.Array => field.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                                .Select(x => x.GetString()!),
                            _ => []
                        };
                        if (field.Name == "proxyAddresses") { values = values.Where(v => v.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase)); }
                        Record(field.Name, row.Type, ns, values);
                    }
                    var references = JsonSerializer.Deserialize<ExternalReference[]>(o.AlternativeKeysJson ?? "null", ContractJson.Options);
                    foreach (var group in (references ?? []).GroupBy(x => (x.Type, x.Namespace)))
                    {
                        Record("", group.Key.Type, group.Key.Namespace, group.Select(x => x.ExternalId));
                    }

                    void Record(string field, string type, string ns, IEnumerable<string> values)
                    {
                        var key = (field, type, ns);
                        if (previous.TryGetValue(key, out var old))
                        {
                            foreach (var entry in old) { entry.ValidUntilUtc = o.ObservedAtUtc; }
                        }
                        var current = new List<AlternateKeyPeriod>();
                        foreach (var original in values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
                        {
                            var normalized = Normalize(ns, original);
                            if (normalized.Length > 500) { throw new ContractException("IndexedAttributeLength", "objects.jsonl"); }
                            current.Add(new AlternateKeyPeriod
                            {
                                WorkspaceId = workspaceId,
                                EnvironmentId = environmentId,
                                ObjectId = o.ObjectId,
                                ObservationId = o.Id,
                                Namespace = ns,
                                Type = type,
                                OriginalValue = original,
                                NormalizedValue = normalized,
                                ValidFromUtc = o.ObservedAtUtc
                            });
                        }
                        db.AlternateKeyPeriods.AddRange(current);
                        previous[key] = current;
                    }
                }
            }
        }
    }

    internal static string Normalize(string ns, string value) => ns switch
    {
        "smtp" when value.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase) => value[5..].Trim().ToUpperInvariant(),
        "upn" or "smtp" or "ad.distinguishedName" or "dnsHostName" or "sAMAccountName" => value.Trim().ToUpperInvariant(),
        "deviceId" or "appId" when Guid.TryParse(value, out var deviceId) => deviceId.ToString(),
        _ => value
    };
}
