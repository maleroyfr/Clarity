using Clarity.Domain.Environments;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;

namespace Clarity.Collectors.Contracts;

/// <summary>
/// Orchestrates all collector runs for a snapshot.
/// Selects the right ICollector per workload, runs them (with parallelism where safe),
/// and writes results back to the snapshot.
/// </summary>
public interface ICollectorRunOrchestrator
{
    Task OrchestrateAsync(
        Snapshot snapshot,
        Domain.Environments.Environment environment,
        IReadOnlyList<WorkloadArea> scope,
        IProgress<CollectorProgress> progress,
        CancellationToken cancellationToken);
}
