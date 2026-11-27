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
        var importForm = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(import);
        importForm.QuerySelectorAll("fluent-dropdown fluent-option").Select(option => option.TextContent.Trim()).ShouldContain("UPN");
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

    [Fact]
    public async Task DependencyViewShowsSharedVolumeAndCoverageInFrenchWithoutAnalyzeControlsForReaders()
    {
        await using var app = new FoundationApplication();
        var (alpha, beta) = await app.SeedAsync();
        var workspace = DevelopmentAuthenticationHandler.AlphaWorkspace;
        var environment = new ProjectEnvironment
        {
            WorkspaceId = workspace,
            ProjectId = alpha,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = "00000000-0000-0000-0000-000000000123"
        };
        var site = new InventoryObject { WorkspaceId = workspace, EnvironmentId = environment.Id, Type = "site", DisplayName = "Synthetic Project site" };
        DependencyFamilyCoverage[] coverage =
        [
            new(DependencyFamily.Sites, DependencyCoverage.Partial,
                [new("sharepoint.sitePermissions", Clarity.Contracts.CoverageStatus.Complete, DateTimeOffset.UtcNow, true)], 0, 1),
            new(DependencyFamily.Applications, DependencyCoverage.NotCollected, [new("apps.ssoApplications", null, null, false)], 0, 0)
        ];
        var analysis = new DependencyAnalysis
        {
            WorkspaceId = workspace,
            ProjectId = alpha,
            EnvironmentId = environment.Id,
            Wave = "Vague synthétique 1",
            OperationId = Guid.NewGuid(),
            ActorTenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
            ActorObjectId = DevelopmentAuthenticationHandler.Personas["consultant"],
            ComputedAtUtc = DateTimeOffset.UtcNow,
            MigratingPrincipals = 1,
            RemainingPrincipals = 1,
            Resources = 1,
            SharedResources = 1,
            KnownVolumeBytes = 1L << 30,
            SharedVolumeBytes = 1L << 30,
            CoverageJson = System.Text.Json.JsonSerializer.Serialize(coverage, Clarity.Contracts.ContractJson.Options)
        };
        await using (var db = app.CreateUnscopedContext())
        {
            db.AddRange(environment, site, analysis, new DependencyResource
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
                RemainingCount = 1,
                RouteCount = 3,
                VolumeApplies = true,
                VolumeBytes = 1L << 30
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var token = TestContext.Current.CancellationToken;
        using var consultant = await app.CreatePersonaClientAsync("consultant");
        var html = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/scope?view=dependencies", token));
        html.ShouldContain("Dépendances et ressources partagées");
        html.ShouldContain("Calculer les dépendances");
        html.ShouldContain("Vague synthétique 1");
        html.ShouldContain("Partagée : 1");
        html.ShouldContain("Volume connu : 1,0 Gio");
        html.ShouldContain("Sites et OneDrive");
        html.ShouldContain("Partielle");
        html.ShouldContain("collecte par ressource");
        html.ShouldContain("Non collectée");

        using var reader = await app.CreatePersonaClientAsync("reader");
        var readOnly = WebUtility.HtmlDecode(await reader.GetStringAsync($"/projects/{alpha}/scope?view=dependencies", token));
        readOnly.ShouldContain("Volume connu : 1,0 Gio");
        readOnly.ShouldNotContain("Calculer les dépendances");
        var foreign = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{beta}/scope?view=dependencies", token));
        foreign.ShouldNotContain("Vague synthétique 1");
    }

    [Fact]
    public async Task DecisionRegisterShowsFlaggedDecisionsInFrenchAndReviewActionsOnlyToConsultants()
    {
        await using var app = new FoundationApplication();
        var (alpha, beta) = await app.SeedAsync();
        var workspace = DevelopmentAuthenticationHandler.AlphaWorkspace;
        var environment = new ProjectEnvironment { WorkspaceId = workspace, ProjectId = alpha, Kind = EnvironmentKind.AdForest, ExternalId = "forest.example.test" };
        var user = new InventoryObject { WorkspaceId = workspace, EnvironmentId = environment.Id, Type = "user", DisplayName = "Synthetic Alice" };
        var decision = new ScopeDecision
        {
            WorkspaceId = workspace,
            ProjectId = alpha,
            EnvironmentId = environment.Id,
            ObjectId = user.Id,
            Value = ScopeValue.Include,
            Fingerprint = new string('a', 64),
            Revision = 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await using (var db = app.CreateUnscopedContext())
        {
            db.AddRange(environment, user, decision, new ReviewFlag
            {
                WorkspaceId = workspace,
                ProjectId = alpha,
                EnvironmentId = environment.Id,
                ObjectId = user.Id,
                Kind = DecisionKind.Scope,
                DecisionRevision = 1,
                Fingerprint = new string('b', 64),
                ChangesJson = "[]",
                FlaggedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var token = TestContext.Current.CancellationToken;
        using var consultant = await app.CreatePersonaClientAsync("consultant");
        var html = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/scope?view=review", token));
        html.ShouldContain(">Registre des décisions</");
        html.ShouldContain("Périmètre : 1 · À revoir : 1");
        html.ShouldContain("Recalculer les conditions de revue");
        html.ShouldContain("À revoir uniquement");

        using var reader = await app.CreatePersonaClientAsync("reader");
        var readOnly = WebUtility.HtmlDecode(await reader.GetStringAsync($"/projects/{alpha}/scope?view=review", token));
        readOnly.ShouldContain("Périmètre : 1 · À revoir : 1");
        readOnly.ShouldNotContain("Recalculer les conditions de revue");
        var foreign = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{beta}/scope?view=review", token));
        foreign.ShouldNotContain("Périmètre : 1");
    }
}
