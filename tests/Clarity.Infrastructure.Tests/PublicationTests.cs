using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class PublicationTests
{
    [SqlFact]
    public async Task PendingFailedAndPartialRefreshKeepPreviousValidObservations()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var initial = DiscoveryFixture.Package();
        var first = await fixture.StageAsync(initial);
        await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, first.Id, initial, initial.Results[0].SectionId);
        var pending = DiscoveryFixture.Package("Refreshed");
        var next = await fixture.StageAsync(pending, confirmed: false);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.ObjectObservations.CountAsync()).ShouldBe(1);
        }
        await Should.ThrowAsync<ConflictException>(() => fixture.Publisher.PublishSectionAsync(
            fixture.Project.Id, next.Id, pending, pending.Results[0].SectionId));

        var failed = DiscoveryFixture.Package() with
        {
            Objects = [],
            Results = [initial.Results[0] with
            {
                Status = CoverageStatus.Failed, ObjectCount = null, RelationshipCount = null,
                Errors = [new SectionIssue("AccessDenied", "Read permission missing.")]
            }]
        };
        var failedRun = await fixture.StageAsync(failed);
        await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, failedRun.Id, failed, failed.Results[0].SectionId);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.ObjectObservations.CountAsync()).ShouldBe(1);
        }
        var partial = DiscoveryFixture.Package("Observed partial value") with
        {
            Results = [initial.Results[0] with
            {
                Status = CoverageStatus.Partial, Warnings = [new SectionIssue("Limited", "Part of the scope was unavailable.")]
            }]
        };
        var partialRun = await fixture.StageAsync(partial);
        await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, partialRun.Id, partial, partial.Results[0].SectionId);
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.InventoryObjects.CountAsync()).ShouldBe(1);
        (await check.ObjectObservations.CountAsync()).ShouldBe(2);
        (await check.ObjectObservations.Select(x => x.DisplayName).ToListAsync()).ShouldContain("Synthetic User");
    }

    [SqlFact]
    public async Task ConcurrentSectionPublicationAndRelaunchAreIdempotent()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var package = DiscoveryFixture.Package();
        var run = await fixture.StageAsync(package);
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => fixture.Publisher.PublishSectionAsync(
            fixture.Project.Id, run.Id, package, package.Results[0].SectionId)));
        await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, run.Id, package, package.Results[0].SectionId);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.ObjectObservations.CountAsync()).ShouldBe(1);
        (await db.ExternalKeys.CountAsync()).ShouldBe(1);
    }

    [SqlFact]
    public async Task InvalidEnvironmentAndMidSectionFieldErrorPublishNothing()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var package = DiscoveryFixture.Package();
        var wrong = package with { Manifest = package.Manifest with { EnvironmentExternalId = "other.example.test" } };
        var wrongRun = await fixture.StageAsync(wrong);
        (await Should.ThrowAsync<ContractException>(() => fixture.Publisher.PublishSectionAsync(
            fixture.Project.Id, wrongRun.Id, wrong, wrong.Results[0].SectionId))).Code.ShouldBe("EnvironmentMismatch");

        var conflicting = DiscoveryFixture.Package() with
        {
            Objects =
            [
                package.Objects[0],
                package.Objects[0] with { ExternalId = "different-user", Attributes = new Dictionary<string, JsonElement>
                {
                    ["displayName"] = JsonSerializer.SerializeToElement(new string('a', 501))
                } }
            ],
            Results = [package.Results[0] with { ObjectCount = 2 }]
        };
        var run = await fixture.StageAsync(conflicting);
        (await Should.ThrowAsync<ContractException>(() => fixture.Publisher.PublishSectionAsync(
            fixture.Project.Id, run.Id, conflicting, conflicting.Results[0].SectionId))).Code.ShouldBe("IndexedAttributeLength");
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.ObjectObservations.CountAsync()).ShouldBe(0);
        (await db.InventoryObjects.CountAsync()).ShouldBe(0);
        (await db.SectionRuns.AnyAsync(x => x.PublishedAtUtc != null)).ShouldBeFalse();
    }

    [SqlFact]
    public async Task RenameAndLateObservationKeepStableIdentityAndAmbiguousAliasesSeparate()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var original = DiscoveryFixture.Package();
        var template = original.Objects[0];
        var alias = new ExternalReference("user", "synthetic.reference", "shared-alias");
        async Task Publish(int day, params (string Id, string Upn)[] users)
        {
            var date = template.ObservedAtUtc.AddDays(day);
            var package = original with
            {
                Manifest = original.Manifest with { RunId = Guid.NewGuid(), StartedAtUtc = date, CompletedAtUtc = date },
                Objects = users.Select(user => template with
                {
                    ExternalId = user.Id,
                    ObservedAtUtc = date,
                    AlternativeKeys = [alias],
                    Attributes = new Dictionary<string, JsonElement>
                    {
                        ["userPrincipalName"] = JsonSerializer.SerializeToElement(user.Upn),
                        ["displayName"] = JsonSerializer.SerializeToElement(user.Id)
                    }
                }).ToArray(),
                Results = [original.Results[0] with { CoveredFields = ["userPrincipalName", "displayName"], ObjectCount = users.Length }]
            };
            var run = await fixture.StageAsync(package);
            await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, run.Id, package, template.SectionId);
        }
        await Publish(0, ("one", "Old@example.test"), ("two", "Old@example.test"));
        await using var db = await fixture.Factory.CreateDbContextAsync();
        var id = await db.ExternalKeys.Where(k => k.ExternalId == "one").Select(k => k.ObjectId).SingleAsync();
        await Publish(2, ("one", "New@example.test"));
        await Publish(1, ("one", "Middle@example.test"));
        (await db.InventoryObjects.CountAsync()).ShouldBe(2);
        (await db.AlternateKeyPeriods.Where(k => k.Namespace == alias.Namespace && k.ValidUntilUtc == null)
            .Select(k => k.ObjectId).Distinct().CountAsync()).ShouldBe(2);
        var history = await db.AlternateKeyPeriods.Where(k => k.ObjectId == id && k.Namespace == "upn")
            .OrderBy(k => k.ValidFromUtc).ToListAsync();
        history.Select(k => k.OriginalValue).ShouldBe(["Old@example.test", "Middle@example.test", "New@example.test"]);
        history[0].ValidUntilUtc.ShouldBe(history[1].ValidFromUtc);
        history[1].ValidUntilUtc.ShouldBe(history[2].ValidFromUtc);
        history[2].ValidUntilUtc.ShouldBeNull();
        history[2].NormalizedValue.ShouldBe("NEW@EXAMPLE.TEST");
        var inventory = new InventoryQueries(fixture.Factory, fixture.Access);
        var filter = new InventoryFilter(EnvironmentRole.Source, fixture.Environment.Id, "example.test", Type: "user", Status: "Observed");
        var page = await inventory.PageAsync(fixture.Project.Id, filter, new(1, 1, "oLd@", true, "DisplayName"));
        page.TotalCount.ShouldBe(2);
        page.Items.Single().Id.ShouldBe(id);
        var found = await inventory.PageAsync(fixture.Project.Id, filter, new(Search: "ONE"));
        found.Items.Single().Upn.ShouldBe("New@example.test");
        (await inventory.PageAsync(fixture.Project.Id, filter with { Side = EnvironmentRole.Target }, new())).TotalCount.ShouldBe(0);
        var other = await fixture.OtherWorkspaceAsync();
        (await new InventoryQueries(other.Factory, other.Access).PageAsync(other.Project.Id, filter, new())).TotalCount.ShouldBe(0);
        await using var outsider = PersistenceTests.Create(fixture.ConnectionString, [Guid.NewGuid()]);
        (await outsider.AlternateKeyPeriods.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [SqlFact]
    public async Task AbsenceRequiresAnExhaustiveComparableSnapshot()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var template = DiscoveryFixture.Package();
        var capability = DiscoveryCatalog.Load().Sections["ad.users"];
        var first = template.Results[0] with { CoveredFields = capability.Attributes.Keys.ToArray(), CoveredRelations = capability.Relations };
        var original = template with { Results = [first] };
        var originalRun = await fixture.StageAsync(original);
        await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, originalRun.Id, original, first.SectionId);
        var day = 0;
        async Task<InventoryPresence> Empty(SectionResult result)
        {
            var date = template.Manifest.StartedAtUtc.AddDays(++day);
            var package = template with
            {
                Manifest = template.Manifest with { RunId = Guid.NewGuid(), Scope = result.Scope, StartedAtUtc = date, CompletedAtUtc = date },
                Objects = [],
                Results = [result with { ObjectCount = 0 }]
            };
            var run = await fixture.StageAsync(package);
            await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, run.Id, package, first.SectionId);
            await using var db = await fixture.Factory.CreateDbContextAsync();
            return (await db.InventoryObjects.SingleAsync()).Presence;
        }
        (await Empty(first with { Status = CoverageStatus.Partial, Warnings = [new SectionIssue("Limited", "Incomplete")] }))
            .ShouldBe(InventoryPresence.Observed);
        (await Empty(first with { CoveredFields = ["displayName"] })).ShouldBe(InventoryPresence.Observed);
        (await Empty(first with { Scope = new CollectionScope("ou", "OU=Other,DC=example,DC=test") })).ShouldBe(InventoryPresence.Observed);
        (await Empty(first)).ShouldBe(InventoryPresence.AbsentFromScope);
        await fixture.Publisher.RebuildEnvironmentAsync(fixture.Project.Id, fixture.Environment.Id);
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.InventoryObjects.SingleAsync()).Presence.ShouldBe(InventoryPresence.AbsentFromScope);
        (await check.CurrentValues.SingleAsync()).ValueJson.ShouldBe("\"Synthetic User\"");
    }

    [SqlFact]
    public async Task MembershipPathsResolveLaterArrivalsPreserveProofAndTerminateOnCycles()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var template = DiscoveryFixture.Package();
        var store = new MemoryArtifactStore();
        var service = ZipImportTests.Service(fixture, store);
        var source = new ExternalReference("user", "ad.objectGUID", "user");
        var group = new ExternalReference("group", "ad.objectGUID", "group");
        var parent = group with { ExternalId = "parent" };
        async Task Publish(int day, ExternalReference[] objects, (ExternalReference From, ExternalReference To)[] links)
        {
            var date = template.Manifest.StartedAtUtc.AddDays(day);
            const string section = "ad.groups";
            var package = template with
            {
                Manifest = template.Manifest with { RunId = Guid.NewGuid(), StartedAtUtc = date, CompletedAtUtc = date },
                Objects = objects.Select(r => new ObjectRecord(section, r.Type, r.Namespace, r.ExternalId, date,
                    new Dictionary<string, JsonElement> { ["displayName"] = JsonSerializer.SerializeToElement(r.ExternalId) })).ToArray(),
                Relationships = links.Select(r => new RelationshipRecord(section, "memberOf", r.From, r.To, date)).ToArray(),
                Results = [template.Results[0] with { SectionId = section, CapabilityId = section, CoveredRelations = ["memberOf"],
                    ObjectCount = objects.Length, RelationshipCount = links.Length }]
            };
            var run = await service.UploadAsync(fixture.Project.Id, fixture.Environment.Id, new MemoryStream(await ZipImportTests.BytesAsync(package)), "groups.zip");
            await service.ConfirmAsync(fixture.Project.Id, run);
            await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default);
        }
        await Publish(0, [source, group], [(source, group), (group, parent)]);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.InventoryRelationships.CountAsync(x => x.ToMethod == EndpointResolution.Pending)).ShouldBe(1);
        var original = await db.InventoryRelationships.Where(x => x.ToObjectId == null).Select(x => x.Id).SingleAsync();
        await Publish(1, [parent], [(group, parent), (parent, group)]);
        (await db.InventoryRelationships.CountAsync()).ShouldBe(3);
        (await db.InventoryRelationships.CountAsync(x => x.ToMethod == EndpointResolution.Pending)).ShouldBe(0);
        (await db.RelationshipSupports.CountAsync(x => x.RelationshipId == original)).ShouldBe(2);
        var ids = await db.ExternalKeys.ToDictionaryAsync(x => x.ExternalId, x => x.ObjectId);
        var graph = new InventoryGraph(fixture.Factory, fixture.Access);
        var paths = await graph.MembershipsAsync(fixture.Project.Id, ids["user"], false);
        paths.Count.ShouldBe(2);
        paths.Single(p => p.ObjectId == ids["parent"]).RelationshipIds.Count.ShouldBe(2);
        (await graph.MembershipsAsync(fixture.Project.Id, ids["parent"], true)).Single(p => p.ObjectId == ids["user"])
            .RelationshipIds.Count.ShouldBe(2);
        await Should.ThrowAsync<AccessDeniedException>(() => graph.MembershipsAsync(Guid.NewGuid(), ids["parent"], true));
        await using var outside = PersistenceTests.Create(fixture.ConnectionString, []);
        (await outside.InventoryRelationships.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.RelationshipSupports.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [SqlFact]
    public async Task ProcessingRechecksMembershipInsteadOfTrustingStagedWorkspace()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var package = DiscoveryFixture.Package();
        var run = await fixture.StageAsync(package);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        }
        await Should.ThrowAsync<AccessDeniedException>(() => fixture.Publisher.PublishSectionAsync(
            fixture.Project.Id, run.Id, package, package.Results[0].SectionId));
    }
}
