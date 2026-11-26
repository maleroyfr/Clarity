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

The initial bootstrap contains the default empty Blazor host; the foundation
slices replace it with an empty client screen and Development-only personas.
No client data is seeded during application startup.

SQL-dependent tests require `CLARITY_TEST_SQL` pointing to a disposable SQL Server
or Azure SQL database. Tests may create and remove test databases. Without the
variable, SQL tests report a clear skip. Unit tests need no SQL service.

Use environment variables or .NET user-secrets for private configuration; never
commit connection strings with credentials, real tenant identifiers or customer
data. Production uses Entra authentication and managed identities.
