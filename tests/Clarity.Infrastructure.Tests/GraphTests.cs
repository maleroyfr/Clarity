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
public sealed partial class GraphTests
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
    [Trait("Recette", "R03")]
    [Trait("Recette", "R08")]
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
        var item = await check.InventoryObjects.AsNoTracking().SingleAsync();
        item.Presence.ShouldBe(InventoryPresence.Observed);
        item.DisplayName.ShouldBe("Synthetic");
        item.Smtp.ShouldBe("updated@example.test");

        using var delta = new SyntheticGraph(_ => Organization(), request =>
        {
            request.RequestUri!.Query.ShouldContain("$deltatoken=full");
            return Response("""{"value":[{"id":"user-a","displayName":"Renamed"}],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/users/delta?$deltatoken=renamed"}""");
        });
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, Guid.NewGuid(), Scope, ["entra.users"]);
        await new CollectionJobProcessor(queue, [Handler(new SyntheticSessions(delta.Transport), store)], NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default);
        var renamed = await check.InventoryObjects.AsNoTracking().SingleAsync();
        renamed.DisplayName.ShouldBe("Renamed");
        renamed.Smtp.ShouldBe(item.Smtp);
        (renamed.LastChangedAtUtc > item.LastChangedAtUtc).ShouldBeTrue();

        var date = DateTimeOffset.UtcNow;
        var supplement = new DiscoveryPackage(new PackageManifest("1.0", Guid.NewGuid(), "clarity.graph.entra", "1.0.0", "M365Tenant",
            Tenant.ToString(), Scope, date, date, new Dictionary<string, string>(), "Offline synthetic report"),
            [new ObjectRecord("entra.users", "user", "microsoft.graph", "user-a", date, new Dictionary<string, JsonElement>
            {
                ["displayName"] = JsonSerializer.SerializeToElement("Report name"),
                ["proxyAddresses"] = JsonSerializer.SerializeToElement(Array.Empty<string>())
            })], [], [new SectionResult("entra.users", "entra.users", CoverageStatus.Complete, Scope, ["displayName", "proxyAddresses"], [], 1, 0, [], [])]);
        var imports = ZipImportTests.Service(fixture, store);
        var uploaded = await imports.UploadAsync(fixture.Project.Id, environment.Id, new MemoryStream(await ZipImportTests.BytesAsync(supplement)), "offline.zip");
        await imports.ConfirmAsync(fixture.Project.Id, uploaded);
        await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default);
        await fixture.Publisher.RebuildEnvironmentAsync(fixture.Project.Id, environment.Id);
        (await check.InventoryObjects.AsNoTracking().SingleAsync()).Id.ShouldBe(item.Id);
        var current = await check.CurrentValues.AsNoTracking().ToDictionaryAsync(x => x.Field);
        current["displayName"].ValueJson.ShouldBe("\"Renamed\"");
        current["displayName"].Conflicting.ShouldBeTrue();
        current["proxyAddresses"].ValueJson.ShouldBe("[]");
        current.ContainsKey("userType").ShouldBeFalse();
        var proof = new InventoryProof(fixture.Factory, fixture.Access);
        var directProof = (await proof.ReadAsync(fixture.Project.Id, new(ProofKind.Observation, current["displayName"].ObservationId))).Sources.Single();
        directProof.Channel.ShouldBe("Microsoft Graph");
        directProof.Modules["Microsoft.Graph"].ShouldBe("REST v1.0");
        directProof.FileName.ShouldBeNull();
        var target = new ProofRef(ProofKind.Observation, current["proxyAddresses"].ObservationId);
        var zipProof = (await proof.ReadAsync(fixture.Project.Id, target)).Sources.Single();
        zipProof.Channel.ShouldBe("ZipImport");
        zipProof.FileName.ShouldBe("offline.zip");
        zipProof.Uploader.ShouldBe(fixture.Identity);
        zipProof.Source.ShouldBe("Offline synthetic report");
        zipProof.Scope.ShouldBe(Scope);
        JsonSerializer.Serialize(directProof).ShouldNotContain("deltatoken");
        var other = await fixture.OtherWorkspaceAsync();
        await Should.ThrowAsync<AccessDeniedException>(() => new InventoryProof(other.Factory, other.Access).ReadAsync(other.Project.Id, target));
        (await check.ObjectObservations.Select(x => x.SectionRunId).Distinct().CountAsync()).ShouldBe(4);
        await using var isolated = PersistenceTests.Create(fixture.ConnectionString, []);
        (await isolated.GraphCursors.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await isolated.CurrentValues.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [SqlFact]
    [Trait("Recette", "R10")]
    public async Task VerifiedConsistencyAnchorAndReviewedUpnSurviveUnrelatedRefreshButNotANewCandidate()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var matching = new InventoryMatching(fixture.Factory, fixture.Access, fixture.Scope);
        var anchor = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        await matching.VerifyAnchorAsync(fixture.Project.Id, fixture.Environment.Id, environment.Id, SourceAnchor.ConsistencyGuid, "Synthetic sync configuration");
        var adUser = Guid.NewGuid().ToString();
        var weakUser = Guid.NewGuid().ToString();
        var template = DiscoveryFixture.Package();
        async Task Ad(params (string Id, string Upn, string? Anchor)[] users)
        {
            var date = DateTimeOffset.UtcNow;
            var package = template with
            {
                Manifest = template.Manifest with { RunId = Guid.NewGuid(), StartedAtUtc = date, CompletedAtUtc = date },
                Objects = users.Select(u => template.Objects[0] with
                {
                    ExternalId = u.Id,
                    ObservedAtUtc = date,
                    Attributes = new Dictionary<string, JsonElement>
                    { ["userPrincipalName"] = JsonSerializer.SerializeToElement(u.Upn), ["msDSConsistencyGuid"] = JsonSerializer.SerializeToElement(u.Anchor) }
                }).ToArray(),
                Results = [template.Results[0] with { ObjectCount = users.Length, CoveredFields = ["userPrincipalName", "msDSConsistencyGuid"] }]
            };
            var run = await fixture.StageAsync(package);
            await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, run.Id, package, template.Objects[0].SectionId);
        }
        await Ad((adUser, "old@example.test", anchor), (weakUser, "shared@example.test", null));
        using var http = new SyntheticGraph(_ => Organization(), _ => Response(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["value"] = new object[] { new { id = "strong", onPremisesSyncEnabled = true, onPremisesImmutableId = anchor },
                new { id = "weak", userPrincipalName = "SHARED@example.test" }, new { id = "corrected", displayName = "Confirmed account" } },
            ["@odata.deltaLink"] = "https://graph.microsoft.com/v1.0/users/delta?$deltatoken=test"
        })));
        var sessions = new SyntheticSessions(http.Transport);
        var graph = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, sessions);
        var connection = await graph.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic");
        await graph.LaunchAsync(fixture.Project.Id, environment.Id, connection, Guid.NewGuid(), Scope, ["entra.users"]);
        await new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, TimeProvider.System),
            [Handler(sessions, new MemoryArtifactStore())], NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        var strong = await db.MappingDecisions.AsNoTracking().SingleAsync(x => x.Rule == AssociationRule.ConfiguredAnchor);
        strong.State.ShouldBe(MappingState.Validated);
        var weak = await db.MappingDecisions.AsNoTracking().SingleAsync(x => x.Rule == AssociationRule.ExactUpn);
        weak.State.ShouldBe(MappingState.Proposed);
        var review = new MappingReview(fixture.Factory, fixture.Access, fixture.Scope);
        MappingSelection[] selection = [new(strong.Id, strong.Revision), new(weak.Id, weak.Revision)];
        (await review.PreviewAsync(fixture.Project.Id, selection, true)).ShouldBe(1);
        await review.ApplyAsync(fixture.Project.Id, selection, true, 1, "Confirmed against synthetic directory");
        await Should.ThrowAsync<ConflictException>(() => review.ApplyAsync(fixture.Project.Id, selection, true, 1, "Stale preview"));
        await Ad((weakUser, "shared@example.test", null));
        (await db.MappingDecisions.AsNoTracking().SingleAsync(x => x.Id == weak.Id)).State.ShouldBe(MappingState.Validated);
        await Ad((Guid.NewGuid().ToString(), "shared@example.test", null));
        (await db.MappingDecisions.CountAsync(x => x.State == MappingState.ToReview)).ShouldBe(2);
        (await db.MappingRevisions.CountAsync(x => x.DecisionId == strong.Id)).ShouldBe(1);
        (await db.MappingRevisions.SingleAsync(x => x.ActorObjectId != null)).Reason.ShouldBe("Confirmed against synthetic directory");
        var history = await new InventoryProof(fixture.Factory, fixture.Access).ReadAsync(fixture.Project.Id, new(ProofKind.Mapping, weak.Id));
        history.Decisions.Single(d => d.Author.HasValue).Author.ShouldBe(fixture.Identity);
        history.Sources.Count.ShouldBeGreaterThanOrEqualTo(2);
        await matching.VerifyAnchorAsync(fixture.Project.Id, fixture.Environment.Id, environment.Id, SourceAnchor.ObjectGuid, "Different verified configuration");
        (await db.MappingDecisions.AsNoTracking().SingleAsync(x => x.Id == strong.Id)).State.ShouldBe(MappingState.ToReview);
        var other = await fixture.OtherWorkspaceAsync();
        var outsider = new MappingReview(other.Factory, other.Access, other.Scope);
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.ApplyAsync(other.Project.Id, selection, false, 2, "Wrong dossier"));
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.PreviewAsync(other.Project.Id, selection, false));
        var queueRows = await review.PageAsync(fixture.Project.Id, EnvironmentRole.Source, "Queue", new());
        var candidate = queueRows.Items.Single(r => r.Id == weak.Id);
        var corrected = await db.ExternalKeys.Where(k => k.EnvironmentId == environment.Id && k.ExternalId == "corrected").Select(k => k.ObjectId).SingleAsync();
        var correction = await review.PreviewCorrectionAsync(fixture.Project.Id, new(candidate.Id, candidate.Revision), corrected);
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.CorrectAsync(other.Project.Id, correction, "Wrong dossier"));
        await review.CorrectAsync(fixture.Project.Id, correction, "Confirmed by synthetic account owner");
        await fixture.Publisher.RebuildEnvironmentAsync(fixture.Project.Id, environment.Id);
        var validated = (await review.PageAsync(fixture.Project.Id, EnvironmentRole.Source, "Validated", new())).Items.Single();
        validated.EntraId.ShouldBe(corrected);
        validated.Rule.ShouldBe(AssociationRule.ConsultantQualified);
        (await db.MappingDecisions.AsNoTracking().SingleAsync(x => x.Id == weak.Id)).State.ShouldBe(MappingState.Rejected);
        var overview = await new InventoryOverview(fixture.Factory, fixture.Access).ReadAsync(fixture.Project.Id, EnvironmentRole.Source);
        var population = overview.Metrics.Single(m => m.Label == "ObservedPopulation");
        population.Amount.ShouldBe(5m);
        var inventory = new InventoryQueries(fixture.Factory, fixture.Access);
        (await inventory.PageAsync(fixture.Project.Id, population.Filter, new())).TotalCount.ShouldBe(5);
        overview.ReviewCount.ShouldBe((await review.PageAsync(fixture.Project.Id, EnvironmentRole.Source, "Queue", new())).TotalCount);
        overview.Metrics.Single(m => m.Label == "enabledUnits").Amount.ShouldBeNull();
        await using var outside = PersistenceTests.Create(fixture.ConnectionString, []);
        (await outside.MappingDecisions.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.MappingRevisions.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.SyncBindings.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [SqlFact]
    public async Task OverviewReconcilesKnownLicenseUnitsAndUnknownValuesToTheirFilteredRows()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        using var http = new SyntheticGraph(_ => Organization(), _ => Response("""
            {"value":[{"id":"known","skuId":"a","prepaidUnits":{"enabled":15},"consumedUnits":0},{"id":"unknown","skuId":"b"}]}
            """), _ => Response("""{"value":[]}"""));
        var sessions = new SyntheticSessions(http.Transport);
        var graph = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, sessions);
        var connection = await graph.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic");
        await graph.LaunchAsync(fixture.Project.Id, environment.Id, connection, Guid.NewGuid(), Scope, ["entra.licenses"]);
        await new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, TimeProvider.System),
            [Handler(sessions, new MemoryArtifactStore())], NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default);
        var overview = new InventoryOverview(fixture.Factory, fixture.Access);
        var data = await overview.ReadAsync(fixture.Project.Id, EnvironmentRole.Source);
        var known = data.Metrics.Single(m => m.Label == "enabledUnits");
        known.Amount.ShouldBe(15m);
        known.KnownResources.ShouldBe(1);
        known.ObservedResources.ShouldBe(2);
        data.Metrics.Single(m => m.Label == "consumedUnits").Amount.ShouldBe(0m);
        var inventory = new InventoryQueries(fixture.Factory, fixture.Access);
        (await inventory.PageAsync(fixture.Project.Id, known.Filter, new())).Items.Single().VolumeValue.ShouldBe("15");
        var unit = data.Units.Single();
        (await inventory.PageAsync(fixture.Project.Id, new(EnvironmentRole.Source, environment.Id, Run: unit.RunId, Section: unit.SectionId), new())).TotalCount.ShouldBe(2);
        (await overview.ReadAsync(fixture.Project.Id, EnvironmentRole.Target)).Metrics.Single(m => m.Label == "enabledUnits").Amount.ShouldBeNull();
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

    private static async Task ValidateAsync(GraphSectionData data, string producer = GraphDiscoveryService.EntraProducerId)
    {
        var package = new DiscoveryPackage(new PackageManifest("1.0", Guid.NewGuid(), producer, "1.0.0", "M365Tenant",
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
