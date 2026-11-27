using System.Net;
using Clarity.Core;
using Clarity.Web.Security;
using Shouldly;

namespace Clarity.Web.Tests;

public sealed class ScopeScreenTests
{
    [Fact]
    public async Task ScopeScreenShowsFrenchSummaryAndControlsOnlyToAuthorizedConsultants()
    {
        await using var app = new FoundationApplication();
        var (alpha, beta) = await app.SeedAsync();
        var workspace = DevelopmentAuthenticationHandler.AlphaWorkspace;
        var author = DevelopmentAuthenticationHandler.Personas["consultant"];
        var environment = new ProjectEnvironment { WorkspaceId = workspace, ProjectId = alpha, Kind = EnvironmentKind.AdForest, ExternalId = "forest.example.test" };
        var user = new InventoryObject { WorkspaceId = workspace, EnvironmentId = environment.Id, Type = "user", DisplayName = "Synthetic Alice" };
        var population = new Population
        {
            WorkspaceId = workspace,
            ProjectId = alpha,
            EnvironmentId = environment.Id,
            Name = "Synthetic Sales population",
            Kind = PopulationKind.Domain,
            Criterion = "example.test",
            MemberCount = 1,
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            AuthorTenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
            AuthorObjectId = author
        };
        var decision = new ScopeDecision
        {
            WorkspaceId = workspace,
            ProjectId = alpha,
            EnvironmentId = environment.Id,
            ObjectId = user.Id,
            Value = ScopeValue.Include,
            Wave = "Vague synthétique 1",
            PopulationId = population.Id,
            Fingerprint = new string('a', 64),
            Revision = 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await using (var db = app.CreateUnscopedContext())
        {
            db.AddRange(environment, user, population, decision,
                new PopulationMember { WorkspaceId = workspace, PopulationId = population.Id, EnvironmentId = environment.Id, ObjectId = user.Id },
                new ScopeDecisionRevision
                {
                    WorkspaceId = workspace,
                    DecisionId = decision.Id,
                    OperationId = Guid.NewGuid(),
                    Number = 1,
                    Value = ScopeValue.Include,
                    Wave = decision.Wave,
                    PopulationId = population.Id,
                    Fingerprint = decision.Fingerprint,
                    Reason = "Synthetic wave workshop",
                    ActorTenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
                    ActorObjectId = author
                });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var token = TestContext.Current.CancellationToken;
        using var consultant = await app.CreatePersonaClientAsync("consultant");
        var html = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/scope", token));
        html.ShouldContain("Assistant d’import de liste");
        html.ShouldContain("Populations : 1");
        html.ShouldContain("Inclure · Vague synthétique 1 : 1");
        html.ShouldContain("Créer une population");
        var import = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/scope?view=import", token));
        import.ShouldContain("Résoudre la liste");
        import.ShouldContain(">UPN</option>");
        var decisions = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/scope?view=decisions", token));
        decisions.ShouldContain("Filtrer par décision");
        decisions.ShouldContain("À arbitrer");

        using var reader = await app.CreatePersonaClientAsync("reader");
        var readOnly = WebUtility.HtmlDecode(await reader.GetStringAsync($"/projects/{alpha}/scope", token));
        readOnly.ShouldContain("Inclure · Vague synthétique 1 : 1");
        readOnly.ShouldNotContain("Créer une population");
        var readOnlyImport = WebUtility.HtmlDecode(await reader.GetStringAsync($"/projects/{alpha}/scope?view=import", token));
        readOnlyImport.ShouldContain("Consultation seule : vous ne pouvez pas modifier le périmètre de ce projet.");
        readOnlyImport.ShouldNotContain("Résoudre la liste");

        var foreign = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{beta}/scope", token));
        foreign.ShouldNotContain("Vague synthétique 1");
        using var admin = await app.CreatePersonaClientAsync("platform-admin");
        var administrative = WebUtility.HtmlDecode(await admin.GetStringAsync($"/projects/{alpha}/scope", token));
        administrative.ShouldNotContain("Vague synthétique 1");
        administrative.ShouldNotContain("Créer une population");
    }
}
