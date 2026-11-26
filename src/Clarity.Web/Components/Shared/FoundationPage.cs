using System.Data.Common;
using Clarity.Contracts;
using Clarity.Core;
using Clarity.Infrastructure;
using Clarity.Web.Operations;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Clarity.Web.Components.Shared;

public abstract class FoundationPage : ComponentBase
{
    [Inject] protected ILogger<FoundationPage> Logger { get; set; } = default!;
    [Inject] protected IStringLocalizer<UiStrings> Messages { get; set; } = default!;
    protected string? Error { get; private set; }
    protected bool Busy { get; private set; }

    protected async Task RunAsync(Func<Task> action)
    {
        if (Busy) { return; }
        Busy = true;
        Error = null;
        using var activity = Telemetry.Activities.StartActivity("UI operation");
        using var scope = Logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
        });
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is AccessDeniedException or ArgumentException or ConflictException or DbException or DbUpdateException
            or ContractException or GraphReadException or DiscoveryConfigurationException or Azure.RequestFailedException
            or Azure.Identity.AuthenticationFailedException or IOException or HttpRequestException or OperationCanceledException)
        {
            Logger.LogWarning("UI operation failed with {ErrorType}", exception.GetType().Name);
            Error = Messages[exception switch
            {
                AccessDeniedException => "Denied",
                ArgumentException => "Invalid",
                ConflictException => "Conflict",
                ContractException contract => contract.Code is "EnvironmentOrScopeMismatch" or "EnvironmentMismatch" or "RequestMismatch" ? "PackageMismatch" : "InvalidPackage",
                GraphReadException => "GraphRemediation",
                DiscoveryConfigurationException => "StorageRequired",
                OperationCanceledException => "OperationInterrupted",
                _ => "Error"
            }];
        }
        finally
        {
            Busy = false;
        }
    }
}
