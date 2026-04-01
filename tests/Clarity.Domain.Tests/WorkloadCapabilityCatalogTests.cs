using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using FluentAssertions;

namespace Clarity.Domain.Tests;

public sealed class WorkloadCapabilityCatalogTests
{
    [Fact]
    public void GetRequiredPermissions_MergesDistinctPermissionsAcrossWorkloads()
    {
        var permissions = WorkloadCapabilityCatalog.GetRequiredPermissions(
            [WorkloadArea.EntraId, WorkloadArea.Intune, WorkloadArea.EntraId]);

        permissions.Select(permission => permission.Name).Should().Contain([
            "Organization.Read.All",
            "User.Read.All",
            "DeviceManagementManagedDevices.Read.All"
        ]);
        permissions.Select(permission => permission.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildChecklistItems_ForExchangeOnline_IncludesPowerShellRuntimeAndModule()
    {
        var items = WorkloadCapabilityCatalog.BuildChecklistItems(WorkloadArea.ExchangeOnline);

        items.Should().Contain(item => item.Key == "tool:pwsh");
        items.Should().Contain(item => item.Key == "module:ExchangeOnlineManagement");
        items.Should().Contain(item => item.Key == "auth:exchange-manage-as-app");
    }
}
