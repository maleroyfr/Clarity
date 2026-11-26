using Clarity.Contracts;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

public sealed class PowerShellContractTests
{
    [Fact]
    public async Task GeneratedPowerShellPackagesConformToZipV1()
    {
        var directory = Environment.GetEnvironmentVariable("CLARITY_SCRIPT_FIXTURES");
        if (string.IsNullOrEmpty(directory))
        {
            Assert.Skip("Set CLARITY_SCRIPT_FIXTURES and run Pester first; CI generates and validates real script output.");
        }
        Directory.Exists(directory).ShouldBeTrue();
        var files = Directory.GetFiles(directory, "*.zip");
        files.ShouldNotBeEmpty();
        var reader = new ZipPackageReader(new PackageValidator(), new PackageLimits());
        foreach (var file in files)
        {
            await using var stream = File.OpenRead(file);
            var package = await reader.ReadAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
            package.Package.Results.ShouldNotBeEmpty();
            DiscoveryCatalog.Load().ValidatePackage(package.Package);
        }
    }
}
