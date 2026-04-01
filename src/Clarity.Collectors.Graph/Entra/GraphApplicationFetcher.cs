using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Entra;

public interface IGraphApplicationFetcher
{
    Task<List<Application>> FetchAllApplicationsAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphApplicationFetcher : IGraphApplicationFetcher
{
    private static readonly string[] SelectFields =
    [
        "id", "displayName", "appId", "signInAudience",
        "createdDateTime", "keyCredentials", "passwordCredentials",
        "requiredResourceAccess"
    ];

    public async Task<List<Application>> FetchAllApplicationsAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var apps = new List<Application>();

        var response = await client.Applications.GetAsync(config =>
        {
            config.QueryParameters.Select = SelectFields;
            config.QueryParameters.Top = Math.Min(options.MaxPageSize, 999);
        }, ct);

        if (response is null)
            return apps;

        var pageIterator = PageIterator<Application, ApplicationCollectionResponse>.CreatePageIterator(
            client,
            response,
            app =>
            {
                apps.Add(app);
                return true;
            });

        await pageIterator.IterateAsync(ct);

        return apps;
    }
}
