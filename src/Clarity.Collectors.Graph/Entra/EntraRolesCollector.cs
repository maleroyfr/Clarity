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

public sealed class EntraRolesCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphRoleFetcher _roleFetcher;

    public string CollectorId => "graph.entra.roles";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.EntraId;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["RoleManagement.Read.Directory"];

    public EntraRolesCollector(IGraphClientFactory clientFactory, IGraphRoleFetcher roleFetcher)
    {
        _clientFactory = clientFactory;
        _roleFetcher = roleFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<Microsoft.Graph.Models.DirectoryRole> roles = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                roles = await _roleFetcher.FetchAllRolesAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(roles.Count);

            foreach (var role in roles)
            {
                if (role.Id is null) continue;

                var properties = new Dictionary<string, string?>
                {
                    ["roleTemplateId"] = role.RoleTemplateId,
                    ["description"]    = role.Description
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(role)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.EntraRole,
                    role.Id,
                    role.DisplayName,
                    properties,
                    rawJson));
            }

            context.Logger.LogInformation("Collected {Count} directory roles total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting roles: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting roles: {Message}", ex.Message);
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
                        "Retry attempt {Attempt} after {Delay} for Graph roles collection.",
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
