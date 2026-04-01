using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Entra;

public interface IGraphConditionalAccessFetcher
{
    Task<List<ConditionalAccessPolicy>> FetchAllPoliciesAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphConditionalAccessFetcher : IGraphConditionalAccessFetcher
{
    public async Task<List<ConditionalAccessPolicy>> FetchAllPoliciesAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var policies = new List<ConditionalAccessPolicy>();

        var response = await client.Identity.ConditionalAccess.Policies.GetAsync(cancellationToken: ct);

        if (response?.Value is null)
            return policies;

        policies.AddRange(response.Value);

        return policies;
    }
}
