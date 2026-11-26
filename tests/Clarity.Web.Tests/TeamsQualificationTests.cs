using System.Net;
using Clarity.Core;
using Clarity.Web.Security;
using Shouldly;

namespace Clarity.Web.Tests;

public sealed class TeamsQualificationTests
{
    [Fact]
    public async Task TeamsDiscoveryAndSheetKeepFrenchQualificationsSeparateAndReaderSafe()
    {
        await using var app = new FoundationApplication();
        var (alpha, beta) = await app.SeedAsync();
        var workspace = DevelopmentAuthenticationHandler.AlphaWorkspace;
        var environment = new ProjectEnvironment
        {
            WorkspaceId = workspace,
            ProjectId = alpha,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = "10000000-0000-0000-0000-000000000001"
        };
        var team = new InventoryObject { WorkspaceId = workspace, EnvironmentId = environment.Id, Type = "team", DisplayName = "Synthetic Teams" };
        var author = DevelopmentAuthenticationHandler.Personas["consultant"];
        await using (var db = app.CreateUnscopedContext())
        {
            db.AddRange(environment, team, new ExternalKey
            {
                WorkspaceId = workspace,
                EnvironmentId = environment.Id,
                ObjectId = team.Id,
                Type = "team",
                Namespace = "microsoft.graph",
                ExternalId = "20000000-0000-0000-0000-000000000002"
            }, new Assessment
            {
                WorkspaceId = workspace,
                ProjectId = alpha,
                EnvironmentId = environment.Id,
                ObjectId = team.Id,
                Kind = AssessmentKind.TeamsChatScope,
                ScopeValue = QualificationScopeValue.Included,
                Revision = 1,
                Reason = "Synthetic chat population workshop",
                EvidenceReference = "Synthetic T-1",
                MetadataFingerprint = new string('a', 64),
                AuthorTenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
                AuthorObjectId = author
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        using var consultant = await app.CreatePersonaClientAsync("consultant");
        var discovery = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/discovery?view=Connections&producer=clarity.graph.teams",
            TestContext.Current.CancellationToken));
        discovery.ShouldContain("Microsoft Graph — Teams (v1.0)");
        var html = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/inventory?object={team.Id}", TestContext.Current.CancellationToken));
        html.ShouldContain("Qualification du périmètre des chats");
        html.ShouldContain("Qualification du périmètre de la téléphonie");
        html.ShouldContain("distinct des observations Microsoft Graph");
        html.ShouldContain("Périmètre non qualifié : inconnu, et non hors périmètre");
        html.ShouldContain("Populations, dépendances et justification du périmètre");
        html.ShouldContain("Synthetic chat population workshop");
        html.ShouldContain(author.ToString());
        html.ShouldContain("Historique des qualifications du consultant");
        using var reader = await app.CreatePersonaClientAsync("reader");
        var readOnly = WebUtility.HtmlDecode(await reader.GetStringAsync($"/projects/{alpha}/inventory?object={team.Id}", TestContext.Current.CancellationToken));
        readOnly.ShouldContain("Synthetic chat population workshop");
        readOnly.ShouldNotContain("Populations, dépendances et justification du périmètre");
        readOnly.ShouldNotContain("Recharger la qualification");
        var foreign = await consultant.GetStringAsync($"/projects/{beta}/inventory?object={team.Id}", TestContext.Current.CancellationToken);
        foreign.ShouldNotContain("Synthetic chat population workshop");
        using var admin = await app.CreatePersonaClientAsync("platform-admin");
        var administrative = await admin.GetStringAsync($"/projects/{alpha}/inventory?object={team.Id}", TestContext.Current.CancellationToken);
        administrative.ShouldNotContain("Synthetic chat population workshop");
    }
}
