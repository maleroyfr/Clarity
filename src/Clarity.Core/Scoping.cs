namespace Clarity.Core;

public enum PopulationKind { Group, OrganizationalUnit, Domain, Site, Filter, ImportedList }
public enum ScopeValue { Include, Exclude, ToArbitrate }
public enum ImportIdentifier { UserPrincipalName, Smtp, SamAccountName, ExternalId, DisplayName }
public enum ImportResolution { Matched, Ambiguous, Unmatched, Chosen }

/// <summary>A consultant-defined population evaluated against one environment's current inventory.</summary>
public sealed class Population : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public required string Name { get; set; }
    public PopulationKind Kind { get; init; }
    public string Criterion { get; init; } = "";
    public Guid? RootObjectId { get; init; }
    public string? ObjectType { get; init; }
    public string? Status { get; init; }
    public ImportIdentifier? Identifier { get; init; }
    public int MemberCount { get; set; }
    public DateTimeOffset EvaluatedAtUtc { get; set; }
    public Guid AuthorTenantId { get; init; }
    public Guid AuthorObjectId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Materialized population member as of <see cref="Population.EvaluatedAtUtc"/>.</summary>
public sealed class PopulationMember : WorkspaceEntity
{
    public Guid PopulationId { get; init; }
    public Guid EnvironmentId { get; init; }
    public Guid ObjectId { get; init; }
}

/// <summary>One line of an imported list and its resolution against the inventory.</summary>
public sealed class PopulationEntry : WorkspaceEntity
{
    public Guid PopulationId { get; init; }
    public int Line { get; init; }
    public required string Value { get; init; }
    public ImportResolution Resolution { get; set; }
    public Guid? ObjectId { get; set; }
    public int CandidateCount { get; set; }
}

/// <summary>Current consultant scope decision for one inventory object; history lives in <see cref="ScopeDecisionRevision"/>.</summary>
public sealed class ScopeDecision : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public Guid ObjectId { get; init; }
    public ScopeValue Value { get; set; }
    public string? Wave { get; set; }
    public Guid? PopulationId { get; set; }
    public required string Fingerprint { get; set; }
    /// <summary>Critical facts behind <see cref="Fingerprint"/>; null for decisions recorded before bases were kept.</summary>
    public string? BasisJson { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ScopeDecisionRevision : WorkspaceEntity
{
    public Guid DecisionId { get; init; }
    public Guid OperationId { get; init; }
    public long Number { get; init; }
    public ScopeValue Value { get; init; }
    public string? Wave { get; init; }
    public Guid? PopulationId { get; init; }
    public required string Fingerprint { get; init; }
    public string? BasisJson { get; init; }
    public Guid? EvidenceObservationId { get; init; }
    public required string Reason { get; init; }
    public Guid ActorTenantId { get; init; }
    public Guid ActorObjectId { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public static class ScopeRules
{
    public const int MaxPopulation = 20000;
    public const int MaxImportLines = 5000;

    /// <summary>Fields whose change makes a recorded scope decision due for review.</summary>
    public static readonly string[] CriticalFields =
        ["userPrincipalName", "mail", "proxyAddresses", "sAMAccountName", "distinguishedName", "enabled", "accountEnabled", "onPremisesImmutableId"];

    public static string? Wave(string? wave)
    {
        var value = wave?.Trim();
        if (value is { Length: > 100 }) { throw new ArgumentException("A migration wave label is limited to 100 characters."); }
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
