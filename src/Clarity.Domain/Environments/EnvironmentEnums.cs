namespace Clarity.Domain.Environments;

public enum EnvironmentType
{
    M365Tenant,
    OnPremAD,
    HybridAD,
    ExchangeOnPrem,
    Standalone
}

public enum EnvironmentStatus
{
    Draft,
    Configuring,
    Ready,
    Error,
    Archived
}

public enum WorkloadConfigStatus
{
    NotConfigured,
    Partial,
    Ready,
    ValidationFailed
}

public enum PrerequisiteCategory
{
    GraphPermission,
    Certificate,
    PowerShellModule,
    NetworkAccess,
    AdminConsent,
    Other
}

public enum AuthType
{
    Certificate,
    ClientSecret,
    WindowsIntegrated,
    ServiceAccount
}

public enum RelationDirection
{
    Unidirectional,
    Bidirectional
}

public enum CollectorRunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum SnapshotStatus
{
    Draft,
    Running,
    Completed,
    Failed,
    Partial
}
