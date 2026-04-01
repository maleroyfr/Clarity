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

namespace Clarity.Collectors.Graph.Entra;

public sealed class EntraDevicesCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphDeviceFetcher _deviceFetcher;

    public string CollectorId => "graph.entra.devices";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.EntraId;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["Device.Read.All"];

    public EntraDevicesCollector(IGraphClientFactory clientFactory, IGraphDeviceFetcher deviceFetcher)
    {
        _clientFactory = clientFactory;
        _deviceFetcher = deviceFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<Microsoft.Graph.Models.Device> devices = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                devices = await _deviceFetcher.FetchAllDevicesAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(devices.Count);

            foreach (var device in devices)
            {
                if (device.Id is null) continue;

                var properties = new Dictionary<string, string?>
                {
                    ["operatingSystem"]                   = device.OperatingSystem,
                    ["operatingSystemVersion"]            = device.OperatingSystemVersion,
                    ["trustType"]                         = device.TrustType,
                    ["isManaged"]                         = device.IsManaged?.ToString(),
                    ["isCompliant"]                       = device.IsCompliant?.ToString(),
                    ["registeredOwnersCount"]             = device.RegisteredOwners?.Count.ToString(),
                    ["approximateLastSignInDateTime"]     = device.ApproximateLastSignInDateTime?.ToString("O")
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(device)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.EntraDevice,
                    device.Id,
                    device.DisplayName,
                    properties,
                    rawJson));

                if (objects.Count % 100 == 0)
                    context.Logger.LogInformation("Collected {Count} devices so far...", objects.Count);
            }

            context.Logger.LogInformation("Collected {Count} devices total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting devices: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting devices: {Message}", ex.Message);
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
                        "Retry attempt {Attempt} after {Delay} for Graph devices collection.",
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
