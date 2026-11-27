# Comparisons

This guide covers the Lot 5 comparison screen. Its first view compares two
collections of one environment; its second view compares the source with the target.
The French product specification (`docs/specification.md`, "Comparaison" screen,
"Comparer deux collectes d’un même environnement" and "Comparer source et cible")
remains authoritative; this page records the implemented choices.

The screen is at `/projects/{projectId}/comparison` (with `?side=Target` for the
target side, and `?view=target` for the source-target view). Business text is French,
from `UiStrings.resx`.

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
affected decision. The current re-review state of every decision against the current
inventory, independent of any comparison, is in the decision register; see
[Decision history](decisions.md).

## Comparing source and target

### Mapping rules

Each project documents its correspondence rules (`TargetMappingRule`). A rule names
an object type (`user`, `group`, `mailbox`, `sharedMailbox`, `resourceMailbox`,
`mailGroup`, `recipient` or `device`), a priority from 1 to 100 and a mandatory
justification. Two kinds exist:

| Kind | Key |
|---|---|
| Shared attribute | A source field must equal a target field, ignoring case (for example `employeeId` → `employeeId`, or `mail` → `mail`). |
| Computed target UPN | The local part of a source field (before any `@`) joined to the rule's target domain, compared with the target `userPrincipalName`. |

Rules are never edited. A change retires the rule with a justification and adds a
new one, so every comparison keeps the exact rules it applied (`RulesJson`). At most
50 active rules exist per project. Adding a rule is idempotent per operation ID.
Adding and retiring write `TargetMapping.RuleAdded` and `TargetMapping.RuleRetired`
audit events.

### Running a comparison

A consultant with the qualify right picks a source and a target environment.
`TargetComparisonService.CompareAsync` reads the current values of observed objects on
both sides, applies the active rules in priority order and calls the pure
`TargetComparisonEngine`. The snapshot (`TargetComparison`, `TargetComparisonItem`)
replaces the previous one for the same pair, is idempotent per operation ID, is
serialized per project, and writes a `TargetComparison.Computed` audit event. It is
recomputed only on request.

The target's data state decides what is compared:

| Data state | Meaning |
|---|---|
| Planned target | The environment's target state is `Planned`. Its documented hypotheses are shown. No target data is read: every source object is "Not compared", and only computed UPNs shared by several source objects are reported. |
| No target data | The target exists but has no published section. Every source object is "Not compared". |
| Data available | The target exists and has published data. Outcomes, collisions and proposed associations are computed. |

Coverage is shown per object type and side: object count, whether a complete
tenant-wide section proves absence, and the rule fields that no published section
collected. Coverage uses the latest published section per capability and scope, and
the same exhaustiveness rule as the collection comparison.

### Outcomes

| Outcome | Rule |
|---|---|
| Matched | Exactly one target candidate, not claimed by another source object. |
| Ambiguous | Several candidates, or one candidate also claimed by another source object. |
| Absent | No candidate, every rule produced a key or the source value is legitimately empty, every target field was collected without conflict, and a complete tenant-wide section of that type was observed. |
| Unknown | No candidate, and absence cannot be proven: the target type is not exhaustive, a field was not collected, a value is conflicting, or the source has no key. |
| Not compared | The target is planned or has no data. |

Each item records its reason (for example `TargetNotExhaustive`,
`SourceAttributeNotCollected` or `TargetPlanned`), the key and the rule used.

### Collisions

| Collision | Reported when |
|---|---|
| UPN | Two source objects compute the same target UPN (`ComputedUpnShared`), or a computed UPN is already used by a target object that is not this object's candidate (`TargetUpnTaken`). |
| SMTP | A source primary or `smtp:` proxy address is held by a target object that is not its candidate (`SmtpTaken`). X500, SIP and other address types are ignored. |
| Alias | A source alias is held by an unrelated target object (`AliasTaken`). |
| Domain | A domain is observed in both tenants (`SharedDomain`), or a computed-UPN rule targets a domain that is not verified in the observed target domains (`TargetDomainNotVerified`). |

Mailboxes and Exchange groups are related to their directory principal (through
`mailboxOf` or a unique `externalDirectoryObjectId`), so an address held by the
mailbox of a matched user is not a collision.

### Source-target associations

When target data is available, each candidate pair becomes a `MappingDecision` of
kind `SourceTarget` (rule `ComputedTargetUpn` or `MappingRuleAttribute`): proposed when
the outcome is matched, to review when it is ambiguous. The decision keeps the rule,
key, candidates, competing source objects and the supporting observation IDs.
Consultants review them in the same mapping queue as hybrid associations, filtered by
kind ("Associations source-cible"). They can validate or reject a proposal, but not
correct it to another target.

The evidence fingerprint covers the rule, key, candidates and competing objects, not
observation IDs. Recomputing with unchanged evidence keeps the consultant's decision.
Changed evidence adds an `AssociationEvidenceChanged` revision to review; a pair that
no longer has a candidate gets `NoCurrentCandidate` and a `CandidateNoLongerObserved`
revision. Earlier revisions remain. Hybrid recomputation, inventory overviews and
dependency resolution consider only hybrid associations, so a source-target
association never merges a source object with a target object.

## Authorization, isolation and audit

- Reading runs, comparisons, items and decisions requires read access to the project.
  Comparing collections or source and target, and adding or retiring mapping rules,
  require the qualify right on an active project.
- Workspace context comes from the authorized project, never from a client-supplied
  workspace ID. Runs must belong to the project and environment. A foreign or missing
  comparison or run is denied.
- `RunComparisons`, `RunComparisonItems`, `TargetMappingRules`, `TargetComparisons` and
  `TargetComparisonItems` carry `WorkspaceId`, composite foreign keys, EF global query
  filters and SQL row-level security policies.
- Each collection comparison writes one `Comparison.RunsCompared` audit event, and each
  source-target comparison one `TargetComparison.Computed` event, keyed by its
  operation ID.

Source-target outcomes that need attention (ambiguous, absent, unknown) and
collisions, plus pending mappings, appear in the exceptions sheet and mappings CSV
of an [export](exports.md).

## Limits

- A comparison covers one environment and two runs. To compare two producers, compare
  each producer's runs separately; mixing producers produces baseline-only and
  later-only units.
- Field-level comparison is limited to collected attributes. Fields outside a section's
  covered list are unknown on that side.
- The Teams, SharePoint and Exchange coverage limits in the connector guides still
  apply. For example, incoming shared channels and channel members are not collected,
  so their absence is never asserted.
- The Entra connector does not collect `employeeId` or `mailNickname`. A rule on an
  uncollected field produces "Unknown" outcomes with `SourceAttributeNotCollected` or
  `TargetAttributeNotCollected` until an import profile supplies the field.
- Entra users are collected incrementally and AD imports are never tenant-wide
  exhaustive sections, so absence of a user in the target is not proven with the
  current connectors; such users are "Unknown" (`TargetNotExhaustive`).
- A planned target only checks computed UPNs among source objects. Target domain
  collisions need observed target domains.
- A source-target comparison holds at most 200,000 objects per side.
