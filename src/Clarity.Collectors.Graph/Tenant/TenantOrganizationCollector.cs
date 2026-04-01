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

public sealed class TenantOrganizationCollector : ICollector
{
    private readonly IGraphClientFactory _clientFactory;
    private readonly IGraphOrganizationFetcher _organizationFetcher;

    public string CollectorId => "graph.tenant.organization";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.EntraId;
    public CollectorType CollectorType => CollectorType.Graph;
    public IReadOnlyList<string> RequiredPermissions => ["Organization.Read.All"];

    public TenantOrganizationCollector(IGraphClientFactory clientFactory, IGraphOrganizationFetcher organizationFetcher)
    {
        _clientFactory = clientFactory;
        _organizationFetcher = organizationFetcher;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = BuildResiliencePipeline(context.Options, context.Logger);

        try
        {
            var client = _clientFactory.Create(context.AuthConfig);
            List<Microsoft.Graph.Models.Organization> orgs = [];

            await pipeline.ExecuteAsync(async ct =>
            {
                orgs = await _organizationFetcher.FetchAllOrganizationsAsync(client, context.Options, ct);
            }, context.CancellationToken);

            var objects = new List<InventoryObject>(orgs.Count);

            foreach (var org in orgs)
            {
                if (org.Id is null) continue;

                var verifiedDomains = org.VerifiedDomains is not null
                    ? string.Join(", ", org.VerifiedDomains
                        .Where(d => d.Name is not null)
                        .Select(d => d.Name!))
                    : null;

                var properties = new Dictionary<string, string?>
                {
                    ["displayName"]      = org.DisplayName,
                    ["verifiedDomains"]  = verifiedDomains,
                    ["tenantType"]       = org.TenantType,
                    ["createdDateTime"]  = org.CreatedDateTime?.ToString("O"),
                    ["countryLetterCode"] = org.CountryLetterCode
                };

                string? rawJson = context.Options.IncludeRawData
                    ? JsonSerializer.Serialize(org)
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.TenantOrganization,
                    org.Id,
                    org.DisplayName,
                    properties,
                    rawJson));
            }

            context.Logger.LogInformation("Collected {Count} organization(s) total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (ODataError ex)
        {
            context.Logger.LogError(ex, "Graph ODataError collecting organization: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("GRAPH_ODATA_ERROR", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context));
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting organization: {Message}", ex.Message);
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
                        "Retry attempt {Attempt} after {Delay} for Graph organization collection.",
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
