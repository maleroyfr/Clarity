# Clarity

Clarity gives migration consultants a shared, traceable inventory of their clients'
Microsoft 365 and Active Directory environments. **Discovery** collects inventory;
Clarity does not execute migrations. This public repository contains synthetic data only.

## Prerequisites

- .NET 11 SDK (the supported patch band is pinned in `global.json`).
- PowerShell 7 and Pester 5 for collection-package tests when those packages exist.
- SQL Server or Azure SQL for persistence and SQL integration tests.

The application uses Blazor Interactive Server, Fluent UI Blazor v5, EF Core 11,
and Azure SQL in a layered modular monolith. Code and documentation are English;
business UI resources are French (`fr-FR`).

## Local start

```powershell
dotnet restore
dotnet run --project src\Clarity.Web -- --environment Development
```

Local startup and authentication configuration are documented in
[local development](docs/local-development.md). The first screen starts empty;
synthetic workspaces belong only in tests.

## Validation

```powershell
dotnet restore
dotnet build --no-restore --configuration Release -warnaserror
dotnet format --verify-no-changes --no-restore
dotnet test --no-build --configuration Release
```

SQL integration tests use `CLARITY_TEST_SQL`. Without that variable, they are
explicitly skipped locally; GitHub Actions provides a SQL Server service container.
Never use a production database for tests.

## Product and architecture

- [Product specification (original French, authoritative)](docs/specification.md)
- [Architecture decision](docs/adr/0001-architecture.md)
- [Architecture and isolation](docs/architecture.md)
- [Discovery collection and import acceptance](docs/discovery.md)
- [Versioned report import profiles](docs/import-profiles.md) and
  [Microsoft 365 usage reports](docs/connectors/microsoft-365-usage-reports.md)
- [Consolidation rules, acceptance matrix and pilot limits](docs/consolidation.md)
- [Applications, SSO metadata and consultant qualification](docs/connectors/applications-sso.md)
- [Exchange Online collection script](docs/connectors/exchange.md)
- [Power BI collection script and visibility limits](docs/connectors/powerbi.md)
- [Azure and local Windows infrastructure collection scripts](docs/connectors/azure-infrastructure.md)
- [Office settings, GPO links and security filtering](docs/connectors/office-gpo.md)
- [SharePoint and OneDrive collection](docs/connectors/sharepoint-onedrive.md)
- [Teams collection and chat/telephony qualifications](docs/connectors/teams.md)
- [Migration scoping: populations, list import and scope decisions](docs/scoping.md)
- [Comparisons: successive collections, source and target, and decisions to review](docs/comparison.md)
- [Decision history, targeted re-review and the decision register](docs/decisions.md)
- [Exports: dated Excel workbook and filtered CSV files](docs/exports.md)
- [Scenario workflows: tenant-to-tenant, carve-out, carve-in and audit only](docs/scenarios.md)
- [Reference acceptance (recette) on a synthetic dossier](docs/recette.md)
- [Client isolation audit: routes, pages, circuit services, RLS and audit trail](docs/isolation-audit.md)
- [Resilience under restarts and throttling, and 50,000-user volume timings](docs/resilience-performance.md)
- [Retention per category, project archival and controlled deletion](docs/retention-deletion.md)
- [Azure provisioning, deployment and rollback](docs/deployment.md)
- [Operations runbook: health, incidents, access, secrets, releases and schema changes](docs/operations.md)
- [Pilot checklist: synthetic readiness evidence and user-led pilot steps](docs/pilot-checklist.md)
- [Contributor conventions](.github/copilot-instructions.md)

Delivery proceeds in focused, independently validated slices. Each commit records
the validation commands and their results. Azure deployment remains disabled until
its GitHub environment and OIDC configuration have been explicitly provisioned.