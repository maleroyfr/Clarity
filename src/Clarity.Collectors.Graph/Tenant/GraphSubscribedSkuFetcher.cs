using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Tenant;

public interface IGraphSubscribedSkuFetcher
{
    Task<List<SubscribedSku>> FetchAllSubscribedSkusAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphSubscribedSkuFetcher : IGraphSubscribedSkuFetcher
{
    public async Task<List<SubscribedSku>> FetchAllSubscribedSkusAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var skus = new List<SubscribedSku>();

        var response = await client.SubscribedSkus.GetAsync(cancellationToken: ct);

        if (response?.Value is null)
            return skus;

        skus.AddRange(response.Value);

        return skus;
    }
}
