using System.Text.Json;
using Clarity.Contracts;

namespace Clarity.Infrastructure;

/// <summary>Links Entra users to their OneDrive drive metadata through Microsoft Graph v1.0; never reads file contents.</summary>
public sealed class OneDriveCollector(DiscoveryCatalog catalog)
{
    public const string ProducerId = "clarity.graph.onedrive";
    public const string UsersPath = "/v1.0/users?$top=999&$select=id,userPrincipalName";
    public const string DriveSelect = "?$select=id,driveType,webUrl,createdDateTime,lastModifiedDateTime,quota,owner,sharepointIds";
    public const int UserLimit = 20000;

    internal static async Task<SectionAccess> ProbeAsync(GraphTransport graph, CatalogSection section, CancellationToken cancellationToken)
    {
        if (GraphReportDownloads.ReportProducer(section, ProducerId) is not null)
        {
            return await GraphReportDownloads.ProbeAsync(graph, section, cancellationToken);
        }
        if (section.Id != "onedrive.accounts") { throw new GraphReadException("SectionUnavailable"); }
        using var users = await graph.GetAsync("/v1.0/users?$top=1&$select=id", cancellationToken);
        var values = GraphValues.Page(users);
        if (values.GetArrayLength() == 0) { return new(section.Id, true, "DrivesNotTestedEmptyScope", section.Permissions); }
        var id = Uri.EscapeDataString(GraphValues.Guid(values[0], "id") ?? throw new GraphReadException("MissingIdentifier"));
        using var drives = await graph.GetOptionalAsync($"/v1.0/users/{id}/drives?$select=id", cancellationToken);
        return new(section.Id, true, "Available", section.Permissions);
    }

    public async Task<GraphSectionData> CollectAsync(GraphTransport graph, string capability, CollectionScope scope,
        CancellationToken cancellationToken)
    {
        var section = catalog.Sections[capability];
        var result = new GraphSectionAccumulator(capability);
        try
        {
            if (capability != "onedrive.accounts") { throw new GraphReadException("SectionUnavailable"); }
            var users = new List<string>();
            await graph.PagesAsync(UsersPath, value =>
            {
                var id = GraphValues.Guid(value, "id") ?? throw new GraphReadException("MissingIdentifier");
                if (users.Count < UserLimit) { users.Add(id); }
                else { result.Count("UserLimit", $"users beyond the {UserLimit} per-run limit were not read."); }
                return Task.CompletedTask;
            }, cancellationToken);
            foreach (var userId in users) { await DrivesAsync(graph, result, userId, cancellationToken); }
        }
        catch (Exception exception) when (GraphSectionAccumulator.IsCollectionFailure(exception))
        {
            result.Fail(exception);
        }
        return result.Build(section, scope);
    }

    private static async Task DrivesAsync(GraphTransport graph, GraphSectionAccumulator result, string userId, CancellationToken cancellationToken)
    {
        using var drives = await graph.GetOptionalAsync($"/v1.0/users/{Uri.EscapeDataString(userId)}/drives{DriveSelect}", cancellationToken);
        var business = drives is not null && drives.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(x => x.TryGetProperty("driveType", out var type) && type.GetString() == "business").ToArray()
            : [];
        if (business.Length == 0)
        {
            result.Count("OneDriveNotProvisioned", "users had no readable OneDrive (never provisioned, not licensed or not accessible).");
            return;
        }
        foreach (var drive in business)
        {
            var ids = drive.TryGetProperty("sharepointIds", out var facet) && facet.ValueKind == JsonValueKind.Object ? facet : default;
            var siteId = ids.ValueKind == JsonValueKind.Object ? GraphValues.Guid(ids, "siteId") : null;
            if (siteId is null)
            {
                // Without the immutable site ID the drive cannot be linked to report or SharePoint evidence; it is not guessed from the URL.
                result.Count("SiteIdUnavailable", "OneDrive drives without a SharePoint site ID were not converted (unresolved link).");
                continue;
            }
            var url = ids.ValueKind == JsonValueKind.Object ? GraphValues.SharePointUrl(ids, "siteUrl") : null;
            var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["siteUrl"] = GraphValues.String(url),
                ["driveType"] = GraphValues.String("business")
            };
            GraphValues.CopyString(drive, "id", attributes, "driveId");
            GraphValues.CopyUtc(drive, "createdDateTime", attributes);
            GraphValues.CopyUtc(drive, "lastModifiedDateTime", attributes);
            if (drive.TryGetProperty("quota", out var quota) && quota.ValueKind == JsonValueKind.Object)
            {
                GraphValues.CopyBytes(quota, "used", attributes, "quotaUsedBytes");
                GraphValues.CopyBytes(quota, "total", attributes, "quotaTotalBytes");
                GraphValues.CopyString(quota, "state", attributes, "quotaState");
            }
            var key = new ExternalReference("oneDrive", "sharepoint.siteId", siteId);
            result.SetObject(new ObjectRecord(result.Capability, "oneDrive", "sharepoint.siteId", siteId, DateTimeOffset.UtcNow, attributes,
                url is null ? null : [new ExternalReference("oneDrive", "url", url)]));
            result.AddRelation("ownerOf", new ExternalReference("user", "microsoft.graph", userId), key);
        }
    }
}
