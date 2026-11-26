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
