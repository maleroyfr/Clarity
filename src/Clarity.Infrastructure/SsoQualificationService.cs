using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record SsoQualificationInput(Guid OperationId, long ExpectedRevision, string MetadataFingerprint, SsoDependencyValue Value,
    SsoProtocol Protocol, string Reason, string EvidenceReference, Guid? EvidenceObservationId);

public sealed class SsoQualificationService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope)
{
    private readonly AssessmentService assessments = new(factory, access, scope, new(AssessmentKind.SsoDependency,
        ["application", "servicePrincipal"],
        ["appId", "signInAudience", "servicePrincipalType", "accountEnabled", "appRoleAssignmentRequired", "preferredSingleSignOnMode"],
        "SsoQualification.Recorded"));

    public Task<QualificationState> ReadAsync(Guid projectId, Guid objectId, CancellationToken token = default) =>
        assessments.ReadAsync(projectId, objectId, token);

    public Task<PageResult<Assessment>> HistoryAsync(Guid projectId, Guid objectId, PageRequest page, CancellationToken token = default) =>
        assessments.HistoryAsync(projectId, objectId, page, token);

    public Task RecordAsync(Guid projectId, Guid objectId, SsoQualificationInput input, CancellationToken token = default) =>
        assessments.RecordAsync(projectId, objectId, new(input.OperationId, input.ExpectedRevision, input.MetadataFingerprint, input.Value,
            input.Protocol, input.Reason, input.EvidenceReference, input.EvidenceObservationId), token);
}
