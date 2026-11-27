# Decision history and targeted re-review

This guide covers the Lot 5 decision history: how consultant decisions are kept
independently from collections, when a decision becomes due for review, and how a
consultant reviews it. The French product specification (`docs/specification.md`,
`Assessment`, `ScopeDecision` and `MappingDecision` entities, "historiser les
décisions et conditions de revue") remains authoritative; this page records the
implemented choices.

## Independent histories

Three decision families are kept apart from the inventory and from each other. A
collection, a publication, a rebuild or a comparison never edits a decision or one
of its revisions.

| Decision | Current row | History | Recorded with |
|---|---|---|---|
| Scope (`Inclure`, `Exclure`, `À arbitrer`, wave) | `ScopeDecision` | `ScopeDecisionRevision` (one per change) | justification, author, date, evidence observation, fingerprint and basis |
| SSO dependency, Teams chat scope, Teams telephony scope | latest `Assessment` revision per object and kind | every `Assessment` revision | justification, evidence reference and observation, author, date, fingerprint and basis |
| Hybrid and source-target mapping | `MappingDecision` | `MappingRevision` | state, justification, author, date |

The **basis** (`BasisJson`) is the exact set of critical values and the presence the
decision was taken against. Its SHA-256 is the decision fingerprint, computed the same
way as before bases were stored, so existing fingerprints remain valid.

## Re-review conditions

A decision is due for review when its object's current presence or one of its
critical fields differs from its basis:

| Decision | Critical fields |
|---|---|
| Scope | `userPrincipalName`, `mail`, `proxyAddresses`, `sAMAccountName`, `distinguishedName`, `enabled`, `accountEnabled`, `onPremisesImmutableId` |
| SSO dependency | `appId`, `signInAudience`, `servicePrincipalType`, `accountEnabled`, `appRoleAssignmentRequired`, `preferredSingleSignOnMode` |
| Teams chat / telephony scope | `visibility` |
| Mapping | Fields used by the matching rules (see [Comparisons](comparison.md#decisions-to-review)) |

Conflicting values count as values: a field that becomes conflicting is a change.
Fields outside these lists, such as a display name, never trigger a review.

## Targeted refresh

Each publication projects current values, then recomputes review flags **only for
the objects it re-projected**: the objects observed by the published section and,
for an exhaustive section, the objects whose presence it re-evaluated. For each
scope decision and latest assessment on those objects:

- if the current basis fingerprint still matches, nothing happens and any earlier
  flag is cleared (for example, a value that changed and changed back);
- otherwise a `ReviewFlag` row records the decision revision, the current
  fingerprint and the changed fields (before → after). Its flag date stays the date
  the decision first became stale and is reset only when the decision gets a new
  revision.

Decisions on objects outside that set are not read. Valid decisions inside it are
compared but never rewritten or flagged. Mapping decisions keep their existing
mechanism: matching refresh moves a
mapping whose evidence changed to the **À revoir** state with a new revision; the
register lists them and links to the mapping review.

Flags are workspace-owned (`WorkspaceId`, composite foreign keys, query filter and
SQL row-level security policy `ReviewFlagsBoundary`).

## Decision register

The scope screen has a **Registre des décisions** view
(`/projects/{projectId}/scope?view=review`, with `&side=Target` for the target). It
shows, per decision kind, the number of current decisions and the number to review,
and lists decisions filtered by kind, search prefix and **À revoir uniquement**
(default). Each row shows the object, the decision value, wave or counterpart, the
revision, the justification, author and date, and either **À jour** or the changed
fields with their before and after values.

Reviewing goes through each decision's own workflow, which writes a new revision:

- **Scope**: **Reconfirmer** records the same value and wave against the current
  basis, with a mandatory justification. The preview counts it under
  **Décisions à reconfirmer**. Batch decisions also reconfirm stale decisions that
  already hold the requested value and wave. **Historique de la décision** shows
  every revision.
- **Assessments**: the link opens the object sheet, where the SSO or Teams
  qualification records a new revision with the current fingerprint.
- **Mappings**: the link opens the mapping review queue.

Recording the new revision clears the flag in the same transaction. Reading
requires the project read permission; reconfirming and recomputing require the
qualify permission.

## Recomputing and legacy decisions

Decisions recorded before bases were stored have no basis. When they become stale
the register flags them with "Champs modifiés inconnus" instead of a field list.
Decisions that were already stale before this version are flagged at the next
publication touching their object, or immediately with **Recalculer les conditions
de revue**. Recomputing re-evaluates every decision of the project under the project
lock and writes a `DecisionReview.Recomputed` audit event; it never changes a
decision.

Open review flags are listed in the exceptions sheet of an [export](exports.md),
and the inventory sheet marks each object with an open flag.

## Limits

- The comparison screen's "Décisions à revoir" list ([Comparisons](comparison.md))
  is the impact of the differences between two compared collections; the register
  is the current state of each decision against the current inventory.
- Only presence and the critical fields above trigger a review. Relationship
  changes (for example group membership) do not flag a scope decision; refresh the
  population and re-apply the decision instead.
- Retention purges must keep or deliberately clear the evidence observations
  referenced by decision revisions and assessments.

The project overview counts decisions to review as the "Revoir les décisions"
scenario step; see [scenarios.md](scenarios.md).
