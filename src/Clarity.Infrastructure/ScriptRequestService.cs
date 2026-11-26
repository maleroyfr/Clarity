using System.IO.Compression;
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
        var producer = catalog.RequireProducer("clarity.ad");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, cancellationToken);
        if (environment.Kind != EnvironmentKind.AdForest || operationId == Guid.Empty || sections.Length == 0
            || sections.Distinct(StringComparer.Ordinal).Count() != sections.Length
            || sections.Any(x => !catalog.Sections.TryGetValue(x, out var section) || !section.ProducerIds.Contains(producer.Id)
                || !section.SupportedScopes.Contains(requestedScope.Kind))
            || requestedScope.Kind == "ou" && string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Invalid AD request scope or sections.");
        }
        var parameters = new ScriptParameters("1.0", operationId, environment.Kind.ToString(), environment.ExternalId,
            requestedScope, sections, producer.Version, requestedScope.Kind == "domain" ? requestedScope.Value : domain);
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

    public async Task<byte[]> DownloadBundleAsync(Guid projectId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await GetAsync(projectId, requestId, "ScriptBundleDownloaded", cancellationToken);
        if (request.ProducerId != "clarity.ad" || request.ScriptVersion != "1.0.0") { throw new ConflictException(); }
        return ScriptBundle.BuildAd();
    }

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

public static class ScriptBundle
{
    public static byte[] BuildAd()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (resource, name) in new[]
            {
                ("Scripts.ad.Collect-AD.ps1", "ad/Collect-AD.ps1"),
                ("Scripts.ad.Clarity.Discovery.AD.psm1", "ad/Clarity.Discovery.AD.psm1"),
                ("Scripts.common.Clarity.Discovery.Common.psd1", "Clarity.Discovery.Common/Clarity.Discovery.Common.psd1"),
                ("Scripts.common.Clarity.Discovery.Common.psm1", "Clarity.Discovery.Common/Clarity.Discovery.Common.psm1")
            })
            {
                var entry = zip.CreateEntry(name);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var source = typeof(ScriptBundle).Assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException("A published script component is missing.");
                using var destination = entry.Open();
                source.CopyTo(destination);
            }
        }
        return output.ToArray();
    }
}
