using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clarity.Contracts;

/// <summary>
/// Interpretation context bound by the caller: the authorized tenant and scope plus the raw-file hash.
/// <paramref name="ScopeObserved"/> is true only when the tenant came from an authenticated connection; consultant uploads
/// always pass false so the section records the scope as supplied rather than observed.
/// </summary>
public sealed record ReportContext(string TenantId, CollectionScope Scope, string ContentHash, bool ScopeObserved = false,
    string SourceId = ReportAdapter.UploadSourceId);

/// <summary>A sanitized row problem: physical line, profile column header and a stable code; never the cell value.</summary>
public sealed record ReportRowIssue(int Line, string? Column, string Code);

public sealed record ReportDiagnostics(
    string ProfileId, string ProfileVersion,
    IReadOnlyList<string> RecognizedColumns, IReadOnlyList<string> UnrecognizedColumns, IReadOnlyList<string> MissingOptionalColumns,
    int DataRows, int UsableRows, int RejectedRows, int ConcealedRows, int PartiallyConcealedRows,
    IReadOnlyList<ReportRowIssue> Issues, bool IssuesTruncated,
    DateOnly RefreshDate, int? PeriodDays, IReadOnlyList<string> UpnDomains, IReadOnlyList<string> SiteRoots,
    string IdentityColumn, string IdentityNamespace, IReadOnlyList<string> CoverageLimits);

public sealed record ReportConversion(DiscoveryPackage Package, ReportDiagnostics Diagnostics);

/// <summary>
/// Converts a profiled administrative report into a canonical Discovery package. The same entry point serves consultant
/// uploads and later connector downloads of the same report, so CSV parsing is never duplicated.
/// </summary>
public static class ReportAdapter
{
    public const string Version = "1.0.0";
    public const string UploadSourceId = "Microsoft 365 usage report CSV (consultant upload)";
    private const int MaxIssues = 50;
    private static readonly JsonElement Null = JsonDocument.Parse("null").RootElement.Clone();

    public static ReportConversion Convert(ReadOnlySpan<byte> content, ReportProfile profile, ReportContext context, ReportLimits limits) =>
        Convert(CsvReportReader.Read(content, limits), profile, context);

    public static ReportConversion Convert(CsvTable table, ReportProfile profile, ReportContext context)
    {
        if (context.Scope.Kind != profile.ScopeKind || !Guid.TryParseExact(context.TenantId, "D", out _)
            || string.IsNullOrWhiteSpace(context.ContentHash))
        {
            throw new ContractException("ReportScopeMismatch", "profile");
        }
        var file = CsvReportReader.FileName;
        var index = table.Headers.Select((header, position) => (header, position))
            .ToDictionary(x => x.header, x => x.position, StringComparer.OrdinalIgnoreCase);
        var variant = profile.UnsupportedVariants.FirstOrDefault(v => v.Headers.Any(index.ContainsKey));
        if (variant is not null) { throw new ContractException("ReportUnsupportedVariant", file, 1, [variant.Code]); }
        var missing = profile.Columns.Where(c => c.Required && !index.ContainsKey(c.Header)).Select(c => c.Header).ToArray();
        if (missing.Length != 0) { throw new ContractException("ReportMissingColumns", file, 1, missing.Take(20).ToArray()); }
        if (table.Rows.Count == 0) { throw new ContractException("ReportHasNoRows", file, 1); }

        var columns = profile.Columns.Where(c => index.ContainsKey(c.Header)).ToArray();
        var identity = profile.Object.Identities.First(i => index.ContainsKey(i.Column));
        var refresh = profile.Column(profile.RefreshDateColumn);
        var issues = new List<ReportRowIssue>();
        var rejected = new HashSet<int>();
        var refreshDates = new SortedSet<DateOnly>();
        var parsed = new List<Row>();
        var domains = new SortedSet<string>(StringComparer.Ordinal);
        var roots = new SortedSet<string>(StringComparer.Ordinal);
        var concealedRows = 0;

        foreach (var row in table.Rows)
        {
            if (row.Fields.Count != table.Headers.Count) { Reject(row.Line, null, "FieldCount"); continue; }
            Evidence(row);
            var refreshCell = row.Fields[index[refresh.Header]].Trim();
            if (refreshCell.Length == 0) { Reject(row.Line, refresh.Header, "MissingValue"); continue; }
            if (!TryDate(refreshCell)) { Reject(row.Line, refresh.Header, "InvalidDate"); continue; }
            refreshDates.Add(DateOnly.ParseExact(refreshCell, "yyyy-MM-dd", CultureInfo.InvariantCulture));
            var current = new Row(row.Line);
            string? failure = null;
            ReportColumn? failed = null;
            foreach (var column in columns)
            {
                var raw = row.Fields[index[column.Header]];
                var cell = raw.Trim();
                if (column.Concealable && IsConcealed(cell)) { current.Concealed.Add(column.Header); continue; }
                var code = Parse(column, raw, cell, out var value, out var normalized);
                if (code is not null) { failure = code; failed = column; break; }
                current.Values[column.Header] = (value, normalized);
            }
            if (failure is not null) { Reject(row.Line, failed!.Header, failure); continue; }
            if (current.Concealed.Contains(identity.Column)) { concealedRows++; continue; }
            var (_, key) = current.Values[identity.Column];
            if (key is null) { Reject(row.Line, identity.Column, "MissingIdentifier"); continue; }
            if (key.Length > 500) { Reject(row.Line, identity.Column, "InvalidIdentifier"); continue; }
            current.ExternalId = key;
            parsed.Add(current);
        }

        if (refreshDates.Count == 0)
        {
            throw new ContractException("ReportNoUsableRows", file, null,
                issues.OrderBy(x => x.Line).Take(10).Select(x => $"{x.Line}|{x.Column}|{x.Code}").ToArray());
        }
        if (refreshDates.Count > 1)
        {
            throw new ContractException("ReportMixedRefreshDates", file, null,
                refreshDates.Take(10).Select(x => x.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray());
        }
        if (roots.Count > 1) { throw new ContractException("ReportEnvironmentConflict", file, null, roots.Take(10).ToArray()); }

        var duplicates = parsed.GroupBy(x => x.ExternalId!, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var row in parsed.Where(x => duplicates.Contains(x.ExternalId!)))
        {
            Reject(row.Line, identity.Column, "DuplicateIdentifier");
        }
        parsed.RemoveAll(x => duplicates.Contains(x.ExternalId!));

        var periodColumn = profile.Column(profile.PeriodColumn);
        var periods = parsed.Select(x => x.Values[periodColumn.Header].Value).Where(x => x is not null)
            .Select(x => System.Convert.ToInt32(x, CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
        if (periods.Length > 1)
        {
            throw new ContractException("ReportMixedPeriods", file, null,
                periods.Take(10).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        var refreshDate = refreshDates.Single();
        var observedAt = new DateTimeOffset(refreshDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var tenant = context.TenantId.ToLowerInvariant();
        var sectionId = $"{profile.CapabilityId}:{tenant}";
        var objects = new List<ObjectRecord>();
        var relationships = new List<RelationshipRecord>();
        var partial = 0;
        foreach (var row in parsed)
        {
            var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var column in columns.Where(c => c.Field is not null && row.Values.ContainsKey(c.Header)))
            {
                attributes[column.Field!] = row.Values[column.Header].Value is { } value ? JsonSerializer.SerializeToElement(value) : Null;
            }
            var alternatives = profile.Object.AlternativeKeys
                .Where(k => row.Values.TryGetValue(k.Column, out var v) && v.Normalized is not null)
                .Select(k => new ExternalReference(profile.Object.Type, k.Namespace, row.Values[k.Column].Normalized!))
                .Distinct().ToArray();
            var self = new ExternalReference(profile.Object.Type, identity.Namespace, row.ExternalId!);
            objects.Add(new ObjectRecord(sectionId, profile.Object.Type, identity.Namespace, row.ExternalId!, observedAt, attributes,
                alternatives.Length == 0 ? null : alternatives));
            foreach (var relation in profile.Relations)
            {
                if (!row.Values.TryGetValue(relation.Column, out var owner) || owner.Value is not string principal) { continue; }
                var endpoint = new ExternalReference(relation.Endpoint.Type, relation.Endpoint.Namespace, principal);
                var (from, to) = relation.Direction == "objectToEndpoint" ? (self, endpoint) : (endpoint, self);
                relationships.Add(new RelationshipRecord(sectionId, relation.Type, from, to, observedAt));
            }
            if (row.Concealed.Count != 0) { partial++; }
        }

        var missingOptional = profile.Columns.Where(c => !c.Required && c.Field is not null && !index.ContainsKey(c.Header))
            .Select(c => c.Header).ToArray();
        var unrecognized = table.Headers.Where(h => !profile.Columns.Any(c => string.Equals(c.Header, h, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var warnings = new List<SectionIssue>();
        var errors = new List<SectionIssue>();
        if (!context.ScopeObserved)
        {
            warnings.Add(new("ScopeSuppliedByConsultant",
                "The tenant scope was supplied and attested by the consultant; the report file does not identify its tenant."));
        }
        warnings.Add(new("ReportCoverageLimited", Limit(
            $"Usage report snapshot interpreted with profile {profile.Id} {profile.Version}; it is not a tenant-wide exhaustive inventory and not Graph connection evidence.")));
        if (concealedRows != 0)
        {
            warnings.Add(new("ConcealedIdentifiers", $"{concealedRows} rows have concealed identifiers and were not converted."));
        }
        if (partial != 0) { warnings.Add(new("ConcealedFields", $"{partial} rows have concealed names or URLs that were omitted.")); }
        if (missingOptional.Length != 0)
        {
            warnings.Add(new("OptionalColumnsMissing", Limit($"Optional columns absent from the file: {string.Join(", ", missingOptional)}.")));
        }
        if (unrecognized.Length != 0) { warnings.Add(new("UnrecognizedColumns", $"{unrecognized.Length} unrecognized columns were ignored.")); }
        if (rejected.Count != 0)
        {
            errors.Add(new("RejectedRows", $"{rejected.Count} of {table.Rows.Count} data rows were rejected by the report profile."));
        }
        CoverageStatus status;
        if (objects.Count == 0)
        {
            status = CoverageStatus.Failed;
            errors.Add(concealedRows != 0 && rejected.Count == 0
                ? new("AllIdentifiersConcealed", "Every row has a concealed identifier; upload a report exported without concealed names.")
                : new("NoUsableRows", "No data row could be converted."));
        }
        else
        {
            status = rejected.Count != 0 || concealedRows != 0 || partial != 0 ? CoverageStatus.Partial : CoverageStatus.Complete;
        }
        var result = new SectionResult(sectionId, profile.CapabilityId, status, context.Scope,
            columns.Where(c => c.Field is not null).Select(c => c.Field!).ToArray(),
            profile.Relations.Where(r => index.ContainsKey(r.Column)).Select(r => r.Type).ToArray(),
            status == CoverageStatus.Failed ? null : objects.Count,
            status == CoverageStatus.Failed ? null : relationships.Count,
            warnings, errors);
        var scopeSource = context.ScopeObserved ? "observed" : "consultantSupplied";
        var manifest = new PackageManifest("1.0",
            RunId(profile, context, scopeSource), profile.ProducerId, profile.Version, profile.EnvironmentKind, context.TenantId,
            context.Scope, observedAt, observedAt,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["reportProfile"] = $"{profile.Id}@{profile.Version}",
                ["reportAdapter"] = Version,
                ["scopeSource"] = scopeSource
            },
            context.SourceId);
        var package = new DiscoveryPackage(manifest, status == CoverageStatus.Failed ? [] : objects,
            status == CoverageStatus.Failed ? [] : relationships, [result]);
        new PackageValidator().Validate(package);

        var ordered = issues.OrderBy(x => x.Line).ThenBy(x => x.Column, StringComparer.Ordinal).ToArray();
        var diagnostics = new ReportDiagnostics(profile.Id, profile.Version,
            columns.Select(c => c.Header).ToArray(), unrecognized.Take(50).Select(CsvReportReader.Truncate).ToArray(), missingOptional,
            table.Rows.Count, objects.Count, rejected.Count, concealedRows, partial,
            ordered.Take(MaxIssues).ToArray(), ordered.Length > MaxIssues,
            refreshDate, periods.Length == 1 ? periods[0] : null, domains.ToArray(), roots.ToArray(),
            identity.Column, identity.Namespace, profile.Coverage.Limits.Select(x => x.Code).ToArray());
        return new ReportConversion(package, diagnostics);

        void Reject(int line, string? column, string code)
        {
            rejected.Add(line);
            issues.Add(new ReportRowIssue(line, column, code));
        }

        void Evidence(CsvRow row)
        {
            foreach (var column in columns.Where(c => c.Type is "upn" or "url"))
            {
                var cell = row.Fields[index[column.Header]].Trim();
                if (column.Type == "upn" && IsUpn(cell)) { domains.Add(cell[(cell.LastIndexOf('@') + 1)..].ToLowerInvariant()); }
                if (column.Type == "url" && TryUrl(cell, out var uri, out _)) { roots.Add(Root(uri!.Host)); }
            }
        }
    }

    /// <summary>SharePoint root host of a site or OneDrive URL, without the OneDrive "-my" suffix.</summary>
    public static string Root(string host)
    {
        var lower = host.ToLowerInvariant();
        var dot = lower.IndexOf('.', StringComparison.Ordinal);
        return dot > 3 && lower[..dot].EndsWith("-my", StringComparison.Ordinal) ? lower[..(dot - 3)] + lower[dot..] : lower;
    }

    private static string? Parse(ReportColumn column, string raw, string cell, out object? value, out string? normalized)
    {
        value = null;
        normalized = null;
        if (cell.Length == 0) { return null; }
        switch (column.Type)
        {
            case "date":
                if (!TryDate(cell)) { return "InvalidDate"; }
                value = cell;
                return null;
            case "boolean":
                if (string.Equals(cell, "true", StringComparison.OrdinalIgnoreCase)) { value = true; return null; }
                if (string.Equals(cell, "false", StringComparison.OrdinalIgnoreCase)) { value = false; return null; }
                return "InvalidBoolean";
            case "bytes" or "count":
                if (!long.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    return column.Type == "bytes" ? "InvalidBytes" : "InvalidCount";
                }
                value = number;
                return null;
            case "days":
                if (!int.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out var days)) { return "InvalidPeriod"; }
                value = days;
                return null;
            case "string":
                if (raw.Length > 500) { return "ValueTooLong"; }
                value = raw;
                return null;
            case "upn":
                if (!IsUpn(cell)) { return "InvalidUpn"; }
                value = cell;
                normalized = cell.ToLowerInvariant();
                return null;
            case "guid":
                if (!Guid.TryParseExact(cell, "D", out var guid)) { return "InvalidIdentifier"; }
                normalized = guid.ToString("D");
                return null;
            case "url":
                if (!TryUrl(cell, out _, out var url)) { return "InvalidUrl"; }
                value = cell;
                normalized = url;
                return null;
            default:
                throw new InvalidOperationException("Unknown report column type.");
        }
    }

    private static bool TryDate(string cell) =>
        DateOnly.TryParseExact(cell, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>Microsoft concealed report names are 32 hexadecimal characters (an MD5-style hash).</summary>
    public static bool IsConcealed(string cell) => cell.Length == 32 && cell.All(char.IsAsciiHexDigit);

    private static bool IsUpn(string cell)
    {
        var at = cell.LastIndexOf('@');
        if (at <= 0 || at == cell.Length - 1 || cell.Length > 500 || cell.Any(char.IsWhiteSpace)) { return false; }
        var domain = cell[(at + 1)..];
        return domain.Contains('.', StringComparison.Ordinal) && !domain.StartsWith('.') && !domain.EndsWith('.');
    }

    private static bool TryUrl(string cell, out Uri? uri, out string? normalized)
    {
        normalized = null;
        if (cell.Length > 500 || cell.Any(char.IsWhiteSpace) || !Uri.TryCreate(cell, UriKind.Absolute, out uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !uri.Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase))
        {
            uri = null;
            return false;
        }
        var authority = cell.IndexOf("://", StringComparison.Ordinal) + 3;
        var slash = cell.IndexOf('/', authority);
        var path = slash < 0 ? "" : cell[slash..];
        normalized = ("https://" + uri.Host.ToLowerInvariant() + path).TrimEnd('/');
        return true;
    }

    private static string Limit(string message) => message.Length <= 500 ? message : message[..500];

    private static Guid RunId(ReportProfile profile, ReportContext context, string scopeSource)
    {
        var seed = string.Join('|', "clarity.report", profile.Id, profile.Version, context.TenantId.ToLowerInvariant(),
            context.Scope.Kind, context.Scope.Value, context.ContentHash, scopeSource, context.SourceId);
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(seed)).AsSpan(0, 16), bigEndian: true);
    }

    private sealed class Row(int line)
    {
        public int Line { get; } = line;
        public Dictionary<string, (object? Value, string? Normalized)> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Concealed { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? ExternalId { get; set; }
    }
}
