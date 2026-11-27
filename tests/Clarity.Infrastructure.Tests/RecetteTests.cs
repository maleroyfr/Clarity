using System.Security.Cryptography;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

/// <summary>
/// End-to-end acceptance journeys of the specification's recette table driven by the synthetic dossier. Each test is tagged with the rows it
/// covers (<c>Recette=Rxx</c>); docs/recette.md maps every row to these journeys and to the focused tests that complement them.
/// </summary>
[Collection("SQL lifecycle")]
public sealed class RecetteTests
{
    private static readonly Guid TargetTenant = Guid.Parse("00000000-0000-0000-0000-000000000601");
    private static readonly Guid HybridTenant = Guid.Parse("00000000-0000-0000-0000-000000000602");

    [Fact]
    public async Task SyntheticDossierIsDeterministicAnonymizedAndValidForEveryCoverageVariant()
    {
        var dossier = new SyntheticDossier();
        var package = dossier.AdForest();
        JsonSerializer.Serialize(package, ContractJson.Options).ShouldBe(JsonSerializer.Serialize(new SyntheticDossier().AdForest(), ContractJson.Options));
        new SyntheticDossier(seed: 7).UserId(1).ShouldNotBe(dossier.UserId(1));
        (package.Objects.Count, package.Relationships.Count).ShouldBe((dossier.Users + dossier.Groups, dossier.Relationships));
        (dossier.Users + dossier.Groups, dossier.Relationships).ShouldBe((20, 40));
        SyntheticDossier.DisplayName(3).ShouldBe(SyntheticDossier.DisplayName(2));
        package.Objects.Where(o => o.Type == "user").Count(o => !o.Attributes["enabled"].GetBoolean()).ShouldBe(1);
        package.Objects.Where(o => o.Type == "user").Select(o => o.Attributes["userPrincipalName"].GetString())
            .ShouldAllBe(upn => upn!.EndsWith(".test", StringComparison.Ordinal));

        var catalog = ZipImportTests.TestCatalog();
        var validator = new PackageValidator();
        var reader = new ZipPackageReader(validator, new PackageLimits());
        DiscoveryPackage[] variants =
        [
            package, dossier.AdForest(1, renamed: [1], groups: CoverageStatus.Partial), dossier.AdForest(groups: CoverageStatus.Failed),
            dossier.AdForest(groups: CoverageStatus.NotApplicable), dossier.AdForest(groups: null), dossier.AdForest(users: false),
            dossier.Entra(TargetTenant, "target.example", count: 8), dossier.Entra(HybridTenant, mails: new Dictionary<int, string> { [2] = SyntheticDossier.Upn(3) })
        ];
        foreach (var variant in variants)
        {
            catalog.ValidatePackage(variant);
            validator.Validate(variant);
            var read = await reader.ReadAsync(new MemoryStream(await ZipImportTests.BytesAsync(variant)), cancellationToken: TestContext.Current.CancellationToken);
            (read.Package.Objects.Count, read.Package.Relationships.Count, read.Package.Results.Count)
                .ShouldBe((variant.Objects.Count, variant.Relationships.Count, variant.Results.Count));
        }
        variants.Select(v => v.Manifest.RunId).Distinct().Count().ShouldBe(variants.Length);
        dossier.AdForest(groups: CoverageStatus.Failed).Results.Single(r => r.CapabilityId == "ad.groups").Errors.ShouldHaveSingleItem();
        Should.Throw<ArgumentException>(() => dossier.AdForest(users: false, groups: null));

        var large = new SyntheticDossier(users: 1000, projectMemberships: 9);
        (large.ProjectGroups, large.Relationships, large.ProjectsOf(1000).Distinct().Count()).ShouldBe((40, 10_004, 9));
    }

    [SqlFact]
    [Trait("Recette", "R01")]
    public async Task SourceOnlyDossierIsUsableAndTargetComparisonActivatesOnceTargetDataExists()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var dossier = new SyntheticDossier();
        var store = new MemoryArtifactStore();
        var project = fixture.Project.Id;
        await UploadAsync(fixture, store, fixture.Environment.Id, dossier.AdForest());
        var scenarios = new ScenarioOverviewService(fixture.Factory, fixture.Access);

        var overview = await scenarios.ReadAsync(project);
        (overview.Facts.Sources, overview.Facts.SourceSections, overview.Facts.SourceSectionsToVerify, overview.Facts.Targets).ShouldBe((1, 2, 0, 0));
        (State(overview, ScenarioStep.SourceCollection), State(overview, ScenarioStep.SourceTargetComparison))
            .ShouldBe((ScenarioStepState.Done, ScenarioStepState.Waiting));
        var inventory = await new InventoryOverview(fixture.Factory, fixture.Access).ReadAsync(project, EnvironmentRole.Source);
        inventory.Metrics.Single(m => m.Label == "ObservedPopulation").Amount.ShouldBe(dossier.Users);
        inventory.Units.Select(u => (u.Capability, u.Status)).ShouldBe([("ad.groups", CoverageStatus.Complete), ("ad.users", CoverageStatus.Complete)]);
        var keys = await KeysAsync(fixture, fixture.Environment.Id);
        var paths = await new InventoryGraph(fixture.Factory, fixture.Access).MembershipsAsync(project, keys[dossier.UserId(1)], false);
        paths.Count.ShouldBe(2 + dossier.ProjectMemberships);
        paths.Single(p => p.ObjectId == keys[dossier.GroupId(SyntheticDossier.AllStaff)]).RelationshipIds.Count.ShouldBe(2);
        paths.Single(p => p.ObjectId == keys[dossier.GroupId(SyntheticDossier.Department(1))]).RelationshipIds.Count.ShouldBe(1);

        var target = await AddEnvironmentAsync(fixture, TargetTenant, EnvironmentRole.Target);
        var comparisons = new TargetComparisonService(fixture.Factory, fixture.Access, fixture.Scope, DiscoveryCatalog.Load());
        await comparisons.AddRuleAsync(project, new("user", TargetRuleKind.ComputedUpn, "userPrincipalName", "", "target.example", 1,
            "Synthetic target UPN"), Guid.NewGuid());
        State(await scenarios.ReadAsync(project), ScenarioStep.TargetCollection).ShouldBe(ScenarioStepState.ToDo);
        var empty = await comparisons.CompareAsync(project, fixture.Environment.Id, target, Guid.NewGuid());
        (await comparisons.ComparisonsAsync(project)).Single().ShouldSatisfyAllConditions(s => s.Data.ShouldBe(TargetDataState.NoData),
            s => s.NotCompared.ShouldBe(dossier.Users), s => s.Matched.ShouldBe(0));
        (await comparisons.ItemsAsync(project, empty, false, null, null, new(Count: 100))).Items.ShouldAllBe(i => i.Reason == "TargetDataMissing");

        await UploadAsync(fixture, store, target, dossier.Entra(TargetTenant, "target.example", count: 8));
        var id = await comparisons.CompareAsync(project, fixture.Environment.Id, target, Guid.NewGuid());
        var summary = (await comparisons.ComparisonsAsync(project)).Single();
        (summary.Id, summary.Data, summary.Matched, summary.Ambiguous, summary.Absent, summary.Unknown, summary.NotCompared, summary.Collisions)
            .ShouldBe((id, TargetDataState.Available, 8, 0, 0, 4, 0, 0));
        var unknown = (await comparisons.ItemsAsync(project, id, false, TargetOutcome.Unknown, null, new(Count: 100))).Items;
        unknown.Select(i => i.SourceId!.Value).ShouldBe(Enumerable.Range(9, 4).Select(i => keys[dossier.UserId(i)]), ignoreOrder: true);
        unknown.ShouldAllBe(i => i.Reason == "TargetNotExhaustive");
        overview = await scenarios.ReadAsync(project);
        (State(overview, ScenarioStep.TargetCollection), State(overview, ScenarioStep.SourceTargetComparison))
            .ShouldBe((ScenarioStepState.Done, ScenarioStepState.Done));
    }

    [SqlFact]
    [Trait("Recette", "R04")]
    [Trait("Recette", "R05")]
    [Trait("Recette", "R06")]
    public async Task SeparateMachineScriptWrongEnvironmentAndFailedSectionRetryFollowTheirRequests()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var dossier = new SyntheticDossier();
        var store = new MemoryArtifactStore();
        var project = fixture.Project.Id;
        var environment = fixture.Environment.Id;
        var scripts = new ScriptRequestService(fixture.Factory, fixture.Access, fixture.Scope, DiscoveryCatalog.Load());
        var imports = ZipImportTests.Service(fixture, store);
        var domain = new CollectionScope("domain", SyntheticDossier.Domain);

        var first = await scripts.CreateAsync(project, environment, Guid.NewGuid(), domain, ["ad.users", "ad.groups"], null);
        var parameters = JsonSerializer.Deserialize<ScriptParameters>(await scripts.DownloadParametersAsync(project, first), ContractJson.Options)!;
        (parameters.RequestId, parameters.ExpectedEnvironmentExternalId, parameters.Domain).ShouldBe((first, SyntheticDossier.Forest, SyntheticDossier.Domain));
        (await scripts.DownloadBundleAsync(project, first)).FileName.ShouldBe("Clarity.Discovery.AD.1.0.0.zip");
        var bytes = await ZipImportTests.BytesAsync(dossier.AdForest(requestId: first));
        var run = await imports.UploadAsync(project, environment, new MemoryStream(bytes), "collected-on-another-machine.zip");
        (await imports.PreviewAsync(project, run)).NewObjects.ShouldBe(dossier.Users + dossier.Groups);
        await imports.ConfirmAsync(project, run);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue();
        await using (var download = await imports.DownloadAsync(project, run))
        {
            using var copy = new MemoryStream();
            await download.CopyToAsync(copy);
            copy.ToArray().ShouldBe(bytes);
        }

        var wrong = await ZipImportTests.BytesAsync(dossier.AdForest(day: 1, forest: "other.example.test"));
        (await Should.ThrowAsync<ContractException>(() => imports.UploadAsync(project, environment, new MemoryStream(wrong), "wrong-forest.zip")))
            .Code.ShouldBe("EnvironmentOrScopeMismatch");
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.CollectionRuns.CountAsync()).ShouldBe(1);
            (await db.ObjectObservations.CountAsync()).ShouldBe(dossier.Users + dossier.Groups);
            (await db.CollectionRequests.SingleAsync(r => r.Id == first)).State.ShouldBe(CollectionRequestState.Integrated);
        }
        store.Count.ShouldBe(1);

        var second = await scripts.CreateAsync(project, environment, Guid.NewGuid(), domain, ["ad.users", "ad.groups"], null);
        await UploadAsync(fixture, store, environment, dossier.AdForest(day: 1, renamed: [1], groups: CoverageStatus.Failed, requestId: second));
        var keys = await KeysAsync(fixture, environment);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.InventoryObjects.SingleAsync(o => o.Id == keys[dossier.UserId(1)])).UserPrincipalName.ShouldBe(SyntheticDossier.Upn(1, renamed: true));
            (await db.InventoryObjects.Where(o => o.Type == "group").Select(o => o.Presence).ToListAsync())
                .ShouldBe(Enumerable.Repeat(InventoryPresence.Observed, dossier.Groups));
        }
        (await new InventoryGraph(fixture.Factory, fixture.Access).MembershipsAsync(project, keys[dossier.UserId(1)], false)).Count
            .ShouldBe(2 + dossier.ProjectMemberships);
        await ExpectGroupsAsync(fixture, CoverageStatus.Failed, ScenarioStepState.Attention);

        var retry = await scripts.CreateAsync(project, environment, Guid.NewGuid(), domain, ["ad.groups"], null);
        await UploadAsync(fixture, store, environment, dossier.AdForest(day: 2, users: false, requestId: retry));
        await ExpectGroupsAsync(fixture, CoverageStatus.Complete, ScenarioStepState.Done);
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.CollectionRequests.CountAsync(r => r.State == CollectionRequestState.Integrated)).ShouldBe(3);
        (await check.InventoryObjects.SingleAsync(o => o.Id == keys[dossier.UserId(1)])).UserPrincipalName.ShouldBe(SyntheticDossier.Upn(1, renamed: true));
    }

    [SqlFact]
    [Trait("Recette", "R10")]
    [Trait("Recette", "R12")]
    public async Task RenamedNestedMemberKeepsItsIdentityFlagsOnlyItsDecisionAndAmbiguousHybridCandidatesAwaitReview()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var dossier = new SyntheticDossier();
        var store = new MemoryArtifactStore();
        var project = fixture.Project.Id;
        await UploadAsync(fixture, store, fixture.Environment.Id, dossier.AdForest());
        var keys = await KeysAsync(fixture, fixture.Environment.Id);
        var (renamed, unchanged) = (keys[dossier.UserId(5)], keys[dossier.UserId(6)]);
        await ScopeTests.Service(fixture).ApplyAsync(project,
            new(Guid.NewGuid(), ScopeValue.Include, "Vague 1", "Synthetic wave", ObjectIds: [renamed, unchanged]), 2);

        await UploadAsync(fixture, store, fixture.Environment.Id, dossier.AdForest(day: 1, renamed: [5]));
        (await KeysAsync(fixture, fixture.Environment.Id))[dossier.UserId(5)].ShouldBe(renamed);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.InventoryObjects.SingleAsync(o => o.Id == renamed)).UserPrincipalName.ShouldBe(SyntheticDossier.Upn(5, renamed: true));
        }
        var nested = (await new InventoryGraph(fixture.Factory, fixture.Access).MembershipsAsync(project, renamed, false))
            .Single(p => p.ObjectId == keys[dossier.GroupId(SyntheticDossier.AllStaff)]);
        nested.RelationshipIds.Count.ShouldBe(2);
        foreach (var edge in nested.RelationshipIds)
        {
            var proof = await new InventoryProof(fixture.Factory, fixture.Access).ReadAsync(project, new(ProofKind.Relationship, edge));
            proof.Sources.ShouldNotBeEmpty();
            proof.Sources.ShouldAllBe(s => s.Capability == "ad.groups" && s.Channel == "ZipImport" && s.Producer == "clarity.ad");
        }

        var register = new DecisionRegisterService(fixture.Factory, fixture.Access, fixture.Scope);
        var flagged = (await register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, true, new())).Items.ShouldHaveSingleItem();
        flagged.ObjectId.ShouldBe(renamed);
        flagged.Changes!.Select(c => (c.Field, c.After)).ShouldBe([("mail", "\"r00005@example.test\""), ("userPrincipalName", "\"r00005@example.test\"")]);
        (await register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, false, new())).Items
            .Single(x => x.ObjectId == unchanged).ShouldSatisfyAllConditions(x => x.NeedsReview.ShouldBeFalse(), x => x.Wave.ShouldBe("Vague 1"));
        (await register.SummaryAsync(project, EnvironmentRole.Source)).Single(x => x.Kind == DecisionKind.Scope).ShouldBe(new(DecisionKind.Scope, 2, 1));

        var cloud = await AddEnvironmentAsync(fixture, HybridTenant, EnvironmentRole.Source);
        await UploadAsync(fixture, store, cloud, dossier.Entra(HybridTenant, mails: new Dictionary<int, string> { [2] = SyntheticDossier.Upn(3) }));
        var entra = await KeysAsync(fixture, cloud);
        var rows = (await new MappingReview(fixture.Factory, fixture.Access, fixture.Scope).PageAsync(project, EnvironmentRole.Source, "All",
            new(Count: 100))).Items;
        (int Ad, int Cloud) Pair(MappingRow row) => (Enumerable.Range(1, dossier.Users).Single(i => keys[dossier.UserId(i)] == row.AdId),
            Enumerable.Range(1, dossier.Users).Single(i => entra[dossier.EntraUserId(HybridTenant, i)] == row.EntraId));
        rows.Where(r => r.State == MappingState.ToReview).Select(Pair).ShouldBe([(2, 2), (3, 3), (3, 2)], ignoreOrder: true);
        rows.Single(r => Pair(r) == (1, 1)).State.ShouldBe(MappingState.Proposed);
        rows.ShouldNotContain(r => r.AdId == renamed);
    }

    [SqlFact]
    [Trait("Recette", "R13")]
    [Trait("Recette", "R14")]
    public async Task PartialSourceIsReportedWithoutAbsenceAndTheExportReflectsFiltersScopeVintageAndProvenance()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var dossier = new SyntheticDossier();
        var store = new MemoryArtifactStore();
        var project = fixture.Project.Id;
        await UploadAsync(fixture, store, fixture.Environment.Id, dossier.AdForest());
        // The later snapshot omits users 11 and 12 and reads groups partially: nothing may be deduced as absent from it.
        await UploadAsync(fixture, store, fixture.Environment.Id, new SyntheticDossier(users: 10).AdForest(day: 1, groups: CoverageStatus.Partial));
        var keys = await KeysAsync(fixture, fixture.Environment.Id);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.InventoryObjects.CountAsync(o => o.Presence != InventoryPresence.Observed)).ShouldBe(0);
        }
        await ExpectGroupsAsync(fixture, CoverageStatus.Partial, ScenarioStepState.Attention);
        int[] wave = [1, 2, 3, 4, 11];
        await ScopeTests.Service(fixture).ApplyAsync(project,
            new(Guid.NewGuid(), ScopeValue.Include, "Vague 1", "Synthetic wave", ObjectIds: [.. wave.Select(i => keys[dossier.UserId(i)])]), wave.Length);
        await ScopeTests.Service(fixture).ApplyAsync(project,
            new(Guid.NewGuid(), ScopeValue.Include, "Vague 2", "Synthetic later wave", ObjectIds: [keys[dossier.UserId(5)]]), 1);

        var exportStore = new MemoryExportStore();
        var exports = ExportTests.Service(fixture, exportStore);
        var id = await exports.LaunchAsync(project, new(Guid.NewGuid(),
            new(EnvironmentRole.Source, Decision: ExportDecisionFilter.Include, Wave: "Vague 1")));
        (await ExportTests.ProcessAsync(fixture, exportStore)).ShouldBeTrue();
        var row = (await exports.PageAsync(project, EnvironmentRole.Source, new())).Items.ShouldHaveSingleItem();
        (row.Id, row.State, row.ObjectCount).ShouldBe((id, CollectionRunState.Completed, (int?)wave.Length));
        var file = row.Artifacts.Single(a => a.Kind == ExportArtifactKind.Workbook);
        var download = await exports.DownloadAsync(project, id, file.Id);
        using var buffer = new MemoryStream();
        await using (download.Content) { await download.Content.CopyToAsync(buffer); }
        Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())).ShouldBe(file.ContentHash);
        buffer.Position = 0;
        using var workbook = new XLWorkbook(buffer);
        var summary = ExportTests.SummaryValues(workbook);
        summary.Where(x => x.Key.StartsWith("Export_Check_", StringComparison.Ordinal)).ShouldAllBe(x => x.Value == "Export_Reconciled");
        (summary["Export_Col_Wave"], summary.ContainsKey("Export_AsOf"), summary.ContainsKey("Export_SelectionHash")).ShouldBe(("Vague 1", true, true));
        var inventory = workbook.Worksheet("Export_Sheet_Inventory").RowsUsed(XLCellsUsedOptions.Contents).Skip(1)
            .ToDictionary(r => Guid.Parse(r.Cell(20).GetString()));
        inventory.Keys.ShouldBe(wave.Select(i => keys[dossier.UserId(i)]), ignoreOrder: true);
        inventory.Values.ShouldAllBe(r => r.Cell(9).GetString() == "Observed" && r.Cell(11).GetString() == "ad.users"
            && r.Cell(13).GetString() == "ScopeDecision_Include" && r.Cell(14).GetString() == "Vague 1");
        inventory[keys[dossier.UserId(11)]].Cell(10).GetDateTime().ShouldBe(SyntheticDossier.Epoch.UtcDateTime);
        inventory[keys[dossier.UserId(1)]].Cell(10).GetDateTime().ShouldBe(SyntheticDossier.Epoch.AddDays(1).UtcDateTime);
        workbook.Worksheet("Export_Sheet_Exceptions").RowsUsed(XLCellsUsedOptions.Contents)
            .Any(r => r.Cell(1).GetString() == "ExportException_CoverageGap" && r.Cell(5).GetString() == "ad.groups — Partial").ShouldBeTrue();
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.AuditEvents.CountAsync(a => a.ResourceId == id && (a.Action == "ExportRequested" || a.Action == "ExportGenerated"))).ShouldBe(2);
        (await check.AuditEvents.CountAsync(a => a.Action == "ExportDownloaded")).ShouldBe(1);
    }

    private static ScenarioStepState State(ScenarioOverview overview, ScenarioStep step) => overview.Steps.Single(s => s.Step == step).State;

    private static async Task ExpectGroupsAsync(DiscoveryFixture fixture, CoverageStatus groups, ScenarioStepState collection)
    {
        (await new InventoryOverview(fixture.Factory, fixture.Access).ReadAsync(fixture.Project.Id, EnvironmentRole.Source)).Units
            .Single(u => u.Capability == "ad.groups").Status.ShouldBe(groups);
        var overview = await new ScenarioOverviewService(fixture.Factory, fixture.Access).ReadAsync(fixture.Project.Id);
        (overview.Facts.SourceSections, overview.Facts.SourceSectionsToVerify, State(overview, ScenarioStep.SourceCollection))
            .ShouldBe((2, groups == CoverageStatus.Complete ? 0 : 1, collection));
    }

    private static async Task UploadAsync(DiscoveryFixture fixture, MemoryArtifactStore store, Guid environment, DiscoveryPackage package)
    {
        var imports = ZipImportTests.Service(fixture, store);
        var run = await imports.UploadAsync(fixture.Project.Id, environment, new MemoryStream(await ZipImportTests.BytesAsync(package)), "dossier.zip");
        await imports.ConfirmAsync(fixture.Project.Id, run);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue();
    }

    private static async Task<Dictionary<string, Guid>> KeysAsync(DiscoveryFixture fixture, Guid environment)
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();
        return await db.ExternalKeys.AsNoTracking().Where(k => k.IsPrimary && k.EnvironmentId == environment)
            .ToDictionaryAsync(k => k.ExternalId, k => k.ObjectId, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Guid> AddEnvironmentAsync(DiscoveryFixture fixture, Guid tenant, EnvironmentRole role)
    {
        var environment = new ProjectEnvironment
        {
            WorkspaceId = fixture.Workspace.Id,
            ProjectId = fixture.Project.Id,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = tenant.ToString(),
            Role = role
        };
        await using var db = await fixture.Factory.CreateDbContextAsync();
        db.Environments.Add(environment);
        await db.SaveChangesAsync();
        return environment.Id;
    }
}
