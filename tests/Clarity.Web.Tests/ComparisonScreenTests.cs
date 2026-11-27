using System.Net;
using Clarity.Contracts;
using Clarity.Core;
using Clarity.Web.Security;
using Shouldly;

namespace Clarity.Web.Tests;

public sealed class ComparisonScreenTests
{
    [Fact]
    public async Task ComparisonScreenShowsFrenchTotalsAndCoverageAndHidesCompareControlsFromReaders()
    {
        await using var app = new FoundationApplication();
        var (alpha, beta) = await app.SeedAsync();
        var workspace = DevelopmentAuthenticationHandler.AlphaWorkspace;
        var environment = new ProjectEnvironment
        {
            WorkspaceId = workspace,
            ProjectId = alpha,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = "00000000-0000-0000-0000-000000000321"
        };
        var scope = new CollectionScope("tenant", environment.ExternalId);
        CollectionRun Run(DateTimeOffset started, string hash) => new()
        {
            WorkspaceId = workspace,
            ProjectId = alpha,
            EnvironmentId = environment.Id,
            ExternalRunId = Guid.NewGuid(),
            Channel = CollectionChannel.Graph,
            ProducerId = "clarity.graph.teams",
            ProducerVersion = "1.0.0",
            ScopeJson = DiscoveryScope.Serialize(scope),
            ScopeHash = DiscoveryScope.Hash(scope),
            ManifestJson = "{}",
            ContentHash = hash,
            State = CollectionRunState.Completed,
            StartedAtUtc = started,
            CompletedAtUtc = started,
            CorrelationId = "synthetic"
        };
        var baseline = Run(new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), new string('a', 64));
        var later = Run(new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), new string('b', 64));
        ComparisonUnit[] units =
        [
            new("teams.teams", "tenant", environment.ExternalId, UnitComparison.Comparable, CoverageStatus.Complete, CoverageStatus.Complete, 2, 2, 1, 1, 0),
            new("teams.sites", "tenant", environment.ExternalId, UnitComparison.NotExhaustive, CoverageStatus.Complete, CoverageStatus.Partial, 2, 1, 0, 0, 1)
        ];
        var comparison = new RunComparison
        {
            WorkspaceId = workspace,
            ProjectId = alpha,
            EnvironmentId = environment.Id,
            BaselineRunId = baseline.Id,
            LaterRunId = later.Id,
            OperationId = Guid.NewGuid(),
            ActorTenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
            ActorObjectId = DevelopmentAuthenticationHandler.Personas["consultant"],
            ComputedAtUtc = DateTimeOffset.UtcNow,
            Added = 1,
            Modified = 1,
            Removed = 3,
            NotObserved = 1,
            Unchanged = 1,
            RelationsAdded = 1,
            RelationsRemoved = 1,
            RelationsUnknown = 1,
            UnitsJson = System.Text.Json.JsonSerializer.Serialize(units, ContractJson.Options)
        };
        await using (var db = app.CreateUnscopedContext())
        {
            db.AddRange(environment, baseline, later, comparison);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var token = TestContext.Current.CancellationToken;
        using var consultant = await app.CreatePersonaClientAsync("consultant");
        var html = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/comparison", token));
        html.ShouldContain("Collectes successives");
        html.ShouldContain("Comparer deux collectes");
        html.ShouldContain("Supprimé : 3");
        html.ShouldContain("Non observé (couverture non exhaustive) : 1");
        html.ShouldContain("Couverture non exhaustive");
        html.ShouldContain("jamais supprimé");
        html.ShouldContain("Décisions à revoir");

        using var reader = await app.CreatePersonaClientAsync("reader");
        var readOnly = WebUtility.HtmlDecode(await reader.GetStringAsync($"/projects/{alpha}/comparison", token));
        readOnly.ShouldContain("Supprimé : 3");
        readOnly.ShouldNotContain("Comparer deux collectes");
        var foreign = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{beta}/comparison", token));
        foreign.ShouldNotContain("Supprimé : 3");
    }
}
