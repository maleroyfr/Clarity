using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Clarity.Web.Operations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Clarity.Web.Tests;

public class OperationsTests
{
    [Fact]
    public async Task Unconfigured_dependencies_are_not_ready_but_host_is_live()
    {
        await using var application = new WebApplicationFactory<Program>();
        using var client = application.CreateClient();
        using var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await ready.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldBe("{\"status\":\"Unhealthy\",\"checks\":{\"sql\":\"Unhealthy\",\"blob\":\"Unhealthy\"}}");
    }

    [Fact]
    public async Task Request_telemetry_does_not_capture_sensitive_URLs_or_headers()
    {
        var captured = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = captured.Add
        };
        ActivitySource.AddActivityListener(listener);
        using var logs = new CapturedLogs();
        await using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "private-value");
        client.DefaultRequestHeaders.Add("Authorization", "Bearer private-value");
        using var response = await client.GetAsync("/health/live?token=private-value", TestContext.Current.CancellationToken);
        var correlation = response.Headers.GetValues("X-Correlation-ID").Single();
        correlation.Length.ShouldBe(32);
        var activity = captured.Single(item => item.TraceId.ToString() == correlation);
        activity.DisplayName.ShouldBe("HTTP request");
        activity.TagObjects.Select(tag => tag.Key).ShouldBe(["http.response.status_code"]);
        activity.Events.ShouldBeEmpty();
        logs.Messages.ShouldNotBeEmpty();
        string.Join("\n", logs.Messages).ShouldNotContain("private-value");
        string.Join("\n", logs.Messages).ShouldContain(correlation);
    }

    [Fact]
    public async Task Blob_readiness_requires_every_container_and_sanitizes_failures()
    {
        var service = Substitute.For<BlobServiceClient>();
        var container = Substitute.For<BlobContainerClient>();
        service.GetBlobContainerClient(Arg.Any<string>()).Returns(container);
        container.GetPropertiesAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(BlobsModelFactory.BlobContainerProperties(default, default), Substitute.For<Response>()));
        var check = new BlobReadinessCheck(service);
        (await check.CheckHealthAsync(new(), TestContext.Current.CancellationToken)).Status.ShouldBe(HealthStatus.Healthy);
        foreach (var name in new[] { "raw-artifacts", "exports", "script-packages" })
        {
            service.Received().GetBlobContainerClient(name);
        }
        container.GetPropertiesAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns<Task<Response<BlobContainerProperties>>>(_ => throw new RequestFailedException(403, "private-value"));
        var failed = await check.CheckHealthAsync(new(), TestContext.Current.CancellationToken);
        failed.Status.ShouldBe(HealthStatus.Unhealthy);
        failed.Description.ShouldBe("Blob Storage is unavailable.");
        failed.Exception.ShouldBeNull();
    }

    private sealed class CapturedLogs : ILoggerProvider, ISupportExternalScope
    {
        public ConcurrentBag<string> Messages { get; } = [];
        private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;
        public ILogger CreateLogger(string categoryName) => new CapturedLogger(this);
        public void Dispose() { }

        private sealed class CapturedLogger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner.scopes.Push(state);
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner.Messages.Add(formatter(state, exception));
                owner.scopes.ForEachScope((scope, messages) =>
                {
                    messages.Add(scope is IEnumerable<KeyValuePair<string, object>> values
                        ? string.Join(",", values.Select(pair => $"{pair.Key}={pair.Value}"))
                        : scope?.ToString() ?? "");
                }, owner.Messages);
            }
        }
    }
}
