using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record TeamsQualificationInput(Guid OperationId, long ExpectedRevision, string MetadataFingerprint,
    QualificationScopeValue Value, string Reason, string EvidenceReference, Guid? EvidenceObservationId);

public sealed class TeamsQualificationService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope)
{
    private AssessmentService Store(AssessmentKind kind) => kind is AssessmentKind.TeamsChatScope or AssessmentKind.TeamsTelephonyScope
        ? new(factory, access, scope, new(kind, DecisionReview.TeamsTypes, DecisionReview.TeamsFields, "TeamsQualification.Recorded"))
        : throw new ArgumentException("Invalid Teams qualification kind.", nameof(kind));

    public Task<QualificationState> ReadAsync(Guid projectId, Guid objectId, AssessmentKind kind, CancellationToken token = default) =>
        Store(kind).ReadAsync(projectId, objectId, token);

    public Task<PageResult<Assessment>> HistoryAsync(Guid projectId, Guid objectId, AssessmentKind kind, PageRequest page, CancellationToken token = default) =>
        Store(kind).HistoryAsync(projectId, objectId, page, token);

    public Task RecordAsync(Guid projectId, Guid objectId, AssessmentKind kind, TeamsQualificationInput input, CancellationToken token = default) =>
        Store(kind).RecordAsync(projectId, objectId, new(input.OperationId, input.ExpectedRevision, input.MetadataFingerprint,
            SsoDependencyValue.Unknown, SsoProtocol.Unknown, input.Reason, input.EvidenceReference, input.EvidenceObservationId, input.Value), token);
}
