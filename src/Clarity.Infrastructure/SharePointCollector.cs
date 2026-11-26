using System.Text.Json;
using Clarity.Contracts;

namespace Clarity.Infrastructure;

/// <summary>Reads SharePoint Online sites and Microsoft 365 group-connected sites through Microsoft Graph v1.0.</summary>
public sealed class SharePointCollector(DiscoveryCatalog catalog)
{
    public const string ProducerId = "clarity.graph.sharepoint";
    // getAllSites documents no optional query parameters, so the request is sent exactly as documented.
    public const string SitesPath = "/v1.0/sites/getAllSites";
    public const string GroupsPath = "/v1.0/groups?$filter=groupTypes/any(c:c+eq+'Unified')&$select=id,resourceProvisioningOptions";

    internal static async Task<SectionAccess> ProbeAsync(GraphTransport graph, CatalogSection section, CancellationToken cancellationToken)
    {
        if (GraphReportDownloads.ReportProducer(section, ProducerId) is not null)
        {
            return await GraphReportDownloads.ProbeAsync(graph, section, cancellationToken);
        }
        switch (section.Id)
        {
            case "sharepoint.sites":
                {
                    using var sites = await graph.GetAsync(SitesPath, cancellationToken);
                    return new(section.Id, true, "Available", section.Permissions);
                }
            case "sharepoint.groupSites":
                {
                    using var groups = await graph.GetAsync(GroupsPath + "&$top=1", cancellationToken);
                    var values = GraphValues.Page(groups);
                    if (values.GetArrayLength() == 0) { return new(section.Id, true, "GroupSitesNotTestedEmptyScope", section.Permissions); }
                    var id = Uri.EscapeDataString(GraphValues.Guid(values[0], "id") ?? throw new GraphReadException("MissingIdentifier"));
                    using var site = await graph.GetOptionalAsync($"/v1.0/groups/{id}/sites/root?$select=id", cancellationToken);
                    return new(section.Id, true, "Available", section.Permissions);
                }
            default:
                throw new GraphReadException("SectionUnavailable");
        }
    }

    public async Task<GraphSectionData> CollectAsync(GraphTransport graph, string capability, CollectionScope scope,
        CancellationToken cancellationToken)
    {
        var section = catalog.Sections[capability];
        var result = new GraphSectionAccumulator(capability);
        try
        {
            switch (capability)
            {
                case "sharepoint.sites":
                    await SitesAsync(graph, result, cancellationToken);
                    break;
                case "sharepoint.groupSites":
                    await GroupSitesAsync(graph, result, cancellationToken);
                    break;
                default:
                    throw new GraphReadException("SectionUnavailable");
            }
        }
        catch (Exception exception) when (GraphSectionAccumulator.IsCollectionFailure(exception))
        {
            result.Fail(exception);
        }
        return result.Build(section, scope);
    }

    private static async Task SitesAsync(GraphTransport graph, GraphSectionAccumulator result, CancellationToken cancellationToken)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        await graph.PagesAsync(SitesPath, value =>
        {
            if (value.TryGetProperty("isPersonalSite", out var personal) && personal.ValueKind == JsonValueKind.True) { return Task.CompletedTask; }
            var graphId = value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            var siteId = GraphValues.SiteCollectionId(graphId);
            var url = GraphValues.SharePointUrl(value, "webUrl");
            if (url is not null && new Uri(url).Host.EndsWith("-my.sharepoint.com", StringComparison.Ordinal)) { return Task.CompletedTask; }
            if (siteId is null || url is null)
            {
                result.Count("SiteIdentifierUnavailable", "sites without a usable site collection ID or SharePoint URL were not converted.");
                return Task.CompletedTask;
            }
            var collection = value.TryGetProperty("siteCollection", out var facet) && facet.ValueKind == JsonValueKind.Object ? facet : default;
            var isRoot = value.TryGetProperty("root", out var root) && root.ValueKind == JsonValueKind.Object;
            var key = new ExternalReference("site", "sharepoint.siteId", siteId);
            if (result.Contains(key))
            {
                // Keep one object per site collection, preferring the entry Graph marks as the collection root.
                result.Count("DuplicateSiteCollection", "additional webs sharing a site collection ID were not converted separately.");
                if (!isRoot || roots.Contains(siteId)) { return Task.CompletedTask; }
            }
            if (isRoot) { roots.Add(siteId); }
            var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["graphId"] = GraphValues.String(graphId),
                ["webUrl"] = GraphValues.String(url)
            };
            GraphValues.CopyString(value, "name", attributes);
            GraphValues.CopyString(value, "displayName", attributes);
            if (collection.ValueKind == JsonValueKind.Object)
            {
                // The resource reference names the property hostname while documented responses show hostName.
                GraphValues.CopyString(collection, collection.TryGetProperty("hostname", out _) ? "hostname" : "hostName", attributes, "hostName");
                GraphValues.CopyString(collection, "dataLocationCode", attributes);
            }
            GraphValues.CopyUtc(value, "createdDateTime", attributes);
            GraphValues.CopyUtc(value, "lastModifiedDateTime", attributes);
            result.SetObject(new ObjectRecord(result.Capability, "site", "sharepoint.siteId", siteId, DateTimeOffset.UtcNow, attributes,
                [new ExternalReference("site", "url", url)]));
            return Task.CompletedTask;
        }, cancellationToken);
    }

    private static async Task GroupSitesAsync(GraphTransport graph, GraphSectionAccumulator result, CancellationToken cancellationToken)
    {
        var groups = new List<(string Id, bool Teams)>();
        await graph.PagesAsync(GroupsPath, value =>
        {
            var id = GraphValues.Guid(value, "id") ?? throw new GraphReadException("MissingIdentifier");
            var teams = value.TryGetProperty("resourceProvisioningOptions", out var options) && options.ValueKind == JsonValueKind.Array
                && options.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "Team");
            groups.Add((id, teams));
            if (groups.Count > 50000) { throw new GraphReadException("ObjectLimit"); }
            return Task.CompletedTask;
        }, cancellationToken);
        foreach (var (groupId, teams) in groups)
        {
            using var site = await graph.GetOptionalAsync($"/v1.0/groups/{Uri.EscapeDataString(groupId)}/sites/root?$select=id,webUrl",
                cancellationToken);
            if (site is null)
            {
                result.Count("GroupSiteUnavailable", "Microsoft 365 groups had no readable root site (not provisioned or not accessible).");
                continue;
            }
            var graphId = site.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            var siteId = GraphValues.SiteCollectionId(graphId);
            var url = GraphValues.SharePointUrl(site.RootElement, "webUrl");
            if (siteId is null || url is null)
            {
                result.Count("SiteIdentifierUnavailable", "group sites without a usable site collection ID or SharePoint URL were not converted.");
                continue;
            }
            var key = new ExternalReference("site", "sharepoint.siteId", siteId);
            var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["graphId"] = GraphValues.String(graphId),
                ["webUrl"] = GraphValues.String(url),
                ["teamsConnected"] = JsonSerializer.SerializeToElement(teams || result.Find(key)?.Attributes["teamsConnected"].GetBoolean() == true)
            };
            result.SetObject(new ObjectRecord(result.Capability, "site", "sharepoint.siteId", siteId, DateTimeOffset.UtcNow, attributes,
                [new ExternalReference("site", "url", url)]));
            result.AddRelation("groupSite", new ExternalReference("group", "microsoft.graph", groupId), key);
        }
    }
}
