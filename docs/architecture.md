# Architecture

Clarity is a modular monolith. The product reference is [the specification](specification.md);
the stack and isolation decision are in [ADR 0001](adr/0001-architecture.md).

## Foundation journeys

`FoundationService` authorizes every operation against a refreshed server-side
membership list. `WorkspaceAuthorization` derives the stable Entra tenant/object
pair from the authenticated principal and uses resource-based authorization.
The UI invokes the same service as the HTTP endpoints, never bypassing it.

`Read`, `Collect`, `Import`, `Qualify`, `Export`, `ManageMembers`,
`ManageConnections` and `AdministerPlatform` are separate operations. Readers can
read but cannot generate exports or mutate. Platform administrators manage client
metadata and membership but need explicit membership to see projects/inventories.
Declaring an environment requires connection-management rights.

Create commands carry a client-generated `OperationId`, retained across retries.
The identifier becomes the resource primary key. A replay with matching content
returns the existing resource; different content conflicts. Concurrent insertion
of the same key is rejected by SQL (HTTP 409); retry the original command to
resolve the winner. Archive and unchanged-role assignments are no-ops on replay.
Sensitive actions append an audit event in the same EF save transaction.

Production uses SQL through a scoped `IDbContextFactory`, one context per
operation. Every project/environment lookup is filtered by authorized workspace;
environment-to-project foreign keys include `WorkspaceId`. External tenant
identifiers are deliberately not globally unique.

All HTTP mutations require antiforgery validation in addition to authentication.
HTTP errors return only status and correlation ID; no exception body or payload.
Development/Testing authentication is never registered in other environments.
In-memory EF is used only by HTTP authorization tests, not by the application.

## SQL defense in depth

`WorkspaceConnectionInterceptor` overwrites every Clarity `SESSION_CONTEXT` key on
each synchronous or asynchronous connection open; pooling never inherits a prior
caller's identity, admin flag or workspace list. An empty scope sees no inventory.
SQL filter and block predicates protect reads, inserts and updates independently
of EF, including `IgnoreQueryFilters`. The application identity must not have
permission to alter security policies, schemas, or its own database roles.

Member self-lookup is allowed only for the authenticated tenant/object pair so
the authorization service can establish its initial workspace list. Platform
administrators can manage workspace/member metadata and append audit events, but
their flag does not bypass inventory predicates. The list is a delimited ASCII
set in SQL's 8 KB session value; scopes exceeding that capacity fail explicitly
rather than truncating access. Revisit the representation before more than 216
simultaneous workspace memberships per user are required.

## UI foundation

Fluent UI v5 owns inputs, buttons, providers and data grids. `ClarityGrid` forwards
paging, search and the exposed name sort to SQL-backed services, with a localized
paginator and explicit empty/error/loading states. Later lots can extend the
sort contract when adding new sortable fields. Detail panels receive focus and
support Escape. The selected project tab uses a conventional bottom underline
(not an accent border on a rounded card); notices use thin neutral outlines.
