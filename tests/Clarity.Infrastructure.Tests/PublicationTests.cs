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
                    Attributes = new Dictionary<string, JsonElement> { ["userPrincipalName"] = JsonSerializer.SerializeToElement(user.Upn) }
                }).ToArray(),
                Results = [original.Results[0] with { CoveredFields = ["userPrincipalName"], ObjectCount = users.Length }]
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
        await using var outsider = PersistenceTests.Create(fixture.ConnectionString, [Guid.NewGuid()]);
        (await outsider.AlternateKeyPeriods.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
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
