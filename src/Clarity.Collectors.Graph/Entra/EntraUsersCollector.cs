using Clarity.Collectors.Contracts;
using Clarity.Collectors.Graph.Auth;
using CollectorError = Clarity.Collectors.Contracts.CollectorError;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Polly;
using Polly.Retry;
using System.Text.Json;

namespace Clarity.Collectors.Graph.Entra;

public sealed class EntraUsersCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphUserFetcher _userFetcher;

    public string CollectorId => "graph.entra.users";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.EntraId;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["User.Read.All", "AuditLog.Read.All"];

    public EntraUsersCollector(IGraphClientFactory clientFactory, IGraphUserFetcher userFetcher)
    {
        _clientFactory = clientFactory;
        _userFetcher = userFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<Microsoft.Graph.Models.User> users = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                users = await _userFetcher.FetchAllUsersAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(users.Count);

            foreach (var user in users)
            {
                if (user.Id is null) continue;

                var properties = new Dictionary<string, string?>
                {
                    ["userPrincipalName"] = user.UserPrincipalName,
                    ["mail"]              = user.Mail,
                    ["accountEnabled"]    = user.AccountEnabled?.ToString(),
                    ["userType"]          = user.UserType,
                    ["jobTitle"]          = user.JobTitle,
                    ["department"]        = user.Department,
                    ["officeLocation"]    = user.OfficeLocation,
                    ["usageLocation"]     = user.UsageLocation,
                    ["createdDateTime"]   = user.CreatedDateTime?.ToString("O"),
                    ["licenseCount"]      = user.AssignedLicenses?.Count.ToString(),
                    ["assignedLicenses"]  = string.Join(",",
                        user.AssignedLicenses?.Select(l => l.SkuId?.ToString()) ?? [])
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(user)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.EntraUser,
                    user.Id,
                    user.DisplayName,
                    properties,
                    rawJson));

                if (objects.Count % 100 == 0)
                    context.Logger.LogInformation("Collected {Count} users so far...", objects.Count);
            }

            context.Logger.LogInformation("Collected {Count} users total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting users: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting users: {Message}", ex.Message);
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
                        "Retry attempt {Attempt} after {Delay} for Graph users collection.",
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
