using System.Text.Json;
using Clarity.Contracts;

namespace Clarity.Infrastructure;

public sealed class IntuneCollector(DiscoveryCatalog catalog)
{
    public const string ProducerId = "clarity.graph.intune";
    private const string Intune = "microsoft.intune";
    private const string Directory = "microsoft.graph";

    private static string Path(CatalogSection section, int index) => section.Endpoints[index]["GET ".Length..];

    private static string ParentType(string capability) => capability switch
    {
        "intune.managedDevices" or "intune.primaryUsers" => "managedDevice",
        "intune.registeredOwners" => "device",
        "intune.autopilotIdentities" => "windowsAutopilotDeviceIdentity",
        "intune.apps" or "intune.appAssignments" => "mobileApp",
        "intune.profiles" or "intune.profileAssignments" => "deviceConfiguration",
        "intune.policies" or "intune.policyAssignments" => "deviceCompliancePolicy",
        _ => throw new GraphReadException("SectionUnavailable")
    };

    private static string SelectParent(CatalogSection section) => Path(section, 0) + "?$select=id"
        + (section.Endpoints.Count == 1 ? "," + string.Join(',', section.Attributes.Keys) : "");

    private static string SelectChild(CatalogSection section, string parentId)
    {
        if (parentId is "." or ".." || parentId.Contains('/') || parentId.Contains('\\') || parentId.Contains(':'))
        { throw new GraphReadException("InvalidParentIdentifier"); }
        var select = section.Attributes.ContainsKey("intent") ? "id,intent,target"
            : section.Relations.Contains("assignmentOf") ? "id,target" : "id";
        return Path(section, 1).Replace("{id}", Uri.EscapeDataString(parentId), StringComparison.Ordinal) + "?$select=" + select;
    }

    internal static async Task<SectionAccess> ProbeAsync(GraphTransport graph, CatalogSection section, CancellationToken token)
    {
        using var page = await graph.GetAsync(SelectParent(section), token);
        if (!page.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            throw new GraphReadException("InvalidGraphPage");
        }
        if (section.Endpoints.Count > 1)
        {
            if (values.GetArrayLength() == 0) { return new(section.Id, false, "IntuneChildNotTested", section.Permissions); }
            using var child = await graph.GetAsync(SelectChild(section, Id(values[0])), token);
            if (!child.RootElement.TryGetProperty("value", out var children) || children.ValueKind != JsonValueKind.Array)
            {
                throw new GraphReadException("InvalidGraphPage");
            }
        }
        return new(section.Id, true, "Available", section.Permissions);
    }

    public async Task<GraphSectionData> CollectAsync(GraphTransport graph, string capability, CollectionScope scope, CancellationToken token)
    {
        var section = catalog.Sections[capability];
        var sectionId = $"{capability}:{scope.Value.ToLowerInvariant()}";
        var parentType = ParentType(capability);
        var objects = new Dictionary<ExternalReference, ObjectRecord>();
        var edges = new Dictionary<(string, ExternalReference, ExternalReference), RelationshipRecord>();
        var parents = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<SectionIssue>();
        try
        {
            await graph.PagesAsync(SelectParent(section), async value =>
            {
                var id = Id(value);
                var parent = new ExternalReference(parentType, parentType == "device" ? Directory : Intune, id);
                if (section.Endpoints.Count == 1)
                {
                    var attributes = Fields(value);
                    AddObject(parent, attributes);
                    if (capability == "intune.managedDevices")
                    {
                        LinkId(parent, value, "azureADDeviceId", "referencesEntraDevice", "device", "deviceId");
                    }
                    else if (capability == "intune.autopilotIdentities")
                    {
                        LinkId(parent, value, "azureActiveDirectoryDeviceId", "referencesEntraDevice", "device", "deviceId");
                        LinkId(parent, value, "managedDeviceId", "referencesManagedDevice", "managedDevice", Intune);
                    }
                }
                else if (parents.Add(id))
                {
                    if (parents.Count > 100000) { throw new GraphReadException("ObjectLimit"); }
                    await graph.PagesAsync(SelectChild(section, id), child =>
                    {
                        var childId = Id(child);
                        if (capability == "intune.primaryUsers")
                        {
                            AddEdge("intunePrimaryUserOf", new("user", Directory, childId), parent);
                        }
                        else if (capability == "intune.registeredOwners")
                        {
                            var type = child.TryGetProperty("@odata.type", out var kind) ? kind.GetString() : null;
                            var ownerType = type?.StartsWith("#microsoft.graph.", StringComparison.Ordinal) == true
                                ? type["#microsoft.graph.".Length..] : "directoryObject";
                            if (ownerType.Length is 0 or > 100) { throw new GraphReadException("GraphDataUnavailable"); }
                            AddEdge("entraRegisteredOwnerOf", new(ownerType, Directory, childId), parent);
                        }
                        else
                        {
                            var assignment = new ExternalReference(parent.Type + "Assignment", Intune, $"{id}:{childId}");
                            var attributes = Fields(child, "intent");
                            if (!child.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.Object
                                || !target.TryGetProperty("@odata.type", out var targetType) || targetType.ValueKind != JsonValueKind.String)
                            {
                                throw new GraphReadException("IntuneTargetUnavailable");
                            }
                            attributes.Add("targetType", targetType.Clone());
                            if (target.TryGetProperty("groupId", out var groupId))
                            {
                                AddField(attributes, "groupId", groupId);
                            }
                            AddObject(assignment, attributes);
                            AddEdge("assignmentOf", assignment, parent);
                            if (targetType.GetString() is ("#microsoft.graph.groupAssignmentTarget" or "#microsoft.graph.exclusionGroupAssignmentTarget")
                                && !LinkId(assignment, target, "groupId", "targetsGroup", "group", Directory)
                                && !errors.Any(e => e.Code == "IntuneTargetUnavailable"))
                            {
                                errors.Add(new("IntuneTargetUnavailable", "A group assignment did not expose a usable target ID."));
                            }
                        }
                        return Task.CompletedTask;
                    }, token);
                }
            }, token);
        }
        catch (Exception e) when (e is GraphReadException or HttpRequestException or JsonException or ContractException or InvalidOperationException)
        {
            errors.Add(new(e is GraphReadException read ? read.Code : "GraphDataUnavailable",
                "Check the section permissions, Intune license and service availability, then relaunch."));
        }
        var status = errors.Count == 0 ? CoverageStatus.Complete : objects.Count + edges.Count > 0 ? CoverageStatus.Partial : CoverageStatus.Failed;
        var result = new SectionResult(sectionId, capability, status, scope, section.Attributes.Keys.ToArray(), section.Relations,
            status == CoverageStatus.Failed ? null : objects.Count, status == CoverageStatus.Failed ? null : edges.Count,
            [new("IntuneVisibleScope", "Visible v1.0 resources and declared relationships only; omission does not prove absence.")], errors);
        return new(objects.Values.ToArray(), edges.Values.ToArray(), result, null);

        Dictionary<string, JsonElement> Fields(JsonElement row, string? only = null)
        {
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var name in section.Attributes.Keys)
            {
                if ((only == null || name == only) && row.TryGetProperty(name, out var field)) { AddField(fields, name, field); }
            }
            return fields;
        }

        void AddField(Dictionary<string, JsonElement> fields, string name, JsonElement field)
        {
            if (field.ValueKind != JsonValueKind.Null && !(section.Attributes[name].Type switch
            {
                "string" => field.ValueKind == JsonValueKind.String,
                "integer" => field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out _),
                _ => false
            })) { throw new GraphReadException("GraphDataUnavailable"); }
            fields.Add(name, field.Clone());
        }

        void AddObject(ExternalReference reference, Dictionary<string, JsonElement> attrs)
        {
            if (reference.ExternalId.Length > 500 || (objects.Count >= 100000 && !objects.ContainsKey(reference)))
            { throw new GraphReadException("ObjectLimit"); }
            objects[reference] = new(sectionId, reference.Type, reference.Namespace, reference.ExternalId, DateTimeOffset.UtcNow, attrs);
        }

        bool LinkId(ExternalReference from, JsonElement row, string field, string relation, string type, string ns)
        {
            if (row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out var guid) && guid != Guid.Empty)
            {
                AddEdge(relation, from, new(type, ns, guid.ToString()));
                return true;
            }
            return false;
        }

        void AddEdge(string type, ExternalReference from, ExternalReference to)
        {
            var key = (type, from, to);
            if (edges.Count >= 150000 && !edges.ContainsKey(key)) { throw new GraphReadException("RelationshipLimit"); }
            edges[key] = new(sectionId, type, from, to, DateTimeOffset.UtcNow);
        }
    }

    private static string Id(JsonElement row)
    {
        if (!row.TryGetProperty("id", out var field) || field.ValueKind != JsonValueKind.String
            || field.GetString() is not { Length: > 0 and <= 500 } id || string.IsNullOrWhiteSpace(id))
        {
            throw new GraphReadException("MissingIdentifier");
        }
        if (!Guid.TryParse(id, out var guid)) { return id; }
        return guid != Guid.Empty ? guid.ToString() : throw new GraphReadException("MissingIdentifier");
    }
}
