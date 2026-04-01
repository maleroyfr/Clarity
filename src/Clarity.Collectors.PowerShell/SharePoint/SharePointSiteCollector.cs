using System.Text.Json;
using Clarity.Collectors.Contracts;
using Clarity.Collectors.PowerShell.Exchange;
using Clarity.Domain.Environments;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;
using CollectorError = Clarity.Collectors.Contracts.CollectorError;
using Microsoft.Extensions.Logging;

namespace Clarity.Collectors.PowerShell.SharePoint;

public sealed class SharePointSiteCollector : ICollector
{
    private readonly IPwshRunner _pwshRunner;

    public string CollectorId => "ps.sharepoint.sites";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.SharePointOnline;
    public CollectorType CollectorType => CollectorType.PowerShell;
    public IReadOnlyList<string> RequiredPermissions => ["Sites.Read.All"];

    public SharePointSiteCollector(IPwshRunner pwshRunner)
    {
        _pwshRunner = pwshRunner;
    }

    public async Task<CollectorResult> RunAsync(CollectorRunContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var executedCommands = new List<string>();

        try
        {
            var script = BuildScript(context, executedCommands);

            context.Logger.LogInformation("Executing SharePoint Online site collection via PowerShell...");

            var result = await _pwshRunner.RunAsync(script, context.CancellationToken);

            if (result.ExitCode != 0 || HasErrors(result.Stderr))
            {
                var errorMessage = !string.IsNullOrWhiteSpace(result.Stderr)
                    ? result.Stderr
                    : $"PowerShell exited with code {result.ExitCode}";

                context.Logger.LogError("SharePoint Online collection failed: {Error}", errorMessage);

                return CollectorResult.Failure(
                    new CollectorError("PS_SHAREPOINT_ERROR", errorMessage),
                    BuildMetadata(startedAt, DateTimeOffset.UtcNow, context),
                    executedCommands);
            }

            var stdout = result.Stdout.Trim();
            if (string.IsNullOrWhiteSpace(stdout))
            {
                context.Logger.LogWarning("SharePoint Online collection returned no data.");
                return CollectorResult.Success(
                    [],
                    RequiredPermissions,
                    BuildMetadata(startedAt, DateTimeOffset.UtcNow, context),
                    warnings: ["No sites returned from Get-PnPTenantSite."],
                    commands: executedCommands);
            }

            var sites = ParseJsonArray(stdout);
            var objects = new List<InventoryObject>(sites.Count);

            foreach (var site in sites)
            {
                var url = site.GetStringProperty("Url");
                var externalId = url ?? site.GetStringProperty("SiteId") ?? site.GetStringProperty("Title");

                if (externalId is null) continue;

                var displayName = site.GetStringProperty("Title");

                var properties = new Dictionary<string, string?>
                {
                    ["Url"] = url,
                    ["Title"] = displayName,
                    ["Template"] = site.GetStringProperty("Template"),
                    ["StorageUsageCurrent"] = site.GetStringProperty("StorageUsageCurrent"),
                    ["StorageQuota"] = site.GetStringProperty("StorageQuota"),
                    ["Owner"] = site.GetStringProperty("Owner"),
                    ["SharingCapability"] = site.GetStringProperty("SharingCapability"),
                    ["LastContentModifiedDate"] = site.GetStringProperty("LastContentModifiedDate"),
                    ["IsHubSite"] = site.GetStringProperty("IsHubSite"),
                };

                string? rawJson = context.Options.IncludeRawData
                    ? site.ToJsonString()
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.SharePointSite,
                    externalId,
                    displayName,
                    properties,
                    rawJson));

                if (objects.Count % 100 == 0)
                    context.Logger.LogInformation("Mapped {Count} SharePoint sites so far...", objects.Count);
            }

            context.Logger.LogInformation("Collected {Count} SharePoint sites total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context),
                commands: executedCommands,
                rawPayloadJson: context.Options.IncludeRawData ? stdout : null);
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting SharePoint sites: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("PS_SHAREPOINT_UNEXPECTED", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context),
                executedCommands);
        }
    }

    private static string BuildScript(CollectorRunContext context, List<string> executedCommands)
    {
        var auth = context.AuthConfig;
        var tenantDomain = context.Options.TenantDomain;

        // Derive the tenant name (e.g. "contoso") from the full domain "contoso.onmicrosoft.com"
        var tenantName = tenantDomain?.Split('.')[0] ?? "unknown";
        var adminUrl = $"https://{tenantName}-admin.sharepoint.com";

        string connectCommand;
        if (auth.AuthType == AuthType.WindowsIntegrated)
        {
            connectCommand = $"Connect-PnPOnline -Url '{adminUrl}' -Interactive";
        }
        else
        {
            connectCommand = $"Connect-PnPOnline -Url '{adminUrl}' " +
                             $"-ClientId '{auth.ClientId}' " +
                             $"-Thumbprint '{auth.CertificateThumbprint}' " +
                             $"-Tenant '{tenantDomain}'";
        }

        const string getSitesCommand = "Get-PnPTenantSite -Detailed | ConvertTo-Json -Depth 3 -Compress";
        const string disconnectCommand = "Disconnect-PnPOnline";

        executedCommands.Add(connectCommand);
        executedCommands.Add(getSitesCommand);
        executedCommands.Add(disconnectCommand);

        return $$"""
            $ErrorActionPreference = 'Stop'
            try {
                {{connectCommand}}
                {{getSitesCommand}}
            }
            finally {
                try { {{disconnectCommand}} } catch { }
            }
            """;
    }

    private static bool HasErrors(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return false;
        return stderr.Contains("error", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("exception", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }

    private static List<JsonElement> ParseJsonArray(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
        }

        return [doc.RootElement.Clone()];
    }

    private CollectorMetadata BuildMetadata(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CollectorRunContext context) =>
        new(CollectorId, Version, WorkloadArea, CollectorType,
            startedAt, completedAt, context.SnapshotId, context.EnvironmentId);
}
