using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record SectionAccess(string CapabilityId, bool Available, string Code, IReadOnlyList<string> Permissions);

public sealed class GraphDiscoveryService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access,
    IWorkspaceContext scope, DiscoveryCatalog catalog, IGraphSessionFactory sessions)
{
    public const string EntraProducerId = "clarity.graph.entra";

    /// <summary>Graph producers a tenant connection may be created for; each producer has its own connection and consent.</summary>
    public static readonly IReadOnlyList<string> ConnectionProducerIds =
        [EntraProducerId, IntuneCollector.ProducerId, ApplicationsSsoCollector.ProducerId, SharePointCollector.ProducerId, OneDriveCollector.ProducerId, TeamsCollector.ProducerId];

    public async Task<Guid> ConnectAsync(Guid projectId, Guid environmentId, string name, CancellationToken cancellationToken = default,
        string producerId = EntraProducerId)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.ManageConnections, cancellationToken);
        if (!ConnectionProducerIds.Contains(producerId)) { throw new ContractException("ProducerUnavailable", "catalog"); }
        catalog.RequireProducer(producerId);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, cancellationToken);
        if (environment.Kind != EnvironmentKind.M365Tenant || !Guid.TryParse(environment.ExternalId, out var tenant))
        {
            throw new ArgumentException("A declared Microsoft 365 tenant GUID is required.");
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PublicationService.LockEnvironmentAsync(db, project.WorkspaceId, tenant, cancellationToken);
        var connection = await db.Connections.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.TenantId == tenant && x.ProducerId == producerId, cancellationToken);
        if (connection is not null) { return connection.Id; }
        connection = new Connection
        {
            WorkspaceId = project.WorkspaceId,
            TenantId = tenant,
            Name = name,
            ProducerId = producerId,
            ConfigurationReference = "collector"
        };
        db.Connections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return connection.Id;
    }

    public async Task<Guid> LaunchAsync(Guid projectId, Guid environmentId, Guid connectionId, Guid operationId,
        CollectionScope requestedScope, string[] sections, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Collect, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, cancellationToken);
        var connection = await ConnectionAsync(db, project, environment, connectionId, cancellationToken);
        ValidateSelection(catalog, connection, requestedScope, sections);
        var payload = JsonSerializer.Serialize(new GraphJobPayload(environmentId, connectionId, requestedScope, sections), ContractJson.Options);
        await new JobService(factory, access, scope).EnqueueAsync(projectId, operationId, CollectionJobKind.Graph, payload,
            DiscoveryScope.Hash(new CollectionScope("tenant", connection.TenantId + ":Microsoft.Graph")),
            System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"), cancellationToken);
        return operationId;
    }

    /// <summary>
    /// Validates a Graph job selection and returns the single producer that will own its package: the connection's producer,
    /// or the Import producer of exactly one usage report that this connection may download.
    /// </summary>
    internal static string ValidateSelection(DiscoveryCatalog catalog, Connection connection, CollectionScope requestedScope, string[] sections)
    {
        catalog.RequireProducer(connection.ProducerId);
        var report = sections.Length == 1 && catalog.Sections.TryGetValue(sections[0], out var only)
            ? GraphReportDownloads.ReportProducer(only, connection.ProducerId) : null;
        if (requestedScope.Kind != "tenant" || requestedScope.Value != connection.TenantId.ToString()
            || sections.Length == 0 || sections.Distinct(StringComparer.Ordinal).Count() != sections.Length
            || (report is null && sections.Any(x => !catalog.Sections.TryGetValue(x, out var section)
                || !section.ProducerIds.Contains(connection.ProducerId))))
        {
            throw new ArgumentException("Invalid Graph tenant scope or sections.");
        }
        return report is null ? connection.ProducerId : catalog.RequireProducer(report).Id;
    }

    public async Task<IReadOnlyList<SectionAccess>> TestAsync(Guid projectId, Guid environmentId, Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Collect, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, cancellationToken);
        var connection = await ConnectionAsync(db, project, environment, connectionId, cancellationToken);
        using var session = await sessions.CreateAsync(connection, cancellationToken);
        await session.Transport.VerifyTenantAsync(connection.TenantId, cancellationToken);
        var checks = new List<SectionAccess>();
        Func<GraphTransport, CatalogSection, CancellationToken, Task<SectionAccess>>? probe = connection.ProducerId switch
        {
            IntuneCollector.ProducerId => IntuneCollector.ProbeAsync,
            ApplicationsSsoCollector.ProducerId => ApplicationsSsoCollector.ProbeAsync,
            SharePointCollector.ProducerId => SharePointCollector.ProbeAsync,
            OneDriveCollector.ProducerId => OneDriveCollector.ProbeAsync,
            TeamsCollector.ProducerId => TeamsCollector.ProbeAsync,
            _ => null
        };
        foreach (var section in catalog.Sections.Values.Where(x => x.ProducerIds.Contains(connection.ProducerId)
            || GraphReportDownloads.ReportProducer(x, connection.ProducerId) is not null))
        {
            if (probe is not null)
            {
                try { checks.Add(await probe(session.Transport, section, cancellationToken)); }
                catch (Exception error) when (error is GraphReadException or HttpRequestException or JsonException or InvalidOperationException or ContractException)
                {
                    checks.Add(new(section.Id, false, error is GraphReadException read ? read.Code : "GraphDataUnavailable", section.Permissions));
                }
                continue;
            }
            string[] paths = section.Id switch
            {
                "entra.users" => ["/v1.0/users?$top=1&$select=id"],
                "entra.groups" => ["/v1.0/groups?$top=1&$select=id"],
                "entra.devices" => ["/v1.0/devices?$top=1&$select=id"],
                "entra.domains" => ["/v1.0/domains?$select=id"],
                "entra.licenses" => ["/v1.0/subscribedSkus", "/v1.0/users?$top=1&$select=id,assignedLicenses"],
                "entra.applications" => ["/v1.0/applications?$top=1&$select=id", "/v1.0/servicePrincipals?$top=1&$select=id"],
                _ => throw new GraphReadException("SectionUnavailable")
            };
            try
            {
                var code = "Available";
                foreach (var path in paths)
                {
                    using var result = await session.Transport.GetAsync(path, cancellationToken);
                    if (section.Id == "entra.groups")
                    {
                        var values = result.RootElement.GetProperty("value");
                        if (values.GetArrayLength() == 0) { code = "MembershipNotTestedEmptyScope"; }
                        else
                        {
                            var id = Uri.EscapeDataString(values[0].GetProperty("id").GetString()!);
                            using var members = await session.Transport.GetAsync($"/v1.0/groups/{id}/members?$top=1&$select=id", cancellationToken);
                            using var owners = await session.Transport.GetAsync($"/v1.0/groups/{id}/owners?$top=1&$select=id", cancellationToken);
                            code = "GraphV1MembershipLimit";
                        }
                    }
                }
                checks.Add(new(section.Id, true, code, section.Permissions));
            }
            catch (GraphReadException exception)
            {
                checks.Add(new(section.Id, false, exception.Code, section.Permissions));
            }
        }
        connection.TestedAtUtc = DateTimeOffset.UtcNow;
        connection.AccessResultsJson = JsonSerializer.Serialize(checks, ContractJson.Options);
        await access.RequireAsync(projectId, WorkspaceOperation.Collect, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return checks;
    }

    internal static async Task<Connection> ConnectionAsync(ClarityDbContext db, Project project, ProjectEnvironment environment,
        Guid connectionId, CancellationToken cancellationToken)
    {
        var connection = await db.Connections.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == connectionId,
            cancellationToken) ?? throw new AccessDeniedException();
        if (environment.Kind != EnvironmentKind.M365Tenant || !Guid.TryParse(environment.ExternalId, out var tenant)
            || connection.TenantId != tenant || !ConnectionProducerIds.Contains(connection.ProducerId))
        {
            throw new ContractException("EnvironmentMismatch", "Graph");
        }
        return connection;
    }
}
