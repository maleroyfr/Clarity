using Clarity.Collectors.PowerShell;
using Clarity.SharedContracts.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clarity.Collectors.PowerShell.Tests;

public sealed class PowerShellPrerequisiteServiceTests
{
    [Fact]
    public async Task CheckPwshAsync_ReturnsAvailable_WhenPwshReportsVersion()
    {
        var runner = Substitute.For<IPwshRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PwshRunResult(0, "7.5.0", string.Empty));

        var service = new PowerShellPrerequisiteService(runner, NullLogger<PowerShellPrerequisiteService>.Instance);

        var result = await service.CheckPwshAsync();

        result.Available.Should().BeTrue();
        result.Version.Should().Be("7.5.0");
    }

    [Fact]
    public async Task CheckModulesAsync_ReturnsInstalledStatus_FromPwshOutput()
    {
        var runner = Substitute.For<IPwshRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var script = callInfo.ArgAt<string>(0);
                if (script.Contains("$PSVersionTable", StringComparison.Ordinal))
                {
                    return new PwshRunResult(0, "7.5.0", string.Empty);
                }

                return new PwshRunResult(0, "OK|3.7.1", string.Empty);
            });

        var service = new PowerShellPrerequisiteService(runner, NullLogger<PowerShellPrerequisiteService>.Instance);

        var result = await service.CheckModulesAsync([WorkloadArea.ExchangeOnline]);

        result.Should().ContainSingle();
        result[0].ModuleName.Should().Be("ExchangeOnlineManagement");
        result[0].Installed.Should().BeTrue();
        result[0].InstalledVersion.Should().Be("3.7.1");
    }
}
