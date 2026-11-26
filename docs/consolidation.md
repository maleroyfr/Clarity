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
means not observed. A successful Graph user delta is comparable for its
explicitly reported fields, never for omitted objects or fields. A `Partial`
[report import](import-profiles.md) section whose only errors are rejected rows is
comparable for the rows it converted, because each row is all-or-nothing; it never
supports absence because report sections always carry a coverage warning.
Comparable streams that disagree retain both observations
and flag the current field. The winning observation carries the freshness date.

Presence is separate from values. A reference-producer deletion signal is dated
evidence. Inferring absence instead requires non-overlapping, complete snapshots
from the same producer/version, connection, capability and exact scope with the
entire catalog field/relation set and no warnings. Every independently observed
stream must support absence. Such objects are *absent from scope*, not known
deleted. Graph user delta never supplies negative evidence by omission, even on
cursor restart. Historical fields and proof survive either kind of removal.

[Power BI](connectors/powerbi.md) publishes operator-visible evidence only:
an empty workspace or gateway list cannot withdraw a previously observed object
or link. Owners and workspace role holders point to typed identity references in
the same environment; access rights are not derived from those links.

| Journey | Executable acceptance |
| --- | --- |
| Graph delta, interrupted publication, complementary confirmed ZIP, field precedence/conflict, explicit empty vs absent and rebuild | `GraphTests.GraphUsesCommonIngestionSurvivesRestartAndPersistsCursorOnlyAfterPublication` |
| Partial, field-limited and differently scoped zero versus comparable exhaustive zero | `PublicationTests.AbsenceRequiresAnExhaustiveComparableSnapshot` |
| A later partial report updates its rows and keeps prior values, presence and a validated decision | `ReportImportTests.PartialReportKeepsPriorValuesPresenceAndValidatedHumanDecisions` |

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
The [Azure and infrastructure scripts](connectors/azure-infrastructure.md) publish
ARM and host containment separately from Windows service dependencies and IIS
pool assignments. None are membership or effective connectivity; unknown
endpoints keep their source reference and proof.
Delegation, ownership and qualification are not transitive membership or proof
of effective access. Later workloads can supply other typed edges; this slice
does not collect them.

| Journey | Executable acceptance |
| --- | --- |
| Confirmed ZIP, pending endpoint becoming resolved, deduplicated evidence, cyclic nested groups and reverse navigation | `PublicationTests.MembershipPathsResolveLaterArrivalsPreserveProofAndTerminateOnCycles` |

## Same-side associations

A consultant attests the actual sync anchor for a specific AD forest and tenant
in this dossier, recording the reason. There is no default. The two supported
encodings are AD objectGUID bytes in GUID byte order and the original 16
mS-DS-ConsistencyGuid bytes reported by the AD script. Compare to the decoded
Entra `onPremisesImmutableId`. Automatic validation requires a unique pair,
nonconflicting directly collected Graph sync-enabled and anchor evidence, and
agreement of the two SIDs when both are present. SID alone requests review.
Exact unique UPN/SMTP proposes; it does not validate or merge. Hybrid device
ID can confirm an AD computer only when Graph reports the matching device ID
and ServerAd trust type. Name/serial evidence is a review hint.

All candidate comparisons stay on the same side, never source versus target.
The matcher fingerprints critical values, trust and competing candidate IDs,
not ingestion timestamps. An identical observation does not erase a decision.
A changed candidate or critical value adds a dated review revision; prior author,
justification and proof remain. Project-level transactional locking serializes
cross-environment recomputation and human decisions. Revision checks reject stale
reviews and competing validated associations.

| Journey | Executable acceptance |
| --- | --- |
| Configured byte anchor through Graph, reviewed UPN preserved on identical evidence, affected decisions reopened on ambiguity/config change | `GraphTests.VerifiedConsistencyAnchorAndReviewedUpnSurviveUnrelatedRefreshButNotANewCandidate` |

## Provenance

The proof panel reauthorizes the dossier on each read. It shows the actual
arrival channel (server Graph connection, ZIP or administrative report import),
declared source and producer version, modules, observed environment/date, verified
scope, fields, relations and section outcome. A ZIP or report additionally
identifies the original filename, uploader's stable identity and upload time; a
report also shows its profile/adapter versions and whether scope was supplied by
the consultant. It does not fetch raw files or
return Blob names, tokens, continuation JSON or credential configuration.
Relationships show endpoint resolution and all supporting source sections;
mapping histories distinguish dated human justifications from rule revisions.

The Graph/ZIP and configured-anchor SQL acceptance cases above exercise proof
lookups, both channels, human history and guessed IDs from another workspace.

## Inventory search

Inventaire uses indexed prefix search on names, primary IDs and dated alternate
keys, including former addresses. Exact filters cover side, environment, domain,
site, object type, presence/account status and collection run. The domain is
the observed domain scope or, if none is available, the reported UPN suffix.
Site is only populated from a reported site field, not a presumed location.
The collection selector suggests recent runs and accepts an older internal run
ID. Count, stable sorting and page boundaries are evaluated in SQL. Type-specific
columns avoid implying that a group has a UPN or a device has a primary user.
Select an environment to rebuild its projections from the published history.

`PublicationTests.RenameAndLateObservationKeepStableIdentityAndAmbiguousAliasesSeparate`
queries a former UPN, combines filters and checks a descending second page and
cross-workspace isolation. `ComponentTests.Grid_forwards_paging_and_sorting_to_the_server_provider`
exercises the actual grid sort property and direction.

## Object sheets

Open a sheet from an inventory row; its URL retains the selected object and grid
filters. The shared panel shows source identity and environment, projected fields
and their evidence, conflicting sources, historical observations and alternate
identifier periods. Direct edges support both directions, including unresolved
references. Nested membership is expanded on demand with proof for each hop.
Reviewed associations navigate to the independent AD or Entra sheet without
merging the two source objects. Links to devices, subscriptions, applications or
later resource types appear only when evidence exists: a device has no implied
primary user and basic application metadata does not establish user access.

The [applications / SSO family](connectors/applications-sso.md) links distinct
service principals and applications by typed appId evidence, including later
arrivals. Explicit role assignments are separate nodes so multiple roles between
the same endpoints remain distinguishable. Its authored SSO assessments use the
existing project lock and revision conventions, stay separate from observations,
and flag changed critical metadata for human review without rewriting history.

The [Intune / Autopilot connector](connectors/intune-autopilot.md) preserves
managed and directory devices independently, linking by an exposed managed ID
or Entra `deviceId`. Intune primary users and Entra registered owners remain
distinct directed observations. Neither creates a confirmed AD/Entra decision.

The [Exchange Online script](connectors/exchange.md) keeps Exchange mailboxes as
independent objects in the `exchange.guid` namespace. A user mailbox links to the
user by Entra object ID first, then an exact UPN, then an exact primary SMTP
address; a weaker key is used only while the stronger one is unobserved and an
ambiguous match is never guessed. Shared and resource mailboxes never link to a
user. Graph usage-report mailboxes stay provisional `m365report.upn` objects and
join Exchange objects only through the reviewable association path above.

The [Office/GPO collector](connectors/office-gpo.md) supplies direct OU links by
AD objectGUID and filter principals by SID. Their endpoints can resolve when the
separate AD family publishes evidence; unknown principals remain pending. A
configured Office setting belongs to a GPO, not directly to a computer. A direct
link or a `GpoApply` permission is not a membership or effective policy claim.

The cyclic-membership SQL case above opens the pending and resolved sheets,
follows a reverse link, and verifies both source observations and isolation.

## Association review

Inventaire → Revue des rapprochements opens proposals and cases requiring
attention. The reviewer can inspect both objects and their evidence, attest the
environment pair's source anchor, and view any decision state. Select up to 100
rows and preview validation or rejection. The preview counts actual state
changes, excluding already-satisfied rows. Confirmation reauthorizes, locks the
dossier and rechecks IDs, revisions, count and one-to-one conflicts. A stale or
mixed-workspace selection changes nothing.

To correct a link, copy the internal ID from the replacement Entra sheet in
the same tenant, preview the named replacement, and confirm with a reason. The
original rejection and new validation commit together. Both histories remain;
no unrelated validated link is silently displaced. The configured-anchor SQL
case above also exercises batch counts, stale and foreign confirmations,
manual correction and preservation after a projection rebuild.

## Overview and acceptance run

Vue d’ensemble counts observed users, not accounts in an entire unobserved
tenant. A validated AD/Entra pair contributes its observed Entra representative
once; a proposal or ambiguity never silently merges the population. Known
license units are summed per observed SKU resource only for nonconflicting,
reported fields. Each total links to those exact rows and shows contributing
versus observed subscriptions. When no measurement is known, the overview
displays *unknown*, not zero. No mailbox or storage estimates are inferred.

Latest published coverage is grouped by environment, capability and exact
scope; its dated link opens that section’s observed rows and its proof explains
failed/partial outcomes. Recent property changes link to sheets. Pending links
include references with **neither** endpoint resolved. Review counts use the
same state selection as the queue. A planned target has no pretend inventory.

| Acceptance | Executable case |
| --- | --- |
| Deduplicated population and actionable decision count | `GraphTests.VerifiedConsistencyAnchorAndReviewedUpnSurviveUnrelatedRefreshButNotANewCandidate` |
| Measured zero, unknown SKU, partial totals, contributing grid and exact section drill-down | `GraphTests.OverviewReconcilesKnownLicenseUnitsAndUnknownValuesToTheirFilteredRows` |
| Pending-edge count resolves to its row | `PublicationTests.MembershipPathsResolveLaterArrivalsPreserveProofAndTerminateOnCycles` |
| Second consultant’s Discovery → dashboard → object sheet and mapping queue, using real SQL and the worker | `DiscoveryEndpointTests.SqlSeparateMachineJourneyDownloadsUploadsPreviewsConfirmsAndPreservesCoverage` |

Run the whole Lot 3 matrix from the repository root:

```powershell
dotnet test -c Release --filter 'FullyQualifiedName~PublicationTests|FullyQualifiedName~GraphTests|FullyQualifiedName~ComponentTests|FullyQualifiedName~DiscoveryEndpointTests|FullyQualifiedName~RowLevelSecurityTests|FullyQualifiedName~DiscoveryModelTests'
```

Set `CLARITY_TEST_SQL` to a **disposable** SQL instance to execute SQL cases;
without it, SQL integrations skip locally. CI also runs the entire baseline,
Pester output contracts, migration bundle twice, Bicep and self-contained Linux
smoke. Use a verified same-side forest/tenant anchor in the review screen;
leave it unverified when unavailable. No production tenant consent, live AD
module run, pilot-sized throughput, or workload not in the catalog is certified
by this matrix. Large dossiers merit a timed pilot: read grids page in SQL;
association refresh and relationship reconciliation process the relevant
project/environment evidence during publication. Upgrade existing inventories
by selecting each environment in Inventaire and recalculating from its published
observations. The migration does not manufacture historical evidence.
