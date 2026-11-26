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
    public async Task InvalidEnvironmentAndMidSectionIdentityConflictPublishNothing()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var package = DiscoveryFixture.Package();
        var wrong = package with { Manifest = package.Manifest with { EnvironmentExternalId = "other.example.test" } };
        var wrongRun = await fixture.StageAsync(wrong);
        (await Should.ThrowAsync<ContractException>(() => fixture.Publisher.PublishSectionAsync(
            fixture.Project.Id, wrongRun.Id, wrong, wrong.Results[0].SectionId))).Code.ShouldBe("EnvironmentMismatch");

        var alias = new ExternalReference("user", "ad.objectSid", "S-1-5-21-100-200-300-1001");
        var conflicting = DiscoveryFixture.Package() with
        {
            Objects =
            [
                package.Objects[0] with { AlternativeKeys = [alias] },
                package.Objects[0] with { ExternalId = "different-user", AlternativeKeys = [alias] }
            ],
            Results = [package.Results[0] with { ObjectCount = 2 }]
        };
        var run = await fixture.StageAsync(conflicting);
        (await Should.ThrowAsync<ContractException>(() => fixture.Publisher.PublishSectionAsync(
            fixture.Project.Id, run.Id, conflicting, conflicting.Results[0].SectionId))).Code.ShouldBe("AmbiguousExternalKey");
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.ObjectObservations.CountAsync()).ShouldBe(0);
        (await db.InventoryObjects.CountAsync()).ShouldBe(0);
        (await db.SectionRuns.AnyAsync(x => x.PublishedAtUtc != null)).ShouldBeFalse();
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
