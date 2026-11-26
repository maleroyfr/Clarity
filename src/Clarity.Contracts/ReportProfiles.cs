using System.Text.Json;
using Json.Schema;

namespace Clarity.Contracts;

public sealed record ReportFormat(string Kind, string Encoding, string Delimiter, string Extension, string DateFormat);
public sealed record ReportIdentity(string Column, string Namespace, string Format);
public sealed record ReportAlternativeKey(string Column, string Namespace);
public sealed record ReportObject(string Type, IReadOnlyList<ReportIdentity> Identities, IReadOnlyList<ReportAlternativeKey> AlternativeKeys);
public sealed record ReportColumn(string Header, string? Field, string Type, string? Unit, bool Required, bool Concealable = false);
public sealed record ReportEndpoint(string Type, string Namespace);
public sealed record ReportRelation(string Type, string Column, string Direction, ReportEndpoint Endpoint);
public sealed record ReportVariant(string Code, IReadOnlyList<string> Headers, string Description);
public sealed record ReportLimit(string Code, string Description);
public sealed record ReportCoverage(bool TenantExhaustive, string Statement, IReadOnlyList<ReportLimit> Limits);
public sealed record ReportSource(string Name, IReadOnlyList<string> Endpoints, IReadOnlyList<string> Cmdlets,
    IReadOnlyList<string> Permissions, IReadOnlyList<string> Roles, IReadOnlyList<string> Documentation);

/// <summary>A repository-versioned description of one administrative report layout and its canonical mapping.</summary>
public sealed record ReportProfile(
    string SchemaVersion, string Id, string Version, string Name, string Readiness, string ProducerId, string CapabilityId,
    string EnvironmentKind, string ScopeKind, string RefreshDateColumn, string PeriodColumn, ReportFormat Format,
    ReportObject Object, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportRelation> Relations,
    IReadOnlyList<ReportVariant> UnsupportedVariants, ReportCoverage Coverage, ReportSource Source)
{
    public ReportColumn Column(string header) =>
        Columns.Single(x => string.Equals(x.Header, header, StringComparison.OrdinalIgnoreCase));

    /// <summary>Catalog attribute type used for a profile column type.</summary>
    public static string AttributeType(string columnType) => columnType switch
    {
        "date" => "date",
        "count" or "days" => "integer",
        "bytes" => "bytes",
        "boolean" => "boolean",
        "string" or "upn" or "guid" or "url" => "string",
        _ => throw new InvalidOperationException("Unknown report column type.")
    };
}

/// <summary>Validated set of supported report profiles, cross-checked against the Discovery catalog.</summary>
public sealed class ReportProfiles
{
    private const string ResourcePrefix = "Clarity.Contracts.ReportProfiles.";
    private static readonly Lazy<ReportProfiles> DefaultProfiles = new(() => Load(DiscoveryCatalog.Load()));

    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        using var stream = typeof(ReportProfiles).Assembly
            .GetManifestResourceStream(ResourcePrefix + "report-profile.schema.json")!;
        using var document = JsonDocument.Parse(stream);
        return JsonSchema.Build(document.RootElement.Clone(),
            new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = new() });
    });

    public static ReportProfiles Default => DefaultProfiles.Value;

    public IReadOnlyList<ReportProfile> All { get; }

    /// <summary>Profiles a consultant may select; planned profiles are never offered.</summary>
    public IReadOnlyList<ReportProfile> Supported => All.Where(x => x.Readiness == "SyntheticValidated").ToArray();

    public ReportProfiles(DiscoveryCatalog catalog, IEnumerable<ReportProfile> profiles)
    {
        All = profiles.OrderBy(x => x.Id, StringComparer.Ordinal).ThenBy(x => x.Version, StringComparer.Ordinal).ToArray();
        var keys = new HashSet<(string, string)>();
        var producers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in All)
        {
            if (!Schema.Value.Evaluate(JsonSerializer.SerializeToElement(profile, ContractJson.Options)).IsValid)
            {
                throw new InvalidOperationException($"Invalid report profile structure: {profile.Id}.");
            }
            if (!keys.Add((profile.Id, profile.Version)) || !producers.Add(profile.ProducerId))
            {
                throw new InvalidOperationException($"Duplicate report profile or producer: {profile.Id}.");
            }
            Validate(catalog, profile);
        }
    }

    public static ReportProfiles Load(DiscoveryCatalog catalog)
    {
        var assembly = typeof(ReportProfiles).Assembly;
        return new ReportProfiles(catalog, assembly.GetManifestResourceNames()
            .Where(x => x.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && x.EndsWith(".json", StringComparison.Ordinal) && !x.EndsWith(".schema.json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                return JsonSerializer.Deserialize<ReportProfile>(stream, ContractJson.Options)
                    ?? throw new InvalidOperationException("Empty report profile.");
            }));
    }

    /// <summary>Returns a supported profile or throws an explicit, consultant-safe error for unknown IDs or versions.</summary>
    public ReportProfile Require(string id, string version) =>
        Supported.SingleOrDefault(x => x.Id == id && x.Version == version)
        ?? throw new ContractException("ReportProfileUnavailable", "profile");

    public ReportProfile? ForProducer(string producerId, string version) =>
        Supported.SingleOrDefault(x => x.ProducerId == producerId && x.Version == version);

    /// <summary>
    /// Rejects a consultant-chosen profile when the file carries every required column of a more specific supported profile
    /// for the same environment kind (for example a SharePoint site-usage report submitted under the OneDrive profile), so a
    /// wrong selection cannot silently convert sites into another object type.
    /// </summary>
    public void RequireBestMatch(ReportProfile profile, IReadOnlyList<string> headers)
    {
        var present = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var required = profile.Columns.Count(x => x.Required);
        var better = Supported.Where(x => x.EnvironmentKind == profile.EnvironmentKind && x.Id != profile.Id
                && x.Columns.Count(c => c.Required) > required && x.Columns.Where(c => c.Required).All(c => present.Contains(c.Header)))
            .Select(x => x.Id).Order(StringComparer.Ordinal).ToArray();
        if (better.Length != 0) { throw new ContractException("ReportProfileMismatch", CsvReportReader.FileName, 1, better); }
    }

    private static void Validate(DiscoveryCatalog catalog, ReportProfile profile)
    {
        void Require(bool condition, string reason)
        {
            if (!condition) { throw new InvalidOperationException($"Invalid report profile {profile.Id}: {reason}."); }
        }

        Require(catalog.Producers.TryGetValue(profile.ProducerId, out var producer), "unknown producer");
        Require(producer!.Channel == "Import" && producer.Version == profile.Version && producer.Readiness == profile.Readiness
            && !producer.Preview, "producer channel, version or readiness");
        var family = catalog.Families.Single(x => x.Producers.Contains(producer));
        Require(family.EnvironmentKind == profile.EnvironmentKind, "environment kind");
        Require(catalog.Sections.TryGetValue(profile.CapabilityId, out var section), "unknown section");
        Require(section!.ProducerIds.Contains(profile.ProducerId, StringComparer.Ordinal)
            && section.SupportedScopes.Contains(profile.ScopeKind, StringComparer.Ordinal), "section producer or scope");
        Require(catalog.Sections.Values.Count(x => x.ProducerIds.Contains(profile.ProducerId, StringComparer.Ordinal)) == 1,
            "producer must feed exactly one section");

        var headers = profile.Columns.Select(x => x.Header).ToArray();
        Require(headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() == headers.Length, "duplicate headers");
        var fields = profile.Columns.Where(x => x.Field is not null).ToArray();
        Require(fields.Select(x => x.Field).Distinct(StringComparer.Ordinal).Count() == fields.Length, "duplicate fields");
        Require(fields.Select(x => x.Field!).ToHashSet(StringComparer.Ordinal).SetEquals(section.Attributes.Keys), "fields differ from catalog");
        foreach (var column in fields)
        {
            var attribute = section.Attributes[column.Field!];
            Require(attribute.Type == ReportProfile.AttributeType(column.Type) && attribute.ReferenceProducer == profile.ProducerId,
                $"catalog type for {column.Field}");
            Require(column.Field!.EndsWith("Bytes", StringComparison.Ordinal) == (column.Type == "bytes"), $"byte naming for {column.Field}");
            Require(column.Type != "bytes" || column.Unit == "bytes", $"byte unit for {column.Field}");
        }
        Require(profile.Relations.Select(x => x.Type).ToHashSet(StringComparer.Ordinal).SetEquals(section.Relations)
            && profile.Relations.Count == section.Relations.Count, "relations differ from catalog");

        bool Has(string header, params string[] types) =>
            profile.Columns.Any(x => string.Equals(x.Header, header, StringComparison.OrdinalIgnoreCase) && types.Contains(x.Type));
        Require(Has(profile.RefreshDateColumn, "date") && profile.Column(profile.RefreshDateColumn).Required
            && profile.Column(profile.RefreshDateColumn).Field is not null, "refresh date column");
        Require(Has(profile.PeriodColumn, "days") && profile.Column(profile.PeriodColumn).Required, "period column");
        Require(profile.Object.Identities.All(x => Has(x.Column, x.Format) && !string.IsNullOrWhiteSpace(x.Namespace)), "identity columns");
        Require(profile.Object.Identities.Select(x => x.Column).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            == profile.Object.Identities.Count, "duplicate identity columns");
        Require(profile.Object.Identities.Any(x => profile.Column(x.Column).Required), "at least one required identity column");
        Require(profile.Object.Identities.All(x => x.Format != "guid" || !profile.Column(x.Column).Concealable), "GUID identities are not concealable");
        Require(profile.Object.AlternativeKeys.All(x => Has(x.Column, "url")), "alternative key columns");
        Require(profile.Relations.All(x => Has(x.Column, "upn")), "relation columns");
        Require(profile.UnsupportedVariants.SelectMany(x => x.Headers).All(x => !headers.Contains(x, StringComparer.OrdinalIgnoreCase)),
            "variant headers overlap supported headers");
        Require(!profile.Coverage.TenantExhaustive && profile.Coverage.Limits.Any(x => x.Code == "NotTenantExhaustive"),
            "coverage must not claim tenant exhaustiveness");
    }
}
