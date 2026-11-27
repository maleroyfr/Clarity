# Migration scoping

This guide covers Lot 5 migration scoping: populations, the list import assistant,
historized scope decisions and migration waves. The French product specification
(`docs/specification.md`, "Périmètre" screens and the `Population` / `ScopeDecision`
entities) remains authoritative; this page records the implemented choices.

The screen is at `/projects/{projectId}/scope` (with `?side=Target` for the target
side). Its views are populations (default), `view=import` (import assistant) and
`view=decisions` (decision list). Business text is French, from `UiStrings.resx`.

## Populations

A population is a named, consultant-defined set of inventory objects of **one
environment** of the project. It is evaluated against the current consolidated
inventory and materialized as `PopulationMembers` with an `EvaluatedAtUtc` date.
It does not change when new collections are published until the consultant
selects **Refresh**; the refresh re-evaluates the same criterion.

| Kind | Criterion | Evaluation |
| --- | --- | --- |
| Group | An inventory group chosen with the group search | Current `memberOf`/`primaryMemberOf` membership edges, transitively (nested groups and their members are included; cycles terminate). |
| Organizational unit | A distinguished name such as `OU=Sales,DC=example,DC=test` | Objects whose current `ad.distinguishedName` alternate key ends with `,<OU DN>` (case-insensitive), so sub-OUs are included. |
| Domain | DNS domain | Same exact column match as the inventory domain filter (collection scope domain, else the UPN suffix). |
| Site | Site value | Same exact match as the inventory site filter. |
| Filter | Search text | Same prefix search as the inventory grid (display name, external IDs, alternate keys). |
| Imported list | See below | Objects matched or chosen from the imported lines. |

Every kind accepts an optional object type and status (`Enabled`, `Disabled`,
`Observed`, `AbsentFromScope`, `Unknown`) refinement. Deleted inventory objects
are never members. A population is limited to 20,000 members; a broader
criterion is rejected and must be refined.

Membership only reflects **collected** relationships. Group populations are as
complete as the published membership sections for that environment; edges that
are pending (unresolved endpoints) are not followed.

## Import assistant

The assistant creates an imported-list population from pasted values (one per
line, at most 5,000 distinct case-insensitive values of 500 characters).
The consultant chooses the identifier used for matching:

| Identifier | Matched against |
| --- | --- |
| UPN | Current `upn` alternate key |
| SMTP | Current `smtp` alternate key (`mail` and `proxyAddresses`) |
| sAMAccountName | Current `sAMAccountName` alternate key; a `DOMAIN\` prefix is ignored |
| External identifier | Exact external key of the environment (for example an AD objectGUID or Graph ID) |
| Display name | Exact display name (case-insensitive) |

Each line is classified as **matched** (exactly one live object), **ambiguous**
(several candidates) or **unmatched**. Ambiguous and unmatched lines stay visible
and are never auto-attached. For an ambiguous line the consultant reviews the
candidates and explicitly **chooses** one; the choice is audited and kept across
refreshes as long as the chosen object remains live. A refresh re-resolves every
other line against the current inventory.

## Scope decisions and waves

A `ScopeDecision` records the consultant's current decision for one object:
**Inclure**, **Exclure** or **À arbitrer**, with an optional migration wave label
(at most 100 characters). Decisions are applied in batch to a whole population
or to up to 100 selected objects:

1. **Preview** shows the number of targeted objects, the number that would change
   and the number already holding the same value and wave.
2. **Confirm** applies the batch with a mandatory justification. The service
   re-computes the affected count under the project lock and rejects the batch
   with a conflict if it differs from the previewed count.
3. Each batch carries an operation ID: replaying the same operation is idempotent;
   reusing it with different content is a conflict.

Each change appends an immutable `ScopeDecisionRevision` (number, value, wave,
population, justification, author, date, latest published evidence observation
and a fingerprint of the object's key fields). Unchanged objects get no revision.
The decision list and member grids show the current value, wave and source
population; **Historique de la décision** shows every revision.

A decision whose object's key fields (`userPrincipalName`, `mail`,
`proxyAddresses`, `sAMAccountName`, `distinguishedName`, `enabled`,
`accountEnabled`, `onPremisesImmutableId`) or presence changed since the
decision is flagged **à revoir**. The decision itself is kept until the
consultant confirms or changes it; unrelated objects are not flagged.

## Authorization, isolation and audit

- Reading requires project read access; creating, refreshing, importing,
  choosing candidates, previewing and applying require the qualify permission
  on an active project. Readers see a read-only screen. Platform administration
  grants no access to scoping data.
- Every table (`Populations`, `PopulationEntries`, `PopulationMembers`,
  `ScopeDecisions`, `ScopeDecisionRevisions`) carries `WorkspaceId`, composite
  foreign keys and SQL row-level security filter and block predicates.
- Population creation, list imports, candidate choices, refreshes and decision
  batches write audit events (`Scope.*`) with a correlation ID. Logs never
  contain imported values.

## Limits

- Evaluation uses the consolidated inventory only; Clarity never queries a tenant
  or directory while scoping.
- Groups, OU paths, domains and sites are only as complete as their collected
  sections; consult the coverage screen before relying on a population.
- Imported lists match one identifier per list; mixed identifier lists must be
  split.
