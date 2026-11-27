# Exports: dated Excel workbook and filtered CSV files

This guide covers the Lot 5 exports: what one export contains, how its dated
observation selection is fixed, how it is authorized, audited and reconciled, and
its limits. The French product specification (`docs/specification.md`, `ExportJob`,
`ExportArtifact`, "La sélection datée des observations est figée au lancement",
"Neutraliser les formules dans les exports") remains authoritative; this page
records the implemented choices.

## What an export produces

A consultant opens **Exports** in a project, chooses the source or target side,
sets the filters and selects **Générer l’export**. A background `ExportJob`
produces three private files:

| File | Content |
|---|---|
| `clarity-<side>-<yyyyMMdd-HHmmss>-summary.xlsx` | Workbook with three sheets: **Synthèse**, **Inventaire utile**, **Exceptions** |
| `clarity-<side>-<stamp>-inventory.csv` | The useful inventory list, same rows and columns as the workbook sheet |
| `clarity-<side>-<stamp>-mappings.csv` | Hybrid and source-target mapping decisions touching the exported objects |

CSV files are UTF-8 with a byte-order mark, use `;` as separator (French Excel)
and `\r\n` line endings; fields that contain `;`, quotes, line breaks or leading or
trailing spaces are quoted. Dates are UTC (`yyyy-MM-ddTHH:mm:ssZ` in CSV,
`yyyy-mm-dd hh:mm:ss` in the workbook). Headers and labels come from the fr-FR UI
resources.

The **summary** sheet records the project, scenario, side, author, selection date,
generation date, selection SHA-256, every filter (with `Tous` for an unset filter),
the counters, the reconciliation checks and the coverage table. The **useful
inventory** has one row per exported object: environment, type, display name, UPN,
SMTP, domain, site, enabled, presence, last observation, evidence capability and
date, scope decision, wave, population, justification, decision date, whether a
review flag is open, whether a value conflicts, and the object identifier. The
**exceptions** sheet lists:

- coverage gaps: published units that are `Partielle` or `Échec`, and catalogued
  synthetic-validated capabilities never collected for the environment;
- conflicting current values;
- open review flags (decisions due for review, see `docs/decisions.md`);
- objects to arbitrate;
- included objects that are not currently observed;
- proposed or to-review mappings;
- ambiguous, absent and unknown source-target comparison outcomes and UPN, SMTP,
  alias or domain collisions (see `docs/comparison.md`);
- dependencies that are shared, to arbitrate or cross-wave in dependency analyses
  (see `docs/scoping.md`).

Clarity does not produce migration-tool import formats. No migration tool schema
has been verified against a vendor reference, so the CSV files are Clarity lists,
not tool-ready import files.

## Filters

The recorded filters are: environment of the chosen side, object type, status
(presence, enabled or disabled), scope decision (all, include, exclude, to
arbitrate, undecided), wave and population. They are normalized (trimmed, empty
values dropped) and stored verbatim as JSON with the export. An environment or a
population from another project or from the other side is refused. The filters
apply in the database; an export is limited to 50,000 inventory rows and fails with
`ExportTooLarge` above that.

## Dated selection

At launch, in one transaction with the queued job and the `ExportRequested` audit,
Clarity captures the selection:

- the latest published coverage unit per environment, capability and scope of the
  chosen side, with its status, run and completion date;
- the catalogued capabilities never collected (uncollected gaps);
- a digest of everything else an export reads: publications, scope decisions and
  their revisions, mappings and their revisions, review flags, populations, target
  comparisons and dependency analyses of the project.

The selection JSON and its SHA-256 are stored on the `ExportJob` with the author,
the launch date (`AsOfUtc`) and the filters. The generator recomputes the
selection before and after reading the data. If anything changed since launch, the
export fails with `SelectionSuperseded` and writes no file: Clarity never mixes
data published or decided after the launch date into an export. Launch a new
export to include the change.

Launch is idempotent per operation identifier: repeating the same request returns
the same export, and reusing the identifier with different filters is a conflict.

## Coverage and reconciliation

Partial, failed or never-collected coverage is reported as a coverage gap, never as
absence of objects. Presence values come from the inventory (`Observé`, `Absent du
périmètre vérifié`, `Suppression observée`, `Inconnu`) and are never inferred from
a missing row in a partial collection.

The summary shows these reconciliation checks, each `Rapproché` (reconciled) or
`Écart à analyser` (mismatch to analyse):

- the inventory total equals the sum of the scope decision breakdown, including
  undecided;
- the inventory total equals the sum of the presence breakdown;
- the exception total equals the sum of the category breakdown;
- the coverage gap count equals partial or failed units plus uncollected
  capabilities.

The inventory CSV row count equals the inventory sheet row count and the mappings
CSV row count equals the mapping counter. Each file's row count, size and SHA-256
are stored as an `ExportArtifact`; the export's object, exception, mapping and
coverage-gap counters are stored on the `ExportJob`.

## Formula injection

Text a spreadsheet would evaluate starts with `=`, `+`, `-`, `@`, a tab or a
carriage return, or the full-width forms of `=`, `+`, `-` and `@`. Such text is
never written as a formula:

- in the workbook, the cell keeps the verbatim text as a string with Excel's quote
  prefix, so the value is displayed and filtered as typed;
- in CSV files, the field is prefixed with an apostrophe (`'=1+1`).

Workbook text that XML cannot carry is replaced with `U+FFFD` and truncated at the
Excel cell maximum.

## Authorization, isolation and audit

- Generating an export requires the `Export` operation: workspace managers and
  consultants on an active project. Readers can list and download exports but not
  generate them.
- Platform administration does not grant export access; downloads need explicit
  workspace membership.
- Every list and download is authorized against the project server-side. A
  download is resolved through the project and export, so an artifact identifier
  from another project or workspace is denied.
- The background job re-authorizes the author before reading and before recording
  results; it runs under the job's workspace scope.
- `ExportJobs` and `ExportArtifacts` carry `WorkspaceId`, composite foreign keys
  and SQL row-level security, like other workspace-owned tables.
- Files are stored in the private `exports` Blob container under
  `<workspace>/<export>/`; the store refuses a blob name outside the caller's
  workspace prefix. Responses are `no-store`.
- Audit events: `ExportRequested` at launch, `ExportGenerated` when files are
  recorded, and `ExportDownloaded` for every download.
- Without SQL and Blob storage configured, the screen shows the storage notice and
  no local substitute is used.

## Limits

- Any project change between launch and generation (a publication, decision,
  mapping, flag, population, comparison or dependency analysis anywhere in the
  project) supersedes the selection, even if it does not touch the filtered
  objects.
- An export is limited to 50,000 inventory rows.
- Export jobs are not cancelled or retried through the discovery job controls; a
  failed export is relaunched with a new operation.
- Generated files have no retention or purge policy yet.
- The author is shown as tenant and object identifiers.
- The population selector lists the first 100 populations of the side.
- Mapping decisions are exported only when one side is in the exported objects.
