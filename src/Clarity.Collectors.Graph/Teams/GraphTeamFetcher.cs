using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Teams;

public interface IGraphTeamFetcher
{
    Task<List<Team>> FetchAllTeamsAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphTeamFetcher : IGraphTeamFetcher
{
    public async Task<List<Team>> FetchAllTeamsAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var teams = new List<Team>();

        var response = await client.Teams.GetAsync(config =>
        {
            config.QueryParameters.Top = Math.Min(options.MaxPageSize, 999);
        }, ct);

        if (response is null)
            return teams;

        var pageIterator = PageIterator<Team, TeamCollectionResponse>.CreatePageIterator(
            client,
            response,
            team =>
            {
                teams.Add(team);
                return true;
            });

        await pageIterator.IterateAsync(ct);

        return teams;
    }
}
