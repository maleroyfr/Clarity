namespace Clarity.Core;

public enum DependencyFamily { Groups, Teams, Sites, Mailboxes, Applications, Devices }

/// <summary>Population side of a principal, derived from the project's current scope decisions.</summary>
public enum DependencySide { Migrating, OtherWave, ToArbitrate, Remaining }

public enum DependencyClassification { MigratingOnly, Shared, ToArbitrate, CrossWave }
public enum DependencyLinkKind { Principal, Route }
public enum DependencyCoverage { NotCollected, Partial, Complete }

/// <summary>
/// Dated snapshot of one dependency analysis for a project environment and optional wave. A recompute replaces the
/// previous snapshot for the same environment and wave; consultant decisions are never stored here.
/// </summary>
public sealed class DependencyAnalysis : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public string? Wave { get; init; }
    public Guid OperationId { get; init; }
    public Guid ActorTenantId { get; init; }
    public Guid ActorObjectId { get; init; }
    public DateTimeOffset ComputedAtUtc { get; init; }
    public int MigratingPrincipals { get; init; }
    public int OtherWavePrincipals { get; init; }
    public int ArbitratePrincipals { get; init; }
    public int RemainingPrincipals { get; init; }
    public int Resources { get; init; }
    public int SharedResources { get; init; }
    public int UnresolvedLinks { get; init; }
    public long KnownVolumeBytes { get; init; }
    public long SharedVolumeBytes { get; init; }
    public int UnknownVolumeResources { get; init; }
    public required string CoverageJson { get; init; }
}

/// <summary>One collective resource (counted once by inventory object ID) reached by at least one migrating principal.</summary>
public sealed class DependencyResource : WorkspaceEntity
{
    public Guid AnalysisId { get; init; }
    public Guid EnvironmentId { get; init; }
    public Guid ObjectId { get; init; }
    public DependencyFamily Family { get; init; }
    public required string Type { get; init; }
    public string? DisplayName { get; init; }
    public DependencyClassification Classification { get; init; }
    public int MigratingCount { get; init; }
    public int OtherWaveCount { get; init; }
    public int ArbitrateCount { get; init; }
    public int RemainingCount { get; init; }
    public int RouteCount { get; init; }
    public bool VolumeApplies { get; init; }
    public long? VolumeBytes { get; init; }
    public int UnresolvedLinks { get; init; }
    public bool MembersNotCollected { get; init; }
    public bool AllUsers { get; init; }
    public ScopeValue? Decision { get; init; }
    public string? DecisionWave { get; init; }
}

/// <summary>
/// Explanation of a resource's inclusion: either a direct route (the relation reaching the resource, with its evidence)
/// or a sampled principal with its population side and the relation chain that links it to the resource.
/// </summary>
public sealed class DependencyLink : WorkspaceEntity
{
    public Guid AnalysisId { get; init; }
    public Guid ResourceObjectId { get; init; }
    public DependencyLinkKind Kind { get; init; }
    public Guid RelatedObjectId { get; init; }
    public DependencySide? Side { get; init; }
    public required string Route { get; init; }
    public Guid? ViaObjectId { get; init; }
    public Guid? RelationshipId { get; init; }
}

public sealed record DependencySectionCoverage(string CapabilityId, Clarity.Contracts.CoverageStatus? Coverage, DateTimeOffset? CompletedAtUtc,
    bool ResourceScoped);

/// <summary>Analysis coverage of one family: latest published section coverage plus what the traversal could not resolve.</summary>
public sealed record DependencyFamilyCoverage(DependencyFamily Family, DependencyCoverage Status, IReadOnlyList<DependencySectionCoverage> Sections,
    int UnresolvedLinks, int MembersNotCollected);

public static class DependencyRules
{
    public const int MaxEdges = 500_000;
    public const long MaxTraversal = 50_000_000;
    public const int MaxResources = 50_000;
    public const int SamplesPerSide = 25;
    public const int MaxRoutes = 50;

    /// <summary>Collective object types listed as resources; other reached types are traversed but not listed.</summary>
    public static DependencyFamily? Family(string type) => type switch
    {
        "group" or "mailGroup" => DependencyFamily.Groups,
        "team" => DependencyFamily.Teams,
        "site" or "oneDrive" => DependencyFamily.Sites,
        "mailbox" or "sharedMailbox" or "resourceMailbox" => DependencyFamily.Mailboxes,
        "servicePrincipal" or "mobileApp" or "deviceConfiguration" or "deviceCompliancePolicy" => DependencyFamily.Applications,
        "managedDevice" or "device" => DependencyFamily.Devices,
        _ => null
    };

    /// <summary>Sections whose published coverage determines the analysis coverage of each family.</summary>
    public static readonly IReadOnlyDictionary<DependencyFamily, string[]> Sections = new Dictionary<DependencyFamily, string[]>
    {
        [DependencyFamily.Groups] = ["ad.groups", "entra.groups", "exchange.mailEnabledGroups"],
        [DependencyFamily.Teams] = ["teams.teams", "teams.members", "teams.channels"],
        [DependencyFamily.Sites] = ["teams.sites", "sharepoint.groupSites", "sharepoint.sitePermissions", "onedrive.accounts"],
        [DependencyFamily.Mailboxes] = ["exchange.mailboxes", "exchange.mailboxPermissions"],
        [DependencyFamily.Applications] = ["applicationsSso.appRoleAssignments", "intune.appAssignments", "intune.profileAssignments", "intune.policyAssignments"],
        [DependencyFamily.Devices] = ["intune.primaryUsers", "intune.registeredOwners", "intune.managedDevices"]
    };

    /// <summary>Shared when known dependents span migrating and remaining principals; otherwise the most cautious open state.</summary>
    public static DependencyClassification Classify(int otherWave, int arbitrate, int remaining) =>
        remaining > 0 ? DependencyClassification.Shared
        : arbitrate > 0 ? DependencyClassification.ToArbitrate
        : otherWave > 0 ? DependencyClassification.CrossWave
        : DependencyClassification.MigratingOnly;

    /// <summary>
    /// Complete only when every applicable section was published complete at tenant or domain scope and the traversal met no
    /// unresolved principal or uncollected roster; resource-scoped sections (one site at a time) never prove tenant coverage.
    /// </summary>
    public static DependencyCoverage Status(IReadOnlyList<DependencySectionCoverage> sections, int unresolved, int membersNotCollected)
    {
        var collected = sections.Where(s => s.Coverage is not (null or Clarity.Contracts.CoverageStatus.Failed)).ToList();
        if (collected.Count == 0) { return DependencyCoverage.NotCollected; }
        return collected.Count == sections.Count && unresolved == 0 && membersNotCollected == 0
            && collected.All(s => !s.ResourceScoped && s.Coverage is Clarity.Contracts.CoverageStatus.Complete or Clarity.Contracts.CoverageStatus.NotApplicable)
            ? DependencyCoverage.Complete : DependencyCoverage.Partial;
    }
}
