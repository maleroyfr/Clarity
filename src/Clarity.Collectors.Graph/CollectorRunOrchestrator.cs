using Clarity.Collectors.Contracts;
using Clarity.Domain.Environments;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;
using Microsoft.Extensions.Logging;
using DomainCollectorError = Clarity.Domain.Snapshots.CollectorError;

namespace Clarity.Collectors.Graph;

/// <summary>
/// Orchestrates Graph collector runs for a snapshot. Runs collectors sequentially
/// to respect Graph throttling limits, reporting progress after each.
/// </summary>
public sealed class CollectorRunOrchestrator : ICollectorRunOrchestrator
{
    private readonly IEnumerable<ICollector> _collectors;
    private readonly ILogger<CollectorRunOrchestrator> _logger;

    public CollectorRunOrchestrator(
        IEnumerable<ICollector> collectors,
        ILogger<CollectorRunOrchestrator> logger)
    {
        _collectors = collectors;
        _logger = logger;
    }

    public async Task OrchestrateAsync(
        Snapshot snapshot,
        Domain.Environments.Environment environment,
        IReadOnlyList<WorkloadArea> scope,
        IProgress<CollectorProgress> progress,
        CancellationToken cancellationToken)
    {
        var matching = _collectors
            .Where(c => scope.Contains(c.WorkloadArea))
            .ToList();

        int total = matching.Count;
        int processed = 0;

        foreach (var collector in matching)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var authConfig = environment.GetActiveAuthConfig(collector.WorkloadArea);
            if (authConfig is null)
            {
                _logger.LogWarning(
                    "No active auth configuration for {WorkloadArea}. Skipping {CollectorId}.",
                    collector.WorkloadArea, collector.CollectorId);

                processed++;
                progress.Report(new CollectorProgress(
                    collector.WorkloadArea,
                    $"Skipped {collector.CollectorId} — no auth configuration.",
                    processed, total, processed == total, HasError: false));
                continue;
            }

            var run = snapshot.StartCollectorRun(
                collector.WorkloadArea,
                collector.CollectorType,
                collector.Version);

            var context = new CollectorRunContext(
                snapshot.Id,
                run.Id,
                snapshot.EnvironmentId,
                collector.WorkloadArea,
                authConfig,
                new CollectorOptions(),
                _logger,
                cancellationToken);

            progress.Report(new CollectorProgress(
                collector.WorkloadArea,
                $"Starting {collector.CollectorId}...",
                processed, total, IsComplete: false, HasError: false));

            CollectorResult result;
            try
            {
                result = await collector.RunAsync(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Collector {CollectorId} threw unexpectedly.", collector.CollectorId);

                run.Fail(new DomainCollectorError(
                    "UNEXPECTED_ERROR", ex.Message, ex.ToString()));

                processed++;
                progress.Report(new CollectorProgress(
                    collector.WorkloadArea,
                    $"{collector.CollectorId} failed unexpectedly.",
                    processed, total, processed == total, HasError: true));
                continue;
            }

            if (result.Status == CollectorRunStatus.Completed)
            {
                run.Complete(result.ItemsCollected, result.PermissionsUsed, result.CommandsExecuted);
            }
            else
            {
                var firstError = result.Errors.FirstOrDefault();
                run.Fail(firstError is not null
                    ? new DomainCollectorError(firstError.Code, firstError.Message, firstError.Detail)
                    : new DomainCollectorError("UNKNOWN", "Collector failed without an error."));
            }

            processed++;
            progress.Report(new CollectorProgress(
                collector.WorkloadArea,
                $"{collector.CollectorId} completed with {result.ItemsCollected} items.",
                processed, total,
                IsComplete: processed == total,
                HasError: result.Status != CollectorRunStatus.Completed));
        }
    }
}
