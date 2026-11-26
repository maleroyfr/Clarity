using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record SectionAccess(string CapabilityId, bool Available, string Code, IReadOnlyList<string> Permissions);

public sealed class GraphDiscoveryService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access,
    IWorkspaceContext scope, DiscoveryCatalog catalog, IGraphSessionFactory sessions)
{
    public async Task<Guid> ConnectAsync(Guid projectId, Guid environmentId, string name, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.ManageConnections, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, cancellationToken);
        if (environment.Kind != EnvironmentKind.M365Tenant || !Guid.TryParse(environment.ExternalId, out var tenant))
        {
            throw new ArgumentException("A declared Microsoft 365 tenant GUID is required.");
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PublicationService.LockEnvironmentAsync(db, project.WorkspaceId, tenant, cancellationToken);
        var connection = await db.Connections.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.TenantId == tenant && x.ProducerId == "clarity.graph.entra", cancellationToken);
        if (connection is not null) { return connection.Id; }
        connection = new Connection
        {
            WorkspaceId = project.WorkspaceId,
            TenantId = tenant,
            Name = name,
            ProducerId = "clarity.graph.entra",
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
        catalog.RequireProducer("clarity.graph.entra");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, cancellationToken);
        var connection = await ConnectionAsync(db, project, environment, connectionId, cancellationToken);
        if (requestedScope.Kind != "tenant" || requestedScope.Value != connection.TenantId.ToString()
            || sections.Length == 0 || sections.Distinct(StringComparer.Ordinal).Count() != sections.Length
            || sections.Any(x => !catalog.Sections.TryGetValue(x, out var section) || !section.ProducerIds.Contains(connection.ProducerId)))
        {
            throw new ArgumentException("Invalid Graph tenant scope or sections.");
        }
        var payload = JsonSerializer.Serialize(new GraphJobPayload(environmentId, connectionId, requestedScope, sections), ContractJson.Options);
        await new JobService(factory, access, scope).EnqueueAsync(projectId, operationId, CollectionJobKind.Graph, payload,
            DiscoveryScope.Hash(new CollectionScope("tenant", connection.TenantId + ":Microsoft.Graph")),
            System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"), cancellationToken);
        return operationId;
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
        foreach (var section in catalog.Sections.Values.Where(x => x.ProducerIds.Contains(connection.ProducerId)))
        {
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
            || connection.TenantId != tenant || connection.ProducerId != "clarity.graph.entra")
        {
            throw new ContractException("EnvironmentMismatch", "Graph");
        }
        return connection;
    }
}
