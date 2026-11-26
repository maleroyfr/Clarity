using System.Text.Json;
using Clarity.Contracts;

namespace Clarity.Infrastructure;

public sealed record GraphSectionData(IReadOnlyList<ObjectRecord> Objects, IReadOnlyList<RelationshipRecord> Relationships,
    SectionResult Result, string? DeltaLink);

public sealed class EntraCollector(DiscoveryCatalog catalog)
{
    public static string UserPath(DiscoveryCatalog catalog) => "/v1.0/users/delta?$select=id,"
        + string.Join(',', catalog.Sections["entra.users"].Attributes.Keys.Where(x => x != "deleted"));

    public async Task<GraphSectionData> CollectAsync(GraphTransport graph, string capability, CollectionScope scope,
        string? cursor, CancellationToken cancellationToken)
    {
        var section = catalog.Sections[capability];
        var id = capability;
        var objects = new Dictionary<ExternalReference, ObjectRecord>();
        var relationships = new Dictionary<(string, ExternalReference, ExternalReference), RelationshipRecord>();
        var warnings = new List<SectionIssue>();
        var errors = new List<SectionIssue>();
        string? delta = null;
        var incremental = cursor is not null;
        try
        {
            switch (capability)
            {
                case "entra.users":
                    try { delta = await ReadObjectsAsync(cursor ?? UserPath(catalog), "user"); }
                    catch (GraphReadException exception) when (exception.Code == "CursorExpired" && cursor is not null)
                    {
                        objects.Clear();
                        incremental = false;
                        delta = await ReadObjectsAsync(UserPath(catalog), "user");
                    }
                    if (delta is null) { throw new GraphReadException("MissingDeltaLink"); }
                    if (incremental) { warnings.Add(new("IncrementalChanges", "Incremental changes only; unchanged users remain in previous observations.")); }
                    break;
                case "entra.groups":
                    await ReadObjectsAsync(Select("/groups", section.Attributes.Keys), "group");
                    foreach (var group in objects.Keys.ToArray())
                    {
                        await ReadRelationsAsync($"/v1.0/groups/{Uri.EscapeDataString(group.ExternalId)}/members?$select=id", "memberOf", group);
                        await ReadRelationsAsync($"/v1.0/groups/{Uri.EscapeDataString(group.ExternalId)}/owners?$select=id", "ownerOf", group);
                    }
                    warnings.Add(new("GraphV1MembershipLimit", "Graph v1.0 members can omit service principals; hidden memberships require separate read permission."));
                    break;
                case "entra.devices":
                    await ReadObjectsAsync(Select("/devices", section.Attributes.Keys), "device");
                    break;
                case "entra.domains":
                    await ReadObjectsAsync(Select("/domains", section.Attributes.Keys), "domain");
                    break;
                case "entra.applications":
                    await ReadObjectsAsync("/v1.0/applications?$select=id,displayName,appId,signInAudience", "application");
                    await ReadObjectsAsync("/v1.0/servicePrincipals?$select=id,displayName,appId,servicePrincipalType,accountEnabled", "servicePrincipal");
                    break;
                case "entra.licenses":
                    await graph.PagesAsync("/v1.0/subscribedSkus?$select=id,skuId,skuPartNumber,capabilityStatus,consumedUnits,prepaidUnits", value =>
                    {
                        var record = Record(value, "subscribedSku");
                        var attributes = record.Attributes.ToDictionary();
                        if (value.TryGetProperty("prepaidUnits", out var prepaid) && prepaid.TryGetProperty("enabled", out var enabled))
                        {
                            attributes["enabledUnits"] = enabled.Clone();
                        }
                        var sku = value.GetProperty("skuId").GetString()!;
                        record = record with { Attributes = attributes, AlternativeKeys = [new ExternalReference("subscribedSku", "microsoft.graph.skuId", sku)] };
                        objects[new(record.Type, record.Namespace, record.ExternalId)] = record;
                        return Task.CompletedTask;
                    }, cancellationToken);
                    await graph.PagesAsync("/v1.0/users?$select=id,assignedLicenses", value =>
                    {
                        var record = Record(value, "user");
                        if (!value.TryGetProperty("assignedLicenses", out var assigned)) { throw new GraphReadException("LicenseFieldsUnavailable"); }
                        var skus = assigned.EnumerateArray().Select(x => x.GetProperty("skuId").GetString()!).ToArray();
                        record = record with { Attributes = new Dictionary<string, JsonElement> { ["assignedSkuIds"] = JsonSerializer.SerializeToElement(skus) } };
                        var from = new ExternalReference("user", "microsoft.graph", record.ExternalId);
                        objects[from] = record;
                        foreach (var sku in skus) { AddRelation("assignedLicense", from, new("subscribedSku", "microsoft.graph.skuId", sku)); }
                        return Task.CompletedTask;
                    }, cancellationToken);
                    break;
                default:
                    throw new GraphReadException("SectionUnavailable");
            }
        }
        catch (Exception exception) when (exception is GraphReadException or HttpRequestException or JsonException or ContractException
            or InvalidOperationException or KeyNotFoundException)
        {
            errors.Add(new SectionIssue(exception is GraphReadException read ? read.Code : "GraphDataUnavailable",
                "Collection was incomplete. Check the documented application permissions, service availability and scope, then relaunch."));
            delta = null;
        }
        var status = errors.Count > 0 ? objects.Count + relationships.Count == 0 ? CoverageStatus.Failed : CoverageStatus.Partial
            : warnings.Count > 0 ? CoverageStatus.Partial : CoverageStatus.Complete;
        var result = new SectionResult(id, capability, status, scope, section.Attributes.Keys.ToArray(), section.Relations,
            status == CoverageStatus.Failed ? null : objects.Count, status == CoverageStatus.Failed ? null : relationships.Count, warnings, errors);
        return new GraphSectionData(objects.Values.ToArray(), relationships.Values.ToArray(), result, delta);

        ObjectRecord Record(JsonElement value, string type)
        {
            var externalId = value.GetProperty("id").GetString() ?? throw new GraphReadException("MissingIdentifier");
            var attrs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var field in section.Attributes.Keys)
            {
                if (value.TryGetProperty(field, out var attribute)) { attrs[field] = attribute.Clone(); }
            }
            if (type == "user" && capability == "entra.users")
            {
                attrs["deleted"] = JsonSerializer.SerializeToElement(value.TryGetProperty("@removed", out _));
            }
            return new ObjectRecord(id, type, "microsoft.graph", externalId, DateTimeOffset.UtcNow, attrs);
        }

        Task<string?> ReadObjectsAsync(string path, string type) => graph.PagesAsync(path, value =>
        {
            var record = Record(value, type);
            var key = new ExternalReference(type, record.Namespace, record.ExternalId);
            if (objects.TryGetValue(key, out var previous))
            {
                var merged = previous.Attributes.ToDictionary();
                foreach (var (name, attribute) in record.Attributes) { merged[name] = attribute; }
                record = record with { Attributes = merged };
            }
            objects[key] = record;
            if (objects.Count > 100000) { throw new GraphReadException("ObjectLimit"); }
            return Task.CompletedTask;
        }, cancellationToken);

        async Task ReadRelationsAsync(string path, string relationType, ExternalReference target)
        {
            await graph.PagesAsync(path, value =>
            {
                var type = value.TryGetProperty("@odata.type", out var kind)
                    ? kind.GetString()!.Replace("#microsoft.graph.", "", StringComparison.Ordinal) : "directoryObject";
                AddRelation(relationType, new(type, "microsoft.graph", value.GetProperty("id").GetString()!), target);
                return Task.CompletedTask;
            }, cancellationToken);
        }

        void AddRelation(string type, ExternalReference from, ExternalReference to)
        {
            relationships[(type, from, to)] = new RelationshipRecord(id, type, from, to, DateTimeOffset.UtcNow);
            if (relationships.Count > 150000) { throw new GraphReadException("RelationshipLimit"); }
        }
    }

    private static string Select(string resource, IEnumerable<string> fields) => "/v1.0" + resource + "?$select=id," + string.Join(',', fields);
}
