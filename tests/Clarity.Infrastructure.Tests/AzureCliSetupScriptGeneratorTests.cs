using Clarity.Infrastructure.Onboarding;
using Clarity.SharedContracts.Enums;
using FluentAssertions;

namespace Clarity.Infrastructure.Tests;

public sealed class AzureCliSetupScriptGeneratorTests
{
    [Fact]
    public void GenerateScript_IncludesTenantAppNameAndResolvedPermissions()
    {
        var generator = new AzureCliSetupScriptGenerator();

        var script = generator.GenerateScript(
            "tenant-id-123",
            "Clarity - Contoso",
            2,
            [WorkloadArea.EntraId, WorkloadArea.Intune]);

        script.Should().Contain("tenant-id-123");
        script.Should().Contain("Clarity - Contoso");
        script.Should().Contain("Organization.Read.All");
        script.Should().Contain("DeviceManagementManagedDevices.Read.All");
    }
}
