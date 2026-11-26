# Consolidation acceptance

## Identity

Only an environment's exact namespace, type and primary external ID determine
internal identity. An alternate reference is evidence, never an instruction to
merge. The publisher indexes original and normalized alternate values with
half-open observation intervals `[from, until)` per producer, capability, verified
scope and field. A missing field does not close a period; an explicit empty
attribute does. Opaque identifiers and URL paths retain case. SMTP aliases keep
their original prefix; comparison ignores SMTP prefix and address case.
An alternate reference without a subsequent value in its namespace remains
last-known, not verified forever. An older run can arrive after a rename.

Existing AD SID / Graph SKU keys remain stored on upgrade; the migration refuses
to guess the primary namespace for an unrecognized multi-key legacy object.
Re-publication of an object indexes its retained attribute history. Lot 2 did
not retain a per-observation list of alternate references outside the raw ZIP;
do not attribute a legacy alias to a fabricated observation.

| Journey | Executable acceptance |
| --- | --- |
| Rename, out-of-order history, shared UPN and source alias, SQL isolation | `PublicationTests.RenameAndLateObservationKeepStableIdentityAndAmbiguousAliasesSeparate` |
| Invalid section rolls back without affecting prior inventory | `PublicationTests.InvalidEnvironmentAndMidSectionFieldErrorPublishNothing` |

Run `dotnet test --project tests/Clarity.Infrastructure.Tests --filter FullyQualifiedName~PublicationTests` with
`CLARITY_TEST_SQL` pointing to a disposable SQL instance. These SQL cases skip
without it. All observations are synthetic.
