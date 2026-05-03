# Clarity

Clarity is a Windows desktop app and supporting services that capture point-in-time snapshots
of Microsoft 365 and on-prem environments, then compare and export the results for audit and
change tracking.

## Highlights
- Collect inventory via Microsoft Graph and PowerShell collectors.
- Store immutable snapshots with metadata about permissions used and commands executed.
- Compare snapshots to surface added, removed, and modified objects.
- Export results to CSV, JSON, and XLSX.

## Supported workloads
- Entra ID (Azure AD)
- Intune
- Exchange Online
- SharePoint Online
- Microsoft Teams
- On-premises Active Directory
- On-premises Exchange

## Project layout
- `src\Clarity.Desktop` - Avalonia UI app
- `src\Clarity.Api` - ASP.NET Core API (minimal scaffolding)
- `src\Clarity.Collectors.*` - Graph, PowerShell, and AD collectors
- `src\Clarity.Comparisons` - Snapshot diff engine
- `src\Clarity.Exports` - CSV/JSON/XLSX export pipeline
- `src\Clarity.Infrastructure` - Data access and local storage

## Getting started
Prerequisites:
- .NET 10 SDK
- PowerShell 7 (for PowerShell-based collectors)

Build:
```
dotnet build Clarity.slnx
```

Run the desktop app:
```
dotnet run --project src\Clarity.Desktop\Clarity.Desktop.csproj
```

Run the API (optional):
```
dotnet run --project src\Clarity.Api\Clarity.Api.csproj
```

## Collectors and prerequisites
Most workloads require an Entra ID app registration with application permissions and admin
consent for Microsoft Graph. Some workloads also require PowerShell modules such as
ExchangeOnlineManagement or PnP.PowerShell and network access to on-prem services.
Clarity surfaces workload-specific prerequisites in the UI.

## Tests
```
dotnet test
```

Some integration tests require access to tenant data or credentials.

## Community
Use GitHub Issues for bugs and feature requests. Include your workload, environment type,
and any relevant logs from `%LOCALAPPDATA%\Clarity\logs` when reporting problems.
For questions or design discussions, open an issue with context and expected behavior.

## Contributing
1. Fork the repo and create a feature branch.
2. Keep PRs focused and describe the scenario being improved.
3. Run `dotnet test` and share any relevant logs or screenshots.
4. Align with existing patterns in the domain, collectors, and UI layers.
