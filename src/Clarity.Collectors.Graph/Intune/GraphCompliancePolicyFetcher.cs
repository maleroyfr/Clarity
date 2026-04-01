using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Intune;

public interface IGraphCompliancePolicyFetcher
{
    Task<List<DeviceCompliancePolicy>> FetchAllCompliancePoliciesAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphCompliancePolicyFetcher : IGraphCompliancePolicyFetcher
{
    public async Task<List<DeviceCompliancePolicy>> FetchAllCompliancePoliciesAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var policies = new List<DeviceCompliancePolicy>();

        var response = await client.DeviceManagement.DeviceCompliancePolicies.GetAsync(config =>
        {
            config.QueryParameters.Top = Math.Min(options.MaxPageSize, 999);
        }, ct);

        if (response is null)
            return policies;

        var pageIterator = PageIterator<DeviceCompliancePolicy, DeviceCompliancePolicyCollectionResponse>.CreatePageIterator(
            client,
            response,
            policy =>
            {
                policies.Add(policy);
                return true;
            });

        await pageIterator.IterateAsync(ct);

        return policies;
    }
}
