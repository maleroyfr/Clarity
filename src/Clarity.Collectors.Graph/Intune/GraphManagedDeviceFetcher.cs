using Clarity.Collectors.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Clarity.Collectors.Graph.Intune;

public interface IGraphManagedDeviceFetcher
{
    Task<List<ManagedDevice>> FetchAllManagedDevicesAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct);
}

public sealed class GraphManagedDeviceFetcher : IGraphManagedDeviceFetcher
{
    private static readonly string[] SelectFields =
    [
        "id", "deviceName", "operatingSystem", "complianceState",
        "managedDeviceOwnerType", "enrolledDateTime", "lastSyncDateTime",
        "serialNumber", "model", "manufacturer"
    ];

    public async Task<List<ManagedDevice>> FetchAllManagedDevicesAsync(
        GraphServiceClient client,
        CollectorOptions options,
        CancellationToken ct)
    {
        var devices = new List<ManagedDevice>();

        var response = await client.DeviceManagement.ManagedDevices.GetAsync(config =>
        {
            config.QueryParameters.Select = SelectFields;
            config.QueryParameters.Top = Math.Min(options.MaxPageSize, 999);
        }, ct);

        if (response is null)
            return devices;

        var pageIterator = PageIterator<ManagedDevice, ManagedDeviceCollectionResponse>.CreatePageIterator(
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
