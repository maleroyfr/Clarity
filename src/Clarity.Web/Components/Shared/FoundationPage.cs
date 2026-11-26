using System.Data.Common;
using Clarity.Core;
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
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is AccessDeniedException or ArgumentException or ConflictException or DbException or DbUpdateException)
        {
            Logger.LogWarning("UI operation failed with {ErrorType}", exception.GetType().Name);
            Error = Messages[exception switch
            {
                AccessDeniedException => "Denied",
                ArgumentException => "Invalid",
                ConflictException => "Conflict",
                _ => "Error"
            }];
        }
        finally
        {
            Busy = false;
        }
    }
}
