using System.Net;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

public sealed partial class GraphTests
{
    private const string OneDriveReportPath = "/v1.0/reports/getOneDriveUsageAccountDetail(period='D30')";

    [Fact]
    public async Task SharePointSitesKeepOneObjectPerCollectionAndSkipPersonalSites()
    {
        using var http = new SyntheticGraph(request =>
        {
            request.RequestUri!.PathAndQuery.ShouldBe(SharePointCollector.SitesPath);
            return Response("""
                {"value":[
                  {"id":"synthetic.sharepoint.com,11111111-1111-1111-1111-111111111111,21111111-1111-1111-1111-111111111111","name":"alpha","displayName":"Alpha","webUrl":"https://Synthetic.SharePoint.com/sites/Alpha/","isPersonalSite":false,"root":{},"siteCollection":{"hostName":"synthetic.sharepoint.com","dataLocationCode":"EUR"},"createdDateTime":"2026-01-02T03:04:05+02:00"},
                  {"id":"synthetic.sharepoint.com,11111111-1111-1111-1111-111111111111,31111111-1111-1111-1111-111111111111","name":"alpha-sub","webUrl":"https://synthetic.sharepoint.com/sites/Alpha/Sub","isPersonalSite":false},
                  {"id":"synthetic-my.sharepoint.com,41111111-1111-1111-1111-111111111111,51111111-1111-1111-1111-111111111111","webUrl":"https://synthetic-my.sharepoint.com/personal/user_synthetic_example","isPersonalSite":true},
                  {"id":"synthetic-my.sharepoint.com,61111111-1111-1111-1111-111111111111,71111111-1111-1111-1111-111111111111","webUrl":"https://synthetic-my.sharepoint.com/"},
                  {"id":"not-a-composite-id","webUrl":"https://synthetic.sharepoint.com/sites/Beta"}
                ]}
                """);
        });
        var data = await new SharePointCollector(Catalog).CollectAsync(http.Transport, "sharepoint.sites", Scope,
            TestContext.Current.CancellationToken);
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        data.Result.Warnings.Select(x => x.Code).ShouldBe(["DuplicateSiteCollection", "SiteIdentifierUnavailable"], ignoreOrder: true);
        var site = data.Objects.ShouldHaveSingleItem();
        (site.Type, site.Namespace, site.ExternalId).ShouldBe(("site", "sharepoint.siteId", "11111111-1111-1111-1111-111111111111"));
        site.Attributes["webUrl"].GetString().ShouldBe("https://synthetic.sharepoint.com/sites/Alpha");
        site.AlternativeKeys.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(new ExternalReference("site", "url", "https://synthetic.sharepoint.com/sites/Alpha"));
        site.Attributes["hostName"].GetString().ShouldBe("synthetic.sharepoint.com");
        site.Attributes["createdDateTime"].GetDateTimeOffset().Offset.ShouldBe(TimeSpan.Zero);
        await ValidateAsync(data, SharePointCollector.ProducerId);
    }

    [Fact]
    public async Task GroupConnectedSitesFlagTeamsAndCountGroupsWithoutReadableSites()
    {
        const string team = "00000000-0000-0000-0000-00000000a001";
        const string plain = "00000000-0000-0000-0000-00000000a002";
        using var http = new SyntheticGraph(
            request =>
            {
                request.RequestUri!.AbsolutePath.ShouldBe("/v1.0/groups");
                return Response($$"""{"value":[{"id":"{{team}}","resourceProvisioningOptions":["Team"]},{"id":"{{plain}}","resourceProvisioningOptions":[]}]}""");
            },
            request =>
            {
                request.RequestUri!.AbsolutePath.ShouldBe($"/v1.0/groups/{team}/sites/root");
                return Response("""{"id":"synthetic.sharepoint.com,81111111-1111-1111-1111-111111111111,91111111-1111-1111-1111-111111111111","webUrl":"https://synthetic.sharepoint.com/sites/Team"}""");
            },
            _ => Response("""{"error":{"code":"itemNotFound"}}""", HttpStatusCode.NotFound));
        var data = await new SharePointCollector(Catalog).CollectAsync(http.Transport, "sharepoint.groupSites", Scope,
            TestContext.Current.CancellationToken);
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        data.Result.Warnings.ShouldHaveSingleItem().Code.ShouldBe("GroupSiteUnavailable");
        var site = data.Objects.ShouldHaveSingleItem();
        site.ExternalId.ShouldBe("81111111-1111-1111-1111-111111111111");
        site.AlternativeKeys.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(new ExternalReference("site", "url", "https://synthetic.sharepoint.com/sites/Team"));
        site.Attributes["teamsConnected"].GetBoolean().ShouldBeTrue();
        var edge = data.Relationships.ShouldHaveSingleItem();
        (edge.RelationType, edge.From, edge.To).ShouldBe(("groupSite", new ExternalReference("group", "microsoft.graph", team),
            new ExternalReference("site", "sharepoint.siteId", site.ExternalId)));
        await ValidateAsync(data, SharePointCollector.ProducerId);
    }

    [Fact]
    public async Task OneDriveAccountsLinkOwnersByImmutableSiteIdsAndReportUnresolvedDrives()
    {
        const string linked = "00000000-0000-0000-0000-00000000b001";
        const string missing = "00000000-0000-0000-0000-00000000b002";
        const string unresolved = "00000000-0000-0000-0000-00000000b003";
        using var http = new SyntheticGraph(
            request =>
            {
                request.RequestUri!.AbsolutePath.ShouldBe("/v1.0/users");
                return Response($$"""{"value":[{"id":"{{linked}}"},{"id":"{{missing}}"},{"id":"{{unresolved}}"}]}""");
            },
            request =>
            {
                request.RequestUri!.AbsolutePath.ShouldBe($"/v1.0/users/{linked}/drives");
                return Response("""
                    {"value":[{"id":"b!drive-a","driveType":"business","createdDateTime":"2026-01-01T00:00:00Z","lastModifiedDateTime":"2026-05-01T10:00:00Z",
                      "quota":{"used":1024,"total":1099511627776,"state":"normal","deleted":5},
                      "sharepointIds":{"siteId":"A1111111-1111-1111-1111-111111111111","siteUrl":"https://Synthetic-my.sharepoint.com/personal/a_synthetic_example"}},
                      {"id":"b!library","driveType":"documentLibrary"}]}
                    """);
            },
            _ => Response("""{"error":{"code":"itemNotFound"}}""", HttpStatusCode.NotFound),
            _ => Response("""{"value":[{"id":"b!drive-c","driveType":"business"}]}"""));
        var data = await new OneDriveCollector(Catalog).CollectAsync(http.Transport, "onedrive.accounts", Scope,
            TestContext.Current.CancellationToken);
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        data.Result.Warnings.Select(x => x.Code).ShouldBe(["OneDriveNotProvisioned", "SiteIdUnavailable"], ignoreOrder: true);
        var drive = data.Objects.ShouldHaveSingleItem();
        (drive.Type, drive.Namespace, drive.ExternalId).ShouldBe(("oneDrive", "sharepoint.siteId", "a1111111-1111-1111-1111-111111111111"));
        const string url = "https://synthetic-my.sharepoint.com/personal/a_synthetic_example";
        drive.Attributes["siteUrl"].GetString().ShouldBe(url);
        drive.Attributes["quotaUsedBytes"].GetInt64().ShouldBe(1024);
        drive.Attributes["quotaState"].GetString().ShouldBe("normal");
        drive.Attributes.ContainsKey("quotaDeletedBytes").ShouldBeFalse();
        drive.AlternativeKeys.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(new ExternalReference("oneDrive", "url", url));
        var owner = data.Relationships.ShouldHaveSingleItem();
        (owner.RelationType, owner.From).ShouldBe(("ownerOf", new ExternalReference("user", "microsoft.graph", linked)));
        await ValidateAsync(data, OneDriveCollector.ProducerId);
    }

    [Fact]
    public async Task ReportDownloadsFollowOnlyAllowListedRedirectsWithoutGraphCredentials()
    {
        var csv = System.Text.Encoding.UTF8.GetBytes("Report Refresh Date\n");
        var ct = TestContext.Current.CancellationToken;
        using var http = new SyntheticGraph(
            request => { request.Headers.Authorization!.Parameter.ShouldBe("synthetic-token"); return Redirect("https://reports.office.com/data/synthetic"); },
            request =>
            {
                request.Headers.Authorization.ShouldBeNull();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(csv) };
            });
        (await http.Transport.DownloadReportAsync(OneDriveReportPath, 1024, false, ct)).ShouldBe(csv);
        using var rejected = new SyntheticGraph(_ => Redirect("https://reports.example.test/data"));
        (await Should.ThrowAsync<GraphReadException>(() => rejected.Transport.DownloadReportAsync(OneDriveReportPath, 1024, false, ct)))
            .Code.ShouldBe("ReportDownloadHostRejected");
        rejected.Calls.ShouldBe(1);
        using var probe = new SyntheticGraph(_ => Redirect("https://reportsneu.office.com/data/synthetic"));
        (await probe.Transport.DownloadReportAsync(OneDriveReportPath, 1, true, ct)).ShouldBeNull();
        probe.Calls.ShouldBe(1);
        using var large = new SyntheticGraph(_ => Redirect("https://reports.office.com/data/synthetic"),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2048]) });
        (await Should.ThrowAsync<GraphReadException>(() => large.Transport.DownloadReportAsync(OneDriveReportPath, 1024, false, ct)))
            .Code.ShouldBe("ReportFileTooLarge");
        (await Should.ThrowAsync<GraphReadException>(() => large.Transport.DownloadReportAsync("/v1.0/users", 1024, false, ct)))
            .Code.ShouldBe("UnsafeGraphLink");
    }

    [SqlFact]
    public async Task SharePointAndOneDriveConnectionsProbeOnlyTheirSectionsAndDownloadableReports()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        static HttpResponseMessage Probe(HttpRequestMessage request) => request.RequestUri!.AbsolutePath == OneDriveReportPath
            ? Redirect("https://reports.office.com/data/synthetic") : Response("", HttpStatusCode.Forbidden);
        using var http = new SyntheticGraph(_ => Organization(), Probe, Probe);
        var service = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, new SyntheticSessions(http.Transport));
        var connection = await service.ConnectAsync(fixture.Project.Id, environment.Id, "OneDrive", producerId: OneDriveCollector.ProducerId);
        var checks = (await service.TestAsync(fixture.Project.Id, environment.Id, connection)).ToDictionary(x => x.CapabilityId);
        checks.Keys.ShouldBe(["onedrive.accounts", "onedrive.accountUsage"], ignoreOrder: true);
        (checks["onedrive.accounts"].Available, checks["onedrive.accounts"].Code).ShouldBe((false, "MissingPermission"));
        (checks["onedrive.accountUsage"].Available, checks["onedrive.accountUsage"].Code).ShouldBe((true, "ReportCoverageLimited"));
        http.Calls.ShouldBe(3);
    }

    [SqlFact]
    public async Task DownloadedReportsAndSharePointSitesPublishThroughTheirOwnProducerConnections()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var csv = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ReportExamples", "m365.oneDriveUsageAccountDetail.v1.0.0.csv"));
        using var http = new SyntheticGraph(_ => Organization(),
            request =>
            {
                request.RequestUri!.AbsolutePath.ShouldBe(OneDriveReportPath);
                return Redirect("https://reports.office.com/data/synthetic");
            },
            request =>
            {
                request.Headers.Authorization.ShouldBeNull();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(csv) };
            },
            _ => Organization(),
            _ => Response("""{"value":[{"id":"synthetic.sharepoint.com,11111111-1111-1111-1111-111111111111,21111111-1111-1111-1111-111111111111","webUrl":"https://synthetic.sharepoint.com/sites/Alpha","root":{}}]}"""));
        var sessions = new SyntheticSessions(http.Transport);
        var store = new MemoryArtifactStore();
        var graph = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, sessions);
        var oneDrive = await graph.ConnectAsync(fixture.Project.Id, environment.Id, "OneDrive", producerId: OneDriveCollector.ProducerId);
        var sharePoint = await graph.ConnectAsync(fixture.Project.Id, environment.Id, "SharePoint", producerId: SharePointCollector.ProducerId);
        sharePoint.ShouldNotBe(oneDrive);
        await Should.ThrowAsync<ArgumentException>(() => graph.LaunchAsync(fixture.Project.Id, environment.Id, oneDrive, Guid.NewGuid(),
            Scope, ["onedrive.accountUsage", "onedrive.accounts"]));
        await Should.ThrowAsync<ArgumentException>(() => graph.LaunchAsync(fixture.Project.Id, environment.Id, sharePoint, Guid.NewGuid(),
            Scope, ["onedrive.accountUsage"]));
        await Should.ThrowAsync<ArgumentException>(() => graph.LaunchAsync(fixture.Project.Id, environment.Id, sharePoint, Guid.NewGuid(),
            Scope, ["sharepoint.siteDetails"]));
        var processor = new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, TimeProvider.System),
            [Handler(sessions, store)], NullLogger<CollectionJobProcessor>.Instance);

        await graph.LaunchAsync(fixture.Project.Id, environment.Id, oneDrive, Guid.NewGuid(), Scope, ["onedrive.accountUsage"]);
        (await processor.ProcessOneAsync(default)).ShouldBeTrue();
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.CollectionJobs.SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
            var run = await db.CollectionRuns.AsNoTracking().SingleAsync();
            (run.Channel, run.ProducerId, run.ConnectionId).ShouldBe((CollectionChannel.Report, "report.m365.oneDriveUsageAccountDetail", (Guid?)oneDrive));
            (await db.InventoryObjects.CountAsync(x => x.Type == "oneDrive")).ShouldBeGreaterThan(0);
            var value = await db.CurrentValues.AsNoTracking().FirstAsync();
            var proof = (await new InventoryProof(fixture.Factory, fixture.Access)
                .ReadAsync(fixture.Project.Id, new(ProofKind.Observation, value.ObservationId))).Sources.Single();
            proof.Source.ShouldBe(GraphReportDownloads.SourceId);
            proof.Modules["scopeSource"].ShouldBe("observed");
        }

        await graph.LaunchAsync(fixture.Project.Id, environment.Id, sharePoint, Guid.NewGuid(), Scope, ["sharepoint.sites"]);
        (await processor.ProcessOneAsync(default)).ShouldBeTrue();
        await using var check = await fixture.Factory.CreateDbContextAsync();
        var sites = await check.CollectionRuns.AsNoTracking().SingleAsync(x => x.ProducerId == SharePointCollector.ProducerId);
        (sites.Channel, sites.ConnectionId, sites.State).ShouldBe((CollectionChannel.Graph, (Guid?)sharePoint, CollectionRunState.Completed));
        var site = await check.InventoryObjects.AsNoTracking().SingleAsync(x => x.Type == "site");
        (await check.ExternalKeys.AsNoTracking().Where(x => x.ObjectId == site.Id).Select(x => x.Namespace + "|" + x.ExternalId).ToListAsync())
            .ShouldBe(["sharepoint.siteId|11111111-1111-1111-1111-111111111111"]);
        (await check.AlternateKeyPeriods.AsNoTracking().Where(x => x.ObjectId == site.Id && x.ValidUntilUtc == null)
            .Select(x => x.Type + "|" + x.Namespace + "|" + x.NormalizedValue).Distinct().ToListAsync())
            .ShouldBe(["site|url|https://synthetic.sharepoint.com/sites/Alpha"]);
        http.Calls.ShouldBe(5);
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }
}
