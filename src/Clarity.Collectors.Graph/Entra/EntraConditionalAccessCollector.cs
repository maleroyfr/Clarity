using Clarity.Collectors.Contracts;
using Clarity.Collectors.Graph.Auth;
using CollectorError = Clarity.Collectors.Contracts.CollectorError;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Polly;
using Polly.Retry;
using System.Text.Json;

namespace Clarity.Collectors.Graph.Entra;

public sealed class EntraConditionalAccessCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphConditionalAccessFetcher _caFetcher;

    public string CollectorId => "graph.entra.conditionalaccesspolicies";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.EntraId;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["Policy.Read.All"];

    public EntraConditionalAccessCollector(IGraphClientFactory clientFactory, IGraphConditionalAccessFetcher caFetcher)
    {
        _clientFactory = clientFactory;
        _caFetcher = caFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<ConditionalAccessPolicy> policies = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                policies = await _caFetcher.FetchAllPoliciesAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(policies.Count);

            foreach (var policy in policies)
            {
                if (policy.Id is null) continue;

                var conditionsSummary = BuildConditionsSummary(policy.Conditions);
                var grantControlsSummary = BuildGrantControlsSummary(policy.GrantControls);

                var properties = new Dictionary<string, string?>
                {
                    ["state"]            = policy.State?.ToString(),
                    ["conditions"]       = conditionsSummary,
                    ["grantControls"]    = grantControlsSummary,
                    ["createdDateTime"]  = policy.CreatedDateTime?.ToString("O"),
                    ["modifiedDateTime"] = policy.ModifiedDateTime?.ToString("O")
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(policy)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.EntraConditionalAccessPolicy,
                    policy.Id,
                    policy.DisplayName,
                    properties,
                    rawJson));
            }

            context.Logger.LogInformation("Collected {Count} conditional access policies total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting conditional access policies: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting conditional access policies: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
    }

    private static string? BuildConditionsSummary(ConditionalAccessConditionSet? conditions)
    {
        if (conditions is null) return null;

        var parts = new List<string>();

        if (conditions.Users?.IncludeUsers is { Count: > 0 } users)
            parts.Add($"users:{string.Join(",", users)}");

        if (conditions.Applications?.IncludeApplications is { Count: > 0 } apps)
            parts.Add($"apps:{string.Join(",", apps)}");

        if (conditions.Platforms?.IncludePlatforms is { Count: > 0 } platforms)
            parts.Add($"platforms:{string.Join(",", platforms)}");

        if (conditions.Locations?.IncludeLocations is { Count: > 0 } locations)
            parts.Add($"locations:{string.Join(",", locations)}");

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    private static string? BuildGrantControlsSummary(ConditionalAccessGrantControls? grantControls)
    {
        if (grantControls is null) return null;

        var parts = new List<string>();

        if (grantControls.BuiltInControls is { Count: > 0 } builtIn)
            parts.Add(string.Join(",", builtIn));

        if (grantControls.Operator is not null)
            parts.Add($"operator:{grantControls.Operator}");

        return parts.Count > 0 ? string.Join("; ", parts) : null;
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
                        "Retry attempt {Attempt} after {Delay} for Graph conditional access policies collection.",
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
