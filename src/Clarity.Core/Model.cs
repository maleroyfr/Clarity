namespace Clarity.Core;

public enum WorkspaceStatus { Active, Archived }
public enum WorkspaceRole { WorkspaceManager, Consultant, Reader }
public enum ProjectScenario { TenantToTenant, CarveOut, CarveIn, AuditOnly }
public enum ProjectStatus { Active, Archived }
public enum EnvironmentRole { Source, Target }
public enum EnvironmentKind { M365Tenant, AdForest, AzureScope, Other }
public enum TargetState { Planned, Existing }

public readonly record struct UserIdentity(Guid TenantId, Guid ObjectId);

public interface IWorkspaceContext
{
    UserIdentity Identity { get; }
    IReadOnlyList<Guid> WorkspaceIds { get; }
    bool IsPlatformAdmin { get; }
}

public sealed record WorkspaceScope(
    UserIdentity Identity, IReadOnlyList<Guid> WorkspaceIds, bool IsPlatformAdmin = false) : IWorkspaceContext;

public abstract class WorkspaceEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid WorkspaceId { get; init; }
}

public sealed class ClientWorkspace
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public WorkspaceStatus Status { get; private set; }

    public void Archive() => Status = WorkspaceStatus.Archived;
}

public sealed class WorkspaceMember : WorkspaceEntity
{
    public Guid TenantId { get; init; }
    public Guid ObjectId { get; init; }
    public WorkspaceRole Role { get; set; }
}

public sealed class PlatformAdmin
{
    public Guid TenantId { get; init; }
    public Guid ObjectId { get; init; }
}

public sealed class Project : WorkspaceEntity
{
    public required string Name { get; set; }
    public ProjectScenario Scenario { get; init; }
    public string Description { get; set; } = "";
    public ProjectStatus Status { get; private set; }
    public string ScopingParameters { get; set; } = "{}";

    public void Archive() => Status = ProjectStatus.Archived;
}

public sealed class ProjectEnvironment : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public EnvironmentRole Role { get; init; }
    public EnvironmentKind Kind { get; init; }
    public required string ExternalId { get; init; }
    public string DeclaredScope { get; init; } = "";
    public TargetState TargetState { get; init; } = TargetState.Existing;
    public string Hypotheses { get; init; } = "";
}

public sealed class AuditEvent : WorkspaceEntity
{
    public Guid ActorTenantId { get; init; }
    public Guid ActorObjectId { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public required string Action { get; init; }
    public Guid? ResourceId { get; init; }
    public required string CorrelationId { get; init; }
}

public static class ModelRules
{
    public static void Validate(object entity)
    {
        if (entity is WorkspaceEntity owned && owned.WorkspaceId == Guid.Empty)
        {
            throw new ArgumentException("A client workspace is required.");
        }

        switch (entity)
        {
            case Connection connection:
                Name(connection.Name);
                if (connection.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(connection.ConfigurationReference))
                {
                    throw new ArgumentException("A tenant and server configuration reference are required.");
                }
                break;
            case CollectionRequest request:
                DiscoveryScope.Validate(request.ScopeJson, request.ScopeHash);
                break;
            case CollectionRun run:
                DiscoveryScope.Validate(run.ScopeJson, run.ScopeHash);
                if (run.ExternalRunId == Guid.Empty || run.CompletedAtUtc < run.StartedAtUtc)
                {
                    throw new ArgumentException("Invalid run identity or dates.");
                }
                break;
            case SectionRun section:
                DiscoveryScope.Validate(section.ScopeJson, section.ScopeHash);
                break;
            case RawArtifact artifact:
                if (artifact.CompressedBytes < 0 || artifact.UncompressedBytes < 0
                    || artifact.ContentHash.Length != 64 || !artifact.ContentHash.All(Uri.IsHexDigit)
                    || !artifact.BlobName.StartsWith(artifact.WorkspaceId.ToString("N") + "/", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Invalid private artifact metadata.");
                }
                break;
            case ClientWorkspace workspace:
                Name(workspace.Name);
                break;
            case Project project:
                Name(project.Name);
                if (!Enum.IsDefined(project.Scenario))
                {
                    throw new ArgumentException("Unknown project scenario.");
                }
                using (System.Text.Json.JsonDocument.Parse(project.ScopingParameters)) { }
                break;
            case WorkspaceMember member:
                if (member.TenantId == Guid.Empty || member.ObjectId == Guid.Empty || !Enum.IsDefined(member.Role))
                {
                    throw new ArgumentException("A stable Entra identity and valid role are required.");
                }
                break;
            case ProjectEnvironment environment:
                if (!Enum.IsDefined(environment.Role) || !Enum.IsDefined(environment.Kind)
                    || !Enum.IsDefined(environment.TargetState))
                {
                    throw new ArgumentException("Unknown environment classification.");
                }
                if (environment.Role == EnvironmentRole.Source && environment.TargetState == TargetState.Planned)
                {
                    throw new ArgumentException("Only a target can be planned.");
                }
                if (string.IsNullOrWhiteSpace(environment.ExternalId) && environment.TargetState == TargetState.Existing)
                {
                    throw new ArgumentException("An existing environment requires an external identifier.");
                }
                break;
        }
    }

    private static void Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            throw new ArgumentException("A name of 1 to 200 characters is required.");
        }
    }
}
