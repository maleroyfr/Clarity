using Clarity.SharedContracts.Enums;

namespace Clarity.Collectors.Contracts;

public interface ICollectorCatalog
{
    IReadOnlyList<ICollector> GetCollectorsForScope(IReadOnlyList<WorkloadArea> scope);
}

public sealed class CollectorCatalog(IEnumerable<ICollector> collectors) : ICollectorCatalog
{
    private readonly IReadOnlyList<ICollector> _collectors = collectors
        .DistinctBy(collector => collector.CollectorId, StringComparer.Ordinal)
        .OrderBy(collector => collector.WorkloadArea)
        .ThenBy(collector => collector.CollectorId, StringComparer.Ordinal)
        .ToList();

    public IReadOnlyList<ICollector> GetCollectorsForScope(IReadOnlyList<WorkloadArea> scope) =>
        _collectors
            .Where(collector => scope.Contains(collector.WorkloadArea))
            .ToList();
}
