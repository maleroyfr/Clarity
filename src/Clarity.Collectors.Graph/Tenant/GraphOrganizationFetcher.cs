using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Tenant;

public interface IGraphOrganizationFetcher
{
    Task<List<Organization>> FetchAllOrganizationsAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphOrganizationFetcher : IGraphOrganizationFetcher
{
    public async Task<List<Organization>> FetchAllOrganizationsAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var orgs = new List<Organization>();

        var response = await client.Organization.GetAsync(cancellationToken: ct);

        if (response?.Value is null)
            return orgs;

        orgs.AddRange(response.Value);

        return orgs;
    }
}
