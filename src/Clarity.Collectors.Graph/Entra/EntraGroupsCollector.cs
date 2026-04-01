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

public sealed class EntraGroupsCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphGroupFetcher _groupFetcher;

    public string CollectorId => "graph.entra.groups";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.EntraId;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["Group.Read.All"];

    public EntraGroupsCollector(IGraphClientFactory clientFactory, IGraphGroupFetcher groupFetcher)
    {
        _clientFactory = clientFactory;
        _groupFetcher = groupFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<Microsoft.Graph.Models.Group> groups = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                groups = await _groupFetcher.FetchAllGroupsAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(groups.Count);

            foreach (var group in groups)
            {
                if (group.Id is null) continue;

                // Determine group type from groupTypes collection
                var groupTypes = group.GroupTypes ?? [];
                string groupType = groupTypes.Contains("Unified")
                    ? "Unified"
                    : group.SecurityEnabled == true
                        ? groupTypes.Contains("DynamicMembership") ? "DynamicSecurity" : "Security"
                        : "Distribution";

                var properties = new Dictionary<string, string?>
                {
                    ["groupType"]       = groupType,
                    ["mailEnabled"]     = group.MailEnabled?.ToString(),
                    ["securityEnabled"] = group.SecurityEnabled?.ToString(),
                    ["membershipRule"]  = group.MembershipRule,
                    ["mail"]            = group.Mail,
                    ["description"]     = group.Description,
                    ["createdDateTime"] = group.CreatedDateTime?.ToString("O")
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(group)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.EntraGroup,
                    group.Id,
                    group.DisplayName,
                    properties,
                    rawJson));

                if (objects.Count % 100 == 0)
                    context.Logger.LogInformation("Collected {Count} groups so far...", objects.Count);
            }

            context.Logger.LogInformation("Collected {Count} groups total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting groups: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting groups: {Message}", ex.Message);
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
                        "Retry attempt {Attempt} after {Delay} for Graph groups collection.",
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
