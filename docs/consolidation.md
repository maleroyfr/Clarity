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

## Current state

Section publication commits observations, the section marker and its projections
together under the environment lock. Re-delivery is a no-op. To reconstruct an
existing environment, `PublicationService.RebuildEnvironmentAsync` reauthorizes
collection access and rebuilds from published evidence, not staging data.

For each explicitly reported field, sort by catalog reference producer, verified
server connection for that producer, complete coverage, observation time, then
stable run/section identifiers. JSON `null`, `[]` and `""` are values; no row
means not observed. Comparable streams that disagree retain both observations
and flag the current field. The winning observation carries the freshness date.

Presence is separate from values. A reference-producer deletion signal is dated
evidence. Inferring absence instead requires non-overlapping, complete snapshots
from the same producer/version, connection, capability and exact scope with the
entire catalog field/relation set and no warnings. Every independently observed
stream must support absence. Such objects are *absent from scope*, not known
deleted. Graph user delta never supplies negative evidence by omission, even on
cursor restart. Historical fields and proof survive either kind of removal.

| Journey | Executable acceptance |
| --- | --- |
| Graph delta, interrupted publication, complementary confirmed ZIP, field precedence/conflict, explicit empty vs absent and rebuild | `GraphTests.GraphUsesCommonIngestionSurvivesRestartAndPersistsCursorOnlyAfterPublication` |
| Partial, field-limited and differently scoped zero versus comparable exhaustive zero | `PublicationTests.AbsenceRequiresAnExhaustiveComparableSnapshot` |

## Relationships

The relationship projection keeps each directed type distinct and retains its
supporting observation IDs. Resolution stays inside one environment: exact primary
keys first, then unique alternate evidence valid at the observation date. A
technical SID/SKU/device ID may be resolved when its first observation arrives
later; a mutable name is not extrapolated backwards. Unknown or ambiguous
endpoints retain the original typed reference. An explicitly deleted edge or
comparable exhaustive omission retires that stream's support, not another
stream's evidence. No single-owner or primary-user constraint is assumed.

`InventoryGraph.MembershipsAsync` follows only current direct membership and
primary-group edges, in either direction. It returns a shortest evidence path
of relationship IDs for each reachable object and never returns the start node.
Delegation, ownership and qualification are not transitive membership or proof
of effective access. Later workloads can supply other typed edges; this slice
does not collect them.

| Journey | Executable acceptance |
| --- | --- |
| Confirmed ZIP, pending endpoint becoming resolved, deduplicated evidence, cyclic nested groups and reverse navigation | `PublicationTests.MembershipPathsResolveLaterArrivalsPreserveProofAndTerminateOnCycles` |
