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
external ID and ordinal SQL collation. Alternate-key conflicts are explicit rather
than silently merging identities. Relationship endpoints may remain unresolved.
UPN, SMTP, status and observation dates are indexed alongside original JSON.
Full current-value projection, matching and object-detail UI remain Lot 3 work.

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
