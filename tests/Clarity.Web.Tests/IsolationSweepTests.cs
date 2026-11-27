using System.Collections;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clarity.Contracts;
using Clarity.Core;
using Clarity.Infrastructure;
using Clarity.Web.Security;
using Clarity.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using ComponentRoute = Microsoft.AspNetCore.Components.RouteAttribute;

namespace Clarity.Web.Tests;

/// <summary>
/// Lot 6 isolation audit (recette R09): sweeps every API route, every routable page and every service a page can call
/// from a Blazor circuit with identifiers guessed or copied from another client workspace.
/// </summary>
public sealed partial class IsolationSweepTests
{
    private const string Discovery = "/api/projects/{projectId:guid}/discovery";
    private const string Exports = "/api/projects/{projectId:guid}/exports";
    private static readonly Guid Alpha = DevelopmentAuthenticationHandler.AlphaWorkspace;
    private static readonly Guid Beta = DevelopmentAuthenticationHandler.BetaWorkspace;
    private static readonly JsonSerializerOptions RenderOptions = new(ContractJson.Options) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

    private static readonly RouteCase[] Routes =
    [
        new("GET", "/api/antiforgery"),
        new("GET", "/api/workspaces"),
        new("POST", "/api/workspaces", _ => JsonContent.Create(new WorkspaceInput(Guid.NewGuid(), "Synthetic hostile client"))),
        new("POST", "/api/workspaces/{workspaceId:guid}/archive"),
        new("GET", "/api/workspaces/{workspaceId:guid}/projects"),
        new("POST", "/api/workspaces/{workspaceId:guid}/projects",
            _ => JsonContent.Create(new ProjectInput(Guid.NewGuid(), "Synthetic hostile project", ProjectScenario.AuditOnly, ""))),
        new("POST", "/api/workspaces/{workspaceId:guid}/members", _ => JsonContent.Create(new MemberInput(
            DevelopmentAuthenticationHandler.SyntheticTenant, DevelopmentAuthenticationHandler.Personas["manager"], WorkspaceRole.WorkspaceManager))),
        new("GET", "/api/projects/{projectId:guid}"),
        new("POST", "/api/projects/{projectId:guid}/archive"),
        new("GET", "/api/projects/{projectId:guid}/environments"),
        new("POST", "/api/projects/{projectId:guid}/environments", _ => JsonContent.Create(new EnvironmentInput(Guid.NewGuid(), EnvironmentRole.Source,
            EnvironmentKind.AdForest, "hostile.example.test", "", TargetState.Existing, ""))),
        new("GET", "/api/projects/{projectId:guid}/scenario"),
        new("GET", $"{Discovery}/{{environmentId:guid}}/runs"),
        new("GET", $"{Discovery}/{{environmentId:guid}}/coverage"),
        new("GET", $"{Discovery}/{{environmentId:guid}}/requests"),
        new("GET", $"{Discovery}/{{environmentId:guid}}/connection", Tenant: true),
        new("POST", $"{Discovery}/{{environmentId:guid}}/connection", _ => JsonContent.Create(new ConnectionInput("Synthetic hostile connection")), Tenant: true),
        new("POST", $"{Discovery}/{{environmentId:guid}}/connection/{{connectionId:guid}}/test", Tenant: true),
        new("POST", $"{Discovery}/{{environmentId:guid}}/graph", ids => JsonContent.Create(new GraphInput(Guid.NewGuid(), ids["connectionId"],
            new("tenant", "synthetic"), ["entra.users"])), Tenant: true, BodySlots: ["connectionId"]),
        new("POST", $"{Discovery}/{{environmentId:guid}}/requests",
            _ => JsonContent.Create(new ScriptInput(Guid.NewGuid(), new("domain", "example.test"), ["ad.users"], null))),
        new("POST", $"{Discovery}/{{environmentId:guid}}/upload", _ => new ByteArrayContent([1, 2])),
        new("POST", $"{Discovery}/{{environmentId:guid}}/reports/{{profileId}}/{{version}}", _ => new ByteArrayContent([1, 2]),
            "?scopeKind=tenant&scopeValue=synthetic&attested=true", Tenant: true),
        new("GET", $"{Discovery}/runs/{{runId:guid}}/preview"),
        new("POST", $"{Discovery}/runs/{{runId:guid}}/confirm"),
        new("GET", $"{Discovery}/runs/{{runId:guid}}/download"),
        new("GET", $"{Discovery}/requests/{{requestId:guid}}/parameters"),
        new("GET", $"{Discovery}/requests/{{requestId:guid}}/bundle"),
        new("GET", $"{Discovery}/jobs"),
        new("POST", $"{Discovery}/jobs/{{jobId:guid}}/cancel"),
        new("POST", $"{Discovery}/jobs/{{jobId:guid}}/retry"),
        new("GET", Exports),
        new("POST", Exports, ids => JsonContent.Create(new ExportRequest(Guid.NewGuid(),
            new ExportFilter(EnvironmentRole.Source, EnvironmentId: ids["environmentId"]))), BodySlots: ["environmentId"]),
        new("GET", $"{Exports}/{{exportId:guid}}/artifacts/{{artifactId:guid}}")
    ];

    private static readonly string[] Pages =
        ["/", "/Error", "/clients/{WorkspaceId:guid}/projects", "/not-found", "/projects/{ProjectId:guid}", "/projects/{ProjectId:guid}/{Section}"];

    private static readonly (string Section, string? View)[] Views =
    [
        ("overview", null), ("discovery", null), ("discovery", "Connections"), ("discovery", "Collections"), ("discovery", "Coverage"),
        ("inventory", null), ("inventory", "Mappings"), ("inventory", "PendingLinks"), ("scope", null), ("scope", "decisions"),
        ("scope", "dependencies"), ("scope", "review"), ("scope", "import"), ("comparison", null), ("comparison", "target"),
        ("exports", null), ("unknown", null)
    ];

    [Fact]
    [Trait("Recette", "R09")]
    public async Task Every_route_page_and_circuit_service_denies_identifiers_from_another_workspace()
    {
        await using var app = new FoundationApplication(discoveryStorage: true);
        var (alpha, beta) = await app.SeedAsync();
        var token = TestContext.Current.CancellationToken;
        using var manager = await app.CreatePersonaClientAsync("manager");
        DiscoverApi(app).ShouldBe(Routes.Select(x => x.Key).Order(StringComparer.Ordinal), ignoreOrder: false);
        typeof(Program).Assembly.GetTypes().Where(x => typeof(IComponent).IsAssignableFrom(x))
            .SelectMany(x => x.GetCustomAttributes<ComponentRoute>()).Select(x => x.Template).Order(StringComparer.Ordinal)
            .ShouldBe(Pages.Order(StringComparer.Ordinal), ignoreOrder: false);
        var fingerprint = new Fingerprint(["Synthetic Beta", "Beta audit"], [beta, Beta], []);
        var violations = new List<string>();

        var hostile = Ids(beta, Beta, _ => Guid.NewGuid());
        foreach (var route in Routes)
        {
            var (status, body, request) = await SendAsync(manager, route, hostile, token);
            var expected = route.Key switch
            {
                "GET /api/antiforgery" or "GET /api/workspaces" => HttpStatusCode.OK,
                _ => HttpStatusCode.Forbidden
            };
            if (status != expected) { violations.Add($"manager→beta {route.Key}: {(int)status}"); }
            if (fingerprint.Find(body, request) is { } leak) { violations.Add($"manager→beta {route.Key}: leaked {leak}"); }
        }

        using var reader = await app.CreatePersonaClientAsync("reader");
        var own = Ids(alpha, Alpha, _ => Guid.NewGuid());
        foreach (var route in Routes.Where(x => x.Method == "POST"))
        {
            var (status, _, _) = await SendAsync(reader, route, own, token);
            if (status != HttpStatusCode.Forbidden) { violations.Add($"reader→alpha {route.Key}: {(int)status}"); }
        }

        using var admin = await app.CreatePersonaClientAsync("platform-admin");
        foreach (var route in Routes.Where(x => x.Pattern.Contains("{projectId", StringComparison.Ordinal)
            || x.Pattern.EndsWith("/projects", StringComparison.Ordinal)))
        {
            var (status, _, _) = await SendAsync(admin, route, own, token);
            if (status != HttpStatusCode.Forbidden) { violations.Add($"platform-admin→alpha {route.Key}: {(int)status}"); }
        }

        var sweep = await SweepCircuitServicesAsync(app, "manager",
            name => Child(name) is null ? Guid.NewGuid() : name.Contains("project", StringComparison.OrdinalIgnoreCase) ? beta : Beta,
            alphaRetry: name => name.Contains("project", StringComparison.OrdinalIgnoreCase) ? alpha : Alpha, fingerprint, token);
        violations.AddRange(sweep.Violations);
        sweep.Swept.ShouldBeGreaterThanOrEqualTo(70);

        foreach (var path in PagePaths(beta).Append($"/clients/{Beta}/projects").Append("/"))
        {
            using var response = await manager.GetAsync(path, token);
            var body = await response.Content.ReadAsByteArrayAsync(token);
            if ((int)response.StatusCode >= 500) { violations.Add($"page {path}: {(int)response.StatusCode}"); }
            if (fingerprint.Find(body, path) is { } leak) { violations.Add($"page {path}: leaked {leak}"); }
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Recette", "R09")]
    public async Task Sql_populated_workspaces_resist_guessed_and_copied_identifiers_and_keep_rls_audit_and_storage_intact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLARITY_TEST_SQL")))
        {
            Assert.Skip("SQL integration test skipped: set CLARITY_TEST_SQL to a disposable SQL Server (provided by CI).");
        }
        await using var app = new FoundationApplication(sql: true, discoveryStorage: true);
        var (alphaProject, betaProject) = await app.SeedAsync();
        var token = TestContext.Current.CancellationToken;
        await using (var db = app.CreateUnscopedContext())
        {
            db.Members.Add(new WorkspaceMember
            {
                WorkspaceId = Beta,
                Role = WorkspaceRole.WorkspaceManager,
                TenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
                ObjectId = DevelopmentAuthenticationHandler.Personas["beta-manager"]
            });
            await db.SaveChangesAsync(token);
        }
        using var manager = await app.CreatePersonaClientAsync("manager");
        using var betaManager = await app.CreatePersonaClientAsync("beta-manager");
        var alpha = await SeedWorkspaceAsync(app, manager, Alpha, alphaProject, "alpha", token);
        var beta = await SeedWorkspaceAsync(app, betaManager, Beta, betaProject, "beta", token);
        var artifacts = (TestArtifactStore)app.Services.GetRequiredService<IArtifactStore>();
        var exports = (TestExportStore)app.Services.GetRequiredService<IExportStore>();
        var stores = (artifacts.Count, exports.Count);
        stores.Item1.ShouldBeGreaterThanOrEqualTo(2);
        stores.Item2.ShouldBe(2);

        var (betaRows, betaIds) = await SnapshotAsync(app, Beta, token);
        var (_, alphaIds) = await SnapshotAsync(app, Alpha, token);
        var shared = new HashSet<Guid>(alphaIds) { Guid.Empty, DevelopmentAuthenticationHandler.SyntheticTenant };
        shared.UnionWith(DevelopmentAuthenticationHandler.Personas.Values);
        var fingerprint = beta.Fingerprint(betaIds.Where(x => !shared.Contains(x)));
        var alphaFingerprint = alpha.Fingerprint([]);
        var violations = new List<string>();

        foreach (var route in Routes.Where(x => x.Method == "GET" && x.Pattern.Contains("{projectId", StringComparison.Ordinal)))
        {
            var (status, body, request) = await SendAsync(manager, route, alpha.Ids(route), token);
            status.ShouldBe(HttpStatusCode.OK, route.Key);
            fingerprint.Find(body, request).ShouldBeNull(route.Key);
        }
        var environments = await manager.GetByteArrayAsync($"/api/projects/{alphaProject}/environments", token);
        alphaFingerprint.Find(environments, "").ShouldNotBeNull();
        var download = await manager.GetByteArrayAsync($"/api/projects/{alphaProject}/discovery/runs/{alpha.Run}/download", token);
        alphaFingerprint.Find(download, "").ShouldBe("payload");
        var auditBefore = await AuditIdsAsync(app, token);

        foreach (var route in Routes)
        {
            var (status, body, request) = await SendAsync(manager, route, beta.Ids(route), token);
            var expected = route.Key is "GET /api/antiforgery" or "GET /api/workspaces" ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            if (status != expected) { violations.Add($"A {route.Key}: {(int)status}"); }
            if (fingerprint.Find(body, request) is { } leak) { violations.Add($"A {route.Key}: leaked {leak}"); }
        }
        foreach (var route in Routes.Where(x => x.Slots.Length > 0))
        {
            var variants = route.Slots.Select(x => new[] { x }).Append(route.Slots).DistinctBy(x => string.Join(",", x));
            foreach (var slots in variants)
            {
                var ids = alpha.Ids(route);
                foreach (var slot in slots) { ids[slot] = beta.Ids(route)[slot]; }
                await HostileAsync($"B[{string.Join(",", slots)}]", route, ids);
            }
            var random = alpha.Ids(route);
            foreach (var slot in route.Slots) { random[slot] = Guid.NewGuid(); }
            await HostileAsync("C", route, random);
        }

        foreach (var path in new[]
        {
            $"/projects/{alphaProject}/inventory?object={beta.Object}", $"/projects/{alphaProject}/inventory?population=true&object={beta.Object}",
            $"/projects/{alphaProject}/inventory?run={beta.Run}", $"/projects/{alphaProject}/inventory?environment={beta.Environment}",
            $"/projects/{alphaProject}/inventory?sectionId={beta.Object}", $"/projects/{alphaProject}/inventory?view=Mappings&object={beta.Object}",
            $"/clients/{Beta}/projects", $"/projects/{betaProject}"
        }.Concat(PagePaths(betaProject)))
        {
            using var response = await manager.GetAsync(path, token);
            var body = await response.Content.ReadAsByteArrayAsync(token);
            if ((int)response.StatusCode >= 500) { violations.Add($"page {path}: {(int)response.StatusCode}"); }
            if (fingerprint.Find(body, path) is { } leak) { violations.Add($"page {path}: leaked {leak}"); }
        }

        var sweep = await SweepCircuitServicesAsync(app, "manager", name => Child(name) switch
        {
            null when name.Contains("project", StringComparison.OrdinalIgnoreCase) => alphaProject,
            null when name.Contains("workspace", StringComparison.OrdinalIgnoreCase) => Alpha,
            null => Guid.NewGuid(),
            "environment" => beta.Environment,
            "connection" => beta.Connection,
            "run" => beta.Run,
            "request" => beta.Request,
            "export" => beta.Export,
            "artifact" => beta.Artifact,
            _ => beta.Object
        }, alphaRetry: null, fingerprint, token);
        violations.AddRange(sweep.Violations);
        sweep.Swept.ShouldBeGreaterThan(0);

        await using (var db = app.CreateUnscopedContext())
        {
            var added = await db.AuditEvents.Where(x => !auditBefore.Contains(x.Id)).ToListAsync(token);
            foreach (var audit in added)
            {
                if (audit.WorkspaceId != Alpha || string.IsNullOrWhiteSpace(audit.Action) || string.IsNullOrWhiteSpace(audit.CorrelationId)
                    || audit.ActorTenantId != DevelopmentAuthenticationHandler.SyntheticTenant
                    || audit.ActorObjectId != DevelopmentAuthenticationHandler.Personas["manager"])
                {
                    violations.Add($"audit {audit.Action} in {audit.WorkspaceId} by {audit.ActorObjectId}");
                }
            }
        }

        var collision = await manager.PostAsJsonAsync($"/api/workspaces/{Alpha}/projects",
            new ProjectInput(betaProject, "Synthetic collision", ProjectScenario.AuditOnly, ""), token);
        collision.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        fingerprint.Find(await collision.Content.ReadAsByteArrayAsync(token), betaProject.ToString()).ShouldBeNull();

        violations.ShouldBeEmpty();
        (await SnapshotAsync(app, Beta, token)).Rows.ShouldBe(betaRows);
        (artifacts.Count, exports.Count).ShouldBe(stores);

        await using (var scoped = app.CreateScopedContext(Alpha))
        await using (var nobody = app.CreateScopedContext())
        await using (var unscoped = app.CreateUnscopedContext())
        {
            var tables = await WorkspaceTablesAsync(unscoped, token);
            tables.Count.ShouldBeGreaterThan(30);
            long foreign = 0, visible = 0, betaVisible = 0;
            foreach (var (table, column) in tables)
            {
                foreign += await ScalarAsync(scoped, $"SELECT COUNT_BIG(*) FROM [dbo].[{table}] WHERE [{column}] <> @w", Alpha, token);
                visible += await ScalarAsync(nobody, $"SELECT COUNT_BIG(*) FROM [dbo].[{table}]", null, token);
                betaVisible += await ScalarAsync(unscoped, $"SELECT COUNT_BIG(*) FROM [dbo].[{table}] WHERE [{column}] = @w", Beta, token);
            }
            foreign.ShouldBe(0);
            visible.ShouldBe(0);
            betaVisible.ShouldBeGreaterThan(10);
        }

        async Task HostileAsync(string phase, RouteCase route, Dictionary<string, Guid> ids)
        {
            var (status, body, request) = await SendAsync(manager, route, ids, token);
            var allowed = route.Method == "GET" ? new[] { 200, 400, 403, 404 } : [400, 403, 404, 409];
            if (!allowed.Contains((int)status)) { violations.Add($"{phase} {route.Key}: {(int)status}"); }
            if (fingerprint.Find(body, request) is { } leak) { violations.Add($"{phase} {route.Key}: leaked {leak}"); }
        }
    }

    private static IEnumerable<string> PagePaths(Guid project) =>
        Views.SelectMany(x => new[] { "Source", "Target" }.Select(side =>
            $"/projects/{project}/{x.Section}?side={side}" + (x.View is null ? "" : $"&view={x.View}")));

    private static string[] DiscoverApi(FoundationApplication app) =>
        [.. app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(x => x.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .SelectMany(x => (x.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Select(method => $"{method} {x.RoutePattern.RawText!.TrimEnd('/')}"))
            .Order(StringComparer.Ordinal)];

    private static Dictionary<string, Guid> Ids(Guid project, Guid workspace, Func<string, Guid> child) => new(StringComparer.Ordinal)
    {
        ["projectId"] = project,
        ["workspaceId"] = workspace,
        ["environmentId"] = child("environmentId"),
        ["connectionId"] = child("connectionId"),
        ["runId"] = child("runId"),
        ["requestId"] = child("requestId"),
        ["jobId"] = child("jobId"),
        ["exportId"] = child("exportId"),
        ["artifactId"] = child("artifactId")
    };

    private static async Task<(HttpStatusCode Status, byte[] Body, string Request)> SendAsync(HttpClient client, RouteCase route,
        IReadOnlyDictionary<string, Guid> ids, CancellationToken token)
    {
        var url = RouteParameter().Replace(route.Pattern, match => match.Groups[1].Value switch
        {
            "profileId" => "m365.mailboxUsageDetail",
            "version" => "1.0.0",
            var name => ids[name].ToString()
        }) + route.Query;
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), url) { Content = route.Body?.Invoke(ids) };
        var requestText = url + (request.Content is null ? "" : await request.Content.ReadAsStringAsync(token));
        using var response = await client.SendAsync(request, token);
        return (response.StatusCode, await response.Content.ReadAsByteArrayAsync(token), requestText);
    }

    /// <summary>Maps a Guid parameter name to the kind of child resource it designates, or null for project, workspace and fresh identifiers.</summary>
    private static string? Child(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower switch
        {
            _ when lower.Contains("project", StringComparison.Ordinal) || lower.Contains("workspace", StringComparison.Ordinal)
                || lower.Contains("operation", StringComparison.Ordinal) || lower.Contains("tenant", StringComparison.Ordinal) => null,
            _ when lower.Contains("environment", StringComparison.Ordinal) => "environment",
            _ when lower.Contains("connection", StringComparison.Ordinal) => "connection",
            _ when lower.Contains("run", StringComparison.Ordinal) || lower.Contains("job", StringComparison.Ordinal) => "run",
            _ when lower.Contains("request", StringComparison.Ordinal) => "request",
            _ when lower.Contains("export", StringComparison.Ordinal) => "export",
            _ when lower.Contains("artifact", StringComparison.Ordinal) => "artifact",
            _ => "object"
        };
    }

    /// <summary>
    /// Invokes, as a circuit would, every public asynchronous method that takes a project or workspace identifier on every
    /// Clarity service injected into a routable component. A call that completes is checked for foreign data; with
    /// <paramref name="alphaRetry"/>, every call must be denied or fail identically for the caller's own workspace.
    /// </summary>
    private static async Task<SweepResult> SweepCircuitServicesAsync(FoundationApplication app, string persona, Func<string, Guid> guidFor,
        Func<string, Guid>? alphaRetry, Fingerprint fingerprint, CancellationToken token)
    {
        var services = typeof(Program).Assembly.GetTypes()
            .Where(x => typeof(IComponent).IsAssignableFrom(x) && !x.ContainsGenericParameters)
            .SelectMany(x => x.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(x => x.GetCustomAttribute<InjectAttribute>() is not null)
            .Select(x => x.PropertyType)
            .Where(x => x.IsClass && x.Namespace?.StartsWith("Clarity.", StringComparison.Ordinal) == true)
            .Distinct().OrderBy(x => x.FullName, StringComparer.Ordinal).ToArray();
        services.Length.ShouldBeGreaterThan(15);
        var output = TestContext.Current.TestOutputHelper;
        var violations = new List<string>();
        var swept = 0;
        foreach (var service in services)
        {
            foreach (var method in service.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(x => !x.IsSpecialName && !x.IsGenericMethodDefinition && IsAsync(x.ReturnType)
                    && x.GetParameters().Any(p => p.ParameterType == typeof(Guid) && p.Name is "projectId" or "workspaceId"))
                .OrderBy(x => x.ToString(), StringComparer.Ordinal))
            {
                var name = $"{service.Name}.{method.Name}({string.Join(",", method.GetParameters().Select(x => x.Name))})";
                var cache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                var children = 0;
                Guid Hostile(string parameter)
                {
                    if (Child(parameter) is not null) { children++; }
                    return cache.TryGetValue(parameter, out var known) ? known : cache[parameter] = guidFor(parameter);
                }
                var arguments = new Sampler(Hostile, token).Arguments(method);
                if (alphaRetry is null && children == 0)
                {
                    output?.WriteLine($"{name}: skipped (no child identifier)");
                    continue;
                }
                swept++;
                var outcome = await InvokeAsync(app, persona, service, method, arguments);
                var last = false;
                if (outcome.Error is ArgumentException)
                {
                    output?.WriteLine($"{name}: first sample {outcome.Describe()}, retrying with the last enumeration values");
                    last = true;
                    outcome = await InvokeAsync(app, persona, service, method, new Sampler(Hostile, token, last).Arguments(method));
                }
                output?.WriteLine($"{name}: {outcome.Describe()}");
                if (outcome.Error is null)
                {
                    if (alphaRetry is not null) { violations.Add($"circuit {name}: completed for another workspace"); }
                    else if (fingerprint.Find(outcome.Rendered, string.Join("|", cache.Values)) is { } leak) { violations.Add($"circuit {name}: leaked {leak}"); }
                    continue;
                }
                if (alphaRetry is null || outcome.Error is AccessDeniedException) { continue; }
                var own = new Sampler(parameter => Child(parameter) is null && !parameter.Contains("operation", StringComparison.OrdinalIgnoreCase)
                    && !parameter.Contains("tenant", StringComparison.OrdinalIgnoreCase) ? alphaRetry(parameter) : cache[parameter], token, last).Arguments(method);
                var retry = await InvokeAsync(app, persona, service, method, own);
                output?.WriteLine($"{name}: own workspace {retry.Describe()}");
                if (retry.Error?.GetType() != outcome.Error.GetType())
                {
                    violations.Add($"circuit {name}: {outcome.Describe()} for another workspace but {retry.Describe()} for its own");
                }
            }
        }
        output?.WriteLine($"Swept {swept} circuit methods on {services.Length} services.");
        return new(swept, violations);
    }

    private static async Task<Outcome> InvokeAsync(FoundationApplication app, string persona, Type service, MethodInfo method, object?[] arguments)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tid", DevelopmentAuthenticationHandler.SyntheticTenant.ToString()),
                new Claim("oid", DevelopmentAuthenticationHandler.Personas[persona].ToString()),
                new Claim(ClaimTypes.Name, persona)
            ], DevelopmentAuthenticationHandler.SchemeName))
        };
        try
        {
            var instance = scope.ServiceProvider.GetRequiredService(service);
            var result = method.Invoke(instance, arguments);
            var task = result switch
            {
                Task t => t,
                ValueTask v => v.AsTask(),
                _ => (Task)result!.GetType().GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(result, null)!
            };
            await task;
            var value = task.GetType().IsGenericType && task.GetType().GetGenericArguments()[0].Name != "VoidTaskResult"
                ? task.GetType().GetProperty(nameof(Task<int>.Result))!.GetValue(task) : null;
            return new(null, Render(value));
        }
        catch (Exception error)
        {
            var inner = error is TargetInvocationException { InnerException: { } wrapped } ? wrapped : error;
            return new(inner, []);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static bool IsAsync(Type type) => type == typeof(Task) || type == typeof(ValueTask)
        || type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>));

    private static byte[] Render(object? value)
    {
        switch (value)
        {
            case null: return [];
            case byte[] bytes: return bytes;
            case string text: return Encoding.UTF8.GetBytes(text);
            case Stream stream:
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
        }
        var properties = value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(x => x.GetIndexParameters().Length == 0).ToArray();
        if (properties.Any(x => typeof(Stream).IsAssignableFrom(x.PropertyType)))
        {
            return [.. properties.SelectMany(x => typeof(Stream).IsAssignableFrom(x.PropertyType)
                ? Render(x.GetValue(value))
                : Encoding.UTF8.GetBytes($"|{Convert.ToString(x.GetValue(value), CultureInfo.InvariantCulture)}|"))];
        }
        try { return JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), RenderOptions); }
        catch (Exception error) when (error is NotSupportedException or InvalidOperationException or JsonException)
        {
            return Encoding.UTF8.GetBytes(value.ToString() ?? "");
        }
    }

    private static async Task<SeededWorkspace> SeedWorkspaceAsync(FoundationApplication app, HttpClient client, Guid workspace, Guid project,
        string prefix, CancellationToken token)
    {
        var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(prefix);
        var forest = $"{prefix}-forest.example.test";
        var ad = Guid.NewGuid();
        (await client.PostAsJsonAsync($"/api/projects/{project}/environments", new EnvironmentInput(ad, EnvironmentRole.Source, EnvironmentKind.AdForest,
            forest, $"{title} declared scope", TargetState.Existing, ""), token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenant = Guid.NewGuid();
        var m365 = Guid.NewGuid();
        (await client.PostAsJsonAsync($"/api/projects/{project}/environments", new EnvironmentInput(m365, EnvironmentRole.Target, EnvironmentKind.M365Tenant,
            tenant.ToString(), "", TargetState.Existing, ""), token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var root = $"/api/projects/{project}/discovery";
        var connected = await client.PostAsJsonAsync($"{root}/{m365}/connection", new ConnectionInput($"{title} Graph connection"), token);
        connected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var connection = await connected.Content.ReadFromJsonAsync<Guid>(token);
        var requestId = Guid.NewGuid();
        (await client.PostAsJsonAsync($"{root}/{ad}/requests", new ScriptInput(requestId, new("domain", "example.test"), ["ad.users"], null), token))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var parameters = await client.GetFromJsonAsync<ScriptParameters>($"{root}/requests/{requestId}/parameters", token);
        var date = DateTimeOffset.UtcNow;
        var package = new DiscoveryPackage(new PackageManifest("1.0", Guid.NewGuid(), "clarity.ad", "1.0.0", "AdForest", forest,
            parameters!.Scope, date, date, new Dictionary<string, string>(), $"{title} synthetic run", requestId),
            [new("ad.users", "user", "ad.objectGUID", $"{prefix}-secret-user", date,
                new Dictionary<string, JsonElement> { ["displayName"] = JsonSerializer.SerializeToElement($"{title} Secret Person") })], [],
            [new("ad.users", "ad.users", CoverageStatus.Complete, parameters.Scope, ["displayName"], [], 1, 0, [], [])]);
        using var zip = new MemoryStream();
        await PackageZipWriter.WriteAsync(package, zip, token);
        var upload = await client.PostAsync($"{root}/{ad}/upload", new ByteArrayContent(zip.ToArray()), token);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var run = await upload.Content.ReadFromJsonAsync<Guid>(token);
        (await client.PostAsync($"{root}/runs/{run}/confirm", null, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        string connectionString;
        await using (var db = app.CreateUnscopedContext()) { connectionString = db.Database.GetConnectionString()!; }
        var processor = new CollectionJobProcessor(new SqlJobQueue(connectionString, TimeProvider.System),
            [new ImportJobHandler(app.Services.GetRequiredService<IArtifactStore>(), new(new(), new()), new(), DiscoveryCatalog.Load(), new())],
            NullLogger<CollectionJobProcessor>.Instance);
        (await processor.ProcessOneAsync(token)).ShouldBeTrue();

        var store = app.Services.GetRequiredService<IExportStore>();
        var content = Encoding.UTF8.GetBytes($"Nom;Décision\r\n{title} Export Person;Inclure\r\n");
        var export = new ExportJob
        {
            WorkspaceId = workspace,
            ProjectId = project,
            Side = EnvironmentRole.Source,
            FiltersJson = JsonSerializer.Serialize(new ExportFilter(EnvironmentRole.Source), ContractJson.Options),
            SelectionJson = "[]",
            SelectionHash = new string('c', 64),
            AsOfUtc = date,
            AuthorTenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
            AuthorObjectId = DevelopmentAuthenticationHandler.Personas[prefix == "beta" ? "beta-manager" : "manager"],
            GeneratedAtUtc = date,
            ObjectCount = 1
        };
        var stored = await store.SaveAsync(workspace, export.Id, "csv", "text/csv", new MemoryStream(content), token);
        var artifact = new ExportArtifact
        {
            WorkspaceId = workspace,
            ExportJobId = export.Id,
            Kind = ExportArtifactKind.InventoryCsv,
            FileName = $"clarity-{prefix}-inventory.csv",
            ContentType = "text/csv; charset=utf-8",
            BlobName = stored.BlobName,
            ContentHash = stored.ContentHash,
            Bytes = stored.Bytes,
            RowCount = 1
        };
        Guid inventoryObject;
        await using (var db = app.CreateUnscopedContext())
        {
            db.AddRange(export, artifact, new CollectionJob
            {
                Id = export.Id,
                WorkspaceId = workspace,
                ProjectId = project,
                ActorTenantId = export.AuthorTenantId,
                ActorObjectId = export.AuthorObjectId,
                Kind = CollectionJobKind.Export,
                PayloadJson = "{}",
                ConcurrencyKey = $"export:{export.Id:N}",
                CorrelationId = "synthetic",
                State = CollectionRunState.Completed,
                Progress = 100
            });
            await db.SaveChangesAsync(token);
            inventoryObject = await db.InventoryObjects.Where(x => x.WorkspaceId == workspace).Select(x => x.Id).SingleAsync(token);
        }
        return new(prefix, title, workspace, project, ad, m365, tenant, connection, requestId, run, export.Id, artifact.Id, inventoryObject,
            zip.ToArray(), content);
    }

    private static async Task<HashSet<Guid>> AuditIdsAsync(FoundationApplication app, CancellationToken token)
    {
        await using var db = app.CreateUnscopedContext();
        return [.. await db.AuditEvents.Select(x => x.Id).ToListAsync(token)];
    }

    /// <summary>Every dbo table holding workspace data, with the column its row-level security predicate reads.</summary>
    private static async Task<List<(string Table, string Column)>> WorkspaceTablesAsync(ClarityDbContext db, CancellationToken token)
    {
        var workspaces = db.Model.FindEntityType(typeof(ClientWorkspace))!.GetTableName()!;
        var tables = new List<(string, string)> { (workspaces, "Id") };
        await db.Database.OpenConnectionAsync(token);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT t.name FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id
            WHERE c.name = 'WorkspaceId' AND SCHEMA_NAME(t.schema_id) = 'dbo' ORDER BY t.name
            """;
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) { tables.Add((reader.GetString(0), "WorkspaceId")); }
        return tables;
    }

    private static async Task<long> ScalarAsync(ClarityDbContext db, string sql, Guid? workspace, CancellationToken token)
    {
        await db.Database.OpenConnectionAsync(token);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (workspace is { } id)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@w";
            parameter.Value = id;
            command.Parameters.Add(parameter);
        }
        return Convert.ToInt64(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    /// <summary>Renders every row a workspace owns, sorted, and collects the identifiers those rows contain.</summary>
    private static async Task<(string[] Rows, HashSet<Guid> Ids)> SnapshotAsync(FoundationApplication app, Guid workspace,
        CancellationToken token)
    {
        await using var db = app.CreateUnscopedContext();
        var rows = new List<string>();
        var ids = new HashSet<Guid>();
        foreach (var (table, column) in await WorkspaceTablesAsync(db, token))
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT * FROM [dbo].[{table}] WHERE [{column}] = @w";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@w";
            parameter.Value = workspace;
            command.Parameters.Add(parameter);
            await using (var reader = await command.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    var fields = new string[reader.FieldCount];
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.GetValue(i);
                        if (value is Guid id) { ids.Add(id); }
                        fields[i] = $"{reader.GetName(i)}={value switch
                        {
                            DBNull => "null",
                            byte[] bytes => Convert.ToHexString(bytes),
                            DateTimeOffset at => at.ToString("O", CultureInfo.InvariantCulture),
                            DateTime at => at.ToString("O", CultureInfo.InvariantCulture),
                            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                        }}";
                    }
                    rows.Add($"{table}|{string.Join("|", fields)}");
                }
            }
        }
        return ([.. rows.Order(StringComparer.Ordinal)], ids);
    }

    [GeneratedRegex(@"\{(\w+)(?::[^}]*)?\}")]
    private static partial Regex RouteParameter();

    private sealed record RouteCase(string Method, string Pattern, Func<IReadOnlyDictionary<string, Guid>, HttpContent>? Body = null, string Query = "",
        bool Tenant = false, string[]? BodySlots = null)
    {
        public string Key => $"{Method} {Pattern}";

        public string[] Slots => [.. RouteParameter().Matches(Pattern).Select(x => x.Groups[1].Value)
            .Where(x => x is not ("projectId" or "workspaceId" or "profileId" or "version")).Concat(BodySlots ?? [])];
    }

    private sealed record SeededWorkspace(string Prefix, string Title, Guid Workspace, Guid Project, Guid Environment, Guid TenantEnvironment,
        Guid Tenant, Guid Connection, Guid Request, Guid Run, Guid Export, Guid Artifact, Guid Object, byte[] Upload, byte[] ExportContent)
    {
        public Dictionary<string, Guid> Ids(RouteCase route)
        {
            var ids = IsolationSweepTests.Ids(Project, Workspace, name => name switch
            {
                "environmentId" => route.Tenant ? TenantEnvironment : Environment,
                "connectionId" => Connection,
                "runId" or "jobId" => Run,
                "requestId" => Request,
                "exportId" => Export,
                _ => Artifact
            });
            return ids;
        }

        public Fingerprint Fingerprint(IEnumerable<Guid> ids) => new(
            [$"Synthetic {Title}", $"{Title} audit", $"{Prefix}-forest.example.test", $"{Prefix}-secret-user", $"{Title} Secret Person",
                $"{Title} Graph connection", $"{Title} Export Person", $"{Title} synthetic run", $"{Title} declared scope"],
            [.. ids, Tenant], [Upload, ExportContent]);
    }

    private sealed class Fingerprint(IEnumerable<string> markers, IEnumerable<Guid> ids, IEnumerable<byte[]> payloads)
    {
        private readonly string[] _markers = [.. markers];
        private readonly Guid[] _ids = [.. ids.Distinct()];
        private readonly byte[][] _payloads = [.. payloads.Where(x => x.Length > 0)];

        /// <summary>Returns the first foreign marker found in a response, ignoring identifiers the request itself supplied.</summary>
        public string? Find(byte[] body, string request)
        {
            if (body.Length == 0) { return null; }
            if (_payloads.Any(x => body.AsSpan().IndexOf(x) >= 0)) { return "payload"; }
            var text = WebUtility.HtmlDecode(Encoding.UTF8.GetString(body));
            if (_markers.FirstOrDefault(x => text.Contains(x, StringComparison.OrdinalIgnoreCase)) is { } marker) { return marker; }
            foreach (var id in _ids)
            {
                string[] forms = [id.ToString("D"), id.ToString("N")];
                if (forms.Any(x => request.Contains(x, StringComparison.OrdinalIgnoreCase))) { continue; }
                if (forms.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase))) { return id.ToString(); }
            }
            return null;
        }
    }

    private sealed record Outcome(Exception? Error, byte[] Rendered)
    {
        public string Describe() => Error is null ? $"completed ({Rendered.Length} bytes)" : Error.GetType().Name;
    }

    private sealed record SweepResult(int Swept, List<string> Violations);

    /// <summary>Builds plausible arguments from parameter types; every Guid comes from the caller-supplied, name-based source.</summary>
    private sealed class Sampler(Func<string, Guid> guidFor, CancellationToken token, bool lastEnumValue = false)
    {
        private readonly NullabilityInfoContext _nullability = new();

        public object?[] Arguments(MethodInfo method) => [.. method.GetParameters().Select(x => Parameter(x, 0))];

        private object? Parameter(ParameterInfo parameter, int depth)
        {
            var type = parameter.ParameterType;
            if (type == typeof(Guid) || Nullable.GetUnderlyingType(type) == typeof(Guid)) { return guidFor(parameter.Name!); }
            if (type == typeof(CancellationToken)) { return token; }
            if (parameter.HasDefaultValue)
            {
                var value = parameter.DefaultValue;
                var target = Nullable.GetUnderlyingType(type) ?? type;
                if (value is null) { return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null; }
                return target.IsEnum && value.GetType() != target ? Enum.ToObject(target, value) : value;
            }
            return Sample(type, parameter.Name!, depth);
        }

        private object? Sample(Type type, string name, int depth)
        {
            if (Nullable.GetUnderlyingType(type) is { } underlying) { return Sample(underlying, name, depth); }
            if (type == typeof(Guid)) { return guidFor(name); }
            if (type == typeof(string) || type == typeof(object)) { return "synthetic"; }
            if (type == typeof(bool)) { return false; }
            if (type == typeof(CancellationToken)) { return token; }
            if (type.IsEnum)
            {
                var values = Enum.GetValues(type);
                return values.GetValue(lastEnumValue ? values.Length - 1 : 0);
            }
            if (type.IsPrimitive || type == typeof(decimal)) { return Convert.ChangeType(1, type, CultureInfo.InvariantCulture); }
            if (type == typeof(DateTimeOffset)) { return DateTimeOffset.UtcNow; }
            if (type == typeof(DateTime)) { return DateTime.UtcNow; }
            if (type == typeof(DateOnly)) { return DateOnly.FromDateTime(DateTime.UtcNow); }
            if (type == typeof(TimeSpan)) { return TimeSpan.FromMinutes(1); }
            if (type == typeof(JsonElement)) { return JsonSerializer.SerializeToElement("synthetic"); }
            if (typeof(Stream).IsAssignableFrom(type)) { return new MemoryStream([1, 2]); }
            if (depth > 4) { return type.IsValueType ? Activator.CreateInstance(type) : null; }
            if (type.IsArray)
            {
                var element = type.GetElementType()!;
                var array = Array.CreateInstance(element, 1);
                array.SetValue(Sample(element, name, depth + 1), 0);
                return array;
            }
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                var arguments = type.GetGenericArguments();
                if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
                {
                    return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
                }
                if (definition == typeof(HashSet<>) || definition == typeof(ISet<>) || definition == typeof(IReadOnlySet<>))
                {
                    var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(arguments))!;
                    set.GetType().GetMethod("Add")!.Invoke(set, [Sample(arguments[0], name, depth + 1)]);
                    return set;
                }
                if (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>) || definition == typeof(IEnumerable<>)
                    || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>))
                {
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(arguments))!;
                    list.Add(Sample(arguments[0], name, depth + 1));
                    return list;
                }
            }
            if (type.IsInterface || type.IsAbstract || typeof(Delegate).IsAssignableFrom(type)) { return null; }
            var constructor = type.GetConstructors().OrderByDescending(x => x.GetParameters().Length).FirstOrDefault();
            var instance = constructor is null
                ? type.IsValueType ? Activator.CreateInstance(type) : null
                : constructor.Invoke([.. constructor.GetParameters().Select(x => Parameter(x, depth + 1))]);
            if (instance is null) { return null; }
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.SetMethod?.IsPublic == true && x.GetIndexParameters().Length == 0))
            {
                var isGuid = property.PropertyType == typeof(Guid) || Nullable.GetUnderlyingType(property.PropertyType) == typeof(Guid);
                if (isGuid || property.GetValue(instance) is null && _nullability.Create(property).WriteState != NullabilityState.Nullable)
                {
                    property.SetValue(instance, Sample(property.PropertyType, property.Name, depth + 1));
                }
            }
            return instance;
        }
    }
}
