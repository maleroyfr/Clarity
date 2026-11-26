using System.Net;
using Clarity.Core;
using Clarity.Web.Security;
using Shouldly;

namespace Clarity.Web.Tests;

public sealed class SsoQualificationTests
{
    [Fact]
    public async Task SsoObjectSheetShowsFrenchManualProvenanceAndOnlyConsultantsCanEdit()
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
        var item = new InventoryObject
        {
            WorkspaceId = workspace,
            EnvironmentId = environment.Id,
            Type = "servicePrincipal",
            DisplayName = "Synthetic SSO portal"
        };
        var author = DevelopmentAuthenticationHandler.Personas["consultant"];
        await using (var db = app.CreateUnscopedContext())
        {
            db.AddRange(environment, item,
                new ExternalKey
                {
                    WorkspaceId = workspace,
                    EnvironmentId = environment.Id,
                    ObjectId = item.Id,
                    Type = item.Type,
                    Namespace = "microsoft.graph",
                    ExternalId = "20000000-0000-0000-0000-000000000002"
                },
                new Assessment
                {
                    WorkspaceId = workspace,
                    ProjectId = alpha,
                    EnvironmentId = environment.Id,
                    ObjectId = item.Id,
                    Revision = 1,
                    Value = SsoDependencyValue.Required,
                    Protocol = SsoProtocol.Saml,
                    Reason = "Synthetic federation dependency",
                    EvidenceReference = "Synthetic workshop SSO-1",
                    MetadataFingerprint = new string('a', 64),
                    AuthorTenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
                    AuthorObjectId = author
                });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        using var consultant = await app.CreatePersonaClientAsync("consultant");
        var html = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/inventory?object={item.Id}", TestContext.Current.CancellationToken));
        html.ShouldContain("Qualification des dépendances SSO");
        html.ShouldContain("distinct des observations Microsoft Graph");
        html.ShouldContain("Il ne prouve ni un SSO opérationnel ni un accès effectif");
        html.ShouldContain("Dépendances et justification");
        html.ShouldContain("Synthetic federation dependency");
        html.ShouldContain(author.ToString());
        html.ShouldContain(environment.ExternalId);
        html.ShouldContain("Historique des qualifications du consultant");
        using var reader = await app.CreatePersonaClientAsync("reader");
        var readOnly = WebUtility.HtmlDecode(await reader.GetStringAsync($"/projects/{alpha}/inventory?object={item.Id}", TestContext.Current.CancellationToken));
        readOnly.ShouldContain("Synthetic federation dependency");
        readOnly.ShouldNotContain("Dépendances et justification");
        readOnly.ShouldNotContain("Recharger la qualification");
        var foreign = await consultant.GetStringAsync($"/projects/{beta}/inventory?object={item.Id}", TestContext.Current.CancellationToken);
        foreign.ShouldNotContain("Synthetic federation dependency");
        using var admin = await app.CreatePersonaClientAsync("platform-admin");
        var administrative = await admin.GetStringAsync($"/projects/{alpha}/inventory?object={item.Id}", TestContext.Current.CancellationToken);
        administrative.ShouldNotContain("Synthetic federation dependency");
    }
}
