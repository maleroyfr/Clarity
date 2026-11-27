# Local development

Use .NET SDK 11.0.100 or a newer patch in the pinned feature band. If installed
with `dotnet-install.ps1 -Version 11.0.100 -InstallDir "$HOME\.dotnet"`, prepend
`$HOME\.dotnet` to PATH and set `DOTNET_ROOT` to the same directory.

```powershell
$env:PATH = "$HOME\.dotnet;$env:PATH"
$env:DOTNET_ROOT = "$HOME\.dotnet"
dotnet restore
dotnet run --project src\Clarity.Web -- --environment Development
```

The first screen is an empty client screen. No client data is seeded during
application startup. Without `ConnectionStrings:Clarity`, Development displays a
configuration notice and disables creation; it never substitutes an in-memory
database. Use a reachable SQL Server/Azure SQL database to persist real local work.

```powershell
$env:ConnectionStrings__Clarity = '<your development SQL connection string>'
$env:CLARITY_MIGRATION_SQL = $env:ConnectionStrings__Clarity
dotnet tool restore
dotnet ef database update --project src\Clarity.Infrastructure
dotnet run --project src\Clarity.Web -- --environment Development
```

The selector provides manager, consultant and reader personas for a synthetic
Alpha workspace, a Beta manager, and a platform administrator. Their fixed
identities are in `DevelopmentAuthenticationHandler`; the handler does not seed
those workspaces. To start from empty, select the platform administrator, create
a client, then use **Attribuer un accès** with the synthetic tenant/object pair
of your chosen persona. Switch to that persona and create a dossier. The
administrator needs explicit membership for inventory access too. Existing SQL
memberships are honored for all local personas.

Fake authentication, its cookie selector and the `X-Clarity-Persona` test header
exist only in Development/Testing. Never expose a Development deployment publicly.
Production requires Entra configuration and SQL; missing configuration does not
fall back to a fake identity. Business labels are neutral French resources with
`fr-FR` request culture.

The Interactive Server layout follows the system light/dark preference through
`IThemeService`. Leave the Fluent JS initializer, `use-reboot`, the app's scoped
CSS bundle (which imports the library bundle), and `FluentProviders` in place.
Choose Fluent UI Blazor v5 components for controls, navigation and feedback;
reserve HTML for semantics and native browser contracts. Use Fluent design tokens
for layout overrides. Check APIs with the Fluent UI Blazor skill and MCP, then
confirm against the installed package: the skill also covers v4, and the server
can document a different v5 minor version.

Discovery provides **Connexions**, **Collectes** and **Couverture** for existing
source or target environments. A planned target cannot be collected. AD script
requests and their separate downloads require SQL. Result uploads and background
processing also require private Blob Storage. Graph additionally requires the
managed-identity certificate configuration in
[the connector guide](connectors/microsoft-graph.md). Nothing deploys automatically.
Inventory projection, matching and other later-lot sections remain unavailable.

Use **Collectes** to select AD sections and a domain/OU, create a request and
download the immutable script ZIP plus separate `parameters.json`. Run the displayed
command on an authorized AD machine, then upload **result.zip**, not the script
ZIP. Preview the observed scope, counts and errors before explicit confirmation.
Another authorized consultant can upload and confirm the result from a different
machine. Requests retain their identity and history in the dossier.

For Graph, a workspace manager declares/reuses a connection for an existing M365
environment with a real tenant GUID. A consultant tests section permissions and
launches selected sections. Grant `Organization.Read.All` for observed-tenant
verification in addition to section-specific rights. Local personas need real
stored workspace membership for jobs; the worker never fabricates membership.

**Recharger** refreshes persisted jobs and section history. Retry failed/cancelled
jobs to resume prepared work without duplicate publication; launch another Graph
collection to obtain fresh source data. **Couverture** preserves links to the last
valid source ZIP through pending/failed updates and distinguishes unknown from
known zero. The preview shows source-key changes, not Lot 3 matching decisions.
Read-only members can inspect/download, but cannot collect, upload or confirm.

SQL-dependent tests require `CLARITY_TEST_SQL` pointing to a disposable SQL Server
or Azure SQL database. Tests may create and remove test databases. Without the
variable, SQL tests report a clear skip. Unit tests need no SQL service.

Use environment variables or .NET user-secrets for private configuration; never
commit connection strings with credentials, real tenant identifiers or customer
data. Production uses Entra authentication and managed identities.

## Health and telemetry

`/health/live` checks the running host without dependencies. `/health/ready` returns
503 until SQL and all three private Blob containers are reachable. Configure
`Storage__BlobServiceUri` with the HTTPS service endpoint. Blob authentication uses
`DefaultAzureCredential`; `AZURE_CLIENT_ID` selects the user-assigned identity in
Azure. Local Blob checks require an already authenticated developer credential.
Neither endpoint returns configuration, exception text or inventory.

Logs are JSON with a generated W3C trace/correlation ID returned in
`X-Correlation-ID`. Only Clarity-owned, sanitized log messages are enabled.
OpenTelemetry records HTTP status/duration and UI operation spans, and the
`Clarity.Publication` stage spans (name and duration only); it intentionally
does not auto-instrument SQL, outbound HTTP, authentication or framework logs that
could include query strings, exception contents or customer data. New telemetry
must follow this allowlist approach, not export then redact. No request body,
header, path, query, principal or exception message is captured.

Set `APPLICATIONINSIGHTS_CONNECTION_STRING` to enable the Azure Monitor trace,
metric and log exporters. Without it, telemetry remains local and makes no Azure
Monitor connections. App Service auto-instrumentation must remain disabled to
avoid a second, unredacted instrumentation pipeline.
