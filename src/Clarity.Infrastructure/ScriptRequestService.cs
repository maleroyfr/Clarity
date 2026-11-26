using System.Text;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed class ScriptRequestService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access,
    IWorkspaceContext scope, DiscoveryCatalog catalog)
{
    public async Task<Guid> CreateAsync(Guid projectId, Guid environmentId, Guid operationId, CollectionScope requestedScope,
        string[] sections, string? domain, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Collect, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, cancellationToken);
        var producer = ScriptBundle.Registry.Select(catalog, environment.Kind.ToString(), sections);
        // A tenant collection is always bound to the environment's tenant, never to a client-supplied identifier.
        var boundToTenant = requestedScope.Kind == "tenant" && string.Equals(requestedScope.Value, environment.ExternalId, StringComparison.OrdinalIgnoreCase);
        if (boundToTenant) { requestedScope = new CollectionScope("tenant", environment.ExternalId); }
        if (operationId == Guid.Empty || sections.Length == 0
            || sections.Distinct(StringComparer.Ordinal).Count() != sections.Length
            || sections.Any(x => !catalog.Sections[x].SupportedScopes.Contains(requestedScope.Kind))
            || ResourceScriptScope.RequiresRequest(producer.Id) && (!ResourceScriptScope.Allows(environment.Kind, environment.ExternalId, requestedScope)
                || !string.IsNullOrWhiteSpace(domain))
            || requestedScope.Kind == "ou" && string.IsNullOrWhiteSpace(domain)
            || requestedScope.Kind == "tenant" && (!boundToTenant || !string.IsNullOrWhiteSpace(domain)))
        {
            throw new ArgumentException("Invalid script request scope or sections.");
        }
        var parameters = new ScriptParameters("1.0", operationId, environment.Kind.ToString(), environment.ExternalId,
            requestedScope, sections, producer.Version, requestedScope.Kind switch
            {
                "domain" => requestedScope.Value,
                "tenant" or "resource" => null,
                _ => domain
            });

        var json = JsonSerializer.Serialize(parameters, ContractJson.Options);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PublicationService.LockEnvironmentAsync(db, project.WorkspaceId, environmentId, cancellationToken);
        var existing = await db.CollectionRequests.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == operationId, cancellationToken);
        if (existing is not null)
        {
            return existing.ProjectId == project.Id && existing.EnvironmentId == environmentId && existing.ParametersJson == json
                ? existing.Id : throw new ConflictException();
        }
        db.CollectionRequests.Add(new CollectionRequest
        {
            Id = operationId,
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            EnvironmentId = environmentId,
            ScopeJson = DiscoveryScope.Serialize(requestedScope),
            ScopeHash = DiscoveryScope.Hash(requestedScope),
            SectionsJson = JsonSerializer.Serialize(sections, ContractJson.Options),
            ParametersJson = json,
            ProducerId = producer.Id,
            ScriptVersion = producer.Version,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId
        });
        Audit(db, project.WorkspaceId, operationId, "ScriptRequested");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return operationId;
    }

    public async Task<byte[]> DownloadParametersAsync(Guid projectId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await GetAsync(projectId, requestId, "ScriptParametersDownloaded", cancellationToken);
        return Encoding.UTF8.GetBytes(request.ParametersJson);
    }

    public async Task<ScriptBundleFile> DownloadBundleAsync(Guid projectId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await GetAsync(projectId, requestId, "ScriptBundleDownloaded", cancellationToken);
        // A bundle is only served for the producer version that the catalog currently publishes.
        return catalog.Producers.TryGetValue(request.ProducerId, out var producer) && producer.Version == request.ScriptVersion
            ? ScriptBundle.Build(producer.Id, producer.Version) ?? throw new ConflictException() : throw new ConflictException();
    }

    /// <summary>The validated PowerShell producers a user can request for an environment kind.</summary>
    public IReadOnlyList<CatalogProducer> Producers(EnvironmentKind kind) => ScriptBundle.Registry.Producers(catalog, kind.ToString());

    private async Task<CollectionRequest> GetAsync(Guid projectId, Guid id, string action, CancellationToken cancellationToken)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var request = await db.CollectionRequests.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.ProjectId == project.Id && x.Id == id, cancellationToken) ?? throw new AccessDeniedException();
        Audit(db, project.WorkspaceId, id, action);
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    private void Audit(ClarityDbContext db, Guid workspaceId, Guid id, string action) => db.AuditEvents.Add(new AuditEvent
    {
        WorkspaceId = workspaceId,
        ResourceId = id,
        Action = action,
        ActorTenantId = scope.Identity.TenantId,
        ActorObjectId = scope.Identity.ObjectId,
        CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
    });
}
