# Retention, archival and controlled deletion

The specification (section 9, "Exploitation et sécurité") requires:

> Journaliser les accès sensibles, imports, modifications de périmètre et générations d’export. Paramétrer la durée de conservation par catégorie de fichier et de donnée avant le pilote réel. Permettre l’archivage et la suppression contrôlée d’un dossier, avec traitement cohérent du SQL et des blobs.

This page describes the retention settings, archival and controlled deletion of a
project (dossier), and how SQL and private Blob storage stay consistent. The
values below are synthetic defaults. Choosing the real values remains a pilot
decision (see [deployment](deployment.md#retention-settings)).

## Retention per category

Settings live in the `Retention` configuration section (App Service names such as
`Retention__RawFileDays`). They are validated at startup: an out-of-range value
stops the application rather than being silently corrected.

| Category | Setting | Default | Allowed | Removed when due | Kept |
|---|---|---|---|---|---|
| Raw collection files (ZIP packages and report files) | `RawFileDays` | 180 | 1–3650 | The Blob file, counted from its upload, unless its run still has a queued or running job | The `RawArtifacts` row with its SHA-256 and `PurgedAtUtc`, the run, its sections and every published observation |
| Export files | `ExportFileDays` | 30 | 1–3650 | Every file of an export, counted from its generation | The export record (selection, timestamp, hash, counters), each artifact's name, row count, size and SHA-256, and `FilesPurgedAtUtc` |
| Audit events | `AuditEventDays` | 730 | 30–3650 | Events older than the setting | One `AuditEventsPurged` event per sweep that purged anything |
| Archived projects | `ArchivedProjectDays` | 180 | 1–3650 | Nothing automatically | The project shows "durée de conservation après archivage atteinte" until a manager deletes it |
| Unreferenced files | `OrphanGraceHours` | 24 | 1–720 | Raw or export files older than the grace period that no unpurged raw row or unexpired export references | — |
| Sweep cadence | `SweepIntervalHours` | 6 | 1–168 | — | The first sweep starts 5 minutes after the application |

Unreferenced files come from uploads rejected or interrupted before their row was
recorded, and from export attempts that lost their lease (see
[resilience](resilience-performance.md#export-files-under-restarts)).

### Inventory evidence and decisions

Observations, current values, relationships and their supports, scope decisions
and their revisions, mapping decisions, assessments, review flags, populations,
comparisons and dependency analyses have **no separate retention**. They stay for
the project's lifetime and are removed only with the project. Retention therefore
never deletes an observation that a decision revision or assessment references,
and never clears such a reference. Purging a raw file removes the means to re-read
it, not the evidence published from it.

Consequences of a purged file:

- downloading a purged raw file or expired export returns HTTP 410 with a French
  message explaining the file is no longer kept;
- the exports screen shows "Fichiers expirés le" with the date, and keeps each
  file's name, rows, size and hash, without a download link;
- a staged upload that was never confirmed can no longer be previewed (410) or
  confirmed: its import job fails with `RawFilePurged`. Upload the file again.

## Retention sweep

`RetentionWorker` runs `RetentionSweeper` in the web process when SQL and Blob
storage are configured. A stopped application performs no purge. Each sweep lists
the workspaces, then works per workspace with an RLS-scoped connection:

1. Purge due raw files, skipping projects being deleted and runs with a queued or
   running job. The Blob file is deleted first, then the row is marked purged with
   a `RawFilePurged` audit event in one transaction.
2. Expire due exports the same way, with `ExportFilesExpired`.
3. Delete audit events older than `AuditEventDays` in batches of 4,000, then add
   `AuditEventsPurged`.
4. Delete unreferenced files older than `OrphanGraceHours`.
5. Count archived projects due for deletion.

Every step acts only on rows still due, so an interrupted sweep is completed by the
next one, and a second sweep changes nothing. A failure in one workspace (SQL,
Blob or network error) is logged with its error type and the sweep's correlation
ID, and the other workspaces continue. System events have an empty actor.

Some audit events double as idempotency markers for operation IDs (for example
project creation). Purging an event older than the retention period only stops
an operation ID that old from being recognised as a replay.

### SQL guard on audit deletion

The application identity is granted `DELETE` on `dbo.AuditEvents` for retention
only. The `RetentionAndDeletion` migration adds a `BEFORE DELETE` block predicate,
`Security.AuditRetention`, to the `Security.ClientBoundary` policy. SQL refuses
any delete that touches an event younger than 29 days (the 30-day minimum minus
one day of clock tolerance) with error 33504, whatever the application requests.
No `UPDATE` is granted on audit events.

## Archival

A consultant or manager of the workspace archives an active project from the
projects list (`POST /api/projects/{id}/archive`, audited `ProjectArchived`).
Archiving records `ArchivedAtUtc`; archiving again changes nothing. An archived project stays
readable and downloadable by its members, but accepts no change, collection,
decision or export. Projects archived before this migration get the date of their
latest `ProjectArchived` audit event, or the migration time when that event is
gone, as their archive date.

## Controlled deletion

### Request

Only a **workspace manager** can request deletion, and only of an **archived**
project, by typing its exact name ("Supprimer" in the projects list, or
`POST /api/projects/{id}/delete` with `{"confirmationName": "..."}`).

| Condition | Result |
|---|---|
| Consultant, reader, platform administrator without membership, or another workspace | 403 |
| Active project | 403 |
| Name differs (after trimming, case-sensitive) | 400 |
| SQL job queue or Blob storage not configured | 503, nothing changes |
| Accepted | The project becomes `Deleting`, its other jobs are cancelled, one deletion job is queued, and `ProjectDeletionRequested` is audited, all in one transaction |
| Requested again while its job is queued or running | Nothing changes |
| Requested again after the job failed | A new job is queued and `ProjectDeletionRetried` is audited |

Deletion is allowed in an archived workspace. A project being deleted can no
longer be opened or read: the list shows "Suppression en cours" without a link.
There is no way back to archived or active.

### Deletion job

The worker runs the deletion with the requester recorded on the job, in stages.
Each stage is idempotent, and a failure leaves the project `Deleting` and
unreadable:

1. **Drain.** Cancel the project's other jobs and wait up to two minutes for any
   running one to stop. Otherwise the job retries with `ProjectJobsRunning`.
2. **Files.** Delete every export file, then every raw file of the project. A
   Blob or network failure retries with `StorageUnavailable`.
3. **Rows.** Delete every project-owned table child-first, in batches of 4,000
   rows. A foreign-key conflict (a row inserted by a racing job) retries with
   `DeletionBlocked`.
4. **Project.** In one fenced transaction, delete the jobs, environments and the
   project, and audit `ProjectDeleted` with the requester.

Files are deleted before the rows that reference them, so no surviving row points
at a deleted file. A retry starts again from the drain stage. The job queue makes
3 attempts with 10 and 20 second delays; after that the job is `Failed` and a
manager requests deletion again.

Workspace-level data is kept: the workspace, its members, platform administrators,
Graph connections and the audit trail. The audit trail keeps the deleted project's
events, including `ProjectDeleted`, until audit retention removes them.

### Blob versions and soft delete

The provisioned storage account keeps Blob versions and a 7-day soft delete. A
lifecycle rule deletes previous versions of `raw-artifacts` and `exports` blobs
at the next daily policy run after they are 7 days old (counted from upload);
soft delete then keeps them 7 more days. So a file removed by retention or
deletion stays recoverable by an operator for roughly 7 to 15 days, then is gone.
`script-packages` is not affected.

## Evidence

| Behaviour | Test |
|---|---|
| Every project-owned table is deleted, children before parents, and nothing workspace-level | `RetentionDeletionTests.DeletionStepsCoverEveryProjectOwnedTableChildrenFirst` |
| Archive and deletion state transitions; deletion becomes due after the configured delay | `RetentionDeletionTests.OnlyArchivedProjectsCanBeDeletedAndBecomeDueAfterTheConfiguredDelay` |
| Deletion of a populated project after a storage failure and retry removes all its rows and files, spares the other project and workspace, and is authorized and idempotent (SQL) | `RetentionDeletionTests.DeletingAnArchivedProjectRemovesItsRowsAndFilesAfterAStorageFailureAndSparesEverythingElse` |
| Sweep purges due raw files, exports, audit events and orphans, skips busy runs and projects being deleted, audits, returns 410 codes, is idempotent; SQL refuses to delete recent audit events (SQL) | `RetentionDeletionTests.RetentionSweepPurgesDueFilesAndAuditKeepsEvidenceSkipsBusyOrDeletingProjectsAndIsIdempotent` |
| Only a manager can request deletion of an archived project with its exact name | `AuthorizationEndpointTests.Only_a_manager_can_request_deletion_of_an_archived_project_confirmed_by_its_exact_name` |
| Out-of-range settings stop startup | `AuthorizationEndpointTests.Retention_settings_outside_their_allowed_range_fail_startup` |
| `AuditEvents` carries the `BEFORE DELETE` guard in the SQL catalog (SQL) | `RowLevelSecurityTests.Every_workspace_table_has_an_enabled_filter_and_insert_update_block_predicates` |
| The deletion route denies another workspace's project | `IsolationSweepTests.Every_route_page_and_circuit_service_denies_identifiers_from_another_workspace` |

SQL tests run in CI against a disposable SQL Server. They are skipped locally
without `CLARITY_TEST_SQL`.

## Limits

- Deletion of archived projects past `ArchivedProjectDays` is not automatic: the
  sweep only counts them and the list shows a badge. A manager decides.
- A failed deletion is not shown separately: the project stays "Suppression en
  cours". Operators see the job failure in the logs; a manager requests again.
- Batch deletion and sweep durations were not measured at pilot volume.
- The lifecycle rule and soft delete were not exercised against Azure Storage;
  the tests use in-memory stores.
- Retention applies only to Clarity's own storage. Copies downloaded by users,
  App Service logs, Application Insights and SQL backups follow their own
  retention.
- Rolling back to a release before this migration while a project is `Deleting`
  is unsupported (see [deployment](deployment.md#schema-changes-and-rollback)).
