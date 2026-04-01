using Clarity.Collectors.Contracts;
using Clarity.Collectors.Graph.Auth;
using CollectorError = Clarity.Collectors.Contracts.CollectorError;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;
using Polly;
using Polly.Retry;
using System.Text.Json;

namespace Clarity.Collectors.Graph.Tenant;

public sealed class SubscribedSkusCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphSubscribedSkuFetcher _subscribedSkuFetcher;

    public string CollectorId => "graph.tenant.subscribedskus";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.EntraId;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["Organization.Read.All"];

    public SubscribedSkusCollector(IGraphClientFactory clientFactory, IGraphSubscribedSkuFetcher subscribedSkuFetcher)
    {
        _clientFactory = clientFactory;
        _subscribedSkuFetcher = subscribedSkuFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<Microsoft.Graph.Models.SubscribedSku> skus = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                skus = await _subscribedSkuFetcher.FetchAllSubscribedSkusAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(skus.Count);

            foreach (var sku in skus)
            {
                if (sku.Id is null) continue;

                var properties = new Dictionary<string, string?>
                {
                    ["skuPartNumber"]          = sku.SkuPartNumber,
                    ["appliesTo"]              = sku.AppliesTo,
                    ["capabilityStatus"]       = sku.CapabilityStatus,
                    ["consumedUnits"]          = sku.ConsumedUnits?.ToString(),
                    ["prepaidUnitsEnabled"]    = sku.PrepaidUnits?.Enabled?.ToString(),
                    ["prepaidUnitsSuspended"]  = sku.PrepaidUnits?.Suspended?.ToString()
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(sku)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.LicenseSku,
                    sku.Id,
                    sku.SkuPartNumber,
                    properties,
                    rawJson));
            }

            context.Logger.LogInformation("Collected {Count} subscribed SKUs total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting subscribed SKUs: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting subscribed SKUs: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
    }

    private static ResiliencePipeline BuildResiliencePipeline(CollectorOptions options, ILogger logger)
    {
        if (options.MaxRetries <= 0)
            return ResiliencePipeline.Empty;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetries,
                Delay = options.RetryDelay == default ? TimeSpan.FromSeconds(2) : options.RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder()
                    .Handle<ODataError>(e => e.ResponseStatusCode == 429)
                    .Handle<HttpRequestException>(),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Retry attempt {Attempt} after {Delay} for Graph subscribed SKUs collection.",
                        args.AttemptNumber + 1,
                        args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private CollectorMetadata BuildMetadata(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CollectorRunContext context) =>
        new(CollectorId, Version, WorkloadArea, CollectorType,
            startedAt, completedAt, context.SnapshotId, context.EnvironmentId);
}
