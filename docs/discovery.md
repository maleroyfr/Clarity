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
