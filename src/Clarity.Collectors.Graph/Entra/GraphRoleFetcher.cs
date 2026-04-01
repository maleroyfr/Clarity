using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Entra;

public interface IGraphRoleFetcher
{
    Task<List<DirectoryRole>> FetchAllRolesAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphRoleFetcher : IGraphRoleFetcher
{
    private static readonly string[] SelectFields =
    [
        "id", "displayName", "description", "roleTemplateId"
    ];

    public async Task<List<DirectoryRole>> FetchAllRolesAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var roles = new List<DirectoryRole>();

        var response = await client.DirectoryRoles.GetAsync(config =>
        {
            config.QueryParameters.Select = SelectFields;
        }, ct);

        if (response is null)
            return roles;

        var pageIterator = PageIterator<DirectoryRole, DirectoryRoleCollectionResponse>.CreatePageIterator(
            client,
            response,
            role =>
            {
                roles.Add(role);
                return true;
            });

        await pageIterator.IterateAsync(ct);

        return roles;
    }
}
