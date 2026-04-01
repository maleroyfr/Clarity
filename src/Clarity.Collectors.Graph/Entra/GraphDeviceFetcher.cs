using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Entra;

public interface IGraphDeviceFetcher
{
    Task<List<Device>> FetchAllDevicesAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphDeviceFetcher : IGraphDeviceFetcher
{
    private static readonly string[] SelectFields =
    [
        "id", "displayName", "operatingSystem", "operatingSystemVersion",
        "trustType", "isManaged", "isCompliant", "registeredOwners",
        "approximateLastSignInDateTime"
    ];

    public async Task<List<Device>> FetchAllDevicesAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var devices = new List<Device>();

        var response = await client.Devices.GetAsync(config =>
        {
            config.QueryParameters.Select = SelectFields;
            config.QueryParameters.Top = Math.Min(options.MaxPageSize, 999);
        }, ct);

        if (response is null)
            return devices;

        var pageIterator = PageIterator<Device, DeviceCollectionResponse>.CreatePageIterator(
            client,
            response,
            device =>
            {
                devices.Add(device);
                return true;
            });

        await pageIterator.IterateAsync(ct);

        return devices;
    }
}
