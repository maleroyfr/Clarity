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

public sealed class IntuneCompliancePoliciesCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphCompliancePolicyFetcher _compliancePolicyFetcher;

    public string CollectorId => "graph.intune.compliancepolicies";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.Intune;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["DeviceManagementConfiguration.Read.All"];

    public IntuneCompliancePoliciesCollector(IGraphClientFactory clientFactory, IGraphCompliancePolicyFetcher compliancePolicyFetcher)
    {
        _clientFactory = clientFactory;
        _compliancePolicyFetcher = compliancePolicyFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<Microsoft.Graph.Models.DeviceCompliancePolicy> policies = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                policies = await _compliancePolicyFetcher.FetchAllCompliancePoliciesAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(policies.Count);

            foreach (var policy in policies)
            {
                if (policy.Id is null) continue;

                var properties = new Dictionary<string, string?>
                {
                    ["displayName"]          = policy.DisplayName,
                    ["description"]          = policy.Description,
                    ["version"]              = policy.Version?.ToString(),
                    ["createdDateTime"]      = policy.CreatedDateTime?.ToString("O"),
                    ["lastModifiedDateTime"] = policy.LastModifiedDateTime?.ToString("O")
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(policy)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.IntuneCompliancePolicy,
                    policy.Id,
                    policy.DisplayName,
                    properties,
                    rawJson));
            }

            context.Logger.LogInformation("Collected {Count} compliance policies total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting compliance policies: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting compliance policies: {Message}", ex.Message);
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
                        "Retry attempt {Attempt} after {Delay} for Graph compliance policies collection.",
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
