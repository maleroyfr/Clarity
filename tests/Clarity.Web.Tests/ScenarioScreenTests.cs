using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clarity.Core;
using Clarity.Web.Security;
using Shouldly;

namespace Clarity.Web.Tests;

public sealed class ScenarioScreenTests
{
    [Fact]
    public async Task CarveOutOverviewGuidesTheConsultantThroughScopingAndSharedResourcesWithoutLeakingAcrossWorkspaces()
    {
        await using var app = new FoundationApplication();
        var (_, beta) = await app.SeedAsync();
        var token = TestContext.Current.CancellationToken;
        var workspace = DevelopmentAuthenticationHandler.AlphaWorkspace;
        var author = DevelopmentAuthenticationHandler.Personas["consultant"];
        var project = new Project { WorkspaceId = workspace, Name = "Synthetic carve-out", Scenario = ProjectScenario.CarveOut };
        var environment = new ProjectEnvironment
        {
            WorkspaceId = workspace,
            ProjectId = project.Id,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = "00000000-0000-0000-0000-000000000456"
        };
        var leaving = new InventoryObject { WorkspaceId = workspace, EnvironmentId = environment.Id, Type = "user", DisplayName = "Synthetic leaver" };
        var pending = new InventoryObject { WorkspaceId = workspace, EnvironmentId = environment.Id, Type = "user", DisplayName = "Synthetic pending" };
        var site = new InventoryObject { WorkspaceId = workspace, EnvironmentId = environment.Id, Type = "site", DisplayName = "Synthetic shared site" };
        var population = new Population
        {
            WorkspaceId = workspace,
            ProjectId = project.Id,
            EnvironmentId = environment.Id,
            Name = "Synthetic departing division",
            Kind = PopulationKind.Domain,
            Criterion = "example.test",
            MemberCount = 2,
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            AuthorTenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
            AuthorObjectId = author
        };
        ScopeDecision Decision(InventoryObject target, ScopeValue value) => new()
        {
            WorkspaceId = workspace,
            ProjectId = project.Id,
            EnvironmentId = environment.Id,
            ObjectId = target.Id,
            Value = value,
            PopulationId = population.Id,
            Fingerprint = new string('a', 64),
            Revision = 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var analysis = new DependencyAnalysis
        {
            WorkspaceId = workspace,
            ProjectId = project.Id,
            EnvironmentId = environment.Id,
            OperationId = Guid.NewGuid(),
            ActorTenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
            ActorObjectId = author,
            ComputedAtUtc = DateTimeOffset.UtcNow,
            MigratingPrincipals = 1,
            RemainingPrincipals = 4,
            Resources = 1,
            SharedResources = 1,
            CoverageJson = "[]"
        };
        await using (var db = app.CreateUnscopedContext())
        {
            db.AddRange(project, environment, leaving, pending, site, population, Decision(leaving, ScopeValue.Include),
                Decision(pending, ScopeValue.ToArbitrate), analysis, new DependencyResource
                {
                    WorkspaceId = workspace,
                    AnalysisId = analysis.Id,
                    EnvironmentId = environment.Id,
                    ObjectId = site.Id,
                    Family = DependencyFamily.Sites,
                    Type = "site",
                    DisplayName = site.DisplayName,
                    Classification = DependencyClassification.Shared,
                    MigratingCount = 1,
                    RemainingCount = 4,
                    RouteCount = 2
                });
            await db.SaveChangesAsync(token);
        }

        using var consultant = await app.CreatePersonaClientAsync("consultant");
        var html = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{project.Id}", token));
        html.ShouldContain("Parcours du scénario · Carve-out");
        html.ShouldContain("Définir la population qui part, inventorier ses dépendances et identifier les ressources également utilisées par les personnes qui restent.");
        html.ShouldContain("Prochaine étape");
        html.ShouldContain("Collecter la source");
        html.ShouldContain("Analyser les dépendances");
        html.ShouldContain("Ressources partagées distinctes : 1");
        html.ShouldContain("Principaux restants : 4");
        html.ShouldContain("À arbitrer : 1");
        html.ShouldContain($"/projects/{project.Id}/scope?side=Source&view=dependencies");

        using var reader = await app.CreatePersonaClientAsync("reader");
        var overview = await reader.GetFromJsonAsync<JsonElement>($"/api/projects/{project.Id}/scenario", token);
        var facts = overview.GetProperty("facts");
        (facts.GetProperty("populations").GetInt32(), facts.GetProperty("included").GetInt32(), facts.GetProperty("toArbitrate").GetInt32())
            .ShouldBe((1, 1, 1));
        facts.GetProperty("sharedResources").GetInt32().ShouldBe(1);
        overview.GetProperty("next").GetProperty("hintKey").GetString().ShouldBe("StepHint_SourceCollection");

        (await consultant.GetAsync($"/api/projects/{beta}/scenario", token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var admin = await app.CreatePersonaClientAsync("platform-admin");
        (await admin.GetAsync($"/api/projects/{project.Id}/scenario", token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        WebUtility.HtmlDecode(await admin.GetStringAsync($"/projects/{project.Id}", token)).ShouldNotContain("Ressources partagées distinctes");
    }
}
