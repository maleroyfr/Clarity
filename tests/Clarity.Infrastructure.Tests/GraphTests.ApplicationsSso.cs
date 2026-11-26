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
    private const string SsoResource = "10000000-0000-0000-0000-000000000abc";
    private const string SsoApplication = "20000000-0000-0000-0000-000000000def";
    private const string SsoAppId = "30000000-0000-0000-0000-000000000abc";
    private const string SsoPrincipal = "40000000-0000-0000-0000-000000000def";
    private const string SsoOtherResource = "50000000-0000-0000-0000-000000000abc";

    [Theory]
    [InlineData("applicationsSso.applications", "applications", "application")]
    [InlineData("applicationsSso.servicePrincipals", "servicePrincipals", "servicePrincipal")]
    public async Task SsoMetadataIsAllowlistedAndNullModeDoesNotMeanNoSso(string capability, string path, string type)
    {
        var result = await SsoAsync(capability, r =>
        {
            r.Method.ShouldBe(HttpMethod.Get);
            r.RequestUri!.AbsolutePath.ShouldBe("/v1.0/" + path);
            r.RequestUri.Query.ShouldNotContain("Credentials");
            return Response($$$"""
                {"value":[{"id":"{{{SsoResource.ToUpperInvariant()}}}","appId":"{{{SsoAppId.ToUpperInvariant()}}}",
                "displayName":"Synthetic portal","preferredSingleSignOnMode":null,"accountEnabled":false,
                "passwordCredentials":[{"secretText":"never-retain"}],"keyCredentials":[{"key":"never-retain"}],
                "samlSingleSignOnSettings":{"relayState":"never-retain"}}]}
                """);
        });
        result.Result.Status.ShouldBe(CoverageStatus.Complete);
        var item = result.Objects.ShouldHaveSingleItem();
        item.Type.ShouldBe(type);
        item.ExternalId.ShouldBe(SsoResource);
        item.Attributes["appId"].GetString().ShouldBe(SsoAppId);
        JsonSerializer.Serialize(result).ShouldNotContain("never-retain");
        if (type == "servicePrincipal")
        {
            item.Attributes["preferredSingleSignOnMode"].ValueKind.ShouldBe(JsonValueKind.Null);
            item.Attributes.ContainsKey("appRoleAssignmentRequired").ShouldBeFalse();
            result.Relationships.ShouldHaveSingleItem().To.ShouldBe(new("application", "appId", SsoAppId));
        }
        else { result.Relationships.ShouldBeEmpty(); }
    }

    [Theory]
    [InlineData("User", "user")]
    [InlineData("Group", "group")]
    [InlineData("ServicePrincipal", "servicePrincipal")]
    [InlineData("FutureType", "directoryObject")]
    [InlineData(null, "directoryObject")]
    public async Task SsoAssignmentsKeepExplicitTypedPrincipalsResourcesAndDistinctRoles(string? principalType, string expectedType)
    {
        var result = await SsoAsync("applicationsSso.appRoleAssignments",
            _ => SsoParents(SsoResource),
            r =>
            {
                r.RequestUri!.PathAndQuery.ShouldBe($"/v1.0/servicePrincipals/{SsoResource}/appRoleAssignedTo?$select=id,appRoleId,principalId,principalType,resourceId,createdDateTime");
                return Response($$"""{"value":[{{SsoAssignment("role-one", principalType)}}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/servicePrincipals/{{SsoResource}}/appRoleAssignedTo?$skiptoken=synthetic"}""");
            },
            _ => Response($$"""{"value":[{{SsoAssignment("role-two", principalType)}},{{SsoAssignment("role-two", principalType)}}]}"""));
        result.Result.Status.ShouldBe(expectedType == "directoryObject" ? CoverageStatus.Partial : CoverageStatus.Complete);
        result.Objects.Count.ShouldBe(2);
        result.Objects.ShouldAllBe(o => o.Type == "appRoleAssignment" && o.Attributes["appRoleId"].GetString() == Guid.Empty.ToString());
        result.Relationships.Count.ShouldBe(4);
        result.Relationships.Where(e => e.RelationType == "appRoleAssignmentPrincipal").ShouldAllBe(e => e.To == new ExternalReference(expectedType, "microsoft.graph", SsoPrincipal));
        result.Relationships.Where(e => e.RelationType == "appRoleAssignmentResource").ShouldAllBe(e => e.To == new ExternalReference("servicePrincipal", "microsoft.graph", SsoResource));
        result.Relationships.ShouldNotContain(e => e.RelationType == "memberOf");
    }

    [Fact]
    public async Task SsoPartialParentsAndPagesPreserveSuccessWithoutInferringAssignmentsOrAbsence()
    {
        var partial = await SsoAsync("applicationsSso.appRoleAssignments",
            _ => SsoParents(SsoOtherResource, SsoResource),
            _ => Response("never-retain", HttpStatusCode.Forbidden),
            _ => Response($$"""{"value":[{{SsoAssignment("assignment", "User")}}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/servicePrincipals/{{SsoResource}}/appRoleAssignedTo?$skiptoken=more"}"""),
            _ => Response("never-retain", HttpStatusCode.Forbidden));
        partial.Result.Status.ShouldBe(CoverageStatus.Partial);
        partial.Objects.ShouldHaveSingleItem();
        partial.Result.Errors.ShouldHaveSingleItem().Code.ShouldBe("MissingPermission");
        JsonSerializer.Serialize(partial).ShouldNotContain("never-retain");
        var mismatch = await SsoAsync("applicationsSso.appRoleAssignments", _ => SsoParents(SsoOtherResource),
            _ => Response($$"""{"value":[{{SsoAssignment("wrong-resource", "Group")}}]}"""));
        mismatch.Result.Status.ShouldBe(CoverageStatus.Failed);
        mismatch.Result.Errors.ShouldHaveSingleItem().Code.ShouldBe("SsoResourceMismatch");
        mismatch.Objects.ShouldBeEmpty();
        var failed = await SsoAsync("applicationsSso.applications", _ => Response("", HttpStatusCode.Forbidden));
        failed.Result.ObjectCount.ShouldBeNull();
        var empty = await SsoAsync("applicationsSso.appRoleAssignments", _ => Response("""{"value":[]}"""));
        empty.Result.Status.ShouldBe(CoverageStatus.Complete);
        empty.Result.ObjectCount.ShouldBe(0);
        empty.Relationships.ShouldBeEmpty();
        var unsafePage = await SsoAsync("applicationsSso.applications",
            _ => Response("""{"value":[],"@odata.nextLink":"https://graph.microsoft.com/beta/applications"}"""));
        unsafePage.Result.Errors.ShouldHaveSingleItem().Code.ShouldBe("UnsafeGraphLink");
    }

    [SqlFact]
    public async Task SsoAccessChecksUseSelectedFieldsAndDistinguishUntestedChildren()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        using var http = new SyntheticGraph(_ => Organization(),
            r => { r.RequestUri!.Query.ShouldContain("signInAudience"); return Response("""{"value":[]}"""); },
            r => { r.RequestUri!.Query.ShouldContain("preferredSingleSignOnMode"); return Response("never-retain", HttpStatusCode.Forbidden); },
            _ => Response("""{"value":[]}"""),
            _ => Organization(), _ => Response("""{"value":[]}"""), _ => Response("""{"value":[]}"""), _ => SsoParents(SsoResource),
            r => { r.RequestUri!.AbsolutePath.ShouldEndWith("/appRoleAssignedTo"); return Response("never-retain", HttpStatusCode.Forbidden); });
        var service = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, new SyntheticSessions(http.Transport));
        var connection = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic apps", producerId: ApplicationsSsoCollector.ProducerId);
        var other = await fixture.OtherWorkspaceAsync();
        await Should.ThrowAsync<AccessDeniedException>(() => new GraphDiscoveryService(other.Factory, other.Access, other.Scope, Catalog,
            new SyntheticSessions(http.Transport)).LaunchAsync(other.Project.Id, environment.Id, connection, Guid.NewGuid(), Scope, ["applicationsSso.applications"]));
        await Should.ThrowAsync<AccessDeniedException>(() => new GraphDiscoveryService(other.Factory, other.Access, other.Scope, Catalog,
            new SyntheticSessions(http.Transport)).TestAsync(other.Project.Id, environment.Id, connection));
        http.Calls.ShouldBe(0);
        var checks = (await service.TestAsync(fixture.Project.Id, environment.Id, connection)).ToDictionary(x => x.CapabilityId);
        checks["applicationsSso.applications"].Available.ShouldBeTrue();
        checks["applicationsSso.servicePrincipals"].Code.ShouldBe("MissingPermission");
        checks["applicationsSso.appRoleAssignments"].Available.ShouldBeFalse();
        checks["applicationsSso.appRoleAssignments"].Code.ShouldBe("SsoAssignmentsNotTested");
        checks.Values.ShouldAllBe(x => x.Permissions.SequenceEqual(new[] { "Application.Read.All" }));
        checks = (await service.TestAsync(fixture.Project.Id, environment.Id, connection)).ToDictionary(x => x.CapabilityId);
        checks["applicationsSso.appRoleAssignments"].Code.ShouldBe("MissingPermission");
        http.Calls.ShouldBe(9);
    }

    [SqlFact]
    public async Task SsoGraphPublicationAndQualifiedHistoryStayIndependentAuthorizedAndWorkspaceScoped()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        using var http = new SyntheticGraph(
            _ => Organization(), _ => SsoMetadata("saml"), _ => SsoParents(SsoResource),
            _ => Response($$"""{"value":[{{SsoAssignment("synthetic-role", "User")}}]}"""),
            _ => Organization(), _ => Response($$"""{"value":[{"id":"{{SsoApplication}}","appId":"{{SsoAppId.ToUpperInvariant()}}","displayName":"Synthetic registration"}]}"""),
            _ => Organization(), _ => Response($$"""{"value":[{"id":"{{SsoPrincipal}}"}],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/users/delta?$deltatoken=synthetic"}"""),
            _ => Organization(), _ => SsoMetadata("saml"),
            _ => Organization(), _ => SsoMetadata("oidc", next: true), _ => Response("never-retain", HttpStatusCode.Forbidden),
            _ => SsoParents(SsoResource), _ => Response("never-retain", HttpStatusCode.Forbidden),
            _ => Organization(), _ => SsoMetadata("oidc"),
            _ => Organization(), _ => Response("""{"value":[]}"""), _ => Response("""{"value":[]}"""));
        var sessions = new SyntheticSessions(http.Transport);
        var service = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, sessions);
        var connection = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic apps", producerId: ApplicationsSsoCollector.ProducerId);
        var directory = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Synthetic directory");
        connection.ShouldNotBe(directory);
        await Should.ThrowAsync<ArgumentException>(() => service.LaunchAsync(fixture.Project.Id, environment.Id, directory, Guid.NewGuid(), Scope, ["applicationsSso.servicePrincipals"]));
        var processor = new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, TimeProvider.System),
            [Handler(sessions, new MemoryArtifactStore())], NullLogger<CollectionJobProcessor>.Instance);
        var operation = Guid.NewGuid();
        string[] sections = ["applicationsSso.servicePrincipals", "applicationsSso.appRoleAssignments"];
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, operation, Scope, sections);
        await service.LaunchAsync(fixture.Project.Id, environment.Id, connection, operation, Scope, sections);
        await processor.ProcessOneAsync(default);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.CollectionJobs.SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
        (await db.CollectionRuns.SingleAsync()).Channel.ShouldBe(CollectionChannel.Graph);
        (await db.InventoryRelationships.SingleAsync(x => x.RelationType == "servicePrincipalForApplication")).ToMethod.ShouldBe(EndpointResolution.Pending);
        (await db.InventoryRelationships.SingleAsync(x => x.RelationType == "appRoleAssignmentPrincipal")).ToMethod.ShouldBe(EndpointResolution.Pending);
        await CollectAsync(connection, ["applicationsSso.applications"]);
        await CollectAsync(directory, ["entra.users"]);
        var applicationLink = await db.InventoryRelationships.AsNoTracking().SingleAsync(x => x.RelationType == "servicePrincipalForApplication");
        applicationLink.ToMethod.ShouldBe(EndpointResolution.Alternate);
        applicationLink.FromObjectId.ShouldNotBe(applicationLink.ToObjectId);
        (await db.InventoryRelationships.AsNoTracking().SingleAsync(x => x.RelationType == "appRoleAssignmentPrincipal")).ToMethod.ShouldBe(EndpointResolution.Primary);
        var subject = applicationLink.FromObjectId!.Value;
        var observation = await db.ObjectObservations.SingleAsync(x => x.ObjectId == subject);
        var qualifications = new SsoQualificationService(fixture.Factory, fixture.Access, fixture.Scope);
        var initial = await qualifications.ReadAsync(fixture.Project.Id, subject);
        var input = new SsoQualificationInput(Guid.NewGuid(), 0, initial.MetadataFingerprint, SsoDependencyValue.Required, SsoProtocol.Saml,
            "Synthetic portal depends on the source identity provider; adapt its federation.", "Synthetic workshop SSO-1", observation.Id);
        await qualifications.RecordAsync(fixture.Project.Id, subject, input);
        await qualifications.RecordAsync(fixture.Project.Id, subject, input);
        var recorded = (await qualifications.ReadAsync(fixture.Project.Id, subject)).Current!;
        recorded.Revision.ShouldBe(1);
        recorded.EnvironmentId.ShouldBe(environment.Id);
        recorded.AuthorTenantId.ShouldBe(fixture.Identity.TenantId);
        recorded.AuthorObjectId.ShouldBe(fixture.Identity.ObjectId);
        recorded.RecordedAtUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-2));
        await Should.ThrowAsync<ConflictException>(() => qualifications.RecordAsync(fixture.Project.Id, subject, input with { Reason = "Changed replay" }));
        await Should.ThrowAsync<ConflictException>(() => qualifications.RecordAsync(fixture.Project.Id, subject, input with { OperationId = Guid.NewGuid() }));
        var otherEvidence = await db.ObjectObservations.FirstAsync(x => x.ObjectId != subject);
        await Should.ThrowAsync<AccessDeniedException>(() => qualifications.RecordAsync(fixture.Project.Id, subject,
            input with { OperationId = Guid.NewGuid(), ExpectedRevision = 1, EvidenceObservationId = otherEvidence.Id }));

        await CollectAsync(connection, ["applicationsSso.servicePrincipals"]);
        (await qualifications.ReadAsync(fixture.Project.Id, subject)).NeedsReview.ShouldBeFalse();
        await CollectAsync(connection, sections);
        (await qualifications.ReadAsync(fixture.Project.Id, subject)).NeedsReview.ShouldBeFalse();
        (await db.CurrentValues.AsNoTracking().SingleAsync(x => x.ObjectId == subject && x.Field == "preferredSingleSignOnMode")).ValueJson.ShouldBe("\"saml\"");
        (await db.InventoryRelationships.AsNoTracking().SingleAsync(x => x.RelationType == "appRoleAssignmentPrincipal")).IsCurrent.ShouldBeTrue();
        (await db.SectionRuns.CountAsync(x => x.Coverage == CoverageStatus.Partial)).ShouldBe(1);
        await CollectAsync(connection, ["applicationsSso.servicePrincipals"]);
        var changed = await qualifications.ReadAsync(fixture.Project.Id, subject);
        changed.NeedsReview.ShouldBeTrue();
        changed.Current!.Id.ShouldBe(recorded.Id);
        (await db.CurrentValues.SingleAsync(x => x.ObjectId == subject && x.Field == "preferredSingleSignOnMode")).ValueJson.ShouldBe("\"oidc\"");
        await Should.ThrowAsync<ConflictException>(() => qualifications.RecordAsync(fixture.Project.Id, subject,
            input with { OperationId = Guid.NewGuid(), ExpectedRevision = 1 }));
        var second = input with
        {
            OperationId = Guid.NewGuid(),
            ExpectedRevision = 1,
            MetadataFingerprint = changed.MetadataFingerprint,
            Value = SsoDependencyValue.Unknown,
            Protocol = SsoProtocol.Unknown,
            Reason = "Synthetic follow-up needs confirmation.",
            EvidenceObservationId = null
        };
        await qualifications.RecordAsync(fixture.Project.Id, subject, second);
        var history = await qualifications.HistoryAsync(fixture.Project.Id, subject, new(1, 1));
        history.TotalCount.ShouldBe(2);
        history.Items.ShouldHaveSingleItem().Id.ShouldBe(recorded.Id);
        history.Items[0].EvidenceObservationId.ShouldBe(observation.Id);
        (await db.AuditEvents.CountAsync(x => x.Action == "SsoQualification.Recorded")).ShouldBe(2);
        (await db.MappingDecisions.CountAsync()).ShouldBe(0);
        (await db.InventoryRelationships.ToListAsync()).ShouldAllBe(x => x.Origin == RelationshipOrigin.Observed);
        await CollectAsync(connection, sections);
        (await db.InventoryObjects.AsNoTracking().SingleAsync(x => x.Id == subject)).Presence.ShouldBe(InventoryPresence.Observed);
        (await db.InventoryRelationships.AsNoTracking().SingleAsync(x => x.RelationType == "appRoleAssignmentPrincipal")).IsCurrent.ShouldBeTrue();
        (await qualifications.ReadAsync(fixture.Project.Id, subject)).Current!.Revision.ShouldBe(2);

        var other = await fixture.OtherWorkspaceAsync();
        var outsider = new SsoQualificationService(other.Factory, other.Access, other.Scope);
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.ReadAsync(other.Project.Id, subject));
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.HistoryAsync(other.Project.Id, subject, new()));
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.RecordAsync(other.Project.Id, subject, second));
        await using (var outside = await other.Factory.CreateDbContextAsync())
        {
            (await outside.Assessments.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
            (await outside.Assessments.IgnoreQueryFilters().Where(x => x.Id == recorded.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Reason, "Forbidden"))).ShouldBe(0);
            outside.Assessments.Add(new Assessment
            {
                WorkspaceId = fixture.Workspace.Id,
                ProjectId = fixture.Project.Id,
                EnvironmentId = environment.Id,
                ObjectId = subject,
                Revision = 3,
                Reason = "Forbidden",
                MetadataFingerprint = second.MetadataFingerprint
            });
            await Should.ThrowAsync<DbUpdateException>(() => outside.SaveChangesAsync());
        }
        await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        (await qualifications.HistoryAsync(fixture.Project.Id, subject, new())).TotalCount.ShouldBe(2);
        await Should.ThrowAsync<AccessDeniedException>(() => qualifications.RecordAsync(fixture.Project.Id, subject, second));
        await db.Members.ExecuteDeleteAsync();
        await Should.ThrowAsync<AccessDeniedException>(() => qualifications.ReadAsync(fixture.Project.Id, subject));
        var adminScope = new WorkspaceScope(fixture.Identity, [], true);
        var adminFactory = new TestContextFactory(fixture.ConnectionString, adminScope);
        await Should.ThrowAsync<AccessDeniedException>(() => new SsoQualificationService(adminFactory,
            new StoredProjectAccess(adminFactory, adminScope), adminScope).ReadAsync(fixture.Project.Id, subject));
        http.Calls.ShouldBe(20);

        async Task CollectAsync(Guid source, string[] selection)
        {
            await service.LaunchAsync(fixture.Project.Id, environment.Id, source, Guid.NewGuid(), Scope, selection);
            await processor.ProcessOneAsync(default);
        }
    }

    private static HttpResponseMessage SsoParents(params string[] ids) =>
        Response(JsonSerializer.Serialize(new { value = ids.Select(id => new { id }) }));

    private static string SsoAssignment(string id, string? type) => JsonSerializer.Serialize(new
    {
        id,
        principalId = SsoPrincipal,
        principalType = type,
        resourceId = SsoResource,
        appRoleId = Guid.Empty.ToString(),
        createdDateTime = "2026-01-01T00:00:00Z"
    });

    private static HttpResponseMessage SsoMetadata(string mode, bool next = false) => Response($$"""
        {"value":[{"id":"{{SsoResource}}","appId":"{{SsoAppId}}","displayName":"Synthetic portal","preferredSingleSignOnMode":"{{mode}}"}]
        {{(next ? ",\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/servicePrincipals?$skiptoken=synthetic\"" : "")}}}
        """);

    private static async Task<GraphSectionData> SsoAsync(string capability, params Func<HttpRequestMessage, HttpResponseMessage>[] pages)
    {
        using var http = new SyntheticGraph(pages);
        var data = await new ApplicationsSsoCollector(Catalog).CollectAsync(http.Transport, capability, Scope, TestContext.Current.CancellationToken);
        http.Calls.ShouldBe(pages.Length);
        data.Result.Warnings.ShouldContain(x => x.Code == "SsoMetadataOnly");
        await ValidateAsync(data, ApplicationsSsoCollector.ProducerId);
        return data;
    }
}
