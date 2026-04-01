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

public sealed class EntraApplicationsCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphApplicationFetcher _applicationFetcher;

    public string CollectorId => "graph.entra.applications";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.EntraId;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["Application.Read.All"];

    public EntraApplicationsCollector(IGraphClientFactory clientFactory, IGraphApplicationFetcher applicationFetcher)
    {
        _clientFactory = clientFactory;
        _applicationFetcher = applicationFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<Microsoft.Graph.Models.Application> apps = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                apps = await _applicationFetcher.FetchAllApplicationsAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(apps.Count);

            foreach (var app in apps)
            {
                if (app.Id is null) continue;

                var properties = new Dictionary<string, string?>
                {
                    ["appId"]                    = app.AppId,
                    ["signInAudience"]           = app.SignInAudience,
                    ["createdDateTime"]          = app.CreatedDateTime?.ToString("O"),
                    ["keyCredentialsCount"]      = app.KeyCredentials?.Count.ToString(),
                    ["passwordCredentialsCount"] = app.PasswordCredentials?.Count.ToString(),
                    ["requiredResourceAccess"]   = app.RequiredResourceAccess is not null
                        ? string.Join("; ", app.RequiredResourceAccess.Select(r =>
                            $"{r.ResourceAppId}({r.ResourceAccess?.Count ?? 0} permissions)"))
                        : null
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(app)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.EntraApplication,
                    app.Id,
                    app.DisplayName,
                    properties,
                    rawJson));

                if (objects.Count % 100 == 0)
                    context.Logger.LogInformation("Collected {Count} applications so far...", objects.Count);
            }

            context.Logger.LogInformation("Collected {Count} applications total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting applications: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting applications: {Message}", ex.Message);
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
                        "Retry attempt {Attempt} after {Delay} for Graph applications collection.",
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
