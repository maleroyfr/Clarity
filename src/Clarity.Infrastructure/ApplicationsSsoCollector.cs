using System.Text.Json;
using Clarity.Contracts;

namespace Clarity.Infrastructure;

public sealed class ApplicationsSsoCollector(DiscoveryCatalog catalog)
{
    public const string ProducerId = "clarity.graph.applicationsSso";
    private const string Assignments = "applicationsSso.appRoleAssignments";
    private const string Directory = "microsoft.graph";

    private static string ParentPath(CatalogSection section) => section.Endpoints[0]["GET ".Length..] + "?$select=id"
        + (section.Id == Assignments ? "" : "," + string.Join(',', section.Attributes.Keys));

    private static string ChildPath(CatalogSection section, string id) =>
        section.Endpoints[1]["GET ".Length..].Replace("{id}", GuidId(id), StringComparison.Ordinal)
        + "?$select=id," + string.Join(',', section.Attributes.Keys);

    internal static async Task<SectionAccess> ProbeAsync(GraphTransport graph, CatalogSection section, CancellationToken token)
    {
        using var page = await graph.GetAsync(ParentPath(section), token);
        var values = Values(page);
        if (section.Id == Assignments)
        {
            if (values.GetArrayLength() == 0) { return new(section.Id, false, "SsoAssignmentsNotTested", section.Permissions); }
            using var child = await graph.GetAsync(ChildPath(section, RequiredString(values[0], "id")), token);
            Values(child);
        }
        return new(section.Id, true, "Available", section.Permissions);
    }

    public async Task<GraphSectionData> CollectAsync(GraphTransport graph, string capability, CollectionScope scope, CancellationToken token)
    {
        var section = catalog.Sections[capability];
        var sectionId = $"{capability}:{scope.Value}";
        var objects = new Dictionary<ExternalReference, ObjectRecord>();
        var edges = new Dictionary<(string, ExternalReference, ExternalReference), RelationshipRecord>();
        var parents = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<SectionIssue>();
        try
        {
            await graph.PagesAsync(ParentPath(section), async row =>
            {
                var id = GuidId(RequiredString(row, "id"));
                if (capability != Assignments)
                {
                    var type = capability == "applicationsSso.applications" ? "application" : "servicePrincipal";
                    var reference = new ExternalReference(type, Directory, id);
                    var fields = Fields(row);
                    if (fields.TryGetValue("appId", out var appId) && appId.ValueKind == JsonValueKind.String)
                    {
                        fields["appId"] = JsonSerializer.SerializeToElement(GuidId(appId.GetString()!));
                    }
                    AddObject(reference, fields);
                    if (type == "servicePrincipal" && fields.TryGetValue("appId", out appId) && appId.ValueKind == JsonValueKind.String)
                    {
                        AddEdge("servicePrincipalForApplication", reference, new("application", "appId", appId.GetString()!));
                    }
                }
                else if (parents.Add(id))
                {
                    if (parents.Count > 100000) { throw new GraphReadException("ObjectLimit"); }
                    try
                    {
                        await graph.PagesAsync(ChildPath(section, id), assignment =>
                        {
                            var assignmentId = RequiredString(assignment, "id");
                            var fields = Fields(assignment);
                            var principal = GuidId(RequiredString(assignment, "principalId"));
                            var resource = GuidId(RequiredString(assignment, "resourceId"));
                            var role = GuidId(RequiredString(assignment, "appRoleId"), allowEmpty: true);
                            if (resource != id) { throw new GraphReadException("SsoResourceMismatch"); }
                            var principalType = fields.TryGetValue("principalType", out var kind) ? kind.GetString() : null;
                            var targetType = principalType switch
                            {
                                "User" => "user",
                                "Group" => "group",
                                "ServicePrincipal" => "servicePrincipal",
                                _ => "directoryObject"
                            };
                            if (targetType == "directoryObject") { Error("SsoPrincipalTypeUnknown"); }
                            fields["principalId"] = JsonSerializer.SerializeToElement(principal);
                            fields["resourceId"] = JsonSerializer.SerializeToElement(resource);
                            fields["appRoleId"] = JsonSerializer.SerializeToElement(role);
                            var reference = new ExternalReference("appRoleAssignment", Directory, assignmentId);
                            AddObject(reference, fields);
                            AddEdge("appRoleAssignmentPrincipal", reference, new(targetType, Directory, principal));
                            AddEdge("appRoleAssignmentResource", reference, new("servicePrincipal", Directory, resource));
                            return Task.CompletedTask;
                        }, token);
                    }
                    catch (Exception e) when (IsDataError(e)) { Error(e is GraphReadException read ? read.Code : "GraphDataUnavailable"); }
                }
            }, token);
        }
        catch (Exception e) when (IsDataError(e)) { Error(e is GraphReadException read ? read.Code : "GraphDataUnavailable"); }
        var status = errors.Count == 0 ? CoverageStatus.Complete : objects.Count + edges.Count > 0 ? CoverageStatus.Partial : CoverageStatus.Failed;
        return new(objects.Values.ToArray(), edges.Values.ToArray(),
            new(sectionId, capability, status, scope, section.Attributes.Keys.ToArray(), section.Relations,
                status == CoverageStatus.Failed ? null : objects.Count, status == CoverageStatus.Failed ? null : edges.Count,
                [new("SsoMetadataOnly", "Visible metadata and explicit assignments only; neither effective access nor absence is established.")], errors), null);

        void Error(string code)
        {
            if (!errors.Any(e => e.Code == code)) { errors.Add(new(code, "Check the section permissions, visible scope and Graph availability, then relaunch.")); }
        }

        Dictionary<string, JsonElement> Fields(JsonElement row)
        {
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (name, definition) in section.Attributes)
            {
                if (!row.TryGetProperty(name, out var value)) { continue; }
                if (value.ValueKind != JsonValueKind.Null && !(definition.Type switch
                {
                    "string" => value.ValueKind == JsonValueKind.String,
                    "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    "utc" => value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var date) && date.Offset == TimeSpan.Zero,
                    _ => false
                })) { throw new GraphReadException("GraphDataUnavailable"); }
                fields.Add(name, value.Clone());
            }
            return fields;
        }

        void AddObject(ExternalReference reference, Dictionary<string, JsonElement> fields)
        {
            if (objects.Count >= 100000 && !objects.ContainsKey(reference)) { throw new GraphReadException("ObjectLimit"); }
            objects[reference] = new(sectionId, reference.Type, reference.Namespace, reference.ExternalId, DateTimeOffset.UtcNow, fields);
        }

        void AddEdge(string relation, ExternalReference from, ExternalReference to)
        {
            var key = (relation, from, to);
            if (edges.Count >= 150000 && !edges.ContainsKey(key)) { throw new GraphReadException("RelationshipLimit"); }
            edges[key] = new(sectionId, relation, from, to, DateTimeOffset.UtcNow);
        }
    }

    private static bool IsDataError(Exception e) => e is GraphReadException or HttpRequestException or JsonException or ContractException or InvalidOperationException;
    private static JsonElement Values(JsonDocument page) => page.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array
        ? values : throw new GraphReadException("InvalidGraphPage");
    private static string RequiredString(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 and <= 500 } text && !string.IsNullOrWhiteSpace(text)
            ? text : throw new GraphReadException("MissingIdentifier");
    private static string GuidId(string value, bool allowEmpty = false) =>
        Guid.TryParse(value, out var id) && (allowEmpty || id != Guid.Empty) ? id.ToString() : throw new GraphReadException("MissingIdentifier");
}
