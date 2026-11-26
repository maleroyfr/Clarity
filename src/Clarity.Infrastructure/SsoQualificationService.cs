using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record SsoQualificationInput(Guid OperationId, long ExpectedRevision, string MetadataFingerprint, SsoDependencyValue Value,
    SsoProtocol Protocol, string Reason, string EvidenceReference, Guid? EvidenceObservationId);
public sealed record SsoQualificationState(Assessment? Current, string MetadataFingerprint, bool NeedsReview);

public sealed class SsoQualificationService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope)
{
    public async Task<SsoQualificationState> ReadAsync(Guid projectId, Guid objectId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var item = await SubjectAsync(db, project, objectId, token);
        var current = await Rows(db, project, objectId).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(token);
        var fingerprint = await FingerprintAsync(db, item, token);
        return new(current, fingerprint, current != null && current.MetadataFingerprint != fingerprint);
    }

    public async Task<PageResult<Assessment>> HistoryAsync(Guid projectId, Guid objectId, PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        await SubjectAsync(db, project, objectId, token);
        var rows = Rows(db, project, objectId);
        return new(await rows.OrderByDescending(x => x.Revision).Skip(page.Start).Take(page.Count).ToListAsync(token), await rows.CountAsync(token));
    }

    public async Task RecordAsync(Guid projectId, Guid objectId, SsoQualificationInput input, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        InventoryMatching.ValidateReason(input.Reason);
        if (input.OperationId == Guid.Empty || input.ExpectedRevision < 0 || !Enum.IsDefined(input.Value) || !Enum.IsDefined(input.Protocol)
            || input.EvidenceReference is null || input.EvidenceReference.Length > 1000 || input.MetadataFingerprint is not { Length: 64 })
        { throw new ArgumentException("Invalid SSO qualification."); }
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var item = await SubjectAsync(db, project, objectId, token);
        var replay = await db.Assessments.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == input.OperationId, token);
        if (replay != null)
        {
            if (replay.ProjectId != projectId || replay.ObjectId != objectId || replay.Revision != input.ExpectedRevision + 1
                || replay.Value != input.Value || replay.Protocol != input.Protocol || replay.Reason != input.Reason.Trim()
                || replay.EvidenceReference != input.EvidenceReference.Trim() || replay.EvidenceObservationId != input.EvidenceObservationId
                || replay.MetadataFingerprint != input.MetadataFingerprint || replay.AuthorTenantId != scope.Identity.TenantId
                || replay.AuthorObjectId != scope.Identity.ObjectId)
            { throw new ConflictException(); }
            return;
        }
        var revision = await Rows(db, project, objectId).Select(x => (long?)x.Revision).MaxAsync(token) ?? 0;
        if (revision != input.ExpectedRevision || input.MetadataFingerprint != await FingerprintAsync(db, item, token))
        { throw new ConflictException(); }
        if (input.EvidenceObservationId.HasValue && !await (from o in db.ObjectObservations
                                                            join s in db.SectionRuns on new { o.WorkspaceId, Id = o.SectionRunId } equals new { s.WorkspaceId, s.Id }
                                                            join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                                                            where o.WorkspaceId == project.WorkspaceId && o.ObjectId == objectId && o.Id == input.EvidenceObservationId
                                                                && r.ProjectId == projectId && r.EnvironmentId == item.EnvironmentId && s.PublishedAtUtc != null
                                                            select o.Id).AnyAsync(token))
        { throw new AccessDeniedException(); }
        db.Assessments.Add(new Assessment
        {
            Id = input.OperationId,
            WorkspaceId = project.WorkspaceId,
            ProjectId = projectId,
            EnvironmentId = item.EnvironmentId,
            ObjectId = objectId,
            Kind = AssessmentKind.SsoDependency,
            Revision = revision + 1,
            Value = input.Value,
            Protocol = input.Protocol,
            Reason = input.Reason.Trim(),
            EvidenceReference = input.EvidenceReference.Trim(),
            EvidenceObservationId = input.EvidenceObservationId,
            MetadataFingerprint = input.MetadataFingerprint,
            AuthorTenantId = scope.Identity.TenantId,
            AuthorObjectId = scope.Identity.ObjectId
        });
        db.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = project.WorkspaceId,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            Action = "SsoQualification.Recorded",
            ResourceId = input.OperationId,
            CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
        });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private static IQueryable<Assessment> Rows(ClarityDbContext db, Project project, Guid objectId) =>
        db.Assessments.AsNoTracking().Where(x => x.WorkspaceId == project.WorkspaceId && x.ProjectId == project.Id
            && x.ObjectId == objectId && x.Kind == AssessmentKind.SsoDependency);

    private static async Task<InventoryObject> SubjectAsync(ClarityDbContext db, Project project, Guid objectId, CancellationToken token) =>
        await (from i in db.InventoryObjects.AsNoTracking()
               join e in db.Environments on new { i.WorkspaceId, Id = i.EnvironmentId } equals new { e.WorkspaceId, e.Id }
               where i.WorkspaceId == project.WorkspaceId && e.ProjectId == project.Id && i.Id == objectId
                   && e.Kind == EnvironmentKind.M365Tenant && (i.Type == "application" || i.Type == "servicePrincipal")
                   && db.ExternalKeys.Any(k => k.WorkspaceId == i.WorkspaceId && k.ObjectId == i.Id && k.IsPrimary && k.Namespace == "microsoft.graph")
               select i).SingleOrDefaultAsync(token) ?? throw new AccessDeniedException();

    private static async Task<string> FingerprintAsync(ClarityDbContext db, InventoryObject item, CancellationToken token)
    {
        string[] critical = ["appId", "signInAudience", "servicePrincipalType", "accountEnabled", "appRoleAssignmentRequired", "preferredSingleSignOnMode"];
        var fields = await db.CurrentValues.Where(x => x.WorkspaceId == item.WorkspaceId && x.ObjectId == item.Id && critical.Contains(x.Field))
            .OrderBy(x => x.Field).Select(x => new { x.Field, x.ValueJson, x.Conflicting }).ToListAsync(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { item.Presence, fields }))));
    }
}
