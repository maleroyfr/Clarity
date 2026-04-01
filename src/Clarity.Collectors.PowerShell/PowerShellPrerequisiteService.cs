using System.Diagnostics;
using System.Text;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using Microsoft.Extensions.Logging;

namespace Clarity.Collectors.PowerShell;

public interface IPwshRunner
{
    Task<PwshRunResult> RunAsync(string script, CancellationToken cancellationToken = default);
}

public sealed record PwshRunResult(int ExitCode, string Stdout, string Stderr);

public interface IPowerShellPrerequisiteService
{
    Task<PwshStatus> CheckPwshAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModuleStatus>> CheckModulesAsync(
        IEnumerable<WorkloadArea> enabledWorkloads,
        CancellationToken cancellationToken = default);

    Task<ModuleInstallResult> InstallModuleAsync(
        string moduleName,
        Version minimumVersion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModuleInstallResult>> InstallAllMissingAsync(
        IEnumerable<WorkloadArea> enabledWorkloads,
        CancellationToken cancellationToken = default);
}

public sealed class PwshRunner : IPwshRunner
{
    public async Task<PwshRunResult> RunAsync(string script, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = "-NoProfile -NonInteractive -Command -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.StandardInput.WriteAsync(script);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new PwshRunResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }
}

public sealed class PowerShellPrerequisiteService(
    IPwshRunner runner,
    ILogger<PowerShellPrerequisiteService> logger) : IPowerShellPrerequisiteService
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(3);

    public async Task<PwshStatus> CheckPwshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var result = await runner.RunAsync("$PSVersionTable.PSVersion.ToString()", cts.Token);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
            {
                return new PwshStatus(true, result.Stdout.Trim());
            }

            return new PwshStatus(false, null);
        }
        catch
        {
            return new PwshStatus(false, null);
        }
    }

    public async Task<IReadOnlyList<ModuleStatus>> CheckModulesAsync(
        IEnumerable<WorkloadArea> enabledWorkloads,
        CancellationToken cancellationToken = default)
    {
        var requirements = WorkloadCapabilityCatalog.GetRequiredModules(enabledWorkloads);
        if (requirements.Count == 0)
        {
            return [];
        }

        var results = new List<ModuleStatus>(requirements.Count);
        foreach (var requirement in requirements)
        {
            results.Add(await CheckModuleAsync(requirement, cancellationToken));
        }

        return results;
    }

    public async Task<ModuleInstallResult> InstallModuleAsync(
        string moduleName,
        Version minimumVersion,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Installing PowerShell module {Module} >= {Version}", moduleName, minimumVersion);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(InstallTimeout);

            var result = await runner.RunAsync(BuildInstallScript(moduleName, minimumVersion.ToString()), cts.Token);
            var output = ExtractResultLine(result.Stdout);

            if (output.StartsWith("OK|", StringComparison.Ordinal))
            {
                return new ModuleInstallResult(true, output[3..], null);
            }

            var error = output.StartsWith("FAIL|", StringComparison.Ordinal)
                ? output[5..]
                : $"Exit code {result.ExitCode}: {(string.IsNullOrWhiteSpace(result.Stderr) ? output : result.Stderr.Trim())}";

            return new ModuleInstallResult(false, null, error);
        }
        catch (OperationCanceledException)
        {
            return new ModuleInstallResult(false, null, "Installation timed out.");
        }
        catch (Exception ex)
        {
            return new ModuleInstallResult(false, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<ModuleInstallResult>> InstallAllMissingAsync(
        IEnumerable<WorkloadArea> enabledWorkloads,
        CancellationToken cancellationToken = default)
    {
        var statuses = await CheckModulesAsync(enabledWorkloads, cancellationToken);
        var results = new List<ModuleInstallResult>(statuses.Count);

        foreach (var status in statuses)
        {
            if (status.Installed)
            {
                results.Add(new ModuleInstallResult(true, status.InstalledVersion, null));
                continue;
            }

            results.Add(await InstallModuleAsync(status.ModuleName, status.MinimumVersion, cancellationToken));
        }

        return results;
    }

    private async Task<ModuleStatus> CheckModuleAsync(
        PowerShellModuleRequirement requirement,
        CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var result = await runner.RunAsync(
                BuildCheckScript(requirement.ModuleName, requirement.MinimumVersion.ToString()),
                cts.Token);

            var output = ExtractResultLine(result.Stdout);

            if (output.StartsWith("OK|", StringComparison.Ordinal))
            {
                return new ModuleStatus(
                    requirement.ModuleName,
                    requirement.MinimumVersion,
                    requirement.Purpose,
                    Installed: true,
                    InstalledVersion: output[3..],
                    NeedsUpgrade: false);
            }

            if (output.StartsWith("OLD|", StringComparison.Ordinal))
            {
                return new ModuleStatus(
                    requirement.ModuleName,
                    requirement.MinimumVersion,
                    requirement.Purpose,
                    Installed: false,
                    InstalledVersion: output[4..],
                    NeedsUpgrade: true);
            }

            return new ModuleStatus(
                requirement.ModuleName,
                requirement.MinimumVersion,
                requirement.Purpose,
                Installed: false,
                InstalledVersion: null,
                NeedsUpgrade: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check PowerShell module {Module}", requirement.ModuleName);
            return new ModuleStatus(
                requirement.ModuleName,
                requirement.MinimumVersion,
                requirement.Purpose,
                Installed: false,
                InstalledVersion: null,
                NeedsUpgrade: false,
                Error: ex.Message);
        }
    }

    private static string BuildCheckScript(string moduleName, string minVersion) =>
        $$"""
        $ProgressPreference = 'SilentlyContinue'
        $mod = Get-Module -ListAvailable -Name '{{moduleName}}' |
            Where-Object { $_.Version -ge [version]'{{minVersion}}' } |
            Sort-Object Version -Descending | Select-Object -First 1
        if ($mod) {
            Write-Output "OK|$($mod.Version)"
        } else {
            $any = Get-Module -ListAvailable -Name '{{moduleName}}' |
                Sort-Object Version -Descending | Select-Object -First 1
            if ($any) {
                Write-Output "OLD|$($any.Version)"
            } else {
                Write-Output "MISSING"
            }
        }
        """;

    private static string BuildInstallScript(string moduleName, string minVersion) =>
        $$"""
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        try {
            $nuget = Get-PackageProvider -Name NuGet -ListAvailable -ErrorAction SilentlyContinue |
                Where-Object { $_.Version -ge [version]'2.8.5.201' } | Select-Object -First 1
            if (-not $nuget) {
                Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser -ErrorAction Stop | Out-Null
            }

            $repo = Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue
            if ($repo -and $repo.InstallationPolicy -ne 'Trusted') {
                Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
            }

            Install-Module '{{moduleName}}' -Scope CurrentUser -MinimumVersion '{{minVersion}}' -Force -AllowClobber -ErrorAction Stop | Out-Null

            $mod = Get-Module -ListAvailable -Name '{{moduleName}}' |
                Where-Object { $_.Version -ge [version]'{{minVersion}}' } |
                Sort-Object Version -Descending | Select-Object -First 1
            if ($mod) {
                Write-Output "OK|$($mod.Version)"
            } else {
                Write-Output "FAIL|Module not found after install"
            }
        }
        catch {
            Write-Output "FAIL|$($_.Exception.Message)"
        }
        """;

    private static string ExtractResultLine(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return string.Empty;
        }

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (line.StartsWith("OK|", StringComparison.Ordinal) ||
                line.StartsWith("FAIL|", StringComparison.Ordinal) ||
                line.StartsWith("OLD|", StringComparison.Ordinal) ||
                line.Equals("MISSING", StringComparison.Ordinal))
            {
                return line;
            }
        }

        return stdout.Trim();
    }
}

public sealed record PwshStatus(bool Available, string? Version);

public sealed record ModuleStatus(
    string ModuleName,
    Version MinimumVersion,
    string Purpose,
    bool Installed,
    string? InstalledVersion,
    bool NeedsUpgrade,
    string? Error = null);

public sealed record ModuleInstallResult(bool Success, string? InstalledVersion, string? Error);
