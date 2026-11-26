using Clarity.Core;
using Microsoft.AspNetCore.Diagnostics;

namespace Clarity.Web.Services;

public sealed class SafeExceptionHandler(ILogger<SafeExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            AccessDeniedException => StatusCodes.Status403Forbidden,
            Clarity.Infrastructure.DiscoveryConfigurationException or Clarity.Infrastructure.GraphReadException =>
                StatusCodes.Status503ServiceUnavailable,
            ConflictException => StatusCodes.Status409Conflict,
            Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } } =>
                StatusCodes.Status409Conflict,
            ArgumentException or Clarity.Contracts.ContractException or Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        logger.LogWarning("Request failed with {ErrorType}; status {StatusCode}; correlation {CorrelationId}",
            exception.GetType().Name, status, context.TraceIdentifier);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { status, correlationId = context.TraceIdentifier }, cancellationToken);
        return true;
    }
}
