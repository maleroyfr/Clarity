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

namespace Clarity.Collectors.Graph.Teams;

public sealed class TeamsCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphTeamFetcher _teamFetcher;

    public string CollectorId => "graph.teams.teams";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.Teams;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["Team.ReadBasic.All"];

    public TeamsCollector(IGraphClientFactory clientFactory, IGraphTeamFetcher teamFetcher)
    {
        _clientFactory = clientFactory;
        _teamFetcher = teamFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<Microsoft.Graph.Models.Team> teams = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                teams = await _teamFetcher.FetchAllTeamsAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(teams.Count);

            foreach (var team in teams)
            {
                if (team.Id is null) continue;

                var properties = new Dictionary<string, string?>
                {
                    ["description"]     = team.Description,
                    ["visibility"]      = team.Visibility?.ToString(),
                    ["createdDateTime"] = team.CreatedDateTime?.ToString("O"),
                    ["isArchived"]      = team.IsArchived?.ToString(),
                    ["memberCount"]     = team.Summary?.MembersCount?.ToString()
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(team)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.Team,
                    team.Id,
                    team.DisplayName,
                    properties,
                    rawJson));

                if (objects.Count % 100 == 0)
                    context.Logger.LogInformation("Collected {Count} teams so far...", objects.Count);
            }

            context.Logger.LogInformation("Collected {Count} teams total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting teams: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting teams: {Message}", ex.Message);
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
                        "Retry attempt {Attempt} after {Delay} for Graph teams collection.",
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
