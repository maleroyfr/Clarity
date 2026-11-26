using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure.Tests;

internal sealed class DiscoveryFixture(TestDatabase database) : IAsyncDisposable
{
    private TestDatabase Database { get; } = database;
    public string ConnectionString => Database.ConnectionString;
    public WorkspaceScope Scope => new(Identity, [Workspace.Id]);
    public JobService Jobs => new(Factory, Access, Scope);
    public ClientWorkspace Workspace { get; } = new() { Name = "Synthetic Discovery" };
    public Project Project { get; private set; } = null!;
    public ProjectEnvironment Environment { get; private set; } = null!;
    public UserIdentity Identity { get; } = new(Guid.NewGuid(), Guid.NewGuid());
    public IDbContextFactory<ClarityDbContext> Factory { get; private set; } = null!;
    public StoredProjectAccess Access { get; private set; } = null!;
    public PublicationService Publisher => new(Factory, Access, new PackageValidator(), DiscoveryCatalog.Load());

    public static async Task<DiscoveryFixture> CreateAsync()
    {
        var fixture = new DiscoveryFixture(await TestDatabase.CreateAsync());
        var scope = new WorkspaceScope(fixture.Identity, [fixture.Workspace.Id]);
        fixture.Factory = new TestContextFactory(fixture.Database.ConnectionString, scope);
        fixture.Access = new StoredProjectAccess(fixture.Factory, scope);
        fixture.Project = new Project { WorkspaceId = fixture.Workspace.Id, Name = "Synthetic audit" };
        fixture.Environment = new ProjectEnvironment
        {
            WorkspaceId = fixture.Workspace.Id,
            ProjectId = fixture.Project.Id,
            ExternalId = "forest.example.test",
            Kind = EnvironmentKind.AdForest
        };
        await using var db = await fixture.Factory.CreateDbContextAsync();
        db.Workspaces.Add(fixture.Workspace);
        db.Members.Add(new WorkspaceMember
        {
            WorkspaceId = fixture.Workspace.Id,
            TenantId = fixture.Identity.TenantId,
            ObjectId = fixture.Identity.ObjectId,
            Role = WorkspaceRole.Consultant
        });
        db.Projects.Add(fixture.Project);
        db.Environments.Add(fixture.Environment);
        await db.SaveChangesAsync();
        return fixture;
    }

    public static DiscoveryPackage Package(string name = "Synthetic User")
    {
        var date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var scope = new CollectionScope("domain", "example.test");
        return new(
            new PackageManifest("1.0", Guid.NewGuid(), "clarity.ad", "1.0.0", "AdForest",
                "forest.example.test", scope, date, date.AddMinutes(1), new Dictionary<string, string>(), "Synthetic"),
            [new ObjectRecord("ad.users:example.test", "user", "ad.objectGUID", "synthetic-user", date,
                new Dictionary<string, JsonElement> { ["displayName"] = JsonSerializer.SerializeToElement(name) })],
            [],
            [new SectionResult("ad.users:example.test", "ad.users", CoverageStatus.Complete, scope,
                ["displayName"], [], 1, 0, [], [])]);
    }

    public async Task<CollectionRun> StageAsync(DiscoveryPackage package, bool confirmed = true)
    {
        var run = new CollectionRun
        {
            WorkspaceId = Workspace.Id,
            ProjectId = Project.Id,
            EnvironmentId = Environment.Id,
            ExternalRunId = package.Manifest.RunId,
            ProducerId = package.Manifest.ProducerId,
            ProducerVersion = package.Manifest.ProducerVersion,
            ScopeJson = DiscoveryScope.Serialize(package.Manifest.Scope),
            ScopeHash = DiscoveryScope.Hash(package.Manifest.Scope),
            ContentHash = new string('a', 32) + Guid.NewGuid().ToString("N"),
            ManifestJson = JsonSerializer.Serialize(package.Manifest, ContractJson.Options),
            StartedAtUtc = package.Manifest.StartedAtUtc,
            CompletedAtUtc = package.Manifest.CompletedAtUtc,
            ConfirmedAtUtc = confirmed ? DateTimeOffset.UtcNow : null,
            CorrelationId = Guid.NewGuid().ToString("N"),
            ActorTenantId = Identity.TenantId,
            ActorObjectId = Identity.ObjectId
        };
        await using var db = await Factory.CreateDbContextAsync();
        db.CollectionRuns.Add(run);
        db.SectionRuns.AddRange(package.Results.Select(result => new SectionRun
        {
            WorkspaceId = Workspace.Id,
            RunId = run.Id,
            SectionId = result.SectionId,
            CapabilityId = result.CapabilityId,
            ScopeJson = DiscoveryScope.Serialize(result.Scope),
            ScopeHash = DiscoveryScope.Hash(result.Scope),
            Coverage = result.Status,
            ResultJson = JsonSerializer.Serialize(result, ContractJson.Options),
            ObjectCount = result.ObjectCount,
            RelationshipCount = result.RelationshipCount
        }));
        await db.SaveChangesAsync();
        return run;
    }

    public ValueTask DisposeAsync() => Database.DisposeAsync();

    internal async Task<(Project Project, TestContextFactory Factory, StoredProjectAccess Access, WorkspaceScope Scope)> OtherWorkspaceAsync()
    {
        var workspace = new ClientWorkspace { Name = "Other synthetic workspace" };
        var identity = new UserIdentity(Guid.NewGuid(), Guid.NewGuid());
        var scope = new WorkspaceScope(identity, [workspace.Id]);
        var factory = new TestContextFactory(ConnectionString, scope);
        await using var db = await factory.CreateDbContextAsync();
        var project = new Project { WorkspaceId = workspace.Id, Name = "Other synthetic dossier" };
        db.AddRange(workspace, project, new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            TenantId = identity.TenantId,
            ObjectId = identity.ObjectId,
            Role = WorkspaceRole.Consultant
        });
        await db.SaveChangesAsync();
        return (project, factory, new StoredProjectAccess(factory, scope), scope);
    }
}

internal sealed class TestContextFactory(string connection, IWorkspaceContext scope) : IDbContextFactory<ClarityDbContext>
{
    public ClarityDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ClarityDbContext>()
        .UseSqlServer(connection).AddInterceptors(new WorkspaceConnectionInterceptor(scope)).Options, scope);
    public Task<ClarityDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
}
