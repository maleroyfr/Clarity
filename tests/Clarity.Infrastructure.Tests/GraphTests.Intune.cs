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
    private const string Registration = "00000000-0000-0000-0000-000000000abc";
    private const string Managed = "00000000-0000-0000-0000-000000000def";

    [Theory]
    [InlineData("intune.managedDevices", "deviceManagement/managedDevices", "managedDevice", "complianceState", "noncompliant")]
    [InlineData("intune.autopilotIdentities", "deviceManagement/windowsAutopilotDeviceIdentities", "windowsAutopilotDeviceIdentity", "enrollmentState", "enrolled")]
    [InlineData("intune.apps", "deviceAppManagement/mobileApps", "mobileApp", "publisher", "Synthetic publisher")]
    [InlineData("intune.profiles", "deviceManagement/deviceConfigurations", "deviceConfiguration", "displayName", "Synthetic profile")]
    [InlineData("intune.policies", "deviceManagement/deviceCompliancePolicies", "deviceCompliancePolicy", "displayName", "Synthetic policy")]
    public async Task IntuneReadsOnlyDocumentedV1MetadataAndLinksExposedIdentifiers(string capability, string path, string type, string field, string text)
    {
        var data = await IntuneAsync(capability, request =>
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/v1.0/" + path);
            request.RequestUri.Query.ShouldContain(field);
            request.RequestUri.Query.ShouldNotContain("productKey");
            return Response(JsonSerializer.Serialize(new
            {
                value = new[] { new Dictionary<string, object?>
                {
                    ["id"] = Managed, [field] = text, ["azureADDeviceId"] = Registration.ToUpperInvariant(),
                    ["managedDeviceId"] = Managed, ["azureActiveDirectoryDeviceId"] = Guid.Empty.ToString(), ["serialNumber"] = null,
                    ["activationLockBypassCode"] = "never-retain", ["productKey"] = "never-retain", ["userId"] = "not-a-primary-user"
                } }
            }));
        });
        data.Result.Status.ShouldBe(CoverageStatus.Complete);
        var obj = data.Objects.ShouldHaveSingleItem();
        obj.Type.ShouldBe(type);
        obj.Namespace.ShouldBe("microsoft.intune");
        obj.Attributes[field].GetString().ShouldBe(text);
        JsonSerializer.Serialize(data).ShouldNotContain("never-retain");
        if (capability == "intune.managedDevices")
        {
            obj.Attributes["serialNumber"].ValueKind.ShouldBe(JsonValueKind.Null);
            obj.Attributes.ContainsKey("managementAgent").ShouldBeFalse();
            data.Relationships.ShouldHaveSingleItem().To.ShouldBe(new("device", "deviceId", Registration));
        }
        else if (capability == "intune.autopilotIdentities")
        {
            data.Relationships.ShouldHaveSingleItem().To.ShouldBe(new("managedDevice", "microsoft.intune", Managed));
        }
        else { data.Relationships.ShouldBeEmpty(); }
    }

    [Theory]
    [InlineData("intune.primaryUsers", "deviceManagement/managedDevices", "users", "intunePrimaryUserOf", "managedDevice")]
    [InlineData("intune.registeredOwners", "devices", "registeredOwners", "entraRegisteredOwnerOf", "device")]
    public async Task IntunePrimaryUsersAndRegisteredOwnersAreDistinctPagedObservations(string capability, string path, string navigation,
        string relation, string deviceType)
    {
        var data = await IntuneAsync(capability,
            r => { r.RequestUri!.PathAndQuery.ShouldBe($"/v1.0/{path}?$select=id"); return Response("""{"value":[{"id":"device","userId":"unrelated"}]}"""); },
            r =>
            {
                r.RequestUri!.PathAndQuery.ShouldBe($"/v1.0/{path}/device/{navigation}?$select=id");
                return Response($$"""{"value":[],"@odata.nextLink":"https://graph.microsoft.com/v1.0/{{path}}/device/{{navigation}}?$skiptoken=test"}""");
            },
            _ => Response("""{"value":[{"id":"u","@odata.type":"#microsoft.graph.user"},{"id":"u","@odata.type":"#microsoft.graph.user"}]}"""));
        data.Result.Status.ShouldBe(CoverageStatus.Complete);
        data.Objects.ShouldBeEmpty();
        var edge = data.Relationships.ShouldHaveSingleItem();
        edge.From.ShouldBe(new("user", "microsoft.graph", "u"));
        edge.RelationType.ShouldBe(relation);
        edge.To.Type.ShouldBe(deviceType);
    }

    [Theory]
    [InlineData("intune.appAssignments", "deviceAppManagement/mobileApps", "mobileApp")]
    [InlineData("intune.profileAssignments", "deviceManagement/deviceConfigurations", "deviceConfiguration")]
    [InlineData("intune.policyAssignments", "deviceManagement/deviceCompliancePolicies", "deviceCompliancePolicy")]
    public async Task IntuneAssignmentsRetainTheirParentIntentAndExclusionWithoutInventingEffectiveRecipients(string capability,
        string path, string parentType)
    {
        var data = await IntuneAsync(capability, r =>
        {
            r.RequestUri!.AbsolutePath.ShouldBe("/v1.0/" + path);
            return Response("""{"value":[{"id":"parent"}]}""");
        }, r =>
        {
            r.RequestUri!.AbsolutePath.ShouldBe($"/v1.0/{path}/parent/assignments");
            return Response($$$"""
                {"value":[{"id":"assignment1","intent":"required","target":{"@odata.type":"#microsoft.graph.exclusionGroupAssignmentTarget","groupId":"{{{Registration}}}"}},
                          {"id":"assignment2","target":{"@odata.type":"#microsoft.graph.allDevicesAssignmentTarget"}}]}
                """);
        });
        data.Result.ObjectCount.ShouldBe(2);
        data.Result.RelationshipCount.ShouldBe(3);
        var exclusion = data.Objects.Single(x => x.ExternalId == "parent:assignment1");
        exclusion.Attributes["targetType"].GetString().ShouldBe("#microsoft.graph.exclusionGroupAssignmentTarget");
        exclusion.Attributes.ContainsKey("intent").ShouldBe(capability == "intune.appAssignments");
        data.Relationships.Where(x => x.RelationType == "assignmentOf").ShouldAllBe(x => x.To.Type == parentType);
        data.Relationships.Single(x => x.RelationType == "targetsGroup").To.ShouldBe(new("group", "microsoft.graph", Registration));
    }

    [Fact]
    public async Task IntuneChildFailurePreservesPriorEvidenceAndNeverRetriesThroughBeta()
    {
        var data = await IntuneAsync("intune.primaryUsers", _ => Response("""{"value":[{"id":"d1"},{"id":"d2"}]}"""),
            _ => Response("""{"value":[{"id":"u"}]}"""), _ => Response("not-retained", HttpStatusCode.Forbidden));
        data.Result.Status.ShouldBe(CoverageStatus.Partial);
        data.Result.RelationshipCount.ShouldBe(1);
        data.Result.Errors.ShouldHaveSingleItem().Code.ShouldBe("MissingPermission");
        var denied = await IntuneAsync("intune.apps", _ => Response("not-retained", HttpStatusCode.Forbidden));
        denied.Result.Status.ShouldBe(CoverageStatus.Failed);
        denied.Result.ObjectCount.ShouldBeNull();
        var beta = await IntuneAsync("intune.apps", _ => Response("""{"value":[],"@odata.nextLink":"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps"}"""));
        beta.Result.Errors.ShouldHaveSingleItem().Code.ShouldBe("UnsafeGraphLink");
        var empty = await IntuneAsync("intune.apps", _ => Response("""{"value":[]}"""));
        empty.Result.ObjectCount.ShouldBe(0);
        empty.Result.Status.ShouldBe(CoverageStatus.Complete);
        var noGroup = await IntuneAsync("intune.policyAssignments", _ => Response("""{"value":[{"id":"policy"}]}"""),
            _ => Response("""{"value":[{"id":"assignment","target":{"@odata.type":"#microsoft.graph.groupAssignmentTarget"}}]}"""));
        noGroup.Result.Status.ShouldBe(CoverageStatus.Partial);
        noGroup.Result.Errors.ShouldHaveSingleItem().Code.ShouldBe("IntuneTargetUnavailable");
        noGroup.Relationships.ShouldHaveSingleItem().RelationType.ShouldBe("assignmentOf");
    }

    private static async Task<GraphSectionData> IntuneAsync(string capability, params Func<HttpRequestMessage, HttpResponseMessage>[] pages)
    {
        using var http = new SyntheticGraph(pages);
        var result = await new IntuneCollector(Catalog).CollectAsync(http.Transport, capability, Scope, TestContext.Current.CancellationToken);
        http.Calls.ShouldBe(pages.Length);
        result.Result.SectionId.ShouldBe($"{capability}:{Tenant}");
        result.Result.Warnings.ShouldContain(x => x.Code == "IntuneVisibleScope");
        await ValidateAsync(result, IntuneCollector.ProducerId);
        return result;
    }

    [SqlFact]
    public async Task IntunePrerequisiteChecksDistinguishReadableParentsFromDeniedOrUntestedChildren()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var responses = new List<Func<HttpRequestMessage, HttpResponseMessage>> { _ => Organization() };
        foreach (var section in Catalog.Sections.Values.Where(s => s.ProducerIds.Contains(IntuneCollector.ProducerId)))
        {
            responses.Add(_ => Response(section.Id == "intune.primaryUsers" ? """{"value":[{"id":"managed"}]}""" : """{"value":[]}"""));
            if (section.Id == "intune.primaryUsers")
            {
                responses.Add(r =>
                {
                    r.RequestUri!.AbsolutePath.ShouldBe("/v1.0/deviceManagement/managedDevices/managed/users");
                    return Response("not-retained", HttpStatusCode.Forbidden);
                });
            }
        }
        using var http = new SyntheticGraph(responses.ToArray());
        var sessions = new SyntheticSessions(http.Transport);
        var service = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, sessions);
        var connection = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Management", producerId: IntuneCollector.ProducerId);
        var other = await fixture.OtherWorkspaceAsync();
        var outsider = new GraphDiscoveryService(other.Factory, other.Access, other.Scope, Catalog, sessions);
        await Should.ThrowAsync<AccessDeniedException>(() => outsider.TestAsync(other.Project.Id, environment.Id, connection));
        http.Calls.ShouldBe(0);
        var checks = (await service.TestAsync(fixture.Project.Id, environment.Id, connection)).ToDictionary(x => x.CapabilityId);
        checks["intune.primaryUsers"].Available.ShouldBeFalse();
        checks["intune.primaryUsers"].Code.ShouldBe("MissingPermission");
        checks["intune.managedDevices"].Available.ShouldBeTrue();
        checks["intune.profileAssignments"].Code.ShouldBe("IntuneChildNotTested");
        checks["intune.profileAssignments"].Available.ShouldBeFalse();
        checks["intune.profileAssignments"].Permissions.ShouldBe(["DeviceManagementConfiguration.Read.All"]);
    }

    [SqlFact]
    public async Task IntunePublicationResolvesDeviceIdentifiersButDoesNotTurnObservedUsersOrOwnersIntoConfirmedAssociations()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var environment = await AddTenantAsync(fixture);
        var template = DiscoveryFixture.Package();
        var ad = template with
        {
            Objects = [template.Objects[0] with { SectionId = "ad.computers", Type = "computer", ExternalId = Registration }],
            Results = [template.Results[0] with { SectionId = "ad.computers", CapabilityId = "ad.computers" }]
        };
        var adRun = await fixture.StageAsync(ad);
        await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, adRun.Id, ad, "ad.computers");
        using var http = new SyntheticGraph(_ => Organization(),
            _ => Response($$"""{"value":[{"id":"{{Managed}}","azureADDeviceId":"{{Registration}}","complianceState":"compliant"}]}"""),
            _ => Response($$"""{"value":[{"id":"{{Managed}}"}]}"""), _ => Response("""{"value":[{"id":"primary"}]}"""),
            _ => Response("""{"value":[{"id":"directory-object"}]}"""), _ => Response("""{"value":[{"id":"owner","@odata.type":"#microsoft.graph.user"}]}"""),
            _ => Organization(), _ => Response($$"""{"value":[{"id":"directory-object","deviceId":"{{Registration.ToUpperInvariant()}}","trustType":"ServerAd"}]}"""),
            _ => Response("""{"value":[{"id":"primary"},{"id":"owner"}],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/users/delta?$deltatoken=test"}"""));
        var sessions = new SyntheticSessions(http.Transport);
        var service = new GraphDiscoveryService(fixture.Factory, fixture.Access, fixture.Scope, Catalog, sessions);
        var tenant = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Directory");
        var intune = await service.ConnectAsync(fixture.Project.Id, environment.Id, "Management", producerId: IntuneCollector.ProducerId);
        tenant.ShouldNotBe(intune);
        await Should.ThrowAsync<ArgumentException>(() => service.LaunchAsync(fixture.Project.Id, environment.Id, tenant, Guid.NewGuid(), Scope, ["intune.apps"]));
        string[] sections = ["intune.managedDevices", "intune.primaryUsers", "intune.registeredOwners"];
        var id = Guid.NewGuid();
        await service.LaunchAsync(fixture.Project.Id, environment.Id, intune, id, Scope, sections);
        await service.LaunchAsync(fixture.Project.Id, environment.Id, intune, id, Scope, sections);
        var processor = new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, TimeProvider.System),
            [Handler(sessions, new MemoryArtifactStore())], NullLogger<CollectionJobProcessor>.Instance);
        await processor.ProcessOneAsync(default);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        var run = await db.CollectionRuns.SingleAsync(x => x.ExternalRunId == id);
        run.ProducerId.ShouldBe(IntuneCollector.ProducerId);
        run.ConnectionId.ShouldBe(intune);
        run.Channel.ShouldBe(CollectionChannel.Graph);
        (await db.InventoryRelationships.AsNoTracking().SingleAsync(x => x.RelationType == "referencesEntraDevice")).ToMethod.ShouldBe(EndpointResolution.Pending);
        await service.LaunchAsync(fixture.Project.Id, environment.Id, tenant, Guid.NewGuid(), Scope, ["entra.devices", "entra.users"]);
        await processor.ProcessOneAsync(default);
        var links = await db.InventoryRelationships.AsNoTracking().ToDictionaryAsync(x => x.RelationType);
        var device = links["referencesEntraDevice"];
        device.ToMethod.ShouldBe(EndpointResolution.Alternate);
        links["intunePrimaryUserOf"].ToObjectId.ShouldBe(device.FromObjectId);
        links["entraRegisteredOwnerOf"].ToObjectId.ShouldBe(device.ToObjectId);
        links["intunePrimaryUserOf"].FromObjectId.ShouldNotBe(links["entraRegisteredOwnerOf"].FromObjectId);
        links.Values.ShouldAllBe(x => x.Origin == RelationshipOrigin.Observed && x.FromObjectId != null && x.ToObjectId != null);
        var decision = await db.MappingDecisions.SingleAsync();
        decision.Rule.ShouldBe(AssociationRule.DeviceIdentifier);
        decision.State.ShouldBe(MappingState.Validated);
        decision.EntraObjectId.ShouldBe(device.ToObjectId!.Value);
    }
}
