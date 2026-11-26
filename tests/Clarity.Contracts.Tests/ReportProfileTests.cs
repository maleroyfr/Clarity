using System.Text;
using System.Text.Json;
using Clarity.Contracts;
using Shouldly;

namespace Clarity.Contracts.Tests;

public sealed class ReportProfileTests
{
    private const string Tenant = "00000000-0000-0000-0000-0000000000aa";
    private const string Mailbox = "m365.mailboxUsageDetail";
    private const string OneDrive = "m365.oneDriveUsageAccountDetail";
    private const string SharePoint = "m365.sharePointSiteUsageDetail";
    private static readonly ReportProfiles Profiles = ReportProfiles.Default;
    private static readonly ReportContext Context = new(Tenant, new CollectionScope("tenant", Tenant), "hash-a");

    [Fact]
    public void OffersOnlyValidatedVersionedProfilesAndRejectsUnknownOrDishonestDefinitions()
    {
        Profiles.Supported.Select(x => $"{x.Id}@{x.Version}").ShouldBe(
            [$"{Mailbox}@1.0.0", $"{OneDrive}@1.0.0", $"{SharePoint}@1.0.0"]);
        Profiles.Supported.ShouldAllBe(x => !x.Coverage.TenantExhaustive && x.Format.Kind == "csv" && x.Format.DateFormat == "yyyy-MM-dd");
        Should.Throw<ContractException>(() => Profiles.Require(Mailbox, "2.0.0")).Code.ShouldBe("ReportProfileUnavailable");
        Should.Throw<ContractException>(() => Profiles.Require("quest.mailbox", "1.0.0")).Code.ShouldBe("ReportProfileUnavailable");
        Profiles.ForProducer("clarity.graph.entra", "1.0.0").ShouldBeNull();

        var catalog = DiscoveryCatalog.Load();
        var profile = Profiles.Require(Mailbox, "1.0.0");
        Should.Throw<InvalidOperationException>(() => new ReportProfiles(catalog,
            [profile with { Coverage = profile.Coverage with { TenantExhaustive = true } }]));
        Should.Throw<InvalidOperationException>(() => new ReportProfiles(catalog,
            [profile with { Columns = [.. profile.Columns.Select(c => c.Type == "bytes" ? c with { Unit = "megabytes" } : c)] }]));
        Should.Throw<InvalidOperationException>(() => new ReportProfiles(catalog, [profile with { Version = "1.1.0" }]));
        Should.Throw<InvalidOperationException>(() => new ReportProfiles(catalog, [profile, profile]));
    }

    [Fact]
    public void ReaderHandlesBomQuotesAndEmbeddedLineBreaksAndRejectsMalformedInput()
    {
        var table = CsvReportReader.Read([0xEF, 0xBB, 0xBF, .. "b,a\r\n\"x, \"\"y\"\"\",\"line\r\nbreak\"\r\n\r\nz,\"\"\r\n"u8], ReportLimits.Default);
        table.Headers.ShouldBe(["b", "a"]);
        table.Rows.Count.ShouldBe(2);
        table.Rows[0].Fields.ShouldBe(["x, \"y\"", "line\r\nbreak"]);
        table.Rows[1].Line.ShouldBe(5);
        table.Rows[1].Fields.ShouldBe(["z", ""]);

        Code(() => CsvReportReader.Read("a,b\nx\"y,z\n"u8, ReportLimits.Default)).ShouldBe("ReportMalformedCsv");
        Code(() => CsvReportReader.Read("a,b\n\"x\"y,z\n"u8, ReportLimits.Default)).ShouldBe("ReportMalformedCsv");
        Code(() => CsvReportReader.Read("a,b\n\"open,z\n"u8, ReportLimits.Default)).ShouldBe("ReportMalformedCsv");
        Code(() => CsvReportReader.Read([0xFF, 0xFE, (byte)'a', 0], ReportLimits.Default)).ShouldBe("ReportInvalidEncoding");
        Code(() => CsvReportReader.Read([(byte)'a', 0xC3, 0x28], ReportLimits.Default)).ShouldBe("ReportInvalidEncoding");
        Code(() => CsvReportReader.Read("A,a\n1,2\n"u8, ReportLimits.Default)).ShouldBe("ReportDuplicateColumns");
        Code(() => CsvReportReader.Read("a,,b\n"u8, ReportLimits.Default)).ShouldBe("ReportInvalidHeader");
        var tight = new ReportLimits(64, 2, 3, 5);
        Code(() => CsvReportReader.Read(new byte[65], tight)).ShouldBe("ReportFileTooLarge");
        Code(() => CsvReportReader.Read("a\n1\n2\n3\n"u8, tight)).ShouldBe("ReportRowLimit");
        Code(() => CsvReportReader.Read("a,b,c,d\n"u8, tight)).ShouldBe("ReportTooManyFields");
        Code(() => CsvReportReader.Read("a\n123456\n"u8, tight)).ShouldBe("ReportFieldTooLong");
    }

    [Fact]
    public void MailboxFixtureKeepsMeasuredZeroBlankUnknownInvalidAndConcealedRowsDistinct()
    {
        var bytes = Example(Mailbox);
        bytes.AsSpan(0, 3).ToArray().ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        var conversion = Convert(bytes, Mailbox);
        var report = conversion.Diagnostics;
        (report.DataRows, report.UsableRows, report.RejectedRows, report.ConcealedRows, report.PartiallyConcealedRows).ShouldBe((6, 4, 1, 1, 0));
        report.Issues.ShouldBe([new ReportRowIssue(5, "Storage Used (Byte)", "InvalidBytes")]);
        report.RefreshDate.ShouldBe(new DateOnly(2026, 5, 31));
        report.PeriodDays.ShouldBe(7);
        report.UpnDomains.ShouldBe(["contoso.example"]);
        report.IdentityNamespace.ShouldBe("m365report.upn");
        report.UnrecognizedColumns.ShouldBeEmpty();
        report.MissingOptionalColumns.ShouldBeEmpty();
        report.CoverageLimits.ShouldContain("NotTenantExhaustive");

        var package = conversion.Package;
        var section = package.Results.Single();
        section.Status.ShouldBe(CoverageStatus.Partial);
        section.SectionId.ShouldBe($"exchange.mailboxUsage:{Tenant}");
        section.Warnings.Select(x => x.Code).ShouldBe(["ScopeSuppliedByConsultant", "ReportCoverageLimited", "ConcealedIdentifiers"]);
        section.Errors.Select(x => x.Code).ShouldBe(["RejectedRows"]);
        package.Manifest.StartedAtUtc.ShouldBe(new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero));
        package.Manifest.ModuleVersions["reportProfile"].ShouldBe($"{Mailbox}@1.0.0");
        package.Manifest.ModuleVersions["reportAdapter"].ShouldBe(ReportAdapter.Version);
        package.Manifest.ModuleVersions["scopeSource"].ShouldBe("consultantSupplied");
        package.Manifest.SourceId.ShouldBe(ReportAdapter.UploadSourceId);
        package.Objects.Select(x => x.ExternalId).ShouldBe(
            ["adele.vance@contoso.example", "alex.wilber@contoso.example", "megan.bowen@contoso.example", "former.employee@contoso.example"]);
        package.Objects.ShouldAllBe(x => x.Type == "mailbox" && x.Namespace == "m365report.upn"
            && x.ObservedAtUtc == package.Manifest.StartedAtUtc);
        package.Objects.ShouldNotContain(x => x.ExternalId.Contains("lee.gu", StringComparison.Ordinal));

        var adele = Attributes(package, "adele.vance@contoso.example");
        adele["storageUsedBytes"].GetInt64().ShouldBe(2147483648);
        adele["prohibitSendReceiveQuotaBytes"].GetInt64().ShouldBe(107374182400);
        adele["hasArchive"].GetBoolean().ShouldBeTrue();
        adele["deletedDate"].ValueKind.ShouldBe(JsonValueKind.Null);
        adele["reportRefreshDate"].GetString().ShouldBe("2026-05-31");
        var alex = Attributes(package, "alex.wilber@contoso.example");
        alex["storageUsedBytes"].GetInt64().ShouldBe(0);
        alex["itemCount"].GetInt64().ShouldBe(0);
        alex["lastActivityDate"].ValueKind.ShouldBe(JsonValueKind.Null);
        alex["displayName"].GetString().ShouldBe("Wilber, Alex");
        var megan = Attributes(package, "megan.bowen@contoso.example");
        megan["storageUsedBytes"].ValueKind.ShouldBe(JsonValueKind.Null);
        megan["hasArchive"].ValueKind.ShouldBe(JsonValueKind.Null);
        megan["displayName"].GetString().ShouldBe("Megan \"MB\" Bowen");
        Attributes(package, "former.employee@contoso.example")["isDeleted"].GetBoolean().ShouldBeTrue();
        Attributes(package, "former.employee@contoso.example").ContainsKey("deleted").ShouldBeFalse();

        package.Relationships.Count.ShouldBe(4);
        package.Relationships.ShouldAllBe(x => x.RelationType == "mailboxOf" && x.From.Type == "mailbox" && x.To.Type == "user"
            && x.To.Namespace == "upn");
        Convert(bytes, Mailbox).Package.Manifest.RunId.ShouldBe(package.Manifest.RunId);
        ReportAdapter.Convert(bytes, Profile(Mailbox), Context with { ContentHash = "hash-b" }, ReportLimits.Default)
            .Package.Manifest.RunId.ShouldNotBe(package.Manifest.RunId);
    }

    [Fact]
    public void OneDriveFixturePrefersSiteIdAndFallsBackToAProvisionalUrlKey()
    {
        var conversion = Convert(Example(OneDrive), OneDrive);
        conversion.Package.Results.Single().Status.ShouldBe(CoverageStatus.Complete);
        conversion.Diagnostics.SiteRoots.ShouldBe(["contoso.sharepoint.com"]);
        conversion.Diagnostics.IdentityNamespace.ShouldBe("sharepoint.siteId");
        var drive = conversion.Package.Objects[0];
        (drive.Type, drive.Namespace, drive.ExternalId).ShouldBe(("oneDrive", "sharepoint.siteId", "6f1c2a10-1111-4a8b-9c11-000000000001"));
        drive.AlternativeKeys!.Single().ShouldBe(new ExternalReference("oneDrive", "url",
            "https://contoso-my.sharepoint.com/personal/adele_vance_contoso_example"));
        drive.Attributes.ContainsKey("siteId").ShouldBeFalse();
        var alex = conversion.Package.Objects[1].Attributes;
        alex["storageUsedBytes"].GetInt64().ShouldBe(0);
        alex["lastActivityDate"].ValueKind.ShouldBe(JsonValueKind.Null);
        conversion.Package.Relationships[0].ShouldBe(new RelationshipRecord($"onedrive.accountUsage:{Tenant}", "ownerOf",
            new ExternalReference("user", "upn", "adele.vance@contoso.example"), new ExternalReference("oneDrive", "sharepoint.siteId",
                "6f1c2a10-1111-4a8b-9c11-000000000001"), drive.ObservedAtUtc));

        var withoutSiteId = Csv(Text(OneDrive), drop: "Site Id");
        var provisional = Convert(withoutSiteId, OneDrive);
        provisional.Diagnostics.IdentityNamespace.ShouldBe("m365report.siteUrl");
        provisional.Package.Objects[0].ExternalId.ShouldBe("https://contoso-my.sharepoint.com/personal/adele_vance_contoso_example");
        provisional.Package.Objects[0].AlternativeKeys!.Single().Namespace.ShouldBe("url");
    }

    [Fact]
    public void SharePointFixtureAcceptsReorderedHeadersAndReportsUnknownColumnsWithoutGuessingOwners()
    {
        var lines = Text(SharePoint).TrimEnd('\n').Split('\n').Select(x => x.Split(',')).ToArray();
        var reordered = string.Join('\n', lines.Select(x => string.Join(',', Enumerable.Reverse(x).Append(x == lines[0] ? "Synthetic Extra" : "\"ignored, value\"")))) + "\n";
        var conversion = Convert(Encoding.UTF8.GetBytes(reordered), SharePoint);
        var section = conversion.Package.Results.Single();
        section.Status.ShouldBe(CoverageStatus.Complete);
        section.Warnings.Select(x => x.Code).ShouldContain("UnrecognizedColumns");
        conversion.Diagnostics.UnrecognizedColumns.ShouldBe(["Synthetic Extra"]);
        conversion.Package.Objects.Select(x => x.ExternalId).ShouldBe(
            ["7a2d3b20-2222-4b9c-8d22-000000000001", "7a2d3b20-2222-4b9c-8d22-000000000002"]);
        var archive = conversion.Package.Objects[1].Attributes;
        archive["ownerPrincipalName"].ValueKind.ShouldBe(JsonValueKind.Null);
        archive["ownerDisplayName"].ValueKind.ShouldBe(JsonValueKind.Null);
        archive["storageUsedBytes"].GetInt64().ShouldBe(0);
        archive["rootWebTemplate"].GetString().ShouldBe("Team Site");
        conversion.Package.Relationships.Single().From.ExternalId.ShouldBe("megan.bowen@contoso.example");

        Code(() => Profiles.RequireBestMatch(Profile(OneDrive), CsvReportReader.Read(Example(SharePoint), ReportLimits.Default).Headers))
            .ShouldBe("ReportProfileMismatch");
        Profiles.RequireBestMatch(Profile(SharePoint), CsvReportReader.Read(Example(SharePoint), ReportLimits.Default).Headers);
        var missing = Should.Throw<ContractException>(() => Convert(Example(OneDrive), SharePoint));
        missing.Code.ShouldBe("ReportMissingColumns");
        missing.Details.ShouldBe(["Page View Count", "Visited Page Count", "Root Web Template"]);
    }

    [Fact]
    public void FileLevelDefectsAreExplicitErrorsInsteadOfEmptySuccess()
    {
        var mailbox = Text(Mailbox).TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal);
        var header = mailbox[..mailbox.IndexOf('\n', StringComparison.Ordinal)];
        var adele = mailbox.Split('\n')[1];
        Code(() => Convert(Encoding.UTF8.GetBytes(header + "\n"), Mailbox)).ShouldBe("ReportHasNoRows");
        var missing = Should.Throw<ContractException>(() => Convert(Csv(mailbox, drop: "Storage Used (Byte)"), Mailbox));
        (missing.Code, missing.Details.Single()).ShouldBe(("ReportMissingColumns", "Storage Used (Byte)"));
        var variant = Should.Throw<ContractException>(() => Convert(Csv(Text(SharePoint), rename: ("Storage Used (Byte)", "Storage Used (MB)")), SharePoint));
        (variant.Code, variant.Details.Single()).ShouldBe(("ReportUnsupportedVariant", "MegabyteUnits"));
        var invalid = Should.Throw<ContractException>(() => Convert(Encoding.UTF8.GetBytes(
            $"{header}\n{adele.Replace("2026-05-31", "31/05/2026", StringComparison.Ordinal)}\n"), Mailbox));
        (invalid.Code, invalid.Details.Single()).ShouldBe(("ReportNoUsableRows", "2|Report Refresh Date|InvalidDate"));
        Code(() => Convert(Encoding.UTF8.GetBytes(
            $"{header}\n{adele}\n{adele.Replace("2026-05-31", "2026-05-30", StringComparison.Ordinal).Replace("adele", "other", StringComparison.Ordinal)}\n"),
            Mailbox)).ShouldBe("ReportMixedRefreshDates");
        Code(() => Convert(Encoding.UTF8.GetBytes(
            $"{header}\n{adele}\n{adele.Replace(",7", ",30", StringComparison.Ordinal).Replace("adele", "other", StringComparison.Ordinal)}\n"),
            Mailbox)).ShouldBe("ReportMixedPeriods");
        var sharepoint = Text(SharePoint);
        Code(() => Convert(Encoding.UTF8.GetBytes(sharepoint.Replace("https://contoso.sharepoint.com/sites/Archive",
            "https://fabrikam.sharepoint.com/sites/Archive", StringComparison.Ordinal)), SharePoint)).ShouldBe("ReportEnvironmentConflict");
        Code(() => ReportAdapter.Convert(Example(Mailbox), Profile(Mailbox), Context with { Scope = new CollectionScope("domain", Tenant) },
            ReportLimits.Default)).ShouldBe("ReportScopeMismatch");
    }

    [Fact]
    public void RowLevelDefectsRejectRowsWithoutLeakingValuesAndConcealedReportsFailHonestly()
    {
        var mailbox = Text(Mailbox).TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal);
        var rows = mailbox.TrimEnd('\n').Split('\n');
        var duplicate = string.Join('\n', rows[0], rows[1], rows[1].Replace("Adele Vance", "Adele Duplicate", StringComparison.Ordinal),
            rows[2], rows[3].Replace(",880,", ",-5,", StringComparison.Ordinal), rows[6].Replace(",True,", ",Yes,", StringComparison.Ordinal)) + "\n";
        var conversion = Convert(Encoding.UTF8.GetBytes(duplicate), Mailbox);
        conversion.Diagnostics.Issues.ShouldBe([
            new ReportRowIssue(2, "User Principal Name", "DuplicateIdentifier"),
            new ReportRowIssue(3, "User Principal Name", "DuplicateIdentifier"),
            new ReportRowIssue(5, "Item Count", "InvalidCount"),
            new ReportRowIssue(6, "Is Deleted", "InvalidBoolean")]);
        conversion.Package.Objects.Single().ExternalId.ShouldBe("alex.wilber@contoso.example");
        conversion.Package.Results.Single().Status.ShouldBe(CoverageStatus.Partial);
        JsonSerializer.Serialize(conversion.Diagnostics).ShouldNotContain("Adele");

        var concealed = string.Join('\n', rows[0], rows[5]) + "\n";
        var failed = Convert(Encoding.UTF8.GetBytes(concealed), Mailbox);
        failed.Package.Objects.ShouldBeEmpty();
        failed.Package.Relationships.ShouldBeEmpty();
        failed.Package.Results.Single().Status.ShouldBe(CoverageStatus.Failed);
        failed.Package.Results.Single().ObjectCount.ShouldBeNull();
        failed.Package.Results.Single().Errors.Single().Code.ShouldBe("AllIdentifiersConcealed");
        failed.Diagnostics.ConcealedRows.ShouldBe(1);

        var site = Text(SharePoint).Replace("https://contoso.sharepoint.com/sites/Finance", "0123456789abcdef0123456789abcdef", StringComparison.Ordinal)
            .Replace("megan.bowen@contoso.example", "FEDCBA9876543210FEDCBA9876543210", StringComparison.Ordinal);
        var partial = Convert(Encoding.UTF8.GetBytes(site), SharePoint);
        partial.Diagnostics.PartiallyConcealedRows.ShouldBe(1);
        partial.Package.Results.Single().Status.ShouldBe(CoverageStatus.Partial);
        partial.Package.Results.Single().Warnings.Select(x => x.Code).ShouldContain("ConcealedFields");
        var finance = partial.Package.Objects[0];
        finance.Attributes.ContainsKey("siteUrl").ShouldBeFalse();
        finance.Attributes.ContainsKey("ownerPrincipalName").ShouldBeFalse();
        finance.AlternativeKeys.ShouldBeNull();
        partial.Package.Relationships.ShouldBeEmpty();
    }

    private static string Code(Action action) => Should.Throw<ContractException>(action).Code;

    private static ReportProfile Profile(string id) => Profiles.Require(id, "1.0.0");

    private static ReportConversion Convert(byte[] bytes, string id)
    {
        var table = CsvReportReader.Read(bytes, ReportLimits.Default);
        Profiles.RequireBestMatch(Profile(id), table.Headers);
        var conversion = ReportAdapter.Convert(table, Profile(id), Context);
        DiscoveryCatalog.Load().ValidatePackage(conversion.Package);
        return conversion;
    }

    private static IReadOnlyDictionary<string, JsonElement> Attributes(DiscoveryPackage package, string id) =>
        package.Objects.Single(x => x.ExternalId == id).Attributes;

    internal static byte[] Example(string id) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ReportExamples", $"{id}.v1.0.0.csv"));

    private static string Text(string id) => Encoding.UTF8.GetString(Example(id));

    private static byte[] Csv(string text, string? drop = null, (string From, string To)? rename = null)
    {
        var lines = text.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        var headers = lines[0].Split(',');
        var index = drop is null ? -1 : Array.IndexOf(headers, drop);
        var output = lines.Select((line, number) =>
        {
            var fields = SplitSimple(line);
            if (index >= 0) { fields.RemoveAt(index); }
            if (number == 0 && rename is { } names) { fields = [.. fields.Select(x => x == names.From ? names.To : x)]; }
            return string.Join(',', fields);
        });
        return Encoding.UTF8.GetBytes(string.Join('\n', output) + "\n");
    }

    private static List<string> SplitSimple(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"') { quoted = !quoted; }
            if (c == ',' && !quoted) { fields.Add(current.ToString()); current.Clear(); continue; }
            current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
