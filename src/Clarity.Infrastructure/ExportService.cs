using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record ExportRequest(Guid OperationId, ExportFilter Filter);
public sealed record ExportJobPayload(Guid ExportId);
public sealed record ExportArtifactView(Guid Id, ExportArtifactKind Kind, string FileName, long Bytes, int RowCount, string ContentHash);
public sealed record ExportRow(Guid Id, ExportFilter Filter, string? Environment, string? Population, CollectionRunState State, int? Progress,
    string? ErrorCode, Guid AuthorTenantId, Guid AuthorObjectId, DateTimeOffset AsOfUtc, DateTimeOffset? GeneratedAtUtc, int? ObjectCount,
    int? ExceptionCount, int? MappingCount, int? CoverageGaps, string SelectionHash, IReadOnlyList<ExportArtifactView> Artifacts,
    DateTimeOffset? FilesPurgedAtUtc);

/// <summary>
/// Launches, lists and downloads consultant exports. A launch captures the dated selection and queues the generation job in one transaction;
/// every launch and download is authorized against the project and audited.
/// </summary>
public sealed class ExportService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope, IExportStore store,
    DiscoveryCatalog catalog)
{
    public async Task<Guid> LaunchAsync(Guid projectId, ExportRequest request, CancellationToken token = default)
    {
        if (request.OperationId == Guid.Empty) { throw new ArgumentException("An export needs an operation identifier."); }
        var filter = request.Filter.Normalize();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Export, token);
        if (!store.IsConfigured) { throw new DiscoveryConfigurationException(); }
        var ws = project.WorkspaceId;
        var filtersJson = JsonSerializer.Serialize(filter, ContractJson.Options);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await PublicationService.LockEnvironmentAsync(db, ws, request.OperationId, token);
        var existing = await db.ExportJobs.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == ws && x.Id == request.OperationId, token);
        if (existing is not null)
        {
            if (existing.ProjectId != project.Id || existing.FiltersJson != filtersJson) { throw new ConflictException(); }
            return existing.Id;
        }
        if (await db.CollectionJobs.AnyAsync(x => x.WorkspaceId == ws && x.Id == request.OperationId, token)) { throw new ConflictException(); }
        if (filter.EnvironmentId is { } environment && !await db.Environments.AnyAsync(e => e.WorkspaceId == ws && e.ProjectId == project.Id
            && e.Id == environment && e.Role == filter.Side, token))
        {
            throw new AccessDeniedException();
        }
        if (filter.PopulationId is { } population && !await (from p in db.Populations
                                                             join e in db.Environments on new { p.WorkspaceId, Id = p.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                                                             where p.WorkspaceId == ws && p.ProjectId == project.Id && p.Id == population && e.Role == filter.Side
                                                             select p.Id).AnyAsync(token))
        {
            throw new AccessDeniedException();
        }
        var selection = await ExportSelection.CaptureAsync(db, project, filter, catalog, token);
        var selectionJson = selection.Serialize();
        var correlation = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        db.CollectionJobs.Add(new CollectionJob
        {
            Id = request.OperationId,
            WorkspaceId = ws,
            ProjectId = project.Id,
            Kind = CollectionJobKind.Export,
            PayloadJson = JsonSerializer.Serialize(new ExportJobPayload(request.OperationId), ContractJson.Options),
            ConcurrencyKey = DiscoveryScope.Hash(new CollectionScope("export", $"{ws:N}:{request.OperationId:N}")),
            CorrelationId = correlation,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId
        });
        db.ExportJobs.Add(new ExportJob
        {
            Id = request.OperationId,
            WorkspaceId = ws,
            ProjectId = project.Id,
            Side = filter.Side,
            FiltersJson = filtersJson,
            SelectionJson = selectionJson,
            SelectionHash = ExportSelection.Hash(selectionJson),
            AsOfUtc = DateTimeOffset.UtcNow,
            AuthorTenantId = scope.Identity.TenantId,
            AuthorObjectId = scope.Identity.ObjectId
        });
        db.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = ws,
            ResourceId = request.OperationId,
            Action = "ExportRequested",
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            CorrelationId = correlation
        });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return request.OperationId;
    }

    public async Task<PageResult<ExportRow>> PageAsync(Guid projectId, EnvironmentRole side, PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        if (!Enum.IsDefined(side)) { throw new ArgumentOutOfRangeException(nameof(side)); }
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var ws = project.WorkspaceId;
        var query = from x in db.ExportJobs.AsNoTracking()
                    join j in db.CollectionJobs on new { x.WorkspaceId, x.Id } equals new { j.WorkspaceId, j.Id }
                    where x.WorkspaceId == ws && x.ProjectId == project.Id && x.Side == side
                    select new { Export = x, j.State, j.Progress, j.ErrorCode };
        var count = await query.CountAsync(token);
        var ordered = page.Descending || string.IsNullOrEmpty(page.Sort)
            ? query.OrderByDescending(x => x.Export.AsOfUtc).ThenBy(x => x.Export.Id)
            : query.OrderBy(x => x.Export.AsOfUtc).ThenBy(x => x.Export.Id);
        var items = await ordered.Skip(page.Start).Take(page.Count).ToListAsync(token);
        var ids = items.Select(x => x.Export.Id).ToList();
        var artifacts = (await db.ExportArtifacts.AsNoTracking().Where(a => a.WorkspaceId == ws && ids.Contains(a.ExportJobId)).ToListAsync(token))
            .ToLookup(a => a.ExportJobId);
        var environments = await db.Environments.AsNoTracking().Where(e => e.WorkspaceId == ws && e.ProjectId == project.Id)
            .ToDictionaryAsync(e => e.Id, e => e.ExternalId, token);
        var populations = await db.Populations.AsNoTracking().Where(p => p.WorkspaceId == ws && p.ProjectId == project.Id)
            .ToDictionaryAsync(p => p.Id, p => p.Name, token);
        var rows = items.Select(x =>
        {
            var filter = JsonSerializer.Deserialize<ExportFilter>(x.Export.FiltersJson, ContractJson.Options)!;
            return new ExportRow(x.Export.Id, filter, filter.EnvironmentId is { } e ? environments.GetValueOrDefault(e) : null,
                filter.PopulationId is { } p ? populations.GetValueOrDefault(p) : null, x.State, x.Progress, x.ErrorCode,
                x.Export.AuthorTenantId, x.Export.AuthorObjectId, x.Export.AsOfUtc, x.Export.GeneratedAtUtc, x.Export.ObjectCount,
                x.Export.ExceptionCount, x.Export.MappingCount, x.Export.CoverageGaps, x.Export.SelectionHash,
                artifacts[x.Export.Id].OrderBy(a => a.Kind).Select(a => new ExportArtifactView(a.Id, a.Kind, a.FileName, a.Bytes, a.RowCount, a.ContentHash))
                    .ToList(), x.Export.FilesPurgedAtUtc);
        }).ToList();
        return new(rows, count);
    }

    public async Task<RawDownload> DownloadAsync(Guid projectId, Guid exportId, Guid artifactId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var ws = project.WorkspaceId;
        var artifact = await (from a in db.ExportArtifacts.AsNoTracking()
                              join x in db.ExportJobs on new { a.WorkspaceId, Id = a.ExportJobId } equals new { x.WorkspaceId, x.Id }
                              where a.WorkspaceId == ws && x.ProjectId == project.Id && x.Id == exportId && a.Id == artifactId
                              select new { a.Id, a.BlobName, a.ContentType, a.FileName, x.FilesPurgedAtUtc }).SingleOrDefaultAsync(token)
            ?? throw new AccessDeniedException();
        if (artifact.FilesPurgedAtUtc is not null) { throw new FileExpiredException("ExportFilesExpired"); }
        db.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = ws,
            ResourceId = artifact.Id,
            Action = "ExportDownloaded",
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
        });
        await db.SaveChangesAsync(token);
        return new RawDownload(await store.OpenAsync(ws, artifact.BlobName, token), artifact.ContentType, artifact.FileName);
    }
}
