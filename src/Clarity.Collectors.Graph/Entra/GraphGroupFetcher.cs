using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Entra;

public interface IGraphGroupFetcher
{
    Task<List<Group>> FetchAllGroupsAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphGroupFetcher : IGraphGroupFetcher
{
    private static readonly string[] SelectFields =
    [
        "id", "displayName", "groupTypes", "mailEnabled",
        "securityEnabled", "membershipRule", "createdDateTime",
        "description", "mail", "proxyAddresses"
    ];

    public async Task<List<Group>> FetchAllGroupsAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var groups = new List<Group>();

        var response = await client.Groups.GetAsync(config =>
        {
            config.QueryParameters.Select = SelectFields;
            config.QueryParameters.Top = Math.Min(options.MaxPageSize, 999);
        }, ct);

        if (response is null)
            return groups;

        var pageIterator = PageIterator<Group, GroupCollectionResponse>.CreatePageIterator(
            client,
            response,
            group =>
            {
                groups.Add(group);
                return true;
            });

        await pageIterator.IterateAsync(ct);

        return groups;
    }
}
