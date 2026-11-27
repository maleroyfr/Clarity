using System.ComponentModel.DataAnnotations;
using Clarity.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clarity.Infrastructure;

/// <summary>Retention per file and data category, bound from the "Retention" configuration section and validated at startup.</summary>
public sealed class RetentionOptions
{
    /// <summary>Days an uploaded raw collection file is kept; its run metadata and published observations stay with the project.</summary>
    [Range(1, 3650)] public int RawFileDays { get; set; } = 180;
    /// <summary>Days generated export files stay downloadable; the export record, selection and hashes stay with the project.</summary>
    [Range(1, 3650)] public int ExportFileDays { get; set; } = 30;
    /// <summary>Days audit events are kept.</summary>
    [Range(30, 3650)] public int AuditEventDays { get; set; } = 730;
    /// <summary>Days after archival when a project is shown as due for deletion; deletion itself stays a manager decision.</summary>
    [Range(1, 3650)] public int ArchivedProjectDays { get; set; } = 180;
    /// <summary>Age before a stored file that no metadata row references is removed (interrupted uploads and exports).</summary>
    [Range(1, 720)] public int OrphanGraceHours { get; set; } = 24;
    [Range(1, 168)] public int SweepIntervalHours { get; set; } = 6;

    public bool DeletionDue(Project project, DateTimeOffset now) =>
        project.Status == ProjectStatus.Archived && project.ArchivedAtUtc is { } archived && archived.AddDays(ArchivedProjectDays) <= now;
}

public sealed record RetentionSweep(int Workspaces, int RawFiles, int ExportJobs, int AuditEvents, int OrphanFiles, int DeletionDue, int Failures);

/// <summary>
/// Applies <see cref="RetentionOptions"/> workspace by workspace as the system actor. Files are always deleted before the metadata that records
/// them as purged, and every step only acts on rows still due, so an interrupted sweep is completed by the next one. Projects being deleted are
/// left to their deletion job.
/// </summary>
public sealed class RetentionSweeper(string connectionString, IArtifactStore artifacts, IExportStore exports, RetentionOptions options,
    TimeProvider time, ILogger<RetentionSweeper> logger)
{
    internal const int BatchSize = 500;
    private static readonly IReadOnlySet<string> KeepNothing = new HashSet<string>();

    public async Task<RetentionSweep> RunOnceAsync(CancellationToken cancellationToken)
    {
        var correlation = Guid.NewGuid().ToString("N");
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlation });
        var now = time.GetUtcNow();
        List<Guid> workspaces;
        await using (var db = new JobContextFactory(connectionString, new WorkspaceScope(default, [], IsPlatformAdmin: true)).CreateDbContext())
        {
            workspaces = await db.Workspaces.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(cancellationToken);
        }
        var total = new RetentionSweep(workspaces.Count, 0, 0, 0, 0, 0, 0);
        foreach (var ws in workspaces)
        {
            try
            {
                var sweep = await SweepAsync(ws, now, correlation, cancellationToken);
                total = total with
                {
                    RawFiles = total.RawFiles + sweep.RawFiles,
                    ExportJobs = total.ExportJobs + sweep.ExportJobs,
                    AuditEvents = total.AuditEvents + sweep.AuditEvents,
                    OrphanFiles = total.OrphanFiles + sweep.OrphanFiles,
                    DeletionDue = total.DeletionDue + sweep.DeletionDue
                };
            }
            catch (Exception exception) when (exception is Azure.RequestFailedException or IOException or HttpRequestException or SqlException
                or DbUpdateException)
            {
                logger.LogWarning("Retention sweep of a workspace failed with {ErrorType}; the next sweep resumes it", exception.GetType().Name);
                total = total with { Failures = total.Failures + 1 };
            }
        }
        logger.LogInformation(
            "Retention sweep: {Workspaces} workspaces, {RawFiles} raw files, {ExportJobs} exports, {AuditEvents} audit events, {OrphanFiles} orphan files purged; {DeletionDue} projects due for deletion; {Failures} failures",
            total.Workspaces, total.RawFiles, total.ExportJobs, total.AuditEvents, total.OrphanFiles, total.DeletionDue, total.Failures);
        return total;
    }

    private async Task<RetentionSweep> SweepAsync(Guid ws, DateTimeOffset now, string correlation, CancellationToken ct)
    {
        var factory = new JobContextFactory(connectionString, new WorkspaceScope(default, [ws]));
        var raw = await PurgeRawFilesAsync(factory, ws, now, correlation, ct);
        var exported = await ExpireExportsAsync(factory, ws, now, correlation, ct);
        var audit = await PurgeAuditAsync(factory, ws, now, correlation, ct);
        var orphans = await PurgeOrphansAsync(factory, ws, now, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var archived = await db.Projects.AsNoTracking().Where(x => x.WorkspaceId == ws && x.Status == ProjectStatus.Archived).ToListAsync(ct);
        return new RetentionSweep(1, raw, exported, audit, orphans, archived.Count(x => options.DeletionDue(x, now)), 0);
    }

    private static IQueryable<Guid> Deleting(ClarityDbContext db, Guid ws) =>
        db.Projects.Where(x => x.WorkspaceId == ws && x.Status == ProjectStatus.Deleting).Select(x => x.Id);

    private async Task<int> PurgeRawFilesAsync(JobContextFactory factory, Guid ws, DateTimeOffset now, string correlation, CancellationToken ct)
    {
        var cutoff = now.AddDays(-options.RawFileDays);
        var purged = 0;
        while (true)
        {
            List<RawArtifact> due;
            await using (var db = await factory.CreateDbContextAsync(ct))
            {
                var deleting = Deleting(db, ws);
                // A run still queued or running needs its file; it is purged by a later sweep once its job has ended.
                var busy = db.CollectionJobs.Where(x => x.WorkspaceId == ws
                    && (x.State == CollectionRunState.Pending || x.State == CollectionRunState.Running)).Select(x => x.Id);
                var runs = db.CollectionRuns.Where(x => x.WorkspaceId == ws && !deleting.Contains(x.ProjectId) && !busy.Contains(x.Id))
                    .Select(x => x.Id);
                due = await db.RawArtifacts.AsNoTracking()
                    .Where(x => x.WorkspaceId == ws && x.PurgedAtUtc == null && x.UploadedAtUtc < cutoff && runs.Contains(x.RunId))
                    .OrderBy(x => x.UploadedAtUtc).Take(BatchSize).ToListAsync(ct);
            }
            foreach (var artifact in due)
            {
                await artifacts.DeleteAsync(ws, artifact.BlobName, ct);
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (await db.RawArtifacts.Where(x => x.WorkspaceId == ws && x.Id == artifact.Id && x.PurgedAtUtc == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.PurgedAtUtc, now), ct) == 1)
                {
                    Audit(db, ws, artifact.RunId, "RawFilePurged", now, correlation);
                    await db.SaveChangesAsync(ct);
                    purged++;
                }
                await transaction.CommitAsync(ct);
            }
            if (due.Count < BatchSize) { return purged; }
        }
    }

    private async Task<int> ExpireExportsAsync(JobContextFactory factory, Guid ws, DateTimeOffset now, string correlation, CancellationToken ct)
    {
        var cutoff = now.AddDays(-options.ExportFileDays);
        var expired = 0;
        while (true)
        {
            List<Guid> due;
            await using (var db = await factory.CreateDbContextAsync(ct))
            {
                var deleting = Deleting(db, ws);
                due = await db.ExportJobs.Where(x => x.WorkspaceId == ws && x.FilesPurgedAtUtc == null && x.GeneratedAtUtc != null
                        && x.GeneratedAtUtc < cutoff && !deleting.Contains(x.ProjectId))
                    .OrderBy(x => x.GeneratedAtUtc).Select(x => x.Id).Take(BatchSize).ToListAsync(ct);
            }
            foreach (var id in due)
            {
                await exports.PurgeExportAsync(ws, id, KeepNothing, ct);
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (await db.ExportJobs.Where(x => x.WorkspaceId == ws && x.Id == id && x.FilesPurgedAtUtc == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.FilesPurgedAtUtc, now), ct) == 1)
                {
                    Audit(db, ws, id, "ExportFilesExpired", now, correlation);
                    await db.SaveChangesAsync(ct);
                    expired++;
                }
                await transaction.CommitAsync(ct);
            }
            if (due.Count < BatchSize) { return expired; }
        }
    }

    private async Task<int> PurgeAuditAsync(JobContextFactory factory, Guid ws, DateTimeOffset now, string correlation, CancellationToken ct)
    {
        var cutoff = now.AddDays(-options.AuditEventDays);
        var purged = 0;
        int deleted;
        do
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            deleted = await db.AuditEvents.Where(x => x.WorkspaceId == ws && x.OccurredAtUtc < cutoff).Take(DeletionJobHandler.BatchSize)
                .ExecuteDeleteAsync(ct);
            purged += deleted;
        }
        while (deleted == DeletionJobHandler.BatchSize);
        if (purged > 0)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            Audit(db, ws, ws, "AuditEventsPurged", now, correlation);
            await db.SaveChangesAsync(ct);
        }
        return purged;
    }

    /// <summary>Removes files older than the grace period that no metadata row references: uploads and exports interrupted before recording.</summary>
    private async Task<int> PurgeOrphansAsync(JobContextFactory factory, Guid ws, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddHours(-options.OrphanGraceHours);
        HashSet<string> raw, exported;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            raw = new(await db.RawArtifacts.Where(x => x.WorkspaceId == ws && x.PurgedAtUtc == null).Select(x => x.BlobName).ToListAsync(ct),
                StringComparer.Ordinal);
            var kept = db.ExportJobs.Where(x => x.WorkspaceId == ws && x.FilesPurgedAtUtc == null).Select(x => x.Id);
            exported = new(await db.ExportArtifacts.Where(x => x.WorkspaceId == ws && kept.Contains(x.ExportJobId)).Select(x => x.BlobName)
                .ToListAsync(ct), StringComparer.Ordinal);
        }
        var purged = 0;
        await foreach (var file in artifacts.ListAsync(ws, ct))
        {
            if (file.LastModifiedUtc < cutoff && !raw.Contains(file.BlobName))
            {
                await artifacts.DeleteAsync(ws, file.BlobName, ct);
                purged++;
            }
        }
        await foreach (var file in exports.ListAsync(ws, ct))
        {
            if (file.LastModifiedUtc < cutoff && !exported.Contains(file.BlobName))
            {
                await exports.DeleteAsync(ws, file.BlobName, ct);
                purged++;
            }
        }
        return purged;
    }

    private static void Audit(ClarityDbContext db, Guid ws, Guid resource, string action, DateTimeOffset now, string correlation) =>
        db.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = ws,
            ResourceId = resource,
            Action = action,
            OccurredAtUtc = now,
            CorrelationId = correlation
        });
}

public sealed class RetentionWorker(RetentionSweeper sweeper, RetentionOptions options, ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(options.SweepIntervalHours));
            do
            {
                try
                {
                    await sweeper.RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is SqlException or Azure.RequestFailedException or IOException or HttpRequestException)
                {
                    logger.LogError("Retention sweep failed with {ErrorType}; correlation {CorrelationId}", exception.GetType().Name,
                        Guid.NewGuid().ToString("N"));
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
