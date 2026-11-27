namespace Clarity.Core;

public enum ExportDecisionFilter { All, Include, Exclude, ToArbitrate, Undecided }
public enum ExportArtifactKind { Workbook, InventoryCsv, MappingsCsv }

/// <summary>The consultant filters of one export, normalized and recorded verbatim with the export.</summary>
public sealed record ExportFilter(EnvironmentRole Side, Guid? EnvironmentId = null, string? Type = null, string? Status = null,
    ExportDecisionFilter Decision = ExportDecisionFilter.All, string? Wave = null, Guid? PopulationId = null)
{
    private static readonly string[] Statuses = ["Enabled", "Disabled", .. Enum.GetNames<InventoryPresence>()];

    public ExportFilter Normalize()
    {
        if (!Enum.IsDefined(Side) || !Enum.IsDefined(Decision)) { throw new ArgumentException("Invalid export side or decision filter."); }
        var type = string.IsNullOrWhiteSpace(Type) ? null : Type.Trim();
        if (type is { Length: > 100 }) { throw new ArgumentException("An object type is limited to 100 characters."); }
        var status = string.IsNullOrWhiteSpace(Status) ? null : Status.Trim();
        if (status is not null && !Statuses.Contains(status, StringComparer.Ordinal)) { throw new ArgumentException("Unknown inventory status."); }
        return new(Side, EnvironmentId == Guid.Empty ? null : EnvironmentId, type, status, Decision, ScopeRules.Wave(Wave),
            PopulationId == Guid.Empty ? null : PopulationId);
    }
}

/// <summary>
/// One consultant export: the filters, the dated observation selection captured at launch (with its SHA-256) and, once generated, the
/// reconciled counters. Its identifier is the request operation identifier and the identifier of the background job that generates it.
/// </summary>
public sealed class ExportJob : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public EnvironmentRole Side { get; init; }
    public required string FiltersJson { get; init; }
    public required string SelectionJson { get; init; }
    public required string SelectionHash { get; init; }
    public DateTimeOffset AsOfUtc { get; init; }
    public Guid AuthorTenantId { get; init; }
    public Guid AuthorObjectId { get; init; }
    public DateTimeOffset? GeneratedAtUtc { get; set; }
    public int? ObjectCount { get; set; }
    public int? ExceptionCount { get; set; }
    public int? MappingCount { get; set; }
    public int? CoverageGaps { get; set; }

    /// <summary>When retention removed the generated files; the request, counters and selection stay as the export's history.</summary>
    public DateTimeOffset? FilesPurgedAtUtc { get; set; }
}

/// <summary>One generated private file of an export, kept in the workspace prefix of the exports Blob container.</summary>
public sealed class ExportArtifact : WorkspaceEntity
{
    public Guid ExportJobId { get; init; }
    public ExportArtifactKind Kind { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required string BlobName { get; init; }
    public required string ContentHash { get; init; }
    public long Bytes { get; init; }
    public int RowCount { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public static class ExportRules
{
    public const int MaxRows = 50_000;

    public static void Validate(ExportArtifact artifact)
    {
        if (!Enum.IsDefined(artifact.Kind) || artifact.Bytes < 0 || artifact.RowCount < 0 || artifact.FileName.Length is < 1 or > 200
            || artifact.ContentHash.Length != 64 || !artifact.ContentHash.All(Uri.IsHexDigit)
            || !artifact.BlobName.StartsWith($"{artifact.WorkspaceId:N}/{artifact.ExportJobId:N}/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid export artifact metadata.");
        }
    }

    public static void Validate(ExportJob export)
    {
        if (!Enum.IsDefined(export.Side) || export.SelectionHash.Length != 64 || export.ObjectCount < 0 || export.ExceptionCount < 0
            || export.MappingCount < 0 || export.CoverageGaps < 0)
        {
            throw new ArgumentException("Invalid export.");
        }
    }
}

/// <summary>
/// Spreadsheet formula-injection neutralization. A text that a spreadsheet would evaluate (leading =, +, -, @, tab or carriage return, including
/// their full-width forms) is kept verbatim in workbooks with an explicit text prefix, and prefixed with an apostrophe in CSV files.
/// </summary>
public static class SpreadsheetText
{
    public static bool IsFormulaLike(string? value) =>
        !string.IsNullOrEmpty(value) && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\uFF1D' or '\uFF0B' or '\uFF0D' or '\uFF20';

    public static string Neutralize(string value) => IsFormulaLike(value) ? "'" + value : value;

    /// <summary>One CSV field for a semicolon-separated file: neutralized, then quoted when needed.</summary>
    public static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) { return ""; }
        var text = Neutralize(value);
        return text.AsSpan().IndexOfAny(";\"\r\n") >= 0 || text != value || text.Trim() != text
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }

    public static string CsvLine(IEnumerable<string?> values) => string.Join(';', values.Select(Csv)) + "\r\n";
}

public interface IExportStore
{
    bool IsConfigured { get; }
    Task<StoredArtifact> SaveAsync(Guid workspaceId, Guid exportId, string extension, string contentType, Stream source, CancellationToken cancellationToken);
    Task<Stream> OpenAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken);
    Task DeleteAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken);

    /// <summary>Deletes every file stored under the export's prefix except the listed blob names and returns how many were removed.</summary>
    Task<int> PurgeExportAsync(Guid workspaceId, Guid exportId, IReadOnlySet<string> keep, CancellationToken cancellationToken);
    IAsyncEnumerable<StoredFile> ListAsync(Guid workspaceId, CancellationToken cancellationToken);
}

/// <summary>Localized business labels of generated files; a missing key falls back to the key itself.</summary>
public interface IExportText
{
    string this[string key] { get; }
}
