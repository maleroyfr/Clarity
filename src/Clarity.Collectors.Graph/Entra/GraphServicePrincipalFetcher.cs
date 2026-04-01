using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Entra;

public interface IGraphServicePrincipalFetcher
{
    Task<List<ServicePrincipal>> FetchAllServicePrincipalsAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphServicePrincipalFetcher : IGraphServicePrincipalFetcher
{
    private static readonly string[] SelectFields =
    [
        "id", "displayName", "appId", "servicePrincipalType",
        "accountEnabled", "appDisplayName"
    ];

    public async Task<List<ServicePrincipal>> FetchAllServicePrincipalsAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var principals = new List<ServicePrincipal>();

        var response = await client.ServicePrincipals.GetAsync(config =>
        {
            config.QueryParameters.Select = SelectFields;
            config.QueryParameters.Top = Math.Min(options.MaxPageSize, 999);
        }, ct);

        if (response is null)
            return principals;

        var pageIterator = PageIterator<ServicePrincipal, ServicePrincipalCollectionResponse>.CreatePageIterator(
            client,
            response,
            sp =>
            {
                principals.Add(sp);
                return true;
            });

        await pageIterator.IterateAsync(ct);

        return principals;
    }
}
