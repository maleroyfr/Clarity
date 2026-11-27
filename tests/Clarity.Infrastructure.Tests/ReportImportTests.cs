using System.Text;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class ReportImportTests
{
    private const string Mailbox = "m365.mailboxUsageDetail";
    private const string OneDrive = "m365.oneDriveUsageAccountDetail";
    private const string SharePoint = "m365.sharePointSiteUsageDetail";
    private const string Version = "1.0.0";
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000123");
    private static readonly CollectionScope Scope = new("tenant", Tenant.ToString());
    private static readonly DateTimeOffset Refresh = new(2026, 5, 31, 0, 0, 0, TimeSpan.Zero);

    [SqlFact]
    [Trait("Recette", "R07")]
    public async Task MailboxReportPublishesOnlyAfterConfirmationThroughTheCommonJobPathWithReportProvenance()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        var bytes = Example(Mailbox);
        var ids = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => UploadAsync(imports, fixture, environment, Mailbox, bytes)));
        ids.Distinct().ShouldHaveSingleItem();
        var id = ids[0];
        store.Count.ShouldBe(1);

        var preview = await imports.PreviewAsync(fixture.Project.Id, id);
        preview.Run.Channel.ShouldBe(CollectionChannel.Report);
        preview.Run.ProducerId.ShouldBe("report.m365.mailboxUsageDetail");
        var report = preview.Report.ShouldNotBeNull();
        (report.DataRows, report.UsableRows, report.RejectedRows, report.ConcealedRows).ShouldBe((6, 4, 1, 1));
        report.Issues.ShouldBe([new ReportRowIssue(5, "Storage Used (Byte)", "InvalidBytes")]);
        report.RefreshDate.ShouldBe(new DateOnly(2026, 5, 31));
        preview.Sections.Single().Status.ShouldBe(CoverageStatus.Partial);
        preview.Sections.Single().Warnings.Select(x => x.Code).ShouldContain("ScopeSuppliedByConsultant");
        preview.Objects.Count.ShouldBe(4);
        preview.Relationships.Count.ShouldBe(4);
        preview.NewObjects.ShouldBe(4);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.ObjectObservations.CountAsync()).ShouldBe(0);
            (await db.InventoryObjects.CountAsync()).ShouldBe(0);
            (await db.CollectionJobs.CountAsync()).ShouldBe(0);
        }

        await Task.WhenAll(imports.ConfirmAsync(fixture.Project.Id, id), imports.ConfirmAsync(fixture.Project.Id, id));
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue();
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.CollectionJobs.SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
        (await check.CollectionRuns.SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
        var objects = await check.InventoryObjects.AsNoTracking().ToListAsync();
        objects.Count.ShouldBe(4);
        objects.ShouldAllBe(x => x.EnvironmentId == environment.Id && x.Type == "mailbox"
            && x.Presence == InventoryPresence.Observed && x.LastObservedAtUtc == Refresh);
        (await check.ExternalKeys.AsNoTracking().Where(x => x.IsPrimary).Select(x => x.Namespace + "|" + x.ExternalId).ToListAsync()).ShouldBe(
            ["m365report.upn|adele.vance@contoso.example", "m365report.upn|alex.wilber@contoso.example",
                "m365report.upn|megan.bowen@contoso.example", "m365report.upn|former.employee@contoso.example"], ignoreOrder: true);
        (await ValueAsync(check, "alex.wilber@contoso.example", "storageUsedBytes")).ShouldBe("0");
        (await ValueAsync(check, "alex.wilber@contoso.example", "lastActivityDate")).ShouldBe("null");
        (await ValueAsync(check, "megan.bowen@contoso.example", "storageUsedBytes")).ShouldBe("null");
        (await ValueAsync(check, "adele.vance@contoso.example", "storageUsedBytes")).ShouldBe("2147483648");
        (await ValueAsync(check, "former.employee@contoso.example", "isDeleted")).ShouldBe("true");
        objects.Single(x => x.UserPrincipalName == "alex.wilber@contoso.example").DisplayName.ShouldBe("Wilber, Alex");
        var edges = await check.InventoryRelationships.AsNoTracking().ToListAsync();
        edges.Count.ShouldBe(4);
        edges.ShouldAllBe(x => x.RelationType == "mailboxOf" && x.FromMethod == EndpointResolution.Primary
            && x.ToMethod == EndpointResolution.Pending && x.ToObjectId == null);

        var storage = await check.CurrentValues.AsNoTracking().FirstAsync(x => x.Field == "storageUsedBytes");
        var proof = (await new InventoryProof(fixture.Factory, fixture.Access)
            .ReadAsync(fixture.Project.Id, new(ProofKind.Observation, storage.ObservationId))).Sources.Single();
        proof.Channel.ShouldBe("ReportImport");
        proof.Producer.ShouldBe("report.m365.mailboxUsageDetail");
        proof.Source.ShouldBe(ReportAdapter.UploadSourceId);
        proof.Modules["reportProfile"].ShouldBe($"{Mailbox}@{Version}");
        proof.Modules["reportAdapter"].ShouldBe(ReportAdapter.Version);
        proof.Modules["scopeSource"].ShouldBe("consultantSupplied");
        proof.Observed.ShouldBe(Refresh);
        proof.Uploaded.ShouldNotBeNull();
        proof.Uploaded.ShouldNotBe(Refresh);
        proof.FileName.ShouldBe($"{Mailbox}.csv");
        proof.Uploader.ShouldBe(fixture.Identity);
        proof.Result.ShouldBe(CoverageStatus.Partial);
        proof.Scope.ShouldBe(Scope);

        var raw = await imports.DownloadRawAsync(fixture.Project.Id, id);
        raw.ContentType.ShouldBe("text/csv");
        raw.FileName.ShouldBe($"{Mailbox}.csv");
        await using (var content = raw.Content)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy);
            copy.ToArray().ShouldBe(bytes);
        }

        (await UploadAsync(imports, fixture, environment, Mailbox, bytes)).ShouldBe(id);
        await imports.ConfirmAsync(fixture.Project.Id, id);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeFalse();
        (await check.CollectionJobs.CountAsync()).ShouldBe(1);
        (await check.CollectionRuns.CountAsync()).ShouldBe(1);
        (await check.ObjectObservations.CountAsync()).ShouldBe(4);
        store.Count.ShouldBe(1);
    }

    [SqlFact]
    [Trait("Recette", "R07")]
    public async Task MixedOneDriveIdentitiesPublishThroughTheCommonJobPathWithoutDuplicates()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        var bytes = Encoding.UTF8.GetBytes(Text(OneDrive).Replace(
            "6f1c2a10-1111-4a8b-9c11-000000000002", "", StringComparison.Ordinal));
        var ids = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => UploadAsync(imports, fixture, environment, OneDrive, bytes)));
        ids.Distinct().ShouldHaveSingleItem();
        var id = ids[0];
        var preview = await imports.PreviewAsync(fixture.Project.Id, id);
        preview.Sections.Single().Status.ShouldBe(CoverageStatus.Complete);
        (preview.Report!.UsableRows, preview.Report.RejectedRows).ShouldBe((2, 0));
        preview.Report.Identities.Select(x => x.Namespace).ShouldBe(["sharepoint.siteId", "m365report.siteUrl"]);
        preview.NewObjects.ShouldBe(2);
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.InventoryObjects.CountAsync()).ShouldBe(0);
        (await check.ObjectObservations.CountAsync()).ShouldBe(0);
        (await check.CollectionJobs.CountAsync()).ShouldBe(0);

        await Task.WhenAll(imports.ConfirmAsync(fixture.Project.Id, id), imports.ConfirmAsync(fixture.Project.Id, id));
        await IntegrateAsync(fixture, store, imports, id);
        var keys = await check.ExternalKeys.AsNoTracking().Where(x => x.IsPrimary && x.Type == "oneDrive")
            .ToDictionaryAsync(x => x.Namespace + "|" + x.ExternalId, x => x.ObjectId);
        keys.Keys.ShouldBe([
            "sharepoint.siteId|6f1c2a10-1111-4a8b-9c11-000000000001",
            "m365report.siteUrl|https://contoso-my.sharepoint.com/personal/alex_wilber_contoso_example"], ignoreOrder: true);
        (await check.InventoryObjects.AsNoTracking().ToListAsync()).ShouldAllBe(x =>
            x.Type == "oneDrive" && x.LastObservedAtUtc == Refresh && x.Presence == InventoryPresence.Observed);
        var owners = await check.InventoryRelationships.AsNoTracking().ToListAsync();
        owners.ShouldAllBe(x => x.RelationType == "ownerOf" && x.FromMethod == EndpointResolution.Pending
            && x.ToMethod == EndpointResolution.Primary);
        owners.Select(x => x.ToObjectId!.Value).ShouldBe(keys.Values, ignoreOrder: true);
        var provisionalId = keys["m365report.siteUrl|https://contoso-my.sharepoint.com/personal/alex_wilber_contoso_example"];
        var storage = await check.CurrentValues.AsNoTracking().SingleAsync(x => x.ObjectId == provisionalId && x.Field == "storageUsedBytes");
        storage.ValueJson.ShouldBe("0");
        var proof = (await new InventoryProof(fixture.Factory, fixture.Access)
            .ReadAsync(fixture.Project.Id, new(ProofKind.Observation, storage.ObservationId))).Sources.Single();
        proof.Channel.ShouldBe("ReportImport");
        proof.Modules["reportProfile"].ShouldBe($"{OneDrive}@{Version}");
        proof.Modules["reportAdapter"].ShouldBe(ReportAdapter.Version);
        proof.Modules["scopeSource"].ShouldBe("consultantSupplied");
        proof.Observed.ShouldBe(Refresh);
        proof.Uploader.ShouldBe(fixture.Identity);
        proof.Result.ShouldBe(CoverageStatus.Complete);

        var raw = await imports.DownloadRawAsync(fixture.Project.Id, id);
        await using (var content = raw.Content)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy);
            copy.ToArray().ShouldBe(bytes);
        }
        (await UploadAsync(imports, fixture, environment, OneDrive, bytes)).ShouldBe(id);
        await imports.ConfirmAsync(fixture.Project.Id, id);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeFalse();
        (await check.InventoryObjects.CountAsync()).ShouldBe(2);
        (await check.ObjectObservations.CountAsync()).ShouldBe(2);
        (await check.CollectionJobs.CountAsync()).ShouldBe(1);
        (await check.SectionRuns.CountAsync(x => x.PublishedAtUtc != null)).ShouldBe(1);
        store.Count.ShouldBe(1);
    }

    [SqlFact]
    [Trait("Recette", "R05")]
    [Trait("Recette", "R07")]
    public async Task SiteReportsResolveOwnersThroughObservedUpnsAndContradictoryTenantEvidenceBlocksPublication()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        var users = await SeedEntraAsync(fixture, store, environment,
            "adele.vance@contoso.example", "alex.wilber@contoso.example", "megan.bowen@contoso.example");
        foreach (var profile in new[] { OneDrive, SharePoint, Mailbox })
        {
            var id = await UploadAsync(imports, fixture, environment, profile, Example(profile));
            await IntegrateAsync(fixture, store, imports, id);
        }

        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.ExternalKeys.AsNoTracking().Where(x => x.IsPrimary && x.Namespace == "sharepoint.siteId")
            .Select(x => x.Type + "|" + x.ExternalId).ToListAsync()).ShouldBe(
            ["oneDrive|6f1c2a10-1111-4a8b-9c11-000000000001", "oneDrive|6f1c2a10-1111-4a8b-9c11-000000000002",
                "site|7a2d3b20-2222-4b9c-8d22-000000000001", "site|7a2d3b20-2222-4b9c-8d22-000000000002"], ignoreOrder: true);
        var owners = await check.InventoryRelationships.AsNoTracking().Where(x => x.RelationType == "ownerOf").ToListAsync();
        owners.Count.ShouldBe(3);
        owners.ShouldAllBe(x => x.FromMethod == EndpointResolution.Alternate && x.ToMethod == EndpointResolution.Primary);
        owners.Select(x => x.FromObjectId!.Value).ShouldBe([users[0], users[1], users[2]], ignoreOrder: true);
        var mailboxes = await check.InventoryRelationships.AsNoTracking().Where(x => x.RelationType == "mailboxOf").ToListAsync();
        mailboxes.Count(x => x.ToMethod == EndpointResolution.Alternate).ShouldBe(3);
        mailboxes.Where(x => x.ToObjectId != null).Select(x => x.ToObjectId!.Value).ShouldBe(users, ignoreOrder: true);
        // The departed mailbox owner was never observed in Entra: the link stays pending instead of being guessed.
        mailboxes.Single(x => x.ToObjectId == null).ToMethod.ShouldBe(EndpointResolution.Pending);
        (await check.InventoryObjects.CountAsync(x => x.Type == "user")).ShouldBe(3);

        var runs = await check.CollectionRuns.CountAsync();
        var otherRoot = Encoding.UTF8.GetBytes(Text(OneDrive).Replace("contoso-my.sharepoint.com", "fabrikam-my.sharepoint.com", StringComparison.Ordinal)
            .Replace("6f1c2a10-1111", "6f1c2a10-9999", StringComparison.Ordinal));
        var conflict = await Should.ThrowAsync<ContractException>(() => UploadAsync(imports, fixture, environment, OneDrive, otherRoot));
        (conflict.Code, conflict.Details[0]).ShouldBe(("ReportEnvironmentConflict", "fabrikam.sharepoint.com"));
        var otherDomain = Encoding.UTF8.GetBytes(Text(Mailbox).Replace("@contoso.example", "@fabrikam.example", StringComparison.Ordinal));
        conflict = await Should.ThrowAsync<ContractException>(() => UploadAsync(imports, fixture, environment, Mailbox, otherDomain));
        (conflict.Code, conflict.Details.Single()).ShouldBe(("ReportEnvironmentConflict", "fabrikam.example"));
        (await check.CollectionRuns.CountAsync()).ShouldBe(runs);
        store.Count.ShouldBe(runs);
    }

    [SqlFact]
    [Trait("Recette", "R09")]
    public async Task UnsupportedProfilesScopesFilesAndUnauthorizedOrGuessedIdentifiersAreRejectedAtTheServerBoundary()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        var bytes = Example(Mailbox);
        var id = await UploadAsync(imports, fixture, environment, Mailbox, bytes);
        async Task<string> Code(Func<Task> action) => (await Should.ThrowAsync<ContractException>(action)).Code;

        (await Code(() => UploadAsync(imports, fixture, environment, Mailbox, bytes, attested: false))).ShouldBe("ReportScopeNotAttested");
        (await Code(() => UploadAsync(imports, fixture, environment, Mailbox, bytes, scope: Guid.NewGuid().ToString())))
            .ShouldBe("ReportScopeMismatch");
        (await Code(() => UploadAsync(imports, fixture, environment, Mailbox, bytes, version: "9.9.9"))).ShouldBe("ReportProfileUnavailable");
        (await Code(() => UploadAsync(imports, fixture, environment, "quest.mailboxInventory", bytes))).ShouldBe("ReportProfileUnavailable");
        (await Code(() => UploadAsync(imports, fixture, fixture.Environment, Mailbox, bytes, scope: fixture.Environment.ExternalId)))
            .ShouldBe("ReportEnvironmentUnsupported");
        var renamed = Encoding.UTF8.GetBytes(Text(Mailbox).Replace(",Item Count,", ",Items,", StringComparison.Ordinal));
        var missing = await Should.ThrowAsync<ContractException>(() => UploadAsync(imports, fixture, environment, Mailbox, renamed));
        (missing.Code, missing.Details.Single()).ShouldBe(("ReportMissingColumns", "Item Count"));
        (await Code(() => UploadAsync(imports, fixture, environment, OneDrive, bytes))).ShouldBe("ReportProfileMismatch");
        (await Code(() => UploadAsync(imports, fixture, environment, OneDrive, Example(SharePoint)))).ShouldBe("ReportProfileMismatch");
        var header = Text(Mailbox).Split('\n')[0] + "\n";
        (await Code(() => UploadAsync(imports, fixture, environment, Mailbox, Encoding.UTF8.GetBytes(header)))).ShouldBe("ReportHasNoRows");
        (await Code(() => UploadAsync(imports, fixture, environment, Mailbox, "\"unterminated\n"u8.ToArray()))).ShouldBe("ReportMalformedCsv");
        var small = new ImportService(fixture.Factory, fixture.Access, fixture.Scope, store,
            new ZipPackageReader(new PackageValidator(), new PackageLimits(CompressedBytes: 100)), new PackageLimits(CompressedBytes: 100),
            ZipImportTests.TestCatalog());
        (await Code(() => UploadAsync(small, fixture, environment, Mailbox, bytes))).ShouldBe("ReportFileTooLarge");
        var claimed = ReportAdapter.Convert(bytes, ReportProfiles.Default.Require(Mailbox, Version),
            new ReportContext(Tenant.ToString(), Scope, "synthetic-hash"), ReportLimits.Default).Package;
        (await Code(async () => await imports.UploadAsync(fixture.Project.Id, environment.Id,
            new MemoryStream(await ZipImportTests.BytesAsync(claimed)), "claimed-report.zip"))).ShouldBe("ReportRequiresProfileUpload");

        await Should.ThrowAsync<AccessDeniedException>(() => imports.UploadReportAsync(fixture.Project.Id, Guid.NewGuid(), Mailbox, Version,
            Scope, true, new MemoryStream(bytes), "guessed-environment.csv"));
        await Should.ThrowAsync<AccessDeniedException>(() => imports.UploadReportAsync(Guid.NewGuid(), environment.Id, Mailbox, Version,
            Scope, true, new MemoryStream(bytes), "guessed-project.csv"));
        await Should.ThrowAsync<AccessDeniedException>(() => imports.PreviewAsync(fixture.Project.Id, Guid.NewGuid()));
        await Should.ThrowAsync<AccessDeniedException>(() => imports.ConfirmAsync(fixture.Project.Id, Guid.NewGuid()));
        await Should.ThrowAsync<AccessDeniedException>(() => imports.DownloadRawAsync(fixture.Project.Id, Guid.NewGuid()));
        var other = await fixture.OtherWorkspaceAsync();
        var outsider = new ImportService(other.Factory, other.Access, other.Scope, store,
            new ZipPackageReader(new PackageValidator(), new PackageLimits()), new PackageLimits(), ZipImportTests.TestCatalog());
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.UploadReportAsync(other.Project.Id, environment.Id, Mailbox, Version,
            Scope, true, new MemoryStream(bytes), "other-workspace.csv"));
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.PreviewAsync(other.Project.Id, id));
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.ConfirmAsync(other.Project.Id, id));
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.DownloadRawAsync(other.Project.Id, id));
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        }
        await Should.ThrowAsync<AccessDeniedException>(() => UploadAsync(imports, fixture, environment, SharePoint, Example(SharePoint)));
        await Should.ThrowAsync<AccessDeniedException>(() => imports.ConfirmAsync(fixture.Project.Id, id));
        (await imports.PreviewAsync(fixture.Project.Id, id)).Report.ShouldNotBeNull().UsableRows.ShouldBe(4);

        store.Count.ShouldBe(1);
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.CollectionRuns.SingleAsync()).Id.ShouldBe(id);
        (await check.CollectionJobs.CountAsync()).ShouldBe(0);
        (await check.ObjectObservations.CountAsync()).ShouldBe(0);
    }

    [SqlFact]
    [Trait("Recette", "R13")]
    public async Task ConcealedOnlyAndAllInvalidReportsStateTheirFailureAndCannotBeConfirmed()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        var rows = Rows(Mailbox);
        foreach (var (content, code) in new[] { (Csv(rows[0], rows[5]), "AllIdentifiersConcealed"), (Csv(rows[0], rows[4]), "NoUsableRows") })
        {
            var id = await UploadAsync(imports, fixture, environment, Mailbox, content);
            var preview = await imports.PreviewAsync(fixture.Project.Id, id);
            var section = preview.Sections.Single();
            section.Status.ShouldBe(CoverageStatus.Failed);
            section.ObjectCount.ShouldBeNull();
            section.Errors.Select(x => x.Code).ShouldContain(code);
            preview.Objects.ShouldBeEmpty();
            preview.Report.ShouldNotBeNull().UsableRows.ShouldBe(0);
            (await Should.ThrowAsync<ContractException>(() => imports.ConfirmAsync(fixture.Project.Id, id))).Code.ShouldBe("ReportNoUsableRows");
        }
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.CollectionRuns.CountAsync()).ShouldBe(2);
        (await check.CollectionRuns.CountAsync(x => x.ConfirmedAtUtc != null)).ShouldBe(0);
        (await check.CollectionJobs.CountAsync()).ShouldBe(0);
        (await check.ObjectObservations.CountAsync()).ShouldBe(0);
        (await check.InventoryObjects.CountAsync()).ShouldBe(0);
    }

    [SqlFact]
    [Trait("Recette", "R07")]
    [Trait("Recette", "R13")]
    public async Task PartialReportKeepsPriorValuesPresenceAndValidatedHumanDecisions()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        var template = DiscoveryFixture.Package();
        var seeded = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var ad = template with
        {
            Manifest = template.Manifest with { RunId = Guid.NewGuid(), StartedAtUtc = seeded, CompletedAtUtc = seeded },
            Objects = [template.Objects[0] with
            {
                ExternalId = Guid.NewGuid().ToString(),
                ObservedAtUtc = seeded,
                Attributes = new Dictionary<string, JsonElement> { ["userPrincipalName"] = JsonSerializer.SerializeToElement("adele.vance@contoso.example") }
            }],
            Results = [template.Results[0] with { CoveredFields = ["userPrincipalName"] }]
        };
        var adRun = await fixture.StageAsync(ad);
        await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, adRun.Id, ad, template.Objects[0].SectionId);
        var adele = (await SeedEntraAsync(fixture, store, environment, "adele.vance@contoso.example")).Single();
        await using var check = await fixture.Factory.CreateDbContextAsync();
        var decision = await check.MappingDecisions.AsNoTracking().SingleAsync(x => x.Rule == AssociationRule.ExactUpn);
        var review = new MappingReview(fixture.Factory, fixture.Access, fixture.Scope);
        MappingSelection[] selection = [new(decision.Id, decision.Revision)];
        (await review.PreviewAsync(fixture.Project.Id, selection, true)).ShouldBe(1);
        await review.ApplyAsync(fixture.Project.Id, selection, true, 1, "Confirmed against synthetic directory");
        var validated = await check.MappingDecisions.AsNoTracking().SingleAsync(x => x.Id == decision.Id);
        validated.State.ShouldBe(MappingState.Validated);
        var revisions = await check.MappingRevisions.CountAsync(x => x.DecisionId == decision.Id);

        var rows = Rows(Mailbox);
        var earlier = Csv(rows[0], rows[1].Replace("2147483648", "1073741824", StringComparison.Ordinal), rows[2],
            rows[4].Replace("12 MB", "1048576", StringComparison.Ordinal));
        earlier = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(earlier).Replace("2026-05-31", "2026-05-24", StringComparison.Ordinal));
        var first = await UploadAsync(imports, fixture, environment, Mailbox, earlier);
        (await imports.PreviewAsync(fixture.Project.Id, first)).Sections.Single().Status.ShouldBe(CoverageStatus.Complete);
        await IntegrateAsync(fixture, store, imports, first);
        (await ValueAsync(check, "lee.gu@contoso.example", "storageUsedBytes")).ShouldBe("1048576");
        (await ValueAsync(check, "adele.vance@contoso.example", "storageUsedBytes")).ShouldBe("1073741824");

        var second = await UploadAsync(imports, fixture, environment, Mailbox, Example(Mailbox));
        await IntegrateAsync(fixture, store, imports, second);
        (await ValueAsync(check, "adele.vance@contoso.example", "storageUsedBytes")).ShouldBe("2147483648");
        (await ValueAsync(check, "lee.gu@contoso.example", "storageUsedBytes")).ShouldBe("1048576");
        (await ValueAsync(check, "lee.gu@contoso.example", "reportRefreshDate")).ShouldBe("\"2026-05-24\"");
        var lee = await check.InventoryObjects.AsNoTracking().SingleAsync(x => x.UserPrincipalName == "lee.gu@contoso.example");
        lee.Presence.ShouldBe(InventoryPresence.Observed);
        lee.LastObservedAtUtc.ShouldBe(new DateTimeOffset(2026, 5, 24, 0, 0, 0, TimeSpan.Zero));
        (await check.InventoryObjects.CountAsync(x => x.Presence != InventoryPresence.Observed)).ShouldBe(0);
        (await check.InventoryObjects.CountAsync(x => x.Type == "mailbox")).ShouldBe(5);
        var current = await check.MappingDecisions.AsNoTracking().SingleAsync(x => x.Id == decision.Id);
        (current.State, current.Revision).ShouldBe((MappingState.Validated, validated.Revision));
        (await check.MappingRevisions.CountAsync(x => x.DecisionId == decision.Id)).ShouldBe(revisions);
        (await check.MappingDecisions.CountAsync()).ShouldBe(1);
        (await check.InventoryRelationships.AsNoTracking().Where(x => x.RelationType == "mailboxOf" && x.ToObjectId == adele).CountAsync())
            .ShouldBe(1);
    }

    [SqlFact]
    [Trait("Recette", "R08")]
    public async Task JobTimeAuthorizationIsRecheckedAndRetryPublishesTheReportExactlyOnce()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        var id = await UploadAsync(imports, fixture, environment, SharePoint, Example(SharePoint));
        await imports.ConfirmAsync(fixture.Project.Id, id);
        await using var check = await fixture.Factory.CreateDbContextAsync();
        await check.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default);
        var failed = await check.CollectionJobs.AsNoTracking().SingleAsync();
        (failed.State, failed.ErrorCode).ShouldBe((CollectionRunState.Failed, "AccessDenied"));
        (await check.ObjectObservations.CountAsync()).ShouldBe(0);

        await check.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.WorkspaceManager));
        await fixture.Jobs.RetryAsync(fixture.Project.Id, id);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue();
        (await check.CollectionJobs.AsNoTracking().SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
        (await check.CollectionRuns.AsNoTracking().SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
        await Should.ThrowAsync<ConflictException>(() => fixture.Jobs.RetryAsync(fixture.Project.Id, id));
        (await UploadAsync(imports, fixture, environment, SharePoint, Example(SharePoint))).ShouldBe(id);
        await imports.ConfirmAsync(fixture.Project.Id, id);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeFalse();
        (await check.ObjectObservations.CountAsync()).ShouldBe(2);
        (await check.InventoryObjects.CountAsync(x => x.Type == "site")).ShouldBe(2);
        (await check.SectionRuns.CountAsync(x => x.PublishedAtUtc != null)).ShouldBe(1);
    }

    private static async Task<string> ValueAsync(ClarityDbContext db, string upn, string field) =>
        await (from v in db.CurrentValues.AsNoTracking()
               join k in db.ExternalKeys on new { v.WorkspaceId, v.ObjectId } equals new { k.WorkspaceId, k.ObjectId }
               where k.IsPrimary && k.Namespace == "m365report.upn" && k.ExternalId == upn && v.Field == field
               select v.ValueJson).SingleAsync();

    private static Task<Guid> UploadAsync(ImportService imports, DiscoveryFixture fixture, ProjectEnvironment environment, string profile,
        byte[] bytes, string? scope = null, bool attested = true, string version = Version) =>
        imports.UploadReportAsync(fixture.Project.Id, environment.Id, profile, version, new CollectionScope("tenant", scope ?? Tenant.ToString()),
            attested, new MemoryStream(bytes), $"{profile}.csv");

    private static async Task IntegrateAsync(DiscoveryFixture fixture, IArtifactStore store, ImportService imports, Guid id)
    {
        await imports.ConfirmAsync(fixture.Project.Id, id);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue();
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.CollectionRuns.AsNoTracking().SingleAsync(x => x.Id == id)).State.ShouldBe(CollectionRunState.Completed);
    }

    private static async Task<Guid[]> SeedEntraAsync(DiscoveryFixture fixture, IArtifactStore store, ProjectEnvironment environment,
        params string[] upns)
    {
        var date = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var ids = upns.Select((_, index) => $"entra-user-{index}").ToArray();
        var package = new DiscoveryPackage(new PackageManifest("1.0", Guid.NewGuid(), "clarity.graph.entra", "1.0.0", "M365Tenant",
                Tenant.ToString(), Scope, date, date, new Dictionary<string, string>(), "Offline synthetic report"),
            [
                .. upns.Select((upn, index) => new ObjectRecord("entra.users", "user", "microsoft.graph", ids[index], date,
                    new Dictionary<string, JsonElement> { ["userPrincipalName"] = JsonSerializer.SerializeToElement(upn) })),
                new ObjectRecord("entra.domains", "domain", "microsoft.graph", "contoso.example", date, new Dictionary<string, JsonElement>
                {
                    ["isDefault"] = JsonSerializer.SerializeToElement(true),
                    ["isInitial"] = JsonSerializer.SerializeToElement(false),
                    ["isVerified"] = JsonSerializer.SerializeToElement(true),
                    ["authenticationType"] = JsonSerializer.SerializeToElement("Managed")
                })
            ], [],
            [
                new SectionResult("entra.users", "entra.users", CoverageStatus.Complete, Scope, ["userPrincipalName"], [], upns.Length, 0, [], []),
                new SectionResult("entra.domains", "entra.domains", CoverageStatus.Complete, Scope,
                    ["isDefault", "isInitial", "isVerified", "authenticationType"], [], 1, 0, [], [])
            ]);
        var imports = ZipImportTests.Service(fixture, store);
        var run = await imports.UploadAsync(fixture.Project.Id, environment.Id, new MemoryStream(await ZipImportTests.BytesAsync(package)), "entra.zip");
        await IntegrateAsync(fixture, store, imports, run);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        var keys = await db.ExternalKeys.AsNoTracking().Where(x => x.EnvironmentId == environment.Id && x.IsPrimary
            && x.Namespace == "microsoft.graph" && x.Type == "user").ToDictionaryAsync(x => x.ExternalId, x => x.ObjectId);
        return ids.Select(x => keys[x]).ToArray();
    }

    private static async Task<ProjectEnvironment> AddTenantAsync(DiscoveryFixture fixture)
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();
        await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.WorkspaceManager));
        var environment = new ProjectEnvironment
        {
            WorkspaceId = fixture.Workspace.Id,
            ProjectId = fixture.Project.Id,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = Tenant.ToString()
        };
        db.Environments.Add(environment);
        await db.SaveChangesAsync();
        return environment;
    }

    private static byte[] Example(string id) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ReportExamples", $"{id}.v1.0.0.csv"));

    private static string Text(string id) =>
        Encoding.UTF8.GetString(Example(id)).TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string[] Rows(string id) => Text(id).TrimEnd('\n').Split('\n');

    private static byte[] Csv(params string[] rows) => Encoding.UTF8.GetBytes(string.Join('\n', rows) + "\n");
}
