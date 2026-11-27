# Comparisons

This guide covers the Lot 5 comparison screen. Its first view compares two
collections of one environment. The French product specification
(`docs/specification.md`, "Comparaison" screen, "Comparer deux collectes d’un même
environnement") remains authoritative; this page records the implemented choices.

The screen is at `/projects/{projectId}/comparison` (with `?side=Target` for the
target side). Business text is French, from `UiStrings.resx`.

## Comparing two collections

A consultant with the qualify right on an active project picks an environment, a
baseline collection and a later collection. Only completed collections with at least
one published section of that project and environment are offered, and only
environments whose target state is `Existing` (a planned target has no collections).
The baseline must start strictly before the later collection, and both must differ.

`RunComparisonService.CompareAsync` reads the published sections of both runs, their
immutable object observations and relationship identity hashes, and calls the pure
`RunComparisonEngine`. The result is a snapshot (`RunComparison`, `RunComparisonItem`)
that never changes evidence or decisions. Comparing the same pair again replaces the
earlier snapshot. The call is idempotent per operation ID, serialized per project,
and writes a `Comparison.RunsCompared` audit event.

Limits: at most 200,000 object observations and 500,000 relations per run. Field
values are truncated to 500 characters, and at most 100 changed fields are kept per
object. Unchanged objects are counted but not stored.

### Comparison units and coverage

A comparison unit is one capability and one collection scope (`CapabilityId`,
`ScopeHash`). For each unit, the screen shows its comparison status, the coverage on
both sides, object counts and relation changes.

| Status | Meaning |
|---|---|
| Comparable | Both runs published exactly one section of the unit, in the same stream (producer, producer version, capability, scope, connection), and both sections are exhaustive. |
| Not exhaustive | At least one side is partial, failed, has warnings or errors, does not cover every catalog field and relation, or is `entra.users` (whose incremental collection never proves absence). |
| Different stream | The producer, its version or the connection changed between runs. |
| Baseline only / Later only | The unit was published by one run only. |

Exhaustiveness uses the same rule as the current projection (`ProjectedSection.IsExhaustive`):
complete coverage with no warnings or errors, and covered fields and relations equal to the
catalog declaration.

### Change classes

| Class | Rule |
|---|---|
| Added | The object exists only in the later run, and every unit that observed it is comparable. |
| Removed | The object exists only in the baseline and every unit that observed it is comparable, or the later run carries a source tombstone (`deleted = true` from the field's reference producer). |
| Modified | The object exists in both runs and at least one field differs in a capability observed on both sides. |
| Newly observed | The object exists only in the later run, but at least one unit is not comparable. |
| Not observed | The object exists only in the baseline, but at least one unit is not comparable. This is never shown as a deletion. |
| Not compared | The object exists in both runs, but in no common capability. |
| Unchanged | Counted, not listed. |

Field differences use the latest observation per object and capability. When a field is
present on one side only, it counts as changed only if the other side's section was
complete and covered that field. Otherwise the absence is unknown, not a cleared value.

Relations are compared by identity hash. Additions and removals are counted only for
comparable units. Elsewhere, every difference is counted as unknown.

## Decisions to review

A comparison never edits decisions. It flags each changed object for the decision kinds
that the change may invalidate, through `DecisionReview`:

| Decision | Flagged when |
|---|---|
| Scope decision | The object is removed, or a scope-critical field changes (`userPrincipalName`, `mail`, `proxyAddresses`, `sAMAccountName`, `distinguishedName`, `enabled`, `accountEnabled`, `onPremisesImmutableId`). |
| Mapping decision | A user, device or computer is removed, or a field used by the matching rules changes: UPN, mail, proxy addresses, SID, consistency GUID, immutable ID, on-premises SID or sync flag, device ID, trust type, serial number, enabled flags, and the display name (ignored for users). |
| SSO assessment | A service principal or application is removed, or `appId`, `signInAudience`, `servicePrincipalType`, `accountEnabled`, `appRoleAssignmentRequired` or `preferredSingleSignOnMode` changes. |
| Teams chat / telephony qualification | A team is removed or its `visibility` changes. |

Additions, newly observed, not observed and not compared objects never flag a decision.
An unproven absence therefore cannot trigger a review.

The "Décisions à revoir" list joins the flagged objects to their live decisions: scope
decisions first (value and wave), then mapping decisions (state and rule), then the
current revision of each assessment. The item list can be filtered to objects with an
affected decision.

## Authorization, isolation and audit

- Reading runs, comparisons, items and decisions requires read access to the project.
  Comparing requires the qualify right on an active project.
- Workspace context comes from the authorized project, never from a client-supplied
  workspace ID. Runs must belong to the project and environment. A foreign or missing
  comparison or run is denied.
- `RunComparisons` and `RunComparisonItems` carry `WorkspaceId`, composite foreign keys,
  EF global query filters and SQL row-level security policies.
- Each comparison writes one `Comparison.RunsCompared` audit event keyed by its
  operation ID.

## Limits

- A comparison covers one environment and two runs. To compare two producers, compare
  each producer's runs separately; mixing producers produces baseline-only and
  later-only units.
- Field-level comparison is limited to collected attributes. Fields outside a section's
  covered list are unknown on that side.
- The Teams, SharePoint and Exchange coverage limits in the connector guides still
  apply. For example, incoming shared channels and channel members are not collected,
  so their absence is never asserted.
