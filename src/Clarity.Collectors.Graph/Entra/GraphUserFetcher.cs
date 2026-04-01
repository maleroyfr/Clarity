using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Entra;

public interface IGraphUserFetcher
{
    Task<List<User>> FetchAllUsersAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphUserFetcher : IGraphUserFetcher
{
    private static readonly string[] SelectFields =
    [
        "id", "displayName", "userPrincipalName", "mail",
        "accountEnabled", "userType", "jobTitle", "department",
        "officeLocation", "usageLocation", "createdDateTime",
        "signInActivity", "assignedLicenses", "assignedPlans"
    ];

    public async Task<List<User>> FetchAllUsersAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var users = new List<User>();

        var response = await client.Users.GetAsync(config =>
        {
            config.QueryParameters.Select = SelectFields;
            config.QueryParameters.Top = Math.Min(options.MaxPageSize, 999);
        }, ct);

        if (response is null)
            return users;

        var pageIterator = PageIterator<User, UserCollectionResponse>.CreatePageIterator(
            client,
            response,
            user =>
            {
                users.Add(user);
                return true;
            });

        await pageIterator.IterateAsync(ct);

        return users;
    }
}
