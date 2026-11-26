using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Clarity.Web.Operations;

public static class Telemetry
{
    public const string Name = "Clarity.Web";
    public static readonly ActivitySource Activities = new(Name);
    private static readonly Meter Meter = new(Name);
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("clarity.request.duration", "s");

    public static void AddClarityTelemetry(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
        // Framework diagnostics can contain URLs, SQL values and exception messages.
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            options.Rules.Clear();
            options.Rules.Add(new LoggerFilterRule(null, null, LogLevel.Information,
                (_, category, level) => category?.StartsWith("Clarity.", StringComparison.Ordinal) == true && level >= LogLevel.Information));
        });

        var connectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(Name))
            .WithTracing(tracing =>
            {
                tracing.AddSource(Name);
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    tracing.AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(Name);
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    metrics.AddAzureMonitorMetricExporter(options => options.ConnectionString = connectionString);
                }
            });
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeScopes = true;
                logging.AddAzureMonitorLogExporter(options => options.ConnectionString = connectionString);
            });
        }
    }

    public static async Task ObserveRequestAsync(HttpContext context, RequestDelegate next)
    {
        using var activity = Activities.StartActivity("HTTP request", ActivityKind.Server);
        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Name);
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var status = context.Response.StatusCode;
            var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
            activity?.SetTag("http.response.status_code", status);
            if (status >= 500) { activity?.SetStatus(ActivityStatusCode.Error); }
            RequestDuration.Record(elapsed, new KeyValuePair<string, object?>("http.response.status_code", status));
            logger.LogInformation("HTTP request completed with {StatusCode} in {ElapsedMilliseconds} ms",
                status, Math.Round(elapsed * 1000, 2));
        }
    }
}
