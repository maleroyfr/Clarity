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

namespace Clarity.Collectors.Graph.Intune;

public sealed class IntuneManagedDevicesCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphManagedDeviceFetcher _managedDeviceFetcher;

    public string CollectorId => "graph.intune.manageddevices";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.Intune;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["DeviceManagementManagedDevices.Read.All"];

    public IntuneManagedDevicesCollector(IGraphClientFactory clientFactory, IGraphManagedDeviceFetcher managedDeviceFetcher)
    {
        _clientFactory = clientFactory;
        _managedDeviceFetcher = managedDeviceFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<Microsoft.Graph.Models.ManagedDevice> devices = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                devices = await _managedDeviceFetcher.FetchAllManagedDevicesAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(devices.Count);

            foreach (var device in devices)
            {
                if (device.Id is null) continue;

                var properties = new Dictionary<string, string?>
                {
                    ["deviceName"]              = device.DeviceName,
                    ["operatingSystem"]          = device.OperatingSystem,
                    ["complianceState"]          = device.ComplianceState?.ToString(),
                    ["managedDeviceOwnerType"]   = device.ManagedDeviceOwnerType?.ToString(),
                    ["enrolledDateTime"]         = device.EnrolledDateTime?.ToString("O"),
                    ["lastSyncDateTime"]         = device.LastSyncDateTime?.ToString("O"),
                    ["serialNumber"]             = device.SerialNumber,
                    ["model"]                    = device.Model,
                    ["manufacturer"]             = device.Manufacturer
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(device)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.IntuneDevice,
                    device.Id,
                    device.DeviceName,
                    properties,
                    rawJson));

                if (objects.Count % 100 == 0)
                    context.Logger.LogInformation("Collected {Count} managed devices so far...", objects.Count);
            }

            context.Logger.LogInformation("Collected {Count} managed devices total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting managed devices: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting managed devices: {Message}", ex.Message);
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
                        "Retry attempt {Attempt} after {Delay} for Graph managed devices collection.",
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
