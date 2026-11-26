using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class GraphTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000123");
    private static readonly CollectionScope Scope = new("tenant", Tenant.ToString());
    private static readonly DiscoveryCatalog Catalog = DiscoveryCatalog.Load();

    [Fact]
    public async Task PagingThrottleAndDeltaReplayPreserveAbsentAndExplicitEmptyFields()
    {
        var waits = new List<TimeSpan>();
        using var http = new SyntheticGraph(
            _ => { var response = Response("", HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3)); return response; },
            _ => Response("""{"value":[],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users/delta?$skiptoken=one"}"""),
            _ => Response("""{"value":[{"id":"user-a","displayName":"Synthetic","mail":"synthetic@example.test"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users/delta?$skiptoken=two"}"""),
            _ => Response("""{"value":[{"id":"user-a","proxyAddresses":[]},{"id":"user-b","@removed":{"reason":"deleted"}}],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/users/delta?$deltatoken=next"}"""));
        var transport = new GraphTransport(new HttpClient(http), _ => ValueTask.FromResult("synthetic-token"),
            (wait, _) => { waits.Add(wait); return Task.CompletedTask; });
        var data = await new EntraCollector(Catalog).CollectAsync(transport, "entra.users", Scope, null, TestContext.Current.CancellationToken);
        waits.ShouldBe([TimeSpan.FromSeconds(3)]);
        data.Result.Status.ShouldBe(CoverageStatus.Complete);
        data.Objects.Count.ShouldBe(2);
        var user = data.Objects.Single(x => x.ExternalId == "user-a");
        user.Attributes["displayName"].GetString().ShouldBe("Synthetic");
        user.Attributes["proxyAddresses"].GetArrayLength().ShouldBe(0);
        user.Attributes.ContainsKey("onPremisesImmutableId").ShouldBeFalse();
        data.Objects.Single(x => x.ExternalId == "user-b").Attributes["deleted"].GetBoolean().ShouldBeTrue();
        await ValidateAsync(data);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ExpiredCursorForcesFullReadAndFailedPageDoesNotAdvanceCursor(HttpStatusCode expired)
    {
        using var http = new SyntheticGraph(
            _ => Response("""{"error":{"code":"syncStateNotFound"}}""", expired),
            request => { request.RequestUri!.AbsolutePath.ShouldBe("/v1.0/users/delta"); return Response("""{"value":[{"id":"a"}],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/users/delta?$deltatoken=new"}"""); });
        var data = await new EntraCollector(Catalog).CollectAsync(http.Transport, "entra.users", Scope,
            "https://graph.microsoft.com/v1.0/users/delta?$deltatoken=expired", TestContext.Current.CancellationToken);
        data.Result.Status.ShouldBe(CoverageStatus.Complete);
        data.DeltaLink.ShouldEndWith("=new");
        using var partial = new SyntheticGraph(
            _ => Response("""{"value":[{"id":"a","mail":null}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users/delta?$skiptoken=two"}"""),
            _ => Response("", HttpStatusCode.Forbidden));
        data = await new EntraCollector(Catalog).CollectAsync(partial.Transport, "entra.users", Scope,
            "https://graph.microsoft.com/v1.0/users/delta?$deltatoken=old", TestContext.Current.CancellationToken);
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        data.DeltaLink.ShouldBeNull();
        data.Result.Errors.ShouldHaveSingleItem().Code.ShouldBe("MissingPermission");
        data.Objects.ShouldHaveSingleItem().Attributes["mail"].ValueKind.ShouldBe(JsonValueKind.Null);
        await ValidateAsync(data);
    }

    [Fact]
    public async Task IncrementalEmptyAndFailedReadsNeverClaimKnownTenantZero()
    {
        using var delta = new SyntheticGraph(_ => Response("""{"value":[],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/users/delta?$deltatoken=new"}"""));
        var collector = new EntraCollector(Catalog);
        var data = await collector.CollectAsync(delta.Transport, "entra.users", Scope,
            "https://graph.microsoft.com/v1.0/users/delta?$deltatoken=old", TestContext.Current.CancellationToken);
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        data.Result.Warnings.ShouldHaveSingleItem().Code.ShouldBe("IncrementalChanges");
        using var failure = new SyntheticGraph(_ => Response("", HttpStatusCode.Forbidden));
        data = await collector.CollectAsync(failure.Transport, "entra.devices", Scope, null, TestContext.Current.CancellationToken);
        data.Result.Status.ShouldBe(CoverageStatus.Failed);
        data.Result.ObjectCount.ShouldBeNull();
        using var empty = new SyntheticGraph(_ => Response("""{"value":[]}"""));
        data = await collector.CollectAsync(empty.Transport, "entra.devices", Scope, null, TestContext.Current.CancellationToken);
        data.Result.Status.ShouldBe(CoverageStatus.Complete);
        data.Result.ObjectCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("https://example.test/v1.0/users")]
    [InlineData("http://graph.microsoft.com/v1.0/users")]
    [InlineData("https://graph.microsoft.com/beta/users")]
    public async Task UnsafeContinuationIsRejectedBeforeSendingAnyCredentials(string link)
    {
        using var http = new SyntheticGraph(_ => Response(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["value"] = Array.Empty<object>(),
            ["@odata.nextLink"] = link
        })));
        (await Should.ThrowAsync<GraphReadException>(() => http.Transport.PagesAsync("/v1.0/users",
            _ => Task.CompletedTask, TestContext.Current.CancellationToken))).Code.ShouldBe("UnsafeGraphLink");
        http.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task RetriesAreBoundedAndLongRetryAfterDoesNotRetryEarly()
    {
        var waits = new List<TimeSpan>();
        using var http = new SyntheticGraph(Enumerable.Range(0, 4).Select<int, Func<HttpRequestMessage, HttpResponseMessage>>(
            _ => _ => Response("", HttpStatusCode.ServiceUnavailable)).ToArray());
        var transport = new GraphTransport(new HttpClient(http), _ => ValueTask.FromResult("synthetic"),
            (wait, _) => { waits.Add(wait); return Task.CompletedTask; });
        (await Should.ThrowAsync<GraphReadException>(() => transport.GetAsync("/v1.0/devices", TestContext.Current.CancellationToken)))
            .Code.ShouldBe("Throttled");
        http.Calls.ShouldBe(4);
        waits.Select(x => x.TotalSeconds).ShouldBe([1d, 2d, 4d]);
        using var longWait = new SyntheticGraph(_ =>
        {
            var response = Response("", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
            return response;
        });
        (await Should.ThrowAsync<GraphReadException>(() => longWait.Transport.GetAsync("/v1.0/devices", TestContext.Current.CancellationToken)))
            .Code.ShouldBe("RetryAfterLimit");
        longWait.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task DirectoryFamiliesProduceCanonicalMetadataAndHonestMembershipCoverage()
    {
        var collector = new EntraCollector(Catalog);
        using var groups = new SyntheticGraph(
            _ => Response("""{"value":[{"id":"group-a","displayName":"Synthetic Group","securityEnabled":true}]}"""),
            _ => Response("""{"value":[{"id":"user-a","@odata.type":"#microsoft.graph.user"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/groups/group-a/members?$skiptoken=two"}"""),
            _ => Response("""{"value":[{"id":"user-a","@odata.type":"#microsoft.graph.user"}]}"""),
            _ => Response("""{"value":[{"id":"user-a","@odata.type":"#microsoft.graph.user"}]}"""));
        var data = await collector.CollectAsync(groups.Transport, "entra.groups", Scope, null, TestContext.Current.CancellationToken);
        data.Relationships.Count.ShouldBe(2);
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        await ValidateAsync(data);
        using var licenses = new SyntheticGraph(
            _ => Response("""{"value":[{"id":"subscription-a","skuId":"sku-a","prepaidUnits":{"enabled":5},"consumedUnits":1}]}"""),
            _ => Response("""{"value":[{"id":"a","assignedLicenses":[{"skuId":"sku-a"}]},{"id":"b","assignedLicenses":[]}]}"""));
        data = await collector.CollectAsync(licenses.Transport, "entra.licenses", Scope, null, TestContext.Current.CancellationToken);
        data.Result.Status.ShouldBe(CoverageStatus.Complete);
        data.Relationships.ShouldHaveSingleItem().To.Namespace.ShouldBe("microsoft.graph.skuId");
        data.Objects.Single(x => x.ExternalId == "b").Attributes["assignedSkuIds"].GetArrayLength().ShouldBe(0);
        await ValidateAsync(data);
        using var apps = new SyntheticGraph(
            request => { request.RequestUri!.Query.ShouldNotContain("passwordCredentials"); return Response("""{"value":[{"id":"app","appId":"synthetic-app","passwordCredentials":[{"secretText":"not-collected"}]}]}"""); },
            _ => Response("""{"value":[{"id":"sp","servicePrincipalType":"Application","accountEnabled":true}]}"""));
        data = await collector.CollectAsync(apps.Transport, "entra.applications", Scope, null, TestContext.Current.CancellationToken);
        data.Objects[0].Attributes.ContainsKey("passwordCredentials").ShouldBeFalse();
        await ValidateAsync(data);
        using var domains = new SyntheticGraph(_ => Response("""{"value":[{"id":"example.test","isVerified":true,"isDefault":true}]}"""));
        await ValidateAsync(await collector.CollectAsync(domains.Transport, "entra.domains", Scope, null, TestContext.Current.CancellationToken));
    }

    [SqlFact]
    public async Task GraphUsesCommonIngestionSurvivesRestartAndPersistsCursorOnlyAfterPublication()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        using var http = new SyntheticGraph(
            _ => Organization(),
            _ => Response("""{"value":[{"id":"user-a","displayName":"Synthetic"}],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/users/delta?$deltatoken=first"}"""));
        var sessions = new SyntheticSessions(http.Transport);
        var service = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, sessions);
        var connection = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic tenant");
        (await service.ConnectAsync(fixture.Project.Id, environment.Id, "Reuse")).ShouldBe(connection);
        var id = Guid.NewGuid();
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, id, Scope, ["entra.users"]);
        var store = new MemoryArtifactStore();
        var handler = Handler(sessions, store);
        var time = new TestClock();
        var queue = new SqlJobQueue(fixture.ConnectionString, time);
        var interrupted = new InterruptPublication(handler);
        using var stop = new CancellationTokenSource();
        var processing = new CollectionJobProcessor(queue, [interrupted], NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(stop.Token);
        await interrupted.Published.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await stop.CancelAsync();
        await processing;
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.ObjectObservations.CountAsync()).ShouldBe(1);
            (await db.GraphCursors.CountAsync()).ShouldBe(0);
            (await db.CollectionRuns.SingleAsync()).Channel.ShouldBe(CollectionChannel.Graph);
        }
        time.Advance();
        await new CollectionJobProcessor(queue, [handler], NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default);
        sessions.Created.ShouldBe(1);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.ObjectObservations.CountAsync()).ShouldBe(1);
            (await db.GraphCursors.SingleAsync()).DeltaLink.ShouldEndWith("=first");
            (await db.CollectionJobs.SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
        }
        using var partial = new SyntheticGraph(_ => Organization(), request =>
        {
            request.RequestUri!.Query.ShouldContain("$deltatoken=first");
            return Response("""{"value":[{"id":"user-a","mail":"updated@example.test"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users/delta?$skiptoken=limited"}""");
        }, _ => Response("", HttpStatusCode.Forbidden));
        var partialId = Guid.NewGuid();
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, partialId, Scope, ["entra.users"]);
        await new CollectionJobProcessor(queue, [Handler(new SyntheticSessions(partial.Transport), store)],
            NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.ObjectObservations.CountAsync()).ShouldBe(2);
            (await db.InventoryObjects.CountAsync()).ShouldBe(1);
            (await db.SectionRuns.SingleAsync(x => x.RunId == partialId)).Coverage.ShouldBe(CoverageStatus.Partial);
            (await db.SectionRuns.SingleAsync(x => x.RunId == partialId)).PublishedAtUtc.ShouldNotBeNull();
            (await db.GraphCursors.SingleAsync()).DeltaLink.ShouldEndWith("=first");
        }
        using var refresh = new SyntheticGraph(_ => Organization(), request =>
        {
            request.RequestUri!.Query.ShouldContain("$deltatoken=first");
            return Response("", HttpStatusCode.Forbidden);
        });
        var nextSessions = new SyntheticSessions(refresh.Transport);
        var nextId = Guid.NewGuid();
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, nextId, Scope, ["entra.users"]);
        await new CollectionJobProcessor(queue, [Handler(nextSessions, store)], NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.ObjectObservations.CountAsync()).ShouldBe(2);
            (await db.GraphCursors.SingleAsync()).DeltaLink.ShouldEndWith("=first");
            (await db.SectionRuns.SingleAsync(x => x.RunId == nextId)).Coverage.ShouldBe(CoverageStatus.Failed);
            await db.GraphCursors.ExecuteUpdateAsync(update => update.SetProperty(x => x.ScopeHash, new string('0', 64)));
        }
        using var full = new SyntheticGraph(_ => Organization(), request =>
        {
            request.RequestUri!.Query.ShouldContain("$select");
            return Response("""{"value":[],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/users/delta?$deltatoken=full"}""");
        });
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, Guid.NewGuid(), Scope, ["entra.users"]);
        await new CollectionJobProcessor(queue, [Handler(new SyntheticSessions(full.Transport), store)], NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default);
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.GraphCursors.SingleAsync()).DeltaLink.ShouldEndWith("=full");
        await using var isolated = PersistenceTests.Create(fixture.ConnectionString, []);
        (await isolated.GraphCursors.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [SqlFact]
    public async Task ObservedTenantMismatchPublishesNothingAndSectionAccessTestsExplainMissingPermission()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        using var wrong = new SyntheticGraph(_ => Response("""{"value":[{"id":"00000000-0000-0000-0000-000000000999"}]}"""));
        var sessions = new SyntheticSessions(wrong.Transport);
        var service = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, sessions);
        var connection = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic");
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, Guid.NewGuid(), Scope, ["entra.users"]);
        await new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, TimeProvider.System),
            [Handler(sessions, new MemoryArtifactStore())], NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.CollectionRuns.CountAsync()).ShouldBe(0);
            (await db.ObjectObservations.CountAsync()).ShouldBe(0);
            (await db.CollectionJobs.SingleAsync()).ErrorCode.ShouldBe("EnvironmentMismatch");
        }
        using var accessHttp = new SyntheticGraph(new[] { new Func<HttpRequestMessage, HttpResponseMessage>(_ => Organization()) }
            .Concat(Enumerable.Range(0, 6).Select<int, Func<HttpRequestMessage, HttpResponseMessage>>(_ => _ => Response("", HttpStatusCode.Forbidden))).ToArray());
        var checks = await new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, new SyntheticSessions(accessHttp.Transport))
            .TestAsync(fixture.Project.Id, environment.Id, connection);
        checks.Count.ShouldBe(6);
        checks.ShouldAllBe(x => !x.Available && x.Code == "MissingPermission" && x.Permissions.Count > 0);
    }

    private static GraphJobHandler Handler(IGraphSessionFactory sessions, IArtifactStore store) => new(sessions, new EntraCollector(Catalog),
        store, new ZipPackageReader(new PackageValidator(), new PackageLimits()), new PackageLimits(), Catalog, new PackageValidator());

    private static async Task<ProjectEnvironment> AddTenantAsync(DiscoveryFixture fixture)
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();
        await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.WorkspaceManager));
        var environment = new ProjectEnvironment { WorkspaceId = fixture.Workspace.Id, ProjectId = fixture.Project.Id, Kind = EnvironmentKind.M365Tenant, ExternalId = Tenant.ToString() };
        db.Environments.Add(environment);
        await db.SaveChangesAsync();
        return environment;
    }

    private static async Task ValidateAsync(GraphSectionData data)
    {
        var package = new DiscoveryPackage(new PackageManifest("1.0", Guid.NewGuid(), "clarity.graph.entra", "1.0.0", "M365Tenant",
            Tenant.ToString(), Scope, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow,
            new Dictionary<string, string>(), "Synthetic Graph"), data.Objects, data.Relationships, [data.Result]);
        var parsed = await new ZipPackageReader(new PackageValidator(), new PackageLimits())
            .ReadAsync(new MemoryStream(await ZipImportTests.BytesAsync(package)), cancellationToken: TestContext.Current.CancellationToken);
        Catalog.ValidatePackage(parsed.Package);
    }

    private static HttpResponseMessage Organization() => Response($$"""{"value":[{"id":"{{Tenant}}"}]}""");
    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json) };

    private sealed class SyntheticGraph(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public GraphTransport Transport => new(new HttpClient(this, disposeHandler: false), _ => ValueTask.FromResult("synthetic-token"));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses[Calls++](request));
    }

    private sealed class SyntheticSessions(GraphTransport transport) : IGraphSessionFactory
    {
        public int Created { get; private set; }
        public Task<GraphSession> CreateAsync(Connection connection, CancellationToken cancellationToken)
        {
            Created++;
            return Task.FromResult(new GraphSession(transport));
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(1);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = DateTimeOffset.UtcNow.AddMinutes(1);
    }

    private sealed class InterruptPublication(GraphJobHandler inner) : ICollectionJobHandler
    {
        public CollectionJobKind Kind => CollectionJobKind.Graph;
        public TaskCompletionSource Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken) =>
            inner.ExecuteAsync(execution with
            {
                ReportProgressAsync = async (progress, token) =>
                {
                    await execution.ReportProgressAsync(progress, token);
                    if (progress > 0) { Published.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
                }
            }, cancellationToken);
    }
}
