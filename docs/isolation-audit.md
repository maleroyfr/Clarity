# Client isolation audit

This page records how Clarity verifies recette row R09 (*Tentative d’accès depuis
un autre client*) across the whole application surface, not only per feature. It
complements the per-feature R09 tests listed in [the recette](recette.md) and the
defense-in-depth design in [architecture](architecture.md).

The audit runs on synthetic data only: the two synthetic client workspaces
`Synthetic Alpha` and `Synthetic Beta`, and generated identifiers and payloads.
Measuring isolation on a real tenant remains part of the user-led pilot.

## What is swept

`tests/Clarity.Web.Tests/IsolationSweepTests.cs` contains two tests, both tagged
`[Trait("Recette", "R09")]`.

### 1. Surface sweep (runs everywhere, no SQL)

`Every_route_page_and_circuit_service_denies_identifiers_from_another_workspace`
uses the in-memory HTTP host and covers the following.

- **Route inventory.** The test enumerates the actual `/api` endpoints from the
  routing data source and compares them with its own list of 34 routes. A new
  endpoint fails the test until it is added to the sweep. Routable Blazor pages
  are enumerated the same way from their `@page` templates.
- **Foreign identifiers.** A workspace manager of Alpha calls every route with
  Beta's project and workspace identifiers and random child identifiers. Every
  call must return 403, except `GET /api/antiforgery` and `GET /api/workspaces`,
  which return the caller's own data.
- **Role checks.** A reader of Alpha calls every mutation and must get 403. A
  platform administrator without membership calls every project route and the
  project list, and must get 403.
- **Circuit services.** Pages call services directly from the Blazor circuit,
  not over HTTP. The test therefore discovers every Clarity service injected
  into a routable component (27 services). It invokes every public asynchronous
  method that takes a `projectId` or `workspaceId` (94 methods) as the Alpha
  manager with Beta identifiers. Arguments are built by reflection, and every
  `Guid` is chosen by parameter name.
  - A call that completes is a violation.
  - A call that fails for any reason other than `AccessDeniedException` must
    fail with the same exception for Alpha's own identifiers. Input validation
    that runs before authorization therefore reveals nothing about the other
    client.
  - If a sampled value is rejected as an invalid argument, the call is retried
    with the last enumeration values. This lets service-specific kinds, such as
    Teams assessments, reach authorization.
- **Pages.** Every project section and view (17 combinations, both sides) is
  rendered with Beta's project. The client project list is rendered for Beta's
  workspace. Pages must not return a 5xx status or any Beta marker.

### 2. Populated SQL sweep (CI, `CLARITY_TEST_SQL`)

`Sql_populated_workspaces_resist_guessed_and_copied_identifiers_and_keep_rls_audit_and_storage_intact`
runs on a disposable SQL Server database with row-level security enabled.

**Setup.** Each workspace is populated through the public HTTP API by its own
manager. Each gets:

- an AD forest and a Microsoft 365 tenant;
- a Graph connection;
- a script request;
- an uploaded, confirmed and integrated ZIP package, which creates an inventory
  object and stores a raw artifact;
- a completed CSV export with a stored artifact.

Every Beta row is then read back. The test builds a fingerprint of Beta's
identifiers, names, uploaded bytes and export bytes, ignoring identifiers that
are shared with Alpha or present in the request itself.

**Checks.**

- **Owner baseline.** The Alpha manager reads every project `GET` route with
  Alpha identifiers and receives 200 without any Beta marker. The raw download
  returns the uploaded bytes.
- **Phase A, copied identifiers.** Every route is called with Beta's real project
  and child identifiers. The expected statuses are the same as in the surface
  sweep.
- **Phase B, mixed identifiers.** Every route that carries child identifiers in
  its path or body is called on Alpha's project, replacing each child identifier
  in turn, and then all of them, with Beta's identifiers. Allowed statuses are
  200, 400, 403 or 404 for `GET` and 400, 403, 404 or 409 for `POST`. No response
  may contain a Beta marker.
- **Phase C, guessed identifiers.** The same routes are called with random child
  identifiers. The same rules apply.
- **Pages with query identifiers.** Alpha inventory and mapping views are opened
  with a Beta object, run, environment or section in the query string.
- **Circuit services with real identifiers.** Every circuit method that takes a
  child identifier runs on Alpha's project with Beta's real identifiers. A
  method that completes must not return Beta data.
- **Audit trail.** Every audit event written during the hostile phases belongs to
  Alpha, names the Alpha manager as actor, and carries an action and a
  correlation identifier. No event is written into Beta.
- **Project identifier collision.** Creating an Alpha project with Beta's project
  identifier returns 409 without content.
- **No side effects.** After all phases, every Beta row in every workspace table
  is unchanged, and the raw and export artifact stores contain the same number
  of artifacts.
- **Row-level security.** For every `dbo` table that has a `WorkspaceId` column,
  plus the workspace table itself:
  - an Alpha-scoped session sees no row of another workspace;
  - an empty scope sees no row at all;
  - the unscoped administrative test session still sees Beta rows, which shows
    the counts are meaningful.

`RowLevelSecurityTests.Every_workspace_table_has_an_enabled_filter_and_insert_update_block_predicates`
checks the SQL catalog independently. Every table with a `WorkspaceId`, and every
table mapped from a `WorkspaceEntity`, must be covered by an enabled,
schema-bound security policy. That policy must have a filter predicate and both
`AFTER INSERT` and `AFTER UPDATE` block predicates, all calling functions in the
`Security` schema. `AuditEvents` must also have the `BEFORE DELETE` block
predicate that refuses deleting recent events (see
[retention and deletion](retention-deletion.md#sql-guard-on-audit-deletion)). A new
table without row-level security therefore fails CI.
`RowLevelSecurityTests.Bypassing_EF_filters_cannot_read_or_mutate_other_clients_and_pooled_scope_is_reset`
proves that raw SQL and `IgnoreQueryFilters` cannot bypass these predicates and
that a pooled connection never inherits a previous scope.

## Expected responses

| Situation | Response |
|---|---|
| Project of another workspace, or unknown project | 403, body limited to the correlation identifier |
| Child identifier (environment, connection, run, request, job, export, artifact) of another workspace or unknown, on an authorized project | 403, or an empty 200 list for environment-filtered lists (runs, coverage, requests) |
| Invalid input (bad enumeration, malformed package) | 400 for own and foreign identifiers alike |
| Client-chosen identifier already used | 409 |
| Missing role for the operation | 403 |

Errors never include exception messages, payloads or the other client's data
([architecture](architecture.md)).

## Audit trail review

Sensitive actions append an `AuditEvent` in the same transaction as the change
they describe. The event records the workspace, the resource, the action, the
actor's tenant and object identifiers, and the correlation identifier from the
current trace. Audit rows are workspace-owned and protected by row-level
security like every other inventory table. SQL also refuses to delete an audit
event younger than 29 days, whatever the application requests.

| Area | Audited actions |
|---|---|
| Foundation | `WorkspaceCreated`, `WorkspaceArchived`, `WorkspaceMemberAssigned`, `ProjectCreated`, `ProjectArchived`, `ProjectRead`, `EnvironmentDeclared` |
| Graph | `ConnectionCreated` (new connections only), `ConnectionTested`, `GraphCollectionRequested` |
| Jobs | `JobCancellationRequested`, `JobRetried` |
| Script and import | `ScriptRequested` and script downloads, `CollectionUploaded`, `CollectionPreviewed`, `CollectionConfirmed`, `CollectionIntegrated`, `RawArtifactDownloaded` |
| Scope and decisions | `Scope.*` population, list and decision actions, assessments, dependency analysis, `DecisionReview.Recomputed` |
| Comparisons | run comparisons, target rules and target comparisons |
| Exports | `ExportRequested`, `ExportGenerated`, `ExportDownloaded` |
| Retention and deletion | `ProjectDeletionRequested`, `ProjectDeletionRetried`, `ProjectDeleted`; system events `RawFilePurged`, `ExportFilesExpired`, `AuditEventsPurged` ([retention and deletion](retention-deletion.md)) |

The Lot 6 audit added the Graph and job actions above. Before Lot 6, connections,
connection tests, Graph launches, cancellations and retries left no audit trail.
`GraphTests.ObservedTenantMismatchPublishesNothingAndSectionAccessTestsExplainMissingPermission`
and `JobTests.BoundedRetryCancellationAndMembershipRevocationArePersisted` assert
these events. A rejected request writes no audit event, because the failed
transaction is never committed. Denials are logged as structured warnings with
the correlation identifier.

## Changes made by the audit

- **Global project identifiers.** Project routes carry only the project
  identifier, so a client-chosen project identifier must be unique across
  workspaces. Migration `20261127050122_GlobalProjectIdentifiers` adds a unique
  index on `Projects.Id`. Before this change, the primary key `(WorkspaceId, Id)`
  allowed two workspaces to hold the same project identifier.
- **Job retry.** Retrying an unknown job, or a job of another project, now
  returns 403 instead of 409. Only a known job that is neither failed nor
  cancelled returns 409, so a guessed job identifier is indistinguishable from
  an unknown one.
- **Discovery grids on SQL Server.** The populated sweep's owner baseline found
  that the collection runs, script requests and jobs grids (and their API
  routes) failed with HTTP 500 on SQL Server: they sorted after projecting into
  a record, which SQL Server cannot translate. The in-memory provider hid the
  problem. These grids now sort and page in the database before projecting.

## Accepted behaviours and limits

- **Project identifier collision.** Creating a project with an identifier that
  already exists in another workspace returns 409. This reveals that the
  identifier exists, but no name, workspace or content. Identifiers are random
  GUIDs generated by the client, so guessing one is impractical.
- **Child identifiers are scoped, not global.** Environment, connection, run
  and other child identifiers are unique within their workspace. The same value
  in two workspaces is harmless, because every lookup includes the authorized
  workspace and project. Composite foreign keys `(WorkspaceId, Id)` enforce
  same-workspace references in SQL. The in-memory test host does not enforce
  them.
- **Graph concurrency is per tenant.** Two workspaces that declare the same
  Microsoft 365 tenant share a Graph throttling and concurrency key. One client
  can delay the other's collection but cannot read it.
- **Non-API routes.** `/health/live` and `/health/ready` are anonymous and
  return no inventory. `/authentication/login` starts the Entra sign-in.
  `POST /development/persona` exists only in Development and Testing.
- **Scope of the sweep.**
  - The circuit sweep covers public asynchronous methods that take a project or
    workspace identifier. Synchronous helpers and methods that take neither are
    outside it.
  - Argument samples are synthetic. A method whose validation rejects both the
    first and the last enumeration values is compared through the equal-failure
    rule rather than reaching authorization.
  - The sweep does not cover authorization of background job execution. That is
    covered by `JobTests` and `ReportImportTests`, which re-check membership at
    execution time.

## Running

```powershell
dotnet test --project tests\Clarity.Web.Tests --configuration Release --filter-class "Clarity.Web.Tests.IsolationSweepTests"
$env:CLARITY_TEST_SQL = "<connection string of a disposable SQL Server database>"
dotnet test --configuration Release --filter "Recette=R09"
```

Without `CLARITY_TEST_SQL`, the SQL sweep is reported as **skipped**, not passed.
CI runs it against a disposable SQL Server service container.
