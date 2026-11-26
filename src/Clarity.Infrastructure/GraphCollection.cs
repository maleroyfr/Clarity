using System.Text.Json;
using Clarity.Contracts;

namespace Clarity.Infrastructure;

/// <summary>
/// Graph usage reports the server may download through the Graph connection of the family's collector producer. Each
/// report stays its own Import producer, interpreted only by its versioned profile, and runs as a single-section job.
/// </summary>
public static class GraphReportDownloads
{
    public const string SourceId = "Microsoft Graph reports API (Clarity connection)";

    private static readonly Dictionary<string, (string Path, string ConnectionProducerId)> Reports = new(StringComparer.Ordinal)
    {
        ["report.m365.sharePointSiteUsageDetail"] = ("/v1.0/reports/getSharePointSiteUsageDetail(period='D30')", SharePointCollector.ProducerId),
        ["report.m365.oneDriveUsageAccountDetail"] = ("/v1.0/reports/getOneDriveUsageAccountDetail(period='D30')", OneDriveCollector.ProducerId)
    };

    public static string? PathFor(string producerId) => Reports.TryGetValue(producerId, out var report) ? report.Path : null;

    /// <summary>Returns the report producer of a section that a connection of <paramref name="connectionProducerId"/> may download.</summary>
    public static string? ReportProducer(CatalogSection section, string connectionProducerId) =>
        section.ProducerIds.Count == 1 && Reports.TryGetValue(section.ProducerIds[0], out var report)
            && report.ConnectionProducerId == connectionProducerId ? section.ProducerIds[0] : null;

    internal static async Task<SectionAccess> ProbeAsync(GraphTransport graph, CatalogSection section, CancellationToken cancellationToken)
    {
        // The probe follows the report redirect only to validate its host; the report body is not downloaded.
        await graph.DownloadReportAsync(PathFor(section.ProducerIds[0]) ?? throw new GraphReadException("ReportDownloadUnavailable"), 1, true,
            cancellationToken);
        return new(section.Id, true, "ReportCoverageLimited", section.Permissions);
    }
}

internal sealed class GraphSectionAccumulator(string capability)
{
    private readonly Dictionary<ExternalReference, ObjectRecord> objects = [];
    private readonly Dictionary<(string, ExternalReference, ExternalReference), RelationshipRecord> relationships = [];
    private readonly Dictionary<string, (int Count, string Message)> counted = new(StringComparer.Ordinal);
    private readonly List<SectionIssue> errors = [];

    public string Capability => capability;
    public bool Contains(ExternalReference key) => objects.ContainsKey(key);
    public ObjectRecord? Find(ExternalReference key) => objects.GetValueOrDefault(key);

    public void SetObject(ObjectRecord record)
    {
        objects[new(record.Type, record.Namespace, record.ExternalId)] = record;
        if (objects.Count > 100000) { throw new GraphReadException("ObjectLimit"); }
    }

    public void AddRelation(string type, ExternalReference from, ExternalReference to)
    {
        relationships[(type, from, to)] = new RelationshipRecord(capability, type, from, to, DateTimeOffset.UtcNow);
        if (relationships.Count > 150000) { throw new GraphReadException("RelationshipLimit"); }
    }

    /// <summary>Counts an expected per-item limitation; counted warnings make the section Partial, never exhaustive.</summary>
    public void Count(string code, string message)
    {
        var current = counted.GetValueOrDefault(code);
        counted[code] = (current.Count + 1, message);
    }

    public static bool IsCollectionFailure(Exception exception) => exception is GraphReadException or HttpRequestException
        or JsonException or ContractException or InvalidOperationException or KeyNotFoundException;

    public void Fail(Exception exception) => errors.Add(new SectionIssue(exception is GraphReadException read ? read.Code : "GraphDataUnavailable",
        "Collection was incomplete. Check the documented application permissions, service availability and scope, then relaunch."));

    public GraphSectionData Build(CatalogSection section, CollectionScope scope)
    {
        var warnings = counted.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new SectionIssue(x.Key, $"{x.Value.Count} item(s): {x.Value.Message}")).ToArray();
        var status = errors.Count > 0 ? objects.Count + relationships.Count == 0 ? CoverageStatus.Failed : CoverageStatus.Partial
            : warnings.Length > 0 ? CoverageStatus.Partial : CoverageStatus.Complete;
        var failed = status == CoverageStatus.Failed;
        var result = new SectionResult(capability, capability, status, scope, section.Attributes.Keys.ToArray(), section.Relations,
            failed ? null : objects.Count, failed ? null : relationships.Count, warnings, errors);
        return new GraphSectionData(failed ? [] : objects.Values.ToArray(), failed ? [] : relationships.Values.ToArray(), result, null);
    }
}

internal static class GraphValues
{
    public static JsonElement Page(JsonDocument page) =>
        page.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array
            ? values : throw new GraphReadException("InvalidGraphPage");

    /// <summary>Normalizes a SharePoint Online URL exactly like the report adapter: lower-case host, no trailing slash.</summary>
    public static string? SharePointUrl(JsonElement value, string property) =>
        value.TryGetProperty(property, out var url) && url.ValueKind == JsonValueKind.String ? SharePointUrl(url.GetString()) : null;

    public static string? SharePointUrl(string? cell)
    {
        if (cell is null || cell.Length > 500 || cell.Any(char.IsWhiteSpace) || !Uri.TryCreate(cell, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !uri.Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var authority = cell.IndexOf("://", StringComparison.Ordinal) + 3;
        var slash = cell.IndexOf('/', authority);
        var path = slash < 0 ? "" : cell[slash..];
        return ("https://" + uri.Host.ToLowerInvariant() + path).TrimEnd('/');
    }

    /// <summary>Returns the site collection GUID of a Graph composite site ID ("hostname,siteCollectionId,webId").</summary>
    public static string? SiteCollectionId(string? graphId)
    {
        var parts = graphId?.Split(',');
        return parts is { Length: 3 } && System.Guid.TryParse(parts[1], out var id) && id != System.Guid.Empty ? id.ToString("D") : null;
    }

    public static string? Guid(JsonElement value, string property) =>
        value.TryGetProperty(property, out var id) && id.ValueKind == JsonValueKind.String && System.Guid.TryParse(id.GetString(), out var guid)
            && guid != System.Guid.Empty ? guid.ToString("D") : null;

    public static void CopyString(JsonElement source, string property, IDictionary<string, JsonElement> target, string? field = null)
    {
        if (source.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Null)
        {
            target[field ?? property] = value.Clone();
        }
    }

    public static void CopyUtc(JsonElement source, string property, IDictionary<string, JsonElement> target)
    {
        if (source.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out var date))
        {
            target[property] = JsonSerializer.SerializeToElement(date.ToUniversalTime());
        }
    }

    public static void CopyBytes(JsonElement source, string property, IDictionary<string, JsonElement> target, string field)
    {
        if (source.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var bytes) && bytes >= 0)
        {
            target[field] = JsonSerializer.SerializeToElement(bytes);
        }
    }

    public static JsonElement String(string? value) => JsonSerializer.SerializeToElement(value);
}
