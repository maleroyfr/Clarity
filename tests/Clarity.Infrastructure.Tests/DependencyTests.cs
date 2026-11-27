using System.Text;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class DependencyTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000123");
    private static readonly CollectionScope Scope = new("tenant", Tenant.ToString());
    private static readonly DateTimeOffset Date = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Alice = "a1000000-0000-0000-0000-000000000001", Bob = "a1000000-0000-0000-0000-000000000002";
    private const string Team = "b1000000-0000-0000-0000-000000000001", Foreign = "c1000000-0000-0000-0000-000000000009";
    private const string TeamSite = "7a2d3b20-2222-4b9c-8d22-000000000001", PrivateSite = "7a2d3b20-2222-4b9c-8d22-000000000002";
    private const string Url = "https://contoso.sharepoint.com/sites/Project";

    [SqlFact]
    public async Task SharedTeamAndSiteCountTheSiteVolumeOnceWithBothPopulationsAndLinkOrigins()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var (environment, ids) = await SeedAsync(fixture);
        var project = fixture.Project.Id;
        await ScopeTests.Service(fixture).ApplyAsync(project,
            new(Guid.NewGuid(), ScopeValue.Include, "Vague 1", "Synthetic migrating user", ObjectIds: [ids["alice"]]), 1);
        var service = Service(fixture);
        var operation = Guid.NewGuid();

        var id = await service.AnalyzeAsync(project, environment.Id, null, operation);
        (await service.AnalyzeAsync(project, environment.Id, null, operation)).ShouldBe(id);
        await Should.ThrowAsync<ConflictException>(() => service.AnalyzeAsync(project, environment.Id, "Vague 1", operation));

        var summary = (await service.AnalysesAsync(project, EnvironmentRole.Source)).Single();
        (summary.MigratingPrincipals, summary.RemainingPrincipals).ShouldBe((1, 1));
        (summary.Resources, summary.SharedResources).ShouldBe((3, 3));
        summary.KnownVolumeBytes.ShouldBe(1073741824);
        summary.SharedVolumeBytes.ShouldBe(1073741824);
        summary.UnknownVolumeResources.ShouldBe(0);
        summary.Families.Select(f => (f.Family, f.Resources, f.Shared))
            .ShouldBe([(DependencyFamily.Groups, 1, 1), (DependencyFamily.Teams, 1, 1), (DependencyFamily.Sites, 1, 1)]);
        var coverage = summary.Coverage.ToDictionary(c => c.Family);
        coverage[DependencyFamily.Teams].Status.ShouldBe(DependencyCoverage.Partial);
        coverage[DependencyFamily.Teams].UnresolvedLinks.ShouldBe(1);
        coverage[DependencyFamily.Teams].MembersNotCollected.ShouldBe(1);
        coverage[DependencyFamily.Sites].Status.ShouldBe(DependencyCoverage.Partial);
        coverage[DependencyFamily.Sites].MembersNotCollected.ShouldBe(1);
        coverage[DependencyFamily.Sites].Sections.Single(s => s.CapabilityId == "sharepoint.sitePermissions").ResourceScoped.ShouldBeTrue();
        coverage[DependencyFamily.Applications].Status.ShouldBe(DependencyCoverage.NotCollected);

        var site = (await service.ResourcesAsync(project, id, new(DependencyFamily.Sites), new())).Items.Single();
        site.ObjectId.ShouldBe(ids[TeamSite]);
        (site.Classification, site.Migrating, site.Remaining, site.Routes).ShouldBe((DependencyClassification.Shared, 1, 1, 3));
        site.VolumeBytes.ShouldBe(1073741824);
        (await service.ResourcesAsync(project, id, new(Classification: DependencyClassification.MigratingOnly), new())).TotalCount.ShouldBe(0);
        var links = await service.LinksAsync(project, id, ids[TeamSite]);
        links.Where(l => l.Kind == DependencyLinkKind.Route).Select(l => (l.ObjectId, l.Route))
            .ShouldBe([(ids["group"], "groupSite"), (ids["team"], "teamSite"), (ids["general"], "channelSite")], ignoreOrder: true);
        links.ShouldAllBe(l => l.Kind == DependencyLinkKind.Principal || l.RelationshipId != null);
        links.Where(l => l.Kind == DependencyLinkKind.Principal).Select(l => (l.ObjectId, l.Side))
            .ShouldBe([(ids["alice"], (DependencySide?)DependencySide.Migrating), (ids["bob"], DependencySide.Remaining)], ignoreOrder: true);

        var wave = await service.AnalyzeAsync(project, environment.Id, " Vague 2 ", Guid.NewGuid());
        var other = (await service.AnalysesAsync(project, EnvironmentRole.Source)).Single(a => a.Id == wave);
        (other.Wave, other.MigratingPrincipals, other.OtherWavePrincipals, other.Resources).ShouldBe(("Vague 2", 0, 1, 0));
        var latest = await service.AnalyzeAsync(project, environment.Id, null, Guid.NewGuid());
        (await service.AnalysesAsync(project, EnvironmentRole.Source)).Select(a => a.Id).ShouldBe([latest, wave], ignoreOrder: true);
        await Should.ThrowAsync<AccessDeniedException>(() => service.ResourcesAsync(project, id, new(), new()));

        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.AuditEvents.CountAsync(a => a.Action == "Scope.DependenciesAnalyzed")).ShouldBe(3);
        (await db.DependencyAnalyses.CountAsync()).ShouldBe(2);
        (await db.DependencyLinks.CountAsync(l => l.AnalysisId == id)).ShouldBe(0);
    }

    [SqlFact]
    public async Task DependenciesAreWorkspaceIsolatedAndReadersCannotAnalyze()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var (environment, ids) = await SeedAsync(fixture);
        var project = fixture.Project.Id;
        await ScopeTests.Service(fixture).ApplyAsync(project,
            new(Guid.NewGuid(), ScopeValue.Include, null, "Synthetic migrating user", ObjectIds: [ids["alice"]]), 1);
        var service = Service(fixture);
        var id = await service.AnalyzeAsync(project, environment.Id, null, Guid.NewGuid());

        var other = await fixture.OtherWorkspaceAsync();
        var foreign = new DependencyService(other.Factory, other.Access, other.Scope, DiscoveryCatalog.Load());
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.AnalysesAsync(project, EnvironmentRole.Source));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.ResourcesAsync(other.Project.Id, id, new(), new()));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.LinksAsync(other.Project.Id, id, ids[TeamSite]));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.AnalyzeAsync(project, environment.Id, null, Guid.NewGuid()));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.AnalyzeAsync(other.Project.Id, environment.Id, null, Guid.NewGuid()));

        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        }
        (await service.AnalysesAsync(project, EnvironmentRole.Source)).Single().Id.ShouldBe(id);
        (await service.ResourcesAsync(project, id, new(), new())).TotalCount.ShouldBe(3);
        await Should.ThrowAsync<AccessDeniedException>(() => service.AnalyzeAsync(project, environment.Id, null, Guid.NewGuid()));

        await using var outside = PersistenceTests.Create(fixture.ConnectionString, []);
        (await outside.DependencyAnalyses.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.DependencyResources.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.DependencyLinks.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    private static DependencyService Service(DiscoveryFixture fixture) => new(fixture.Factory, fixture.Access, fixture.Scope, DiscoveryCatalog.Load());

    /// <summary>
    /// Publishes a synthetic tenant: Alice and Bob in a Microsoft 365 group whose team also has a foreign-tenant member, a standard
    /// channel on the team site and a private channel on its own site; then a site usage report sizing the team site.
    /// </summary>
    private static async Task<(ProjectEnvironment Environment, Dictionary<string, Guid> Ids)> SeedAsync(DiscoveryFixture fixture)
    {
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.WorkspaceManager));
        }
        var environment = new ProjectEnvironment
        {
            WorkspaceId = fixture.Workspace.Id,
            ProjectId = fixture.Project.Id,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = Tenant.ToString()
        };
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            db.Environments.Add(environment);
            await db.SaveChangesAsync();
        }
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        foreach (var package in Packages())
        {
            var run = await imports.UploadAsync(fixture.Project.Id, environment.Id, new MemoryStream(await ZipImportTests.BytesAsync(package)), "package.zip");
            await IntegrateAsync(fixture, store, imports, run);
        }
        var csv = string.Join('\n',
            "Report Refresh Date,Site Id,Site URL,Owner Display Name,Is Deleted,Last Activity Date,File Count,Active File Count,Page View Count,Visited Page Count,Storage Used (Byte),Storage Allocated (Byte),Root Web Template,Owner Principal Name,Report Period",
            $"2026-05-31,{TeamSite},{Url},,False,2026-05-30,340,12,1500,25,1073741824,27487790694400,Group,,7") + "\n";
        var report = await imports.UploadReportAsync(fixture.Project.Id, environment.Id, "m365.sharePointSiteUsageDetail", "1.0.0", Scope, true,
            new MemoryStream(Encoding.UTF8.GetBytes(csv)), "sites.csv");
        await IntegrateAsync(fixture, store, imports, report);

        await using var check = await fixture.Factory.CreateDbContextAsync();
        var keys = await check.ExternalKeys.AsNoTracking().Where(k => k.EnvironmentId == environment.Id && k.IsPrimary).ToListAsync();
        var ids = new Dictionary<string, Guid>
        {
            ["alice"] = keys.Single(k => k.Type == "user" && k.ExternalId == Alice).ObjectId,
            ["bob"] = keys.Single(k => k.Type == "user" && k.ExternalId == Bob).ObjectId,
            ["group"] = keys.Single(k => k.Type == "group").ObjectId,
            ["team"] = keys.Single(k => k.Type == "team").ObjectId,
            ["general"] = keys.Single(k => k.Type == "teamChannel" && k.ExternalId.EndsWith("/general", StringComparison.Ordinal)).ObjectId,
            [TeamSite] = keys.Single(k => k.ExternalId == TeamSite).ObjectId,
            [PrivateSite] = keys.Single(k => k.ExternalId == PrivateSite).ObjectId
        };
        (await check.InventoryObjects.CountAsync(i => i.EnvironmentId == environment.Id && i.Type == "site")).ShouldBe(2);
        return (environment, ids);
    }

    /// <summary>Entra then Teams packages, shaped like the Graph collectors' output and validated against the catalog.</summary>
    internal static DiscoveryPackage[] Packages()
    {
        static Dictionary<string, JsonElement> Attributes(params (string Name, object Value)[] values) =>
            values.ToDictionary(x => x.Name, x => JsonSerializer.SerializeToElement(x.Value));
        static ExternalReference Ref(string type, string ns, string id) => new(type, ns, id);
        static PackageManifest Manifest(string producer) => new("1.0", Guid.NewGuid(), producer, "1.0.0", "M365Tenant", Tenant.ToString(), Scope, Date, Date,
            new Dictionary<string, string>(), "Synthetic dependencies");

        ObjectRecord[] users = [new("entra.users", "user", "microsoft.graph", Alice, Date, Attributes(("displayName", "Alice"), ("userPrincipalName", "alice@contoso.example"))),
            new("entra.users", "user", "microsoft.graph", Bob, Date, Attributes(("displayName", "Bob"), ("userPrincipalName", "bob@contoso.example")))];
        ObjectRecord[] groups = [new("entra.groups", "group", "microsoft.graph", Team, Date, Attributes(("displayName", "Project"), ("groupTypes", new[] { "Unified" })))];
        RelationshipRecord[] groupLinks = [new("entra.groups", "memberOf", Ref("user", "microsoft.graph", Alice), Ref("group", "microsoft.graph", Team), Date),
            new("entra.groups", "memberOf", Ref("user", "microsoft.graph", Bob), Ref("group", "microsoft.graph", Team), Date)];
        var entra = new DiscoveryPackage(Manifest("clarity.graph.entra"), [.. users, .. groups], groupLinks,
        [
            new SectionResult("entra.users", "entra.users", CoverageStatus.Complete, Scope, ["displayName", "userPrincipalName"], [], 2, 0, [], []),
            new SectionResult("entra.groups", "entra.groups", CoverageStatus.Complete, Scope, ["displayName", "groupTypes"], ["memberOf"], 1, 2, [], [])
        ]);

        var team = Ref("team", "microsoft.graph", Team);
        var general = Ref("teamChannel", "microsoft.teams.channel", Team + "/general");
        var secret = Ref("teamChannel", "microsoft.teams.channel", Team + "/secret");
        var teamSite = Ref("site", "sharepoint.siteId", TeamSite);
        var privateSite = Ref("site", "sharepoint.siteId", PrivateSite);
        ObjectRecord Membership(string id, string user, string tenant) => new("teams.members", "teamMembership", "microsoft.teams.membership", Team + "/" + id, Date,
            Attributes(("userId", user), ("tenantId", tenant), ("roles", Array.Empty<string>())));
        ObjectRecord[] objects =
        [
            new("teams.teams", "team", "microsoft.graph", Team, Date, Attributes(("displayName", "Project"))),
            Membership("m-alice", Alice, Tenant.ToString()), Membership("m-bob", Bob, Tenant.ToString()), Membership("m-guest", Foreign, Foreign),
            new("teams.channels", "teamChannel", general.Namespace, general.ExternalId, Date, Attributes(("displayName", "General"), ("membershipType", "standard"))),
            new("teams.channels", "teamChannel", secret.Namespace, secret.ExternalId, Date, Attributes(("displayName", "Secret"), ("membershipType", "private"))),
            new("teams.sites", "site", "sharepoint.siteId", TeamSite, Date, Attributes(("webUrl", Url), ("teamsConnected", true))),
            new("teams.sites", "site", "sharepoint.siteId", PrivateSite, Date, Attributes(("webUrl", Url + "-Secret"), ("teamsConnected", true)))
        ];
        RelationshipRecord[] members = [.. new[] { ("m-alice", Ref("user", "microsoft.graph", Alice)), ("m-bob", Ref("user", "microsoft.graph", Bob)),
            ("m-guest", Ref("user", "microsoft.graph.tenantUser", Foreign + "/" + Foreign)) }.SelectMany(x => new RelationshipRecord[]
        {
            new("teams.members", "membershipInTeam", Ref("teamMembership", "microsoft.teams.membership", Team + "/" + x.Item1), team, Date),
            new("teams.members", "membershipUser", Ref("teamMembership", "microsoft.teams.membership", Team + "/" + x.Item1), x.Item2, Date),
            new("teams.members", "memberOf", x.Item2, team, Date)
        })];
        RelationshipRecord[] teamLinks = [new("teams.teams", "teamGroup", team, Ref("group", "microsoft.graph", Team), Date)];
        RelationshipRecord[] channelLinks = [new("teams.channels", "channelInTeam", general, team, Date), new("teams.channels", "channelInTeam", secret, team, Date)];
        RelationshipRecord[] siteLinks = [new("teams.sites", "groupSite", Ref("group", "microsoft.graph", Team), teamSite, Date),
            new("teams.sites", "teamSite", team, teamSite, Date), new("teams.sites", "channelSite", general, teamSite, Date),
            new("teams.sites", "channelSite", secret, privateSite, Date)];
        var teams = new DiscoveryPackage(Manifest("clarity.graph.teams"), objects, [.. teamLinks, .. members, .. channelLinks, .. siteLinks],
        [
            new SectionResult("teams.teams", "teams.teams", CoverageStatus.Complete, Scope, ["displayName"], ["teamGroup"], 1, 1, [], []),
            new SectionResult("teams.members", "teams.members", CoverageStatus.Complete, Scope, ["userId", "tenantId", "roles"],
                ["membershipInTeam", "membershipUser", "memberOf"], 3, 9, [], []),
            new SectionResult("teams.channels", "teams.channels", CoverageStatus.Complete, Scope, ["displayName", "membershipType"], ["channelInTeam"], 2, 2, [], []),
            new SectionResult("teams.sites", "teams.sites", CoverageStatus.Complete, Scope, ["webUrl", "teamsConnected"], ["groupSite", "teamSite", "channelSite"],
                2, 4, [], [])
        ]);
        foreach (var package in new[] { entra, teams })
        {
            DiscoveryCatalog.Load().ValidatePackage(package);
            new PackageValidator().Validate(package);
        }
        return [entra, teams];
    }

    private static async Task IntegrateAsync(DiscoveryFixture fixture, IArtifactStore store, ImportService imports, Guid id)
    {
        await imports.ConfirmAsync(fixture.Project.Id, id);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue();
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.CollectionRuns.AsNoTracking().SingleAsync(x => x.Id == id)).State.ShouldBe(CollectionRunState.Completed);
    }
}
