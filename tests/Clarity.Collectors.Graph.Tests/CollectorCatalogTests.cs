using Clarity.Collectors.Contracts;
using Clarity.SharedContracts.Enums;
using FluentAssertions;

namespace Clarity.Collectors.Graph.Tests;

public sealed class CollectorCatalogTests
{
    [Fact]
    public void GetCollectorsForScope_FiltersAndOrdersCollectors()
    {
        var catalog = new CollectorCatalog(
        [
            new FakeCollector("graph.teams.teams", WorkloadArea.Teams),
            new FakeCollector("graph.entra.users", WorkloadArea.EntraId),
            new FakeCollector("graph.entra.groups", WorkloadArea.EntraId)
        ]);

        var collectors = catalog.GetCollectorsForScope([WorkloadArea.EntraId]);

        collectors.Select(collector => collector.CollectorId).Should().Equal(
            "graph.entra.groups",
            "graph.entra.users");
    }

    private sealed class FakeCollector(string collectorId, WorkloadArea workloadArea) : ICollector
    {
        public string CollectorId => collectorId;
        public string Version => "1.0.0";
        public WorkloadArea WorkloadArea => workloadArea;
        public CollectorType CollectorType => CollectorType.Graph;
        public IReadOnlyList<string> RequiredPermissions => [];

        public Task<CollectorResult> RunAsync(CollectorRunContext context) =>
            Task.FromResult(CollectorResult.Success([], [], new CollectorMetadata(
                CollectorId,
                Version,
                WorkloadArea,
                CollectorType,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                context.SnapshotId,
                context.EnvironmentId)));
    }
}
