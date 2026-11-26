using System.Text.Json;
using Clarity.Contracts;

namespace Clarity.Infrastructure;

public sealed class TeamsCollector(DiscoveryCatalog catalog)
{
    public const string ProducerId = "clarity.graph.teams";
    public const string TeamsPath = "/v1.0/teams?$select=id,displayName,visibility";
    private const string ParentsPath = "/v1.0/teams?$select=id";

    private static ExternalReference Team(string id) => new("team", "microsoft.graph", id);
    private static ExternalReference Channel(string team, string channel) => new("teamChannel", "microsoft.teams.channel", team + "/" + channel);
    private static string MembersPath(string team) => $"/v1.0/teams/{team}/members?$top=999&$select=id,displayName,userId,tenantId,roles";
    private static string ChannelsPath(string team) => $"/v1.0/teams/{team}/channels?$select=id,displayName,membershipType,createdDateTime,webUrl";
    private static string GroupSitePath(string team) => $"/v1.0/groups/{team}/sites/root?$select=id,webUrl";
    private static string FolderPath(string team, string channel) => $"/v1.0/teams/{team}/channels/{Uri.EscapeDataString(channel)}/filesFolder";

    internal static async Task<SectionAccess> ProbeAsync(GraphTransport graph, CatalogSection section, CancellationToken token)
    {
        using var teams = await graph.GetAsync((section.Id == "teams.teams" ? TeamsPath : ParentsPath) + "&$top=1", token);
        var values = GraphValues.Page(teams);
        if (section.Id == "teams.teams") { return new(section.Id, true, "Available", section.Permissions); }
        if (values.GetArrayLength() == 0) { return new(section.Id, false, "TeamsChildrenNotTested", section.Permissions); }
        var team = GraphValues.Guid(values[0], "id") ?? throw new GraphReadException("MissingIdentifier");
        if (section.Id == "teams.members")
        {
            using var members = await graph.GetAsync(MembersPath(team), token);
            GraphValues.Page(members);
        }
        else
        {
            using var channels = await graph.GetAsync(ChannelsPath(team), token);
            var children = GraphValues.Page(channels);
            if (section.Id == "teams.sites")
            {
                using var site = await graph.GetOptionalAsync(GroupSitePath(team), token);
                if (site is null) { return new(section.Id, false, "TeamsSiteUnavailable", section.Permissions); }
                if (GraphValues.SiteCollectionId(OptionalString(site.RootElement, "id")) is null)
                { return new(section.Id, false, "TeamsSiteUnresolved", section.Permissions); }
                if (children.GetArrayLength() == 0) { return new(section.Id, false, "TeamsSitesNotTested", section.Permissions); }
                var location = await ChannelSiteAsync(graph, team, RequiredString(children[0], "id"), token);
                if (location.Id is null) { return new(section.Id, false, "TeamsSiteUnresolved", section.Permissions); }
            }
            else if (section.Id != "teams.channels") { throw new GraphReadException("SectionUnavailable"); }
        }
        return new(section.Id, true, "TeamsSampleOnly", section.Permissions);
    }

    public async Task<GraphSectionData> CollectAsync(GraphTransport graph, string capability, CollectionScope scope, CancellationToken token)
    {
        var result = new GraphSectionAccumulator(capability);
        var parents = new HashSet<string>(StringComparer.Ordinal);
        await AttemptAsync(result, () => graph.PagesAsync(capability == "teams.teams" ? TeamsPath : ParentsPath, async row =>
        {
            var team = GraphValues.Guid(row, "id") ?? throw new GraphReadException("MissingIdentifier");
            if (!parents.Add(team)) { return; }
            if (parents.Count > 20000) { throw new GraphReadException("TeamsLimit"); }
            switch (capability)
            {
                case "teams.teams":
                    var fields = new Dictionary<string, JsonElement>();
                    GraphValues.CopyString(row, "displayName", fields);
                    GraphValues.CopyString(row, "visibility", fields);
                    Put(result, Team(team), fields);
                    // Microsoft documents the team ID as the ID of its backing Microsoft 365 group.
                    result.AddRelation("teamGroup", Team(team), new("group", "microsoft.graph", team));
                    break;
                case "teams.members":
                    await AttemptAsync(result, () => graph.PagesAsync(MembersPath(team), member =>
                    {
                        Member(result, team, scope.Value, member);
                        return Task.CompletedTask;
                    }, token));
                    break;
                case "teams.channels":
                    await AttemptAsync(result, () => graph.PagesAsync(ChannelsPath(team), channel =>
                    {
                        var key = Channel(team, RequiredString(channel, "id"));
                        var attributes = new Dictionary<string, JsonElement>();
                        GraphValues.CopyString(channel, "displayName", attributes);
                        GraphValues.CopyString(channel, "membershipType", attributes);
                        GraphValues.CopyString(channel, "webUrl", attributes);
                        GraphValues.CopyUtc(channel, "createdDateTime", attributes);
                        if (!attributes.TryGetValue("membershipType", out var kind) || kind.GetString() is not ("standard" or "private" or "shared"))
                        { result.Count("TeamsChannelTypeUnknown", "channel types were unavailable or unknown; no site or access inheritance was assumed."); }
                        Put(result, key, attributes);
                        result.AddRelation("channelInTeam", key, Team(team));
                        return Task.CompletedTask;
                    }, token));
                    break;
                case "teams.sites":
                    await SitesAsync(graph, result, team, token);
                    break;
                default:
                    throw new GraphReadException("SectionUnavailable");
            }
        }, token));
        return result.Build(catalog.Sections[capability], scope);
    }

    private static void Member(GraphSectionAccumulator result, string team, string tenant, JsonElement member)
    {
        var key = new ExternalReference("teamMembership", "microsoft.teams.membership", team + "/" + RequiredString(member, "id"));
        var fields = new Dictionary<string, JsonElement>();
        GraphValues.CopyString(member, "displayName", fields);
        var userId = GraphValues.Guid(member, "userId");
        var tenantId = GraphValues.Guid(member, "tenantId");
        if (userId is not null) { fields["userId"] = GraphValues.String(userId); }
        if (tenantId is not null) { fields["tenantId"] = GraphValues.String(tenantId); }
        var roles = member.TryGetProperty("roles", out var value) && value.ValueKind == JsonValueKind.Array
            && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String) ? value : default;
        if (roles.ValueKind == JsonValueKind.Array) { fields["roles"] = roles.Clone(); }
        else { result.Count("TeamsMemberRolesUnknown", "membership roles were unavailable; ownership was not inferred."); }
        Put(result, key, fields);
        result.AddRelation("membershipInTeam", key, Team(team));
        // Never resolve a foreign tenant's user ID against the local directory, or parse the opaque membership ID.
        var user = userId is not null && tenantId is not null
            ? new ExternalReference("user", tenantId == tenant ? "microsoft.graph" : "microsoft.graph.tenantUser",
                tenantId == tenant ? userId : tenantId + "/" + userId)
            : null;
        if (user is not null) { result.AddRelation("membershipUser", key, user); }
        if (user is null || tenantId != tenant)
        { result.Count("TeamsMemberUnresolved", "members lacked a local tenant-qualified user ID; their membership evidence remains separate and reviewable."); }
        result.AddRelation("memberOf", user ?? key, Team(team));
        if (roles.ValueKind == JsonValueKind.Array && roles.EnumerateArray().Any(x => x.GetString() == "owner"))
        { result.AddRelation("ownerOf", user ?? key, Team(team)); }
    }

    private static async Task SitesAsync(GraphTransport graph, GraphSectionAccumulator result, string team, CancellationToken token)
    {
        var root = new ExternalReference("site", "microsoft.graph.groupSite", team);
        await AttemptAsync(result, async () =>
        {
            using var site = await graph.GetOptionalAsync(GroupSitePath(team), token);
            if (site is not null)
            {
                root = Site(result, new(GraphValues.SiteCollectionId(OptionalString(site.RootElement, "id")),
                    GraphValues.SharePointUrl(site.RootElement, "webUrl")), root);
            }
            else { result.Count("TeamsSiteUnavailable", "group root sites were not readable or not provisioned; the endpoint reference requires verification."); }
        });
        result.AddRelation("groupSite", new("group", "microsoft.graph", team), root);
        result.AddRelation("teamSite", Team(team), root);
        await AttemptAsync(result, () => graph.PagesAsync(ChannelsPath(team), async channel =>
        {
            var id = RequiredString(channel, "id");
            var target = new ExternalReference("site", "microsoft.teams.filesFolder", team + "/" + id);
            await AttemptAsync(result, async () =>
            {
                var location = await ChannelSiteAsync(graph, team, id, token);
                target = Site(result, location, target);
            });
            // Each channel is resolved independently. Private/shared channels never fall back to the team's root site.
            result.AddRelation("channelSite", Channel(team, id), target);
        }, token));
    }

    private sealed record SiteLocation(string? Id, string? Url);

    private static async Task<SiteLocation> ChannelSiteAsync(GraphTransport graph, string team, string channel, CancellationToken token)
    {
        // filesFolder does not support $select. Its response is used only for IDs, never persisted as raw JSON.
        using var folder = await graph.GetOptionalAsync(FolderPath(team, channel), token);
        if (folder is null) { return new(null, null); }
        var location = Location(folder.RootElement);
        if (location.Id is not null) { return location; }
        var parent = folder.RootElement.TryGetProperty("parentReference", out var reference) && reference.ValueKind == JsonValueKind.Object ? reference : default;
        var drive = parent.ValueKind == JsonValueKind.Object ? OptionalString(parent, "driveId") : null;
        var item = OptionalString(folder.RootElement, "id");
        if (drive is null || item is null) { return location; }
        using var metadata = await graph.GetOptionalAsync($"/v1.0/drives/{Uri.EscapeDataString(drive)}/items/{Uri.EscapeDataString(item)}?$select=sharepointIds,parentReference", token);
        if (metadata is null) { return location; }
        var resolved = Location(metadata.RootElement);
        return new(resolved.Id, resolved.Url ?? location.Url);
    }

    private static SiteLocation Location(JsonElement value)
    {
        string? id = null, url = null;
        if (value.TryGetProperty("sharepointIds", out var ids) && ids.ValueKind == JsonValueKind.Object)
        {
            id = GraphValues.Guid(ids, "siteId");
            url = GraphValues.SharePointUrl(ids, "siteUrl");
        }
        if (id is null && value.TryGetProperty("parentReference", out var parent) && parent.ValueKind == JsonValueKind.Object)
        { id = GraphValues.SiteCollectionId(OptionalString(parent, "siteId")); }
        return new(id, url);
    }

    private static ExternalReference Site(GraphSectionAccumulator result, SiteLocation location, ExternalReference unresolved)
    {
        if (location.Id is null)
        { result.Count("TeamsSiteUnresolved", "sites lacked an immutable site collection ID; URL-only objects and endpoint references require verification."); }
        if (location.Id is null && location.Url is null) { return unresolved; }
        var key = new ExternalReference("site", location.Id is null ? "sharepoint.siteUrl" : "sharepoint.siteId", location.Id ?? location.Url!);
        var attributes = new Dictionary<string, JsonElement> { ["teamsConnected"] = JsonSerializer.SerializeToElement(true) };
        if (location.Url is not null) { attributes["webUrl"] = GraphValues.String(location.Url); }
        else if (result.Find(key)?.Attributes.TryGetValue("webUrl", out var prior) == true) { attributes["webUrl"] = prior; }
        var url = attributes.TryGetValue("webUrl", out var current) ? current.GetString() : null;
        result.SetObject(new(result.Capability, key.Type, key.Namespace, key.ExternalId, DateTimeOffset.UtcNow, attributes,
            url is null ? null : [new("site", "url", url)]));
        return key;
    }

    private static void Put(GraphSectionAccumulator result, ExternalReference key, Dictionary<string, JsonElement> attributes) =>
        result.SetObject(new(result.Capability, key.Type, key.Namespace, key.ExternalId, DateTimeOffset.UtcNow, attributes));

    private static async Task AttemptAsync(GraphSectionAccumulator result, Func<Task> read)
    {
        try { await read(); }
        catch (Exception error) when (GraphSectionAccumulator.IsCollectionFailure(error)) { result.Fail(error); }
    }

    private static string? OptionalString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String
        && item.GetString() is { Length: > 0 and <= 450 } text && !string.IsNullOrWhiteSpace(text) ? text : null;
    private static string RequiredString(JsonElement value, string name) => OptionalString(value, name) ?? throw new GraphReadException("MissingIdentifier");
}
