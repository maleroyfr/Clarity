# Operations runbook

This runbook covers day-to-day operation of a deployed Clarity instance: health
and log checks, incident handling, access administration, secret rotation,
releases, rollback, schema changes, retention and deletion. Provisioning, the
first SQL bootstrap and the release pipeline are described in
[deployment](deployment.md); this page links to it rather than repeating it.

Azure deployment is **disabled** in this repository (`AZURE_DEPLOY_ENABLED` is
unset). Nothing here has been exercised against a live Azure subscription or a
real tenant. Every procedure was written against the code and the synthetic test
evidence listed in the [pilot checklist](pilot-checklist.md). The user-led pilot
must confirm each procedure in the target environment.

The specification (section 8) asks the platform to "Exposer des contrôles de
santé et suivre chaque échec jusqu’à son dossier dans l’interface autorisée", and
assigns operations to the platform administrator: "L’administrateur de la
plateforme gère les espaces et l’exploitation."

## Roles and identities

| Who | Does | Never |
|---|---|---|
| Platform administrator (Clarity role) | Creates and archives client workspaces, assigns the first members | Reads inventories without an explicit membership |
| Workspace manager | Manages members, archives projects, requests controlled deletion | Acts outside its workspace |
| Consultant / reader | Collects, imports, qualifies, exports / reads and downloads | Administers members |
| SQL administrator group (Entra) | SQL bootstrap, grant repairs, member or platform administrator removal, incident queries | Uses its rights for routine work; disables row-level security |
| Migration service principal | Runs the reviewed EF bundle (`db_owner`) | Serves the web process |
| Deployment service principal | ZIP-deploys the CI artifact (Website Contributor) | Reads SQL, Blob or Key Vault data |
| Application managed identity (database user `[clarity-app]` in `infra/bootstrap-sql.sql`, renamed to the identity's name at bootstrap) | SQL DML within the reviewed grants, Blob data, Key Vault secret read | Alters schema, roles or the security policy |

See [deployment](deployment.md#1-establish-identities-and-the-github-environment)
for how each identity is created and scoped.

## Routine checks

### Health

| Endpoint | Checks | Healthy | Unhealthy |
|---|---|---|---|
| `GET /health/live` | None: the process answers | 200 | No answer: the app is stopped or crashing |
| `GET /health/ready` | SQL (`CanConnectAsync`, 10 s) and Blob (properties of `raw-artifacts`, `exports` and `script-packages`, 10 s) | 200 | 503 with the failing check names |

Both endpoints are anonymous and return only a status and check names, never a
connection string, exception or inventory. A ready check failing on SQL usually
means private DNS, the SQL private endpoint or the application database user;
failing on Blob usually means private DNS, the storage private endpoint or the
identity's Storage Blob Data Contributor assignment. Background workers run only
when SQL and Blob storage are both configured.

### Logs and telemetry

- The app writes JSON console logs (App Service log stream) for `Clarity.*`
  categories at Information and above. When `APPLICATIONINSIGHTS_CONNECTION_STRING`
  is set (the template sets it), logs, traces and metrics also go to Application
  Insights.
- Every HTTP response carries `X-Correlation-ID`, the request's trace identifier.
  Failed API calls return `{ "status": ..., "correlationId": ... }`. Each job
  stores the correlation of the request that launched it, and the Discovery
  jobs list shows it as "Corrélation". Search logs and traces by that value.
- Metrics: `clarity.request.duration`. Publication stages appear as
  `Clarity.Publication` activities (see
  [resilience and performance](resilience-performance.md)).
- Logs carry error types, codes and correlation identifiers. They never carry
  tokens, raw Graph responses, file content or exception messages. Do not turn on
  App Service request/body logging or auto-instrumentation to get more detail.

Messages worth an alert or a daily look:

| Message | Meaning | Action |
|---|---|---|
| `Request failed with {ErrorType}; status {StatusCode}; correlation {CorrelationId}` | An API call failed. 403/409/410/400 are expected user outcomes; 500 and 503 are not | Investigate repeated 500/503 by correlation |
| `UI operation failed with {ErrorType}` | A screen action failed; the user saw a French explanation | Same as above |
| `Collection job failed with {ErrorType}; attempt {Attempt}; correlation {CorrelationId}` | A job attempt failed; it is retried or ends `Failed` with a code (below) | Act only on repeated or non-retryable failures |
| `Collection dispatcher SQL failure {Code}` (Error) | The worker cannot claim jobs. `229` means a missing permission | See [SQL permission errors](#sql-permission-errors) |
| `Collection lease renewal failed with SQL code {Code}` | A running job could not renew its lease and will be reclaimed | Check SQL health if repeated |
| `Retention sweep of a workspace failed with {ErrorType}; the next sweep resumes it` | One workspace's sweep step failed; others continued | Check SQL/Blob health; the next sweep retries |
| `Retention sweep failed with {ErrorType}; correlation {CorrelationId}` | The whole sweep failed | Same; purges are delayed, not lost |

### Jobs

Collection, import, export and deletion jobs are persistent rows claimed by a
45-second lease. Retryable failures (`RetryableCollectionException`, SQL deadlock
1205 or timeout) are re-queued after `2^attempt × 5` seconds, up to 3 attempts. A
restart or crash is recovered automatically when the lease expires; no operator
action is needed. Codes shown to users and stored on the job:

| Code | Meaning | Who acts |
|---|---|---|
| `Throttled`, `RetryAfterLimit` | Microsoft Graph kept throttling, or asked to wait more than 60 s | Consultant relaunches the section later |
| `AccessDenied` | The requester lost access between launch and execution | Workspace manager, if access should be restored |
| `CancelledOrLeaseLost` | Cancelled by a user, or the lease moved to another attempt | Nobody, unless it repeats |
| `SelectionSuperseded` | The project changed while an export was generated; no file was kept | Consultant requests the export again |
| `RawFilePurged` | A staged upload's file passed its retention before confirmation | Consultant uploads again |
| `EnvironmentOrScopeMismatch` and other contract codes | The package does not match the project or environment | Consultant fixes the package ([discovery](discovery.md)) |
| `ProjectJobsRunning`, `StorageUnavailable`, `DeletionBlocked` | A deletion stage must be retried ([retention and deletion](retention-deletion.md#deletion-job)) | Automatic; after 3 attempts a manager requests deletion again |
| `ProcessingFailed` | Unexpected failure | Operator, by correlation |

Consultants retry failed or cancelled collection and import jobs from the
Discovery jobs list (`JobRetried`). Export jobs have no cancel or retry control;
a failed export is requested again ([exports](exports.md)).

### Retention

- The retention sweep runs in the web process 5 minutes after start, then every
  `SweepIntervalHours` (default 6). A stopped app performs no purge.
- Archived projects past `ArchivedProjectDays` show the badge "durée de
  conservation après archivage atteinte" in the projects list. Deletion stays a
  manager's decision; nothing is deleted automatically.
- Settings and their ranges are in [retention and deletion](retention-deletion.md).
  Record the chosen values with the release that introduces them.

## Incident handling

General rules:

- Ask the user for the correlation identifier, time and screen. Never ask for,
  paste or attach tokens, cookies, raw Graph responses, exports or customer files
  in tickets or chat.
- Keep row-level security on. Never disable `Security.ClientBoundary` to
  investigate; set the session context for the workspace instead (below).
- Record every manual SQL or Blob change in the change log with its reason,
  because changes made outside Clarity write no Clarity audit event.

### SQL investigation inside one workspace

Row-level security filters every session, including administrators. To read or
change one workspace's rows as the SQL administrator, set its scope for the
session and clear it afterwards:

```sql
EXEC sys.sp_set_session_context N'clarity.workspaces', N'|<workspace-id>|';
SELECT Id, ProjectId, Kind, State, ErrorCode, Attempt, CorrelationId, CreatedAtUtc
FROM dbo.CollectionJobs WHERE CorrelationId = '<correlation-id>';
EXEC sys.sp_set_session_context N'clarity.workspaces', NULL;
```

Read only what the incident needs. Do not export inventory rows from production.

### SQL permission errors

SQL error 229 (a SELECT, INSERT, UPDATE or DELETE permission was denied) in the
dispatcher log, or a sudden `SqlException` failure of one feature right after a
release, means the application database user lacks a grant. The usual cause is
a release that added a table whose `GRANT` line in `infra/bootstrap-sql.sql` was
not applied. As the SQL administrator, apply the missing `GRANT` lines from that
file with the same name substitution as the bootstrap (skip `CREATE USER`, which
already exists; `GRANT` and `DENY` are repeatable). Never grant `db_owner`,
`db_datawriter` or schema-wide permissions as a shortcut. Compare with the
expected set:

```sql
SELECT p.state_desc, p.permission_name, p.class_desc,
    CASE p.class WHEN 1 THEN OBJECT_NAME(p.major_id) WHEN 3 THEN SCHEMA_NAME(p.major_id) END AS securable
FROM sys.database_permissions p
JOIN sys.database_principals u ON u.principal_id = p.grantee_principal_id
WHERE u.name = N'<application-database-user>' ORDER BY securable, p.permission_name;
```

### Throttling, restarts and slow collections

- Throttling and restarts are handled by job retries and leases
  ([resilience and performance](resilience-performance.md)). Two workspaces that
  declare the same Microsoft 365 tenant share Graph throttling.
- Imports and publications are serialized per environment; a large shard occupies
  its environment for the durations measured in
  [resilience and performance](resilience-performance.md#measured-timings).
- Restarting the app is safe at any time: running jobs are reclaimed after the
  lease expires and resume without duplicates.

### A project stuck in "Suppression en cours"

A deletion that failed three times leaves the project `Deleting` and unreadable.
Find the job by searching the logs for the deletion failure, or query
`dbo.CollectionJobs` in that workspace for the project's failed deletion job.
Fix the cause (running job, storage outage, racing insert), then a workspace
manager requests deletion again with the project's exact name. The new attempt
restarts from the drain stage and is audited `ProjectDeletionRetried`.

### Suspected cross-client access

1. Treat it as a security incident. Collect correlation identifiers, times and
   the identities involved; do not copy the data seen.
2. Remove the suspected member's access (below) or the Entra enterprise
   application assignment. Authorization is re-read on every operation and jobs
   re-check membership at execution time, so removal takes effect immediately.
3. Review the workspace's audit trail (`dbo.AuditEvents`, in the workspace scope)
   for the actor's tenant and object identifiers. The audited actions are listed
   in the [isolation audit](isolation-audit.md#audit-trail-review).
4. Confirm the security policy is intact: `SELECT is_enabled FROM sys.security_policies
   WHERE name = 'ClientBoundary'` must return 1.

### Accidental or unwanted data loss

- Blob files removed by retention or deletion stay recoverable from versions and
  soft delete for roughly 7 to 15 days
  ([retention and deletion](retention-deletion.md#blob-versions-and-soft-delete)).
- SQL point-in-time restore goes to a **separate** database, within the database's
  backup retention (the Azure SQL default unless changed at provisioning).
  Compare and copy back only reviewed rows, inside the workspace scope. Never
  restore over production without a reviewed plan.

## Access administration

| Change | How | Audit |
|---|---|---|
| Create a client workspace | Platform administrator, home page | `WorkspaceCreated` |
| Assign a member or change a role | Platform administrator or workspace manager, home page | `WorkspaceMemberAssigned` |
| Archive a workspace | Platform administrator, home page | `WorkspaceArchived` |
| Remove a member | SQL administrator (no UI or API) | Change log |
| Add or remove a platform administrator | SQL administrator (no UI or API) | Change log |
| Block sign-in entirely | Remove the user's assignment to the Entra enterprise application | Entra audit log |

Platform administration never grants inventory access: a platform administrator
who must work on a client is assigned as a member like anyone else.

Removing a member (the `Members` table has no foreign key from inventory data):

```sql
EXEC sys.sp_set_session_context N'clarity.workspaces', N'|<workspace-id>|';
DELETE FROM dbo.Members
WHERE WorkspaceId = '<workspace-id>' AND TenantId = '<tid>' AND ObjectId = '<oid>';
EXEC sys.sp_set_session_context N'clarity.workspaces', NULL;
```

Adding or removing a platform administrator uses `dbo.PlatformAdmins (TenantId,
ObjectId)`, as in the bootstrap's last step. Keep at least one platform
administrator. The application identity can only read that table.

## Secrets

The only server secret is the web OIDC credential, stored as Key Vault secret
`clarity-oidc` and resolved by App Service through the app identity. Graph
connections and local scripts carry no stored secret. To rotate it:

1. Create a new credential on the web app registration, with an expiry alert.
2. From private access, temporarily take Key Vault Secrets Officer and add a new
   version of `clarity-oidc` without echoing it in terminal history or logs.
3. Force App Service to refetch the versionless reference with an authenticated
   `POST https://management.azure.com/<web-app-resource-id>/config/configreferences/appsettings/refresh?api-version=2022-03-01`
   (otherwise it refetches within 24 hours). Check the reference status in the
   app settings, sign-in and `/health/ready`.
4. Delete the old credential, then remove the temporary Secrets Officer role.

## Releases and rollback

- A release is a successful main CI run's `clarity-linux-<commit>` artifact. The
  `deploy.yml` workflow applies its EF bundle as the migration identity, deploys
  the web ZIP as the deployment identity and requires both health endpoints to
  return 200 ([deployment](deployment.md#5-release-execution-and-validation)).
- Deployment runs only when the repository variable `AZURE_DEPLOY_ENABLED` is
  `true` and the environment's reviewers approve. Enabling the gate is the
  user's decision.
- After each release: check both health endpoints, sign in, open one authorized
  project, and confirm a guessed identifier from another workspace is refused.
- Roll back by redeploying the last healthy artifact's web ZIP, never by running
  an older bundle or a down migration. The full procedure is in
  [deployment](deployment.md#schema-changes-and-rollback).

## Schema-change strategy

Every schema change ships as a reviewed EF migration in the release that needs
it, and stays compatible with the previous application release so that the
application can be rolled back without touching the schema.

1. **Expand, then contract.** Add tables and nullable or defaulted columns first;
   deploy readers and writers that tolerate both shapes; backfill separately;
   remove obsolete schema in a later release, after the rollback window.
2. **Keep isolation in the same migration.** A new workspace-owned table has
   `WorkspaceId`, composite foreign keys `(WorkspaceId, ...)`, and filter and
   insert/update block predicates added to `Security.ClientBoundary`. Do not
   weaken `Security.AuditRetention`. CI enforces this through
   `RowLevelSecurityTests`.
3. **Grant it in the same release.** Add the table's `GRANT` line to
   `infra/bootstrap-sql.sql` with the narrowest permissions it needs.
   `DatabaseGrantTests` fails CI when a model table has no grant or a grant has
   no table, and the SQL tests run as a login holding exactly those grants.
4. **Delete it with its project.** Add the table to the controlled deletion
   order; `RetentionDeletionTests` fails CI otherwise.
5. **Apply in order.** For a release that adds a table, run the reviewed bundle
   from the private runner as the migration identity (as in
   [bootstrap step 2](deployment.md#3-private-access-sql-bootstrap-and-oidc-secret)),
   apply the new `GRANT` lines as the SQL administrator, then approve the
   deployment. The workflow's bundle run then finds nothing to apply. Approving
   first also works, but features using the new table fail with error 229 until
   the grants are applied.
6. **Never migrate from the web process, never down-migrate.** Review the
   generated SQL, RLS changes and backup position before approval. A failed
   migration stops the deployment; inspect `__EFMigrationsHistory` privately
   before retrying the repeatable bundle.
7. **Known constraint.** Releases before the retention migration do not know the
   `Deleting` project status; do not roll back below it while a deletion is
   pending.

## Retention and deletion operations

- Choose the `Retention__*` values before admitting pilot data and record them;
  out-of-range values stop startup
  ([retention and deletion](retention-deletion.md#retention-per-category)).
- Archival, deletion requests and their outcomes are audited per workspace.
  Deletion removes SQL rows and Blob files of the project only; workspace-level
  data and the audit trail stay.
- Retention applies to Clarity's own storage. Downloaded copies, App Service
  logs, Application Insights and SQL backups follow their own retention; align
  them with the chosen values during the pilot.
