using System.Net;
using System.Net.Http.Json;
using Clarity.Core;
using Clarity.Infrastructure;
using Clarity.Web.Security;
using Clarity.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace Clarity.Web.Tests;

public class AuthorizationEndpointTests
{
    [Fact]
    public async Task Source_only_project_can_add_a_planned_target_later_and_render_navigation()
    {
        await using var app = new FoundationApplication();
        var (alpha, _) = await app.SeedAsync();
        using var client = await app.CreatePersonaClientAsync("manager");
        var source = new EnvironmentInput(Guid.NewGuid(), EnvironmentRole.Source, EnvironmentKind.M365Tenant,
            "synthetic-tenant", "Declared scope", TargetState.Existing, "");
        (await client.PostAsJsonAsync($"/api/projects/{alpha}/environments", source, cancellationToken: TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var target = new EnvironmentInput(Guid.NewGuid(), EnvironmentRole.Target, EnvironmentKind.M365Tenant,
            "", "", TargetState.Planned, "target.example.invalid");
        (await client.PostAsJsonAsync($"/api/projects/{alpha}/environments", target, cancellationToken: TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var environments = await client.GetFromJsonAsync<ProjectEnvironment[]>($"/api/projects/{alpha}/environments", cancellationToken: TestContext.Current.CancellationToken);
        environments!.Length.ShouldBe(2);
        environments.ShouldContain(x => x.TargetState == TargetState.Planned && x.Role == EnvironmentRole.Target);
        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/projects/{alpha}", TestContext.Current.CancellationToken));
        html.ShouldContain("Synthetic Alpha");
        html.ShouldContain("Vue d'ensemble");
        html.ShouldContain("Discovery");
        html.ShouldContain("Déclarée");
        html.ShouldContain("Côté actif");
    }

    [Fact]
    public async Task Guessed_project_id_in_another_workspace_cannot_be_read_or_archived()
    {
        await using var app = new FoundationApplication();
        var (alpha, beta) = await app.SeedAsync();
        using var client = await app.CreatePersonaClientAsync("manager");
        (await client.GetAsync($"/api/projects/{alpha}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/projects/{beta}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/projects/{beta}/archive", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/workspaces/{DevelopmentAuthenticationHandler.BetaWorkspace}/projects", TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await using var db = app.CreateUnscopedContext();
        (await db.Projects.IgnoreQueryFilters().SingleAsync(x => x.Id == beta, cancellationToken: TestContext.Current.CancellationToken)).Status.ShouldBe(ProjectStatus.Active);
    }

    [Fact]
    public async Task Reader_cannot_mutate_and_manager_action_is_audited()
    {
        await using var app = new FoundationApplication();
        var (alpha, _) = await app.SeedAsync();
        using var reader = await app.CreatePersonaClientAsync("reader");
        (await reader.GetAsync($"/api/projects/{alpha}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsync($"/api/projects/{alpha}/archive", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var manager = await app.CreatePersonaClientAsync("manager");
        (await manager.PostAsync($"/api/projects/{alpha}/archive", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = app.CreateUnscopedContext();
        (await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.ResourceId == alpha && x.Action == "ProjectArchived", cancellationToken: TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Platform_admin_needs_explicit_membership_for_inventory()
    {
        await using var app = new FoundationApplication();
        var (alpha, _) = await app.SeedAsync();
        using var admin = await app.CreatePersonaClientAsync("platform-admin");
        (await admin.GetAsync($"/api/projects/{alpha}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var input = new WorkspaceInput(Guid.NewGuid(), "Synthetic new client");
        (await admin.PostAsJsonAsync("/api/workspaces", input, cancellationToken: TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync("/api/workspaces", input, cancellationToken: TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync("/api/workspaces", input with { Name = "Different content" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using var db = app.CreateUnscopedContext();
        (await db.Workspaces.IgnoreQueryFilters().CountAsync(x => x.Id == input.OperationId, cancellationToken: TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Fake_authentication_is_not_registered_outside_local_environments(string environment)
    {
        await using var app = new FoundationApplication(environment);
        using var scope = app.Services.CreateScope();
        var schemes = await scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        schemes.ShouldNotContain(x => x.Name == DevelopmentAuthenticationHandler.SchemeName);
    }

    [Fact]
    public async Task Cookie_authenticated_mutation_requires_antiforgery_token()
    {
        await using var app = new FoundationApplication();
        var (alpha, _) = await app.SeedAsync();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        (await client.PostAsync($"/api/projects/{alpha}/archive", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

internal sealed class FoundationApplication(string environment = "Testing", bool sql = false, bool discoveryStorage = false) : WebApplicationFactory<Program>
{
    private readonly DbContextOptions<ClarityDbContext> _options = CreateOptions(sql);

    private static DbContextOptions<ClarityDbContext> CreateOptions(bool sql)
    {
        var builder = new DbContextOptionsBuilder<ClarityDbContext>();
        if (!sql) { return builder.UseInMemoryDatabase(Guid.NewGuid().ToString()).Options; }
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLARITY_TEST_SQL"))
        { InitialCatalog = $"ClarityWebTest_{Guid.NewGuid():N}" };
        return builder.UseSqlServer(connection.ConnectionString).Options;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("AzureAd:Instance", "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId", DevelopmentAuthenticationHandler.SyntheticTenant.ToString());
        builder.UseSetting("AzureAd:ClientId", DevelopmentAuthenticationHandler.Personas["manager"].ToString());
        builder.UseSetting("ConnectionStrings:Clarity", "Server=localhost;Database=unused;Integrated Security=True");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextFactory<ClarityDbContext>>();
            services.AddScoped<IDbContextFactory<ClarityDbContext>>(provider =>
                new TestContextFactory(_options, provider.GetRequiredService<IWorkspaceContext>()));
            if (discoveryStorage)
            {
                services.RemoveAll<IArtifactStore>();
                services.AddSingleton<IArtifactStore, TestArtifactStore>();
                services.RemoveAll<IExportStore>();
                services.AddSingleton<IExportStore, TestExportStore>();
                services.RemoveAll<DiscoveryAvailability>();
                services.AddSingleton(new DiscoveryAvailability(true, false));
            }
        });
    }

    internal ClarityDbContext CreateUnscopedContext() => new TestContextFactory(_options,
        new WorkspaceScope(default, [DevelopmentAuthenticationHandler.AlphaWorkspace, DevelopmentAuthenticationHandler.BetaWorkspace], true)).CreateDbContext();

    internal async Task<(Guid Alpha, Guid Beta)> SeedAsync()
    {
        await using var db = CreateUnscopedContext();
        if (sql) { await db.Database.MigrateAsync(); }
        db.Workspaces.AddRange(
            new ClientWorkspace { Id = DevelopmentAuthenticationHandler.AlphaWorkspace, Name = "Synthetic Alpha" },
            new ClientWorkspace { Id = DevelopmentAuthenticationHandler.BetaWorkspace, Name = "Synthetic Beta" });
        var alpha = new Project { WorkspaceId = DevelopmentAuthenticationHandler.AlphaWorkspace, Name = "Alpha audit" };
        var beta = new Project { WorkspaceId = DevelopmentAuthenticationHandler.BetaWorkspace, Name = "Beta audit" };
        db.Projects.AddRange(alpha, beta);
        foreach (var (persona, role) in new[] { ("manager", WorkspaceRole.WorkspaceManager), ("consultant", WorkspaceRole.Consultant), ("reader", WorkspaceRole.Reader) })
        {
            db.Members.Add(new WorkspaceMember
            {
                WorkspaceId = DevelopmentAuthenticationHandler.AlphaWorkspace,
                Role = role,
                TenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
                ObjectId = DevelopmentAuthenticationHandler.Personas[persona]
            });
        }
        await db.SaveChangesAsync();
        return (alpha.Id, beta.Id);
    }

    internal async Task<HttpClient> CreatePersonaClientAsync(string persona)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Clarity-Persona", persona);
        var token = await client.GetFromJsonAsync<AntiforgeryToken>("/api/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!.Token);
        return client;
    }

    private sealed record AntiforgeryToken(string Token);
    private sealed class TestContextFactory(DbContextOptions<ClarityDbContext> options, IWorkspaceContext scope) : IDbContextFactory<ClarityDbContext>
    {
        public ClarityDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<ClarityDbContext>(options).AddInterceptors(new WorkspaceConnectionInterceptor(scope)).Options, scope);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (sql)
        {
            await using var db = CreateUnscopedContext();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
