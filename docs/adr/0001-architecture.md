# ADR 0001: Layered modular monolith with client isolation

Status: Accepted

## Context

Clarity consolidates migration inventory for multiple clients. A consultant can
belong to more than one client workspace. A tenant observed in two workspaces must
never link data across those workspaces. Inventory must remain explainable through
provenance, authorization, and explicit coverage.

## Decision

Use .NET 11, Blazor Web App Interactive Server, Fluent UI Blazor v5, EF Core 11,
Azure SQL Database, and Azure Blob Storage. Deploy one modular monolith to Linux
Azure App Service. Entra ID authenticates production users through
Microsoft.Identity.Web. Fake personas are registered only in Development/Testing.

- `Clarity.Core`: domain entities, rules, application use cases, and ports.
- `Clarity.Infrastructure`: EF/SQL, Blob, and later collectors, imports and workers.
- `Clarity.Web`: UI, endpoints, authentication, authorization, composition.
- `Clarity.Contracts`: introduced with collection/import contracts in Lot 2.

Dependencies point inward: Core has no infrastructure or UI dependency.
Create one EF context per operation through `IDbContextFactory`; never retain
a context for a Blazor circuit. Grids filter, sort and page in the database.

## Client security boundary

Every workspace-owned table carries `WorkspaceId`. Composite foreign keys
`(WorkspaceId, Id)` enforce same-workspace references. EF query filters scope reads.
SQL row-level security independently enforces authorized workspace access using
`SESSION_CONTEXT`, reset by a connection interceptor on **every** open, including
pooled reuse. Server-side authorization is mandatory even when UI controls hide an
operation. Workspace context comes from identity and authorized resources, never
from a client-supplied workspace identifier alone. Workers re-establish scope.

Platform administration does not implicitly grant inventory access; membership
must be explicit. Authorizations, idempotency and provenance ship with the features
they protect, not as subsequent hardening.

## Operations and consequences

Use structured correlated telemetry without secrets or sensitive payloads.
Raw artifacts live in private Blob containers; relational references remain in SQL.
Apply backward-compatible database migrations explicitly during deployment using
a separate authorized identity. Serialize deployments, smoke-test health, and
retain the previous application artifact for rollback. Deployment is opt-in.

SQL integration tests run in CI. Unit tests do not require external services.
This arrangement keeps the first implementation operable without introducing
distributed consistency, while leaving modules independently testable.
