using Clarity.SharedContracts.Enums;

namespace Clarity.Collectors.Contracts;

/// <summary>
/// Core collector abstraction. Every collector (Graph, PowerShell, LDAP) implements this.
/// </summary>
public interface ICollector
{
    /// <summary>Stable, unique identifier for this collector (e.g. "graph.entra.users").</summary>
    string CollectorId { get; }

    string Version { get; }
    WorkloadArea WorkloadArea { get; }
    CollectorType CollectorType { get; }

    /// <summary>Graph API permissions or PS module prerequisites required.</summary>
    IReadOnlyList<string> RequiredPermissions { get; }

    Task<CollectorResult> RunAsync(CollectorRunContext context);
}

/// <summary>
/// A collector broken into discrete capability units (one per object type).
/// Allows partial success — if users succeed but devices fail, both results are captured.
/// </summary>
public interface ICollectorCapability
{
    InventoryObjectType ObjectType { get; }
    bool IsSupported(CollectorRunContext context);

    Task<IReadOnlyList<Domain.Snapshots.InventoryObject>> CollectAsync(CollectorRunContext context);
}

/// <summary>Progress report emitted during a snapshot run.</summary>
public sealed record CollectorProgress(
    WorkloadArea WorkloadArea,
    string Message,
    int ItemsProcessed,
    int? TotalItems,
    bool IsComplete,
    bool HasError);
