using Clarity.Domain.Comparisons;
using Clarity.Domain.Mappings;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;
using Microsoft.Extensions.Logging;

namespace Clarity.Comparisons;

public sealed class ComparisonService(
    ISnapshotRepository snapshotRepository,
    IInventoryObjectRepository inventoryRepository,
    IComparisonJobRepository comparisonJobRepository,
    ILogger<ComparisonService> logger) : IComparisonService
{
    public async Task<ComparisonJobResult> RunComparisonAsync(
        ComparisonRequest request,
        CancellationToken ct = default)
    {
        var job = ComparisonJob.Create(
            request.CustomerId,
            request.Name,
            request.Mode,
            request.LeftSnapshotId,
            request.RightSnapshotId,
            request.WorkloadFilter,
            request.ObjectTypeFilter);

        try
        {
            // Validate both snapshots exist before starting work
            _ = await snapshotRepository.GetByIdAsync(request.LeftSnapshotId, ct)
                ?? throw new InvalidOperationException($"Left snapshot '{request.LeftSnapshotId}' was not found.");
            _ = await snapshotRepository.GetByIdAsync(request.RightSnapshotId, ct)
                ?? throw new InvalidOperationException($"Right snapshot '{request.RightSnapshotId}' was not found.");

            await comparisonJobRepository.AddAsync(job, ct);
            await comparisonJobRepository.SaveChangesAsync(ct);

            job.MarkRunning();
            await comparisonJobRepository.UpdateAsync(job, ct);
            await comparisonJobRepository.SaveChangesAsync(ct);

            // Load both snapshot inventories
            var leftObjects  = await inventoryRepository.GetBySnapshotAsync(request.LeftSnapshotId,  null, ct);
            var rightObjects = await inventoryRepository.GetBySnapshotAsync(request.RightSnapshotId, null, ct);

            leftObjects  = ApplyFilters(leftObjects,  request);
            rightObjects = ApplyFilters(rightObjects, request);

            logger.LogInformation(
                "Comparing snapshot {Left} ({LeftCount} objects) vs {Right} ({RightCount} objects)",
                request.LeftSnapshotId, leftObjects.Count,
                request.RightSnapshotId, rightObjects.Count);

            // Build lookup dictionaries keyed by (ObjectType, ExternalId)
            var leftDict  = leftObjects.ToDictionary(o => (o.ObjectType, o.ExternalId));
            var rightDict = rightObjects.ToDictionary(o => (o.ObjectType, o.ExternalId));

            var deltaItems = new List<ComparisonDeltaItem>();

            // Added — in right but not left
            foreach (var (key, obj) in rightDict)
            {
                if (!leftDict.ContainsKey(key))
                    deltaItems.Add(new ComparisonDeltaItem(
                        obj.ObjectType, obj.ExternalId, obj.DisplayName,
                        ChangeType.Added, DeltaSeverity.Warning,
                        rightValueJson: obj.RawDataJson));
            }

            // Removed — in left but not right
            foreach (var (key, obj) in leftDict)
            {
                if (!rightDict.ContainsKey(key))
                    deltaItems.Add(new ComparisonDeltaItem(
                        obj.ObjectType, obj.ExternalId, obj.DisplayName,
                        ChangeType.Removed, DeltaSeverity.Critical,
                        leftValueJson: obj.RawDataJson));
            }

            // Present in both — diff properties
            foreach (var (key, leftObj) in leftDict)
            {
                if (!rightDict.TryGetValue(key, out var rightObj))
                    continue;

                var diffs = DiffProperties(leftObj, rightObj);

                if (diffs.Count > 0)
                    deltaItems.Add(new ComparisonDeltaItem(
                        leftObj.ObjectType, leftObj.ExternalId, leftObj.DisplayName,
                        ChangeType.Modified, DeltaSeverity.Info, diffs,
                        leftValueJson: leftObj.RawDataJson,
                        rightValueJson: rightObj.RawDataJson));
                else
                    deltaItems.Add(new ComparisonDeltaItem(
                        leftObj.ObjectType, leftObj.ExternalId, leftObj.DisplayName,
                        ChangeType.Unchanged, DeltaSeverity.Info));
            }

            // Group delta items by WorkloadArea and create ComparisonResult per workload
            var resultsByWorkload = deltaItems
                .GroupBy(d => WorkloadAreaMapping.GetWorkloadArea(d.ObjectType));

            var comparisonResults = new List<ComparisonResult>();
            foreach (var group in resultsByWorkload)
            {
                var result = ComparisonResult.Create(job.Id, group.Key);
                result.SetDeltas(group);
                comparisonResults.Add(result);
            }

            job.Complete(comparisonResults);
            await comparisonJobRepository.UpdateAsync(job, ct);
            await comparisonJobRepository.SaveChangesAsync(ct);

            var summary = BuildSummary(leftObjects.Count, rightObjects.Count, comparisonResults);

            logger.LogInformation(
                "Comparison {JobId} completed: +{Added} -{Removed} ~{Modified} ={Unchanged}",
                job.Id, summary.Added, summary.Removed, summary.Modified, summary.Unchanged);

            return new ComparisonJobResult(true, job.Id, null, summary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Comparison job failed for left={Left} right={Right}",
                request.LeftSnapshotId, request.RightSnapshotId);

            job.Fail();
            await comparisonJobRepository.UpdateAsync(job, ct);
            await comparisonJobRepository.SaveChangesAsync(ct);

            return new ComparisonJobResult(false, job.Id, ex.Message, null);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<InventoryObject> ApplyFilters(
        IReadOnlyList<InventoryObject> objects,
        ComparisonRequest request)
    {
        IEnumerable<InventoryObject> result = objects;

        if (request.WorkloadFilter?.Count > 0)
        {
            var allowedTypes = request.WorkloadFilter
                .SelectMany(WorkloadAreaMapping.GetObjectTypes)
                .ToHashSet();
            result = result.Where(o => allowedTypes.Contains(o.ObjectType));
        }

        if (request.ObjectTypeFilter?.Count > 0)
        {
            var allowedTypes = request.ObjectTypeFilter.ToHashSet();
            result = result.Where(o => allowedTypes.Contains(o.ObjectType));
        }

        return result.ToList();
    }

    private static List<PropertyDiff> DiffProperties(InventoryObject left, InventoryObject right)
    {
        var diffs = new List<PropertyDiff>();

        var allKeys = left.Properties.Keys.Union(right.Properties.Keys);
        foreach (var key in allKeys)
        {
            var leftVal  = left.GetProperty(key);
            var rightVal = right.GetProperty(key);

            if (!string.Equals(leftVal, rightVal, StringComparison.Ordinal))
                diffs.Add(new PropertyDiff(key, leftVal, rightVal));
        }

        return diffs;
    }

    private static ComparisonSummary BuildSummary(
        int totalLeft,
        int totalRight,
        IEnumerable<ComparisonResult> results)
    {
        var resultList = results.ToList();

        var byWorkload = resultList
            .Select(r => new WorkloadAreaSummary(
                r.WorkloadArea,
                r.TotalAdded,
                r.TotalRemoved,
                r.TotalModified,
                r.TotalUnchanged))
            .OrderBy(w => w.WorkloadArea.ToString())
            .ToList();

        return new ComparisonSummary(
            totalLeft,
            totalRight,
            resultList.Sum(r => r.TotalAdded),
            resultList.Sum(r => r.TotalRemoved),
            resultList.Sum(r => r.TotalModified),
            resultList.Sum(r => r.TotalUnchanged),
            byWorkload);
    }
}
