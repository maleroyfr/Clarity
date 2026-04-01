using System.Text.Json;
using Clarity.Collectors.Contracts;
using Clarity.Domain.Environments;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;
using CollectorError = Clarity.Collectors.Contracts.CollectorError;
using Microsoft.Extensions.Logging;

namespace Clarity.Collectors.PowerShell.Exchange;

public sealed class ExchangeOnlineCollector : ICollector
{
    private readonly IPwshRunner _pwshRunner;

    public string CollectorId => "ps.exchange.mailboxes";
    public string Version => "1.0.0";
    public WorkloadArea WorkloadArea => WorkloadArea.ExchangeOnline;
    public CollectorType CollectorType => CollectorType.PowerShell;
    public IReadOnlyList<string> RequiredPermissions => ["Exchange.ManageAsApp"];

    public ExchangeOnlineCollector(IPwshRunner pwshRunner)
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

            context.Logger.LogInformation("Executing Exchange Online mailbox collection via PowerShell...");

            var result = await _pwshRunner.RunAsync(script, context.CancellationToken);

            if (result.ExitCode != 0 || HasErrors(result.Stderr))
            {
                var errorMessage = !string.IsNullOrWhiteSpace(result.Stderr)
                    ? result.Stderr
                    : $"PowerShell exited with code {result.ExitCode}";

                context.Logger.LogError("Exchange Online collection failed: {Error}", errorMessage);

                return CollectorResult.Failure(
                    new CollectorError("PS_EXCHANGE_ERROR", errorMessage),
                    BuildMetadata(startedAt, DateTimeOffset.UtcNow, context),
                    executedCommands);
            }

            var stdout = result.Stdout.Trim();
            if (string.IsNullOrWhiteSpace(stdout))
            {
                context.Logger.LogWarning("Exchange Online collection returned no data.");
                return CollectorResult.Success(
                    [],
                    RequiredPermissions,
                    BuildMetadata(startedAt, DateTimeOffset.UtcNow, context),
                    warnings: ["No mailboxes returned from Get-EXOMailbox."],
                    commands: executedCommands);
            }

            var mailboxes = ParseJsonArray(stdout);
            var objects = new List<InventoryObject>(mailboxes.Count);

            foreach (var mailbox in mailboxes)
            {
                var externalId = mailbox.GetStringProperty("ExternalDirectoryObjectId")
                                 ?? mailbox.GetStringProperty("Guid")
                                 ?? mailbox.GetStringProperty("PrimarySmtpAddress");

                if (externalId is null) continue;

                var displayName = mailbox.GetStringProperty("DisplayName");

                var properties = new Dictionary<string, string?>
                {
                    ["PrimarySmtpAddress"] = mailbox.GetStringProperty("PrimarySmtpAddress"),
                    ["RecipientTypeDetails"] = mailbox.GetStringProperty("RecipientTypeDetails"),
                    ["IsMailboxEnabled"] = mailbox.GetStringProperty("IsMailboxEnabled"),
                    ["ItemCount"] = mailbox.GetStringProperty("ItemCount"),
                    ["TotalItemSize"] = mailbox.GetStringProperty("TotalItemSize"),
                    ["WhenCreated"] = mailbox.GetStringProperty("WhenCreated"),
                    ["Database"] = mailbox.GetStringProperty("Database"),
                    ["RetentionPolicy"] = mailbox.GetStringProperty("RetentionPolicy"),
                };

                string? rawJson = context.Options.IncludeRawData
                    ? mailbox.ToJsonString()
                    : null;

                objects.Add(InventoryObject.Create(
                    context.SnapshotId,
                    context.CollectorRunId,
                    InventoryObjectType.Mailbox,
                    externalId,
                    displayName,
                    properties,
                    rawJson));

                if (objects.Count % 100 == 0)
                    context.Logger.LogInformation("Mapped {Count} mailboxes so far...", objects.Count);
            }

            context.Logger.LogInformation("Collected {Count} mailboxes total.", objects.Count);

            return CollectorResult.Success(
                objects,
                RequiredPermissions,
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context),
                commands: executedCommands,
                rawPayloadJson: context.Options.IncludeRawData ? stdout : null);
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Unexpected error collecting Exchange Online mailboxes: {Message}", ex.Message);
            return CollectorResult.Failure(
                new CollectorError("PS_EXCHANGE_UNEXPECTED", ex.Message, ex.ToString()),
                BuildMetadata(startedAt, DateTimeOffset.UtcNow, context),
                executedCommands);
        }
    }

    private static string BuildScript(CollectorRunContext context, List<string> executedCommands)
    {
        var auth = context.AuthConfig;
        var tenantDomain = context.Options.TenantDomain;

        string connectCommand;
        if (auth.AuthType == AuthType.WindowsIntegrated)
        {
            connectCommand = "Connect-ExchangeOnline -ShowBanner:$false";
        }
        else
        {
            connectCommand = $"Connect-ExchangeOnline -AppId '{auth.ClientId}' " +
                             $"-CertificateThumbprint '{auth.CertificateThumbprint}' " +
                             $"-Organization '{tenantDomain}' -ShowBanner:$false";
        }

        const string getMailboxCommand = "Get-EXOMailbox -ResultSize Unlimited -PropertySets All | ConvertTo-Json -Depth 3 -Compress";
        const string disconnectCommand = "Disconnect-ExchangeOnline -Confirm:$false";

        executedCommands.Add(connectCommand);
        executedCommands.Add(getMailboxCommand);
        executedCommands.Add(disconnectCommand);

        return $$"""
            $ErrorActionPreference = 'Stop'
            try {
                {{connectCommand}}
                {{getMailboxCommand}}
            }
            finally {
                try { {{disconnectCommand}} } catch { }
            }
            """;
    }

    private static bool HasErrors(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return false;
        // PowerShell may emit non-critical warnings to stderr; only treat actual errors
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

        // Single object returned (PowerShell doesn't wrap single items in an array)
        return [doc.RootElement.Clone()];
    }

    private CollectorMetadata BuildMetadata(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CollectorRunContext context) =>
        new(CollectorId, Version, WorkloadArea, CollectorType,
            startedAt, completedAt, context.SnapshotId, context.EnvironmentId);
}

internal static class JsonElementExtensions
{
    public static string? GetStringProperty(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                JsonValueKind.Null => null,
                _ => value.GetRawText(),
            };
        }

        return null;
    }

    public static string ToJsonString(this JsonElement element)
    {
        return element.GetRawText();
    }
}
