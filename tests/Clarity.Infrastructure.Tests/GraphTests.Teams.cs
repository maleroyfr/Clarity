using System.Net;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

public sealed partial class GraphTests
{
    private const string TeamId = "a1000000-0000-0000-0000-000000000001";
    private const string OtherTeamId = "a1000000-0000-0000-0000-000000000002";
    private const string TeamUserId = "b1000000-0000-0000-0000-000000000001";
    private const string TeamSiteId = "c1000000-0000-0000-0000-000000000001";
    private const string PrivateSiteId = "c1000000-0000-0000-0000-000000000002";
    private const string SharedSiteId = "c1000000-0000-0000-0000-000000000003";
    private const string TeamUrl = "https://synthetic.sharepoint.com/sites/Team";

    [Fact]
    public async Task TeamsMetadataAndChannelsKeepTypedIdsAndExcludeContentAndUnpopulatedSettings()
    {
        var teams = await TeamsAsync("teams.teams",
            r =>
            {
                r.RequestUri!.PathAndQuery.ShouldBe(TeamsCollector.TeamsPath);
                return Response($$"""{"value":[{"id":"{{TeamId.ToUpperInvariant()}}","displayName":"Synthetic team","visibility":"private","isArchived":null,"description":"never-retain","messages":["never-retain"]}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/teams?$skiptoken=next"}""");
            },
            _ => TeamParents(TeamId, OtherTeamId));
        teams.Objects.Count.ShouldBe(2);
        teams.Relationships.Count.ShouldBe(2);
        teams.Relationships.ShouldAllBe(x => x.RelationType == "teamGroup" && x.From.Type == "team" && x.To.Type == "group" && x.From.ExternalId == x.To.ExternalId);
        JsonSerializer.Serialize(teams).ShouldNotContain("never-retain");
        teams.Objects.ShouldAllBe(x => !x.Attributes.ContainsKey("isArchived"));
        var channels = await TeamsAsync("teams.channels", _ => TeamParents(TeamId), _ => TeamChannels());
        channels.Result.Status.ShouldBe(CoverageStatus.Complete);
        channels.Objects.Select(x => x.Attributes["membershipType"].GetString()).ShouldBe(["standard", "private", "shared"]);
        channels.Objects.ShouldAllBe(x => x.Type == "teamChannel" && x.Namespace == "microsoft.teams.channel" && x.ExternalId.StartsWith(TeamId + "/", StringComparison.Ordinal));
        channels.Relationships.ShouldAllBe(x => x.RelationType == "channelInTeam" && x.To == new ExternalReference("team", "microsoft.graph", TeamId));
    }

    [Fact]
    public async Task TeamsMembersKeepOpaqueEvidenceAndNeverResolveForeignOrUnknownTenantsLocally()
    {
        var data = await TeamsAsync("teams.members", _ => TeamParents(TeamId),
            r =>
            {
                r.RequestUri!.Query.ShouldContain("$select=id,displayName,userId,tenantId,roles");
                return Response($$"""{"value":[{"id":"opaque-owner","userId":"{{TeamUserId.ToUpperInvariant()}}","tenantId":"{{Tenant}}","roles":["owner"],"email":"never-retain"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/teams/{{TeamId}}/members?$skiptoken=more"}""");
            },
            _ => Response($$"""
                {"value":[
                  {"id":"opaque-owner","userId":"{{TeamUserId}}","tenantId":"{{Tenant}}","roles":["owner"]},
                  {"id":"opaque-foreign","userId":"{{TeamUserId}}","tenantId":"00000000-0000-0000-0000-000000000999","roles":["guest"]},
                  {"id":"opaque-unknown","userId":"{{TeamUserId}}","roles":[]},
                  {"id":"opaque-no-roles","tenantId":"{{Tenant}}"}
                ]}
                """));
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        data.Objects.Count.ShouldBe(4);
        data.Result.Warnings.Select(x => x.Code).ShouldBe(["TeamsMemberRolesUnknown", "TeamsMemberUnresolved"], ignoreOrder: true);
        var owner = data.Relationships.Where(x => x.RelationType == "ownerOf").ShouldHaveSingleItem();
        owner.From.ShouldBe(new("user", "microsoft.graph", TeamUserId));
        var members = data.Relationships.Where(x => x.RelationType == "memberOf").ToArray();
        members.Count(x => x.From.Namespace == "microsoft.graph").ShouldBe(1);
        members.ShouldContain(x => x.From.Namespace == "microsoft.graph.tenantUser" && x.From.ExternalId.EndsWith("/" + TeamUserId, StringComparison.Ordinal));
        members.Count(x => x.From.Type == "teamMembership").ShouldBe(2);
        JsonSerializer.Serialize(data).ShouldNotContain("never-retain");
    }

    [Fact]
    public async Task TeamsSitesDeduplicateStandardChannelsButKeepPrivateSharedAndUrlOnlyIdentitiesDistinct()
    {
        var data = await TeamsAsync("teams.sites", _ => TeamParents(TeamId), _ => TeamRoot(),
            _ => Response("""{"value":[{"id":"19:standard@thread.tacv2"},{"id":"19:private@thread.tacv2"},{"id":"19:shared@thread.tacv2"},{"id":"19:provisional@thread.tacv2"},{"id":"19:missing@thread.tacv2"}]}"""),
            r =>
            {
                r.RequestUri!.Query.ShouldBeEmpty();
                r.RequestUri.AbsolutePath.ShouldContain("19%3Astandard%40thread.tacv2");
                return TeamFolder(TeamSiteId);
            },
            _ => Response("""{"id":"opaque-item","parentReference":{"driveId":"opaque-drive"},"@microsoft.graph.downloadUrl":"never-retain","webUrl":"https://synthetic.sharepoint.com/sites/DoNotInfer/Folder"}"""),
            r =>
            {
                r.RequestUri!.PathAndQuery.ShouldBe("/v1.0/drives/opaque-drive/items/opaque-item?$select=sharepointIds,parentReference");
                return TeamFolder(PrivateSiteId);
            },
            _ => Response($$"""{"parentReference":{"siteId":"synthetic.sharepoint.com,{{SharedSiteId}},d1000000-0000-0000-0000-000000000003"} }"""),
            _ => Response($$"""{"sharepointIds":{"siteUrl":"{{TeamUrl}}"} }"""),
            _ => Response("", HttpStatusCode.NotFound));
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        data.Objects.Count.ShouldBe(4);
        data.Objects.Where(x => x.Namespace == "sharepoint.siteId").Select(x => x.ExternalId)
            .ShouldBe([TeamSiteId, PrivateSiteId, SharedSiteId], ignoreOrder: true);
        data.Objects.Single(x => x.Namespace == "sharepoint.siteUrl").ExternalId.ShouldBe(TeamUrl);
        data.Objects.Where(x => x.Attributes.ContainsKey("webUrl")).ShouldAllBe(x => x.Attributes["webUrl"].GetString() == TeamUrl);
        data.Objects.ShouldAllBe(x => x.Attributes["teamsConnected"].GetBoolean());
        data.Relationships.Count(x => x.To.ExternalId == TeamSiteId).ShouldBe(3);
        data.Relationships.Single(x => x.To.Namespace == "microsoft.teams.filesFolder").To.ExternalId.ShouldEndWith("/19:missing@thread.tacv2");
        JsonSerializer.Serialize(data).ShouldNotContain("never-retain");
        JsonSerializer.Serialize(data).ShouldNotContain("DoNotInfer");
    }

    [Fact]
    public async Task TeamsFailuresPreserveOtherParentsAndNeverBecomeKnownZeroOrAChannelRootFallback()
    {
        var data = await TeamsAsync("teams.channels", _ => TeamParents(TeamId, OtherTeamId),
            _ => Response("never-retain", HttpStatusCode.Forbidden), _ => TeamChannels());
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        data.Objects.Count.ShouldBe(3);
        data.Result.Errors.ShouldHaveSingleItem().Code.ShouldBe("MissingPermission");
        var sites = await TeamsAsync("teams.sites", _ => TeamParents(TeamId), _ => Response("never-retain", HttpStatusCode.Forbidden),
            _ => Response("""{"value":[{"id":"private"},{"id":"shared"}]}"""),
            _ => Response("never-retain", HttpStatusCode.Forbidden), _ => TeamFolder(SharedSiteId));
        sites.Result.Status.ShouldBe(CoverageStatus.Partial);
        sites.Objects.ShouldHaveSingleItem().ExternalId.ShouldBe(SharedSiteId);
        sites.Relationships.Single(x => x.RelationType == "teamSite").To.Namespace.ShouldBe("microsoft.graph.groupSite");
        sites.Relationships.Single(x => x.From.ExternalId == TeamId + "/private").To.Namespace.ShouldBe("microsoft.teams.filesFolder");
        JsonSerializer.Serialize(sites).ShouldNotContain("never-retain");
        foreach (var capability in new[] { "teams.teams", "teams.members", "teams.channels", "teams.sites" })
        {
            var empty = await TeamsAsync(capability, _ => Response("""{"value":[]}"""));
            (empty.Result.Status, empty.Result.ObjectCount, empty.Result.RelationshipCount).ShouldBe((CoverageStatus.Complete, 0, 0));
            var denied = await TeamsAsync(capability, _ => Response("", HttpStatusCode.Forbidden));
            denied.Result.Status.ShouldBe(CoverageStatus.Failed);
            denied.Result.ObjectCount.ShouldBeNull();
        }
        var unsafePage = await TeamsAsync("teams.teams",
            _ => Response("""{"value":[],"@odata.nextLink":"https://example.test/v1.0/teams"}"""));
        unsafePage.Result.Errors.ShouldHaveSingleItem().Code.ShouldBe("UnsafeGraphLink");
    }

    [SqlFact]
    public async Task TeamsJourneyReusesCanonicalSitesAndLinksAndKeepsScopedQualificationsIndependent()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var visibility = "private";
        var name = "Synthetic team";
        HttpResponseMessage Read(HttpRequestMessage request)
        {
            request.Method.ShouldBe(HttpMethod.Get);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1.0/organization") { return Organization(); }
            if (path == "/v1.0/teams") { return Response(JsonSerializer.Serialize(new { value = new[] { new { id = TeamId, displayName = name, visibility } } })); }
            if (path == $"/v1.0/teams/{TeamId}/members") { return TeamMembers(); }
            if (path == $"/v1.0/teams/{TeamId}/channels") { return TeamChannels(); }
            if (path == $"/v1.0/groups/{TeamId}/sites/root") { return TeamRoot(); }
            if (path.EndsWith("/filesFolder", StringComparison.Ordinal))
            { return TeamFolder(path.Contains("private", StringComparison.Ordinal) ? PrivateSiteId : path.Contains("shared", StringComparison.Ordinal) ? SharedSiteId : TeamSiteId); }
            if (path == "/v1.0/groups") { return Response($$"""{"value":[{"id":"{{TeamId}}","displayName":"Synthetic group","groupTypes":["Unified"],"resourceProvisioningOptions":["Team"]}]}"""); }
            if (path.StartsWith("/v1.0/groups/", StringComparison.Ordinal)) { return Response("""{"value":[]}"""); }
            if (path == "/v1.0/users/delta") { return Response($$"""{"value":[{"id":"{{TeamUserId}}","displayName":"Synthetic user"}],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/users/delta?$deltatoken=synthetic"}"""); }
            if (path == SharePointCollector.SitesPath) { return Response($$"""{"value":[{"id":"synthetic.sharepoint.com,{{TeamSiteId}},d1000000-0000-0000-0000-000000000001","webUrl":"{{TeamUrl}}","root":{} }]}"""); }
            throw new InvalidOperationException("Unexpected synthetic Teams request.");
        }
        using var http = new SyntheticGraph(Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(Read, 100).ToArray());
        var sessions = new SyntheticSessions(http.Transport);
        var service = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, sessions);
        var connection = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic Teams", producerId: TeamsCollector.ProducerId);
        (await service.ConnectAsync(fixture.Project.Id, environment.Id, "Replay", producerId: TeamsCollector.ProducerId)).ShouldBe(connection);
        var directory = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic directory");
        var sharePoint = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic SharePoint", producerId: SharePointCollector.ProducerId);
        await Should.ThrowAsync<ArgumentException>(() => service.LaunchAsync(fixture.Project.Id, environment.Id, directory, Guid.NewGuid(), Scope, ["teams.teams"]));
        var checks = await service.TestAsync(fixture.Project.Id, environment.Id, connection);
        checks.Count.ShouldBe(4);
        checks.ShouldAllBe(x => x.Available);
        var processor = new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, TimeProvider.System),
            [Handler(sessions, new MemoryArtifactStore())], NullLogger<CollectionJobProcessor>.Instance);
        var operation = Guid.NewGuid();
        string[] sections = ["teams.teams", "teams.members", "teams.channels", "teams.sites"];
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, operation, Scope, sections);
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, operation, Scope, sections);
        (await processor.ProcessOneAsync(default)).ShouldBeTrue();
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.CollectionJobs.SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
        (await db.CollectionRuns.SingleAsync()).ConnectionId.ShouldBe(connection);
        (await db.InventoryObjects.CountAsync(x => x.Type == "site")).ShouldBe(3);
        (await db.InventoryRelationships.SingleAsync(x => x.RelationType == "teamGroup")).ToMethod.ShouldBe(EndpointResolution.Pending);
        (await db.InventoryRelationships.SingleAsync(x => x.RelationType == "membershipUser")).ToMethod.ShouldBe(EndpointResolution.Pending);
        await CollectAsync(directory, ["entra.groups", "entra.users"]);
        await CollectAsync(sharePoint, ["sharepoint.sites", "sharepoint.groupSites"]);
        var teamGroup = await db.InventoryRelationships.AsNoTracking().SingleAsync(x => x.RelationType == "teamGroup");
        teamGroup.ToMethod.ShouldBe(EndpointResolution.Primary);
        teamGroup.FromObjectId.ShouldNotBe(teamGroup.ToObjectId);
        (await db.InventoryRelationships.AsNoTracking().SingleAsync(x => x.RelationType == "membershipUser")).ToMethod.ShouldBe(EndpointResolution.Primary);
        (await db.InventoryObjects.CountAsync(x => x.Type == "site")).ShouldBe(3);
        var site = await db.ExternalKeys.SingleAsync(x => x.Namespace == "sharepoint.siteId" && x.ExternalId == TeamSiteId);
        (await db.ObjectObservations.CountAsync(x => x.ObjectId == site.ObjectId)).ShouldBe(3);
        (await db.AlternateKeyPeriods.Where(x => x.ObjectId == site.ObjectId).Select(x => x.NormalizedValue).Distinct().ToListAsync()).ShouldBe([TeamUrl]);
        var groupSite = await db.InventoryRelationships.AsNoTracking().SingleAsync(x => x.RelationType == "groupSite");
        var proof = await new InventoryProof(fixture.Factory, fixture.Access).ReadAsync(fixture.Project.Id, new(ProofKind.Relationship, groupSite.Id));
        proof.Sources.Count.ShouldBe(2);

        var subject = teamGroup.FromObjectId!.Value;
        var observation = await db.ObjectObservations.SingleAsync(x => x.ObjectId == subject);
        var qualification = new TeamsQualificationService(fixture.Factory, fixture.Access, fixture.Scope);
        const AssessmentKind chats = AssessmentKind.TeamsChatScope, phones = AssessmentKind.TeamsTelephonyScope;
        var initial = await qualification.ReadAsync(fixture.Project.Id, subject, chats);
        var input = new TeamsQualificationInput(Guid.NewGuid(), 0, initial.MetadataFingerprint, QualificationScopeValue.Included,
            "Synthetic workshop: chat migration included for this team's population.", "Synthetic workshop T-1", observation.Id);
        await qualification.RecordAsync(fixture.Project.Id, subject, chats, input);
        await qualification.RecordAsync(fixture.Project.Id, subject, chats, input);
        await Should.ThrowAsync<ConflictException>(() => qualification.RecordAsync(fixture.Project.Id, subject, phones, input));
        await qualification.RecordAsync(fixture.Project.Id, subject, phones,
            input with { OperationId = Guid.NewGuid(), Value = QualificationScopeValue.Excluded, Reason = "Synthetic population has no telephony scope." });
        var recorded = (await qualification.ReadAsync(fixture.Project.Id, subject, chats)).Current!;
        (recorded.Revision, recorded.AuthorObjectId, recorded.EnvironmentId, recorded.ScopeValue)
            .ShouldBe((1L, fixture.Identity.ObjectId, environment.Id, (QualificationScopeValue?)QualificationScopeValue.Included));
        await Should.ThrowAsync<ConflictException>(() => qualification.RecordAsync(fixture.Project.Id, subject, chats, input with { Reason = "Changed replay" }));
        await Should.ThrowAsync<ConflictException>(() => qualification.RecordAsync(fixture.Project.Id, subject, chats, input with { OperationId = Guid.NewGuid() }));
        name = "Renamed synthetic team";
        await CollectAsync(connection, ["teams.teams"]);
        (await qualification.ReadAsync(fixture.Project.Id, subject, chats)).NeedsReview.ShouldBeFalse();
        visibility = "public";
        await CollectAsync(connection, ["teams.teams"]);
        var changed = await qualification.ReadAsync(fixture.Project.Id, subject, chats);
        changed.NeedsReview.ShouldBeTrue();
        (await qualification.ReadAsync(fixture.Project.Id, subject, phones)).NeedsReview.ShouldBeTrue();
        changed.Current!.Id.ShouldBe(recorded.Id);
        var revision = input with { OperationId = Guid.NewGuid(), ExpectedRevision = 1, MetadataFingerprint = changed.MetadataFingerprint, Value = QualificationScopeValue.Unknown };
        var unrelatedObservation = await db.ObjectObservations.FirstAsync(x => x.ObjectId != subject);
        await Should.ThrowAsync<AccessDeniedException>(() => qualification.RecordAsync(fixture.Project.Id, subject, chats,
            revision with { EvidenceObservationId = unrelatedObservation.Id }));
        await qualification.RecordAsync(fixture.Project.Id, subject, chats, revision);
        (await qualification.HistoryAsync(fixture.Project.Id, subject, chats, new(1, 1))).Items.ShouldHaveSingleItem().Id.ShouldBe(recorded.Id);
        (await qualification.HistoryAsync(fixture.Project.Id, subject, phones, new())).TotalCount.ShouldBe(1);
        (await db.AuditEvents.CountAsync(x => x.Action == "TeamsQualification.Recorded")).ShouldBe(3);
        (await db.InventoryRelationships.ToListAsync()).ShouldAllBe(x =>
            x.Origin == (x.RelationType == "memberOf" ? RelationshipOrigin.Membership : RelationshipOrigin.Observed));
        (await db.CurrentValues.AnyAsync(x => x.Field == "ScopeValue")).ShouldBeFalse();
        var other = await fixture.OtherWorkspaceAsync();
        var outsider = new TeamsQualificationService(other.Factory, other.Access, other.Scope);
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.ReadAsync(other.Project.Id, subject, chats));
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.HistoryAsync(other.Project.Id, subject, chats, new()));
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.RecordAsync(other.Project.Id, subject, chats, revision));
        await using (var outside = await other.Factory.CreateDbContextAsync())
        { (await outside.Assessments.IgnoreQueryFilters().CountAsync()).ShouldBe(0); }
        await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        (await qualification.HistoryAsync(fixture.Project.Id, subject, chats, new())).TotalCount.ShouldBe(2);
        await Should.ThrowAsync<AccessDeniedException>(() => qualification.RecordAsync(fixture.Project.Id, subject, chats, revision));

        async Task CollectAsync(Guid producerConnection, string[] selection)
        {
            await service.LaunchAsync(fixture.Project.Id, environment.Id, producerConnection, Guid.NewGuid(), Scope, selection);
            (await processor.ProcessOneAsync(default)).ShouldBeTrue();
            (await db.CollectionJobs.AsNoTracking().ToListAsync()).ShouldAllBe(x => x.State == CollectionRunState.Completed);
        }
    }

    [SqlFact]
    public async Task TeamsProbesExposeUntestedScopesMissingConsentAndTenantMismatchWithoutPublication()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        using var http = new SyntheticGraph(_ => Organization(), _ => Response("""{"value":[]}"""), _ => Response("""{"value":[]}"""),
            _ => Response("", HttpStatusCode.Forbidden), _ => TeamParents(TeamId),
            _ => Response("""{"value":[]}"""), _ => TeamRoot(),
            _ => Response("""{"value":[{"id":"00000000-0000-0000-0000-000000000999"}]}"""));
        var sessions = new SyntheticSessions(http.Transport);
        var service = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, sessions);
        var connection = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic Teams", producerId: TeamsCollector.ProducerId);
        var other = await fixture.OtherWorkspaceAsync();
        var outsider = new GraphDiscoveryService(other.Factory, other.Access, other.Scope, Catalog, sessions);
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.TestAsync(other.Project.Id, environment.Id, connection));
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.LaunchAsync(other.Project.Id, environment.Id, connection, Guid.NewGuid(), Scope, ["teams.teams"]));
        http.Calls.ShouldBe(0);
        var checks = (await service.TestAsync(fixture.Project.Id, environment.Id, connection)).ToDictionary(x => x.CapabilityId);
        checks["teams.teams"].Available.ShouldBeTrue();
        (checks["teams.members"].Available, checks["teams.members"].Code).ShouldBe((false, "TeamsChildrenNotTested"));
        checks["teams.channels"].Code.ShouldBe("MissingPermission");
        checks["teams.sites"].Code.ShouldBe("TeamsSitesNotTested");
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, Guid.NewGuid(), Scope, ["teams.teams"]);
        await new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, TimeProvider.System), [Handler(sessions, new MemoryArtifactStore())],
            NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.CollectionJobs.SingleAsync()).ErrorCode.ShouldBe("EnvironmentMismatch");
        (await db.CollectionRuns.CountAsync()).ShouldBe(0);
        (await db.ObjectObservations.CountAsync()).ShouldBe(0);
        http.Calls.ShouldBe(8);
    }

    [Fact]
    public async Task TeamsParentLimitIsBoundedAndPreservesExactlyTheProcessedTeams()
    {
        var data = await TeamsAsync("teams.teams", _ => TeamParents(Enumerable.Range(1, 20001)
            .Select(i => $"a1000000-0000-0000-0000-{i:d12}").ToArray()));
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        data.Objects.Count.ShouldBe(20000);
        data.Relationships.Count.ShouldBe(20000);
        data.Result.Errors.ShouldHaveSingleItem().Code.ShouldBe("TeamsLimit");
    }

    private static async Task<GraphSectionData> TeamsAsync(string capability, params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
    {
        using var http = new SyntheticGraph(responses);
        var data = await new TeamsCollector(Catalog).CollectAsync(http.Transport, capability, Scope, TestContext.Current.CancellationToken);
        await ValidateAsync(data, TeamsCollector.ProducerId);
        http.Calls.ShouldBe(responses.Length);
        return data;
    }

    private static HttpResponseMessage TeamParents(params string[] ids) => Response(JsonSerializer.Serialize(new { value = ids.Select(id => new { id }) }));
    private static HttpResponseMessage TeamChannels() => Response("""
        {"value":[{"id":"19:standard@thread.tacv2","displayName":"General","membershipType":"standard"},
        {"id":"19:private@thread.tacv2","displayName":"Private","membershipType":"private"},
        {"id":"19:shared@thread.tacv2","displayName":"Shared","membershipType":"shared"}]}
        """);
    private static HttpResponseMessage TeamMembers() => Response($$"""{"value":[{"id":"opaque-owner","displayName":"Synthetic owner","userId":"{{TeamUserId}}","tenantId":"{{Tenant}}","roles":["owner"]}]}""");
    private static HttpResponseMessage TeamRoot() => Response($$"""{"id":"synthetic.sharepoint.com,{{TeamSiteId}},d1000000-0000-0000-0000-000000000001","webUrl":"{{TeamUrl}}"}""");
    private static HttpResponseMessage TeamFolder(string siteId) => Response($$"""{"sharepointIds":{"siteId":"{{siteId}}","siteUrl":"{{TeamUrl}}"} }""");
}
