# Discovery

## Contracts, catalog and persistence

ZIP v1 is defined in `contracts/zip-v1`; `catalog` declares stable capabilities,
field reference producers and readiness. Only validated, non-preview producers
are eligible by default. The original French specification remains authoritative.

Connections belong to a client workspace and real tenant, independently of an
individual dossier, so authorized projects can reuse them. They store only a
server configuration key, not a certificate, token or local-script credential.
Collection requests bind a project and environment to an immutable producer/script
version, exact scope, section selection and requesting identity.

Every new Discovery table uses a workspace composite primary key, EF query filter
and SQL filter/block predicates. Environment and request references additionally
include project to prevent accidental same-client cross-dossier attachment.
External run IDs are unique per project/environment; content hashes are unique
per project/environment/exact scope. Scope comparison is exact and conservative:
the canonical `{kind,value}` JSON is SHA-256 hashed, with no speculative equivalence
between distinct OU DNs, URLs or resource IDs.

A run records the original manifest, producer and version, observed dates, arrival
channel, actor and correlation. Each section retains its own verified scope,
coverage and counters. `PublishedAtUtc` is separate from arrival/confirmation so
unpublished results cannot replace valid observations. Raw artifacts reference a
private workspace-prefixed Blob name and SHA-256 with compressed/uncompressed
byte counts. Neither blob names nor credentials are public download URLs.

Apply migrations explicitly with the operator identity as described in
`local-development.md`; app startup never migrates production implicitly.

## Atomic section publication

`PublicationService` is the common observation sink. It requires current project
collection authorization, a confirmed run, matching manifest/section metadata and
the actual declared environment. A SQL transaction and an environment-scoped
transactional application lock serialize stable-key creation and section
publication. The unique section marker makes retries and competing publications
idempotent. An identity conflict rolls back the entire section.

The store appends object and relationship observations; it never replaces prior
valid data when staging, failing or publishing partial data. Failed/NotApplicable
sections publish their coverage only. Stable keys use environment/namespace/type/
external ID and ordinal SQL collation. [Consolidation](consolidation.md) retains
alternate keys independently, including ambiguous values and rename history.
Relationship endpoints may remain unresolved.
UPN, SMTP, status and observation dates are indexed alongside original JSON.
The publisher also refreshes the current-value, relationship and association
projections in the same transaction; the [consolidation matrix](consolidation.md)
documents the implemented rules and dossier screens.

## Durable jobs

SQL holds job state, requesting identity, correlation, attempts, progress,
cancellation, availability and a fenced lease token. A short SQL application lock
serializes claims; an active provider/tenant concurrency key excludes another
claim with that key across processes. Leases last 45 seconds and are renewed every
10 seconds. The processor retries explicitly retryable errors at most three times
with bounded backoff; explicit relaunch resets that attempt budget, not published
sections. Host interruption leaves a reclaimable lease. Stale lease holders cannot
renew or mark a replacement attempt complete.

The dispatcher has a narrowly scoped SQL session flag granting read/update of
job **metadata only**, not inventory, connection credentials or workspace content.
It cannot insert jobs. Every normal connection open resets this flag, including
pooled reuse. A claimed job creates a fresh workspace context for its recorded actor
and rechecks stored membership, project and active workspace before handling data.
There is no platform-admin inventory bypass or fake worker membership.

Handlers check lease/cancellation between idempotent sections, and publication
rechecks authorization per section. Cancellation preserves already committed
observations. Processing errors store sanitized codes and log type/attempt/
correlation only. Payloads contain internal IDs and collection metadata, never
tokens or raw service responses. SQL integration tests interrupt the processor
after publication and resume it with a new lease, rather than testing only a
happy-path handler call.

## ZIP upload and confirmation

The upload boundary authorizes the project and actual environment before streaming
to the private `raw-artifacts` Blob container. It never extracts files or executes
scripts. Exactly four case-sensitive root names are allowed; extra files, duplicate
names, directories and alternate paths are rejected. UTF-8 decoding is strict.
Limits are 16 MiB compressed, 128 MiB total expanded, 64 MiB per entry, 2 MiB per
manifest/results document, 150,000 observation lines and 65,536 characters per line.
Both advertised ZIP lengths and bytes actually read are bounded. The compressed
archive is buffered only after a bounded Blob upload so ZIP seeking is portable.

The server verifies SHA-256 on every read for preview/processing, catalog fields,
observed environment and optional request binding. Initial AD and Entra producers
use one exact requested domain/OU/tenant scope; differently scoped results are not
silently treated as equivalent. A preview includes section coverage/errors,
object/relationship samples, and new versus already-known source identities.
It does not predict Lot 3 matching decisions or deletions.

Confirmation atomically creates a single import job with the confirmation actor
and marks the run confirmed. No observations exist before confirmation. Parallel
identical content deduplicates under the environment lock; a reused run ID with
different bytes is a conflict. Rejected/duplicate uploads delete their temporary
Blob; abrupt host death may leave an unreferenced Blob for operator retention
cleanup before a real pilot. Raw downloads reauthorize the project; no SAS URL or
Blob reference is handed to the browser.

## Graph Entra

The [Graph connector](connectors/microsoft-graph.md) produces the same canonical
packages and uses the same atomic ingestion pipeline. Tenant verification precedes
collection. SQL stores private delta continuations; a prepared package survives
worker interruption and cursors advance only after publication. Graph launch is an
authorized collection action, while uploaded artifacts require explicit confirmation.
The connection carries a server configuration reference only. Live consent,
managed identity and Key Vault certificate access remain operator configuration.

[Intune and Windows Autopilot](connectors/intune-autopilot.md) use a separate
v1.0 Graph producer and per-family connection for the same verified tenant. Its
access tests and section selections are independent of Entra. It uses full
paged reads with explicit visibility limits rather than directory delta cursors.

[Applications and SSO](connectors/applications-sso.md) have their own v1.0 Graph
producer and access checks, retaining application/service-principal identities
and typed app-role assignments. Consultant qualifications live in independent,
authored assessment history on the object sheet, never Graph-observed access.

PowerShell collectors share one registry-driven request, bundle download, upload and
UI path. The server infers the producer from the requested sections (sections of
different producers cannot be mixed), serves the family's own deterministic bundle
only while its catalog version matches the request, and the Discovery screen lists
the producers that have a published bundle. A new family is registered by files
alone; see [Registering a PowerShell collection family](../scripts/README.md).

The [Exchange Online script](connectors/exchange.md) is a second PowerShell
producer next to AD. Its request is bound to the tenant itself, the producer and
the exact section set, its bundle is a separate deterministic ZIP, and the
operator-owned read-only Exchange session is checked against the tenant before
any read. Result ZIPs use the same upload, preview, confirmation and publication
path as AD.

## Authorized Discovery screens

The Fluent UI v5 Discovery panel exposes French Connexions, Collectes and Couverture
views, separated by the selected source/target environment. Grids page and sort
in SQL. Coverage units are capability plus exact scope, never a conflation of
different domains/OUs. Pending results do not replace a last-valid link; failed
counts stay unknown. Raw proof, portable parameters and immutable bundles use
reauthorized, no-store download endpoints without public Blob URLs.

All mutation endpoints validate antiforgery tokens. Interactive callbacks use the
same authorized services; hiding a button is never the authorization boundary.
Script requests and Graph launches use stable operation IDs. Uploading result
bytes triggers no execution and requires explicit confirmation before publication.
Preview/list DTOs intentionally omit private Graph continuations and Blob paths.

The worker is registered only when both SQL and Blob configuration are present;
missing configuration produces explicit disabled actions, never local persistence.
Job state is authoritative for execution/history and is synchronized to its run
on authorized completion, cancellation and retry. If membership was revoked, the
dispatcher still records the terminal job metadata but must not update inventory
through a former member's scope. Existing observations remain untouched.

## Lot 2 acceptance coverage

| Journey | Executable evidence |
| --- | --- |
| ZIP v1 structure, references, absent/empty values and version rejection | `Clarity.Contracts.Tests` |
| Valid and partial Graph publication through the actual ZIP/Blob/publisher pipeline, cursor retention on failure, interrupted-job resume | `GraphTests.GraphUsesCommonIngestionSurvivesRestartAndPersistsCursorOnlyAfterPublication` |
| Identical parallel ZIP uploads integrated once, run-ID conflict, wrong environment/request/workspace, partial package | `ZipImportTests` |
| Previous observations survive pending/failing sections and section rollback is atomic | `PublicationTests` |
| Competing leases, restart, stale fencing, bounded attempts, cancellation/retry and preserved sections | `JobTests` |
| Portable parameters, immutable separate bundle, AD range/primary group/external-reference behavior | `ScriptRequestTests`, Pester and generated-output `PowerShellContractTests` |
| A second consultant downloads/uploads/previews/confirms a request; coverage retains last-valid proof through failure | `DiscoveryEndpointTests.SqlSeparateMachineJourneyDownloadsUploadsPreviewsConfirmsAndPreservesCoverage` |
| Server authorization, antiforgery, French screens, private cursor exclusion and SQL isolation | Web endpoint tests and SQL RLS/filter tests |

CI executes the SQL cases on disposable SQL Server and runs Pester before validating
its generated packages. Local SQL skips are intentional when `CLARITY_TEST_SQL`
is absent. Live AD/Graph/Key Vault validation is an operator pilot, not a claim made
by synthetic acceptance.

## Administrative report imports

Administrative exports use versioned [import profiles](import-profiles.md), not the
ZIP v1 upload. The first profiles interpret the
[Microsoft 365 usage reports](connectors/microsoft-365-usage-reports.md) for
mailboxes, OneDrive accounts and SharePoint sites in an M365 tenant environment.
They reuse the same private Blob retention, preview, explicit confirmation, leased
job and `PublicationService` path, arrive on channel `ReportImport`, and never count
as a verified Graph connection. The Exchange Online PowerShell family is documented
in [Exchange Online](connectors/exchange.md); the other Lot 4 collection families
(PnP, Teams, Power BI and Azure) are not implemented.

The separate [Office/GPO PowerShell family](connectors/office-gpo.md) records
direct GPO links, security-filter principals and supported Office 16.0 user
settings for a verified domain or OU subtree. Its ZIP references existing AD
identities without enumerating AD users, computers or groups.
