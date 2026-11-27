# Migration scoping

This guide covers Lot 5 migration scoping: populations, the list import assistant,
historized scope decisions and migration waves, and the population dependency and
shared-resource analysis. The French product specification
(`docs/specification.md`, "Périmètre" screens and the `Population` / `ScopeDecision`
entities) remains authoritative; this page records the implemented choices.

The screen is at `/projects/{projectId}/scope` (with `?side=Target` for the target
side). Its views are populations (default), `view=import` (import assistant),
`view=decisions` (decision list), `view=dependencies` (dependency analysis) and
`view=review` (decision register). Business text is French, from `UiStrings.resx`.
Comparing successive collections and listing the decisions to review is described in
[Comparisons](comparison.md); decision histories and re-review conditions in
[Decision history](decisions.md).

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
   and the number already holding the same value and wave against unchanged key
   fields. A decision with the same value and wave whose key fields changed is
   counted as changed and reconfirmed (**Décisions à reconfirmer**).
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
consultant confirms or changes it; unrelated objects are not flagged. Publication
materializes these flags with the changed fields, and the **Registre des décisions**
view reconfirms them; see [Decision history](decisions.md).

## Dependencies and shared resources

**Calculer les dépendances** computes, for one environment and an optional wave,
which collective resources the migrating population depends on and which of
them are **shared** with principals that stay behind (specification: "une
ressource est partagée lorsque ses dépendances connues couvrent à la fois des
populations migrantes et restantes"). The result is a dated snapshot that never
changes a consultant decision; a new computation for the same environment and
wave replaces the previous snapshot. Each computation carries an operation ID
(replay returns the same analysis, reuse with different content is a conflict)
and writes a `Scope.DependenciesAnalyzed` audit event.

### Sides

Only `user` objects are principals. Each principal's side comes from its current
`ScopeDecision`:

| Decision | Side |
| --- | --- |
| Include, in the selected wave (or any wave when no wave is selected) | Migrating |
| Include, in another wave | Other wave |
| To arbitrate | To arbitrate |
| Exclude, or no decision | Remaining |

A principal without its own decision inherits the side of the objects bound to
it by a **validated** hybrid mapping decision (AD ↔ Entra); conflicting mapped
sides become *to arbitrate*.

### Traversal

The analysis reads current relations only (`DependencyEngine.Relations`). Each
relation is read as "dependent → resource":

| Category | Relations | Followed backward as |
| --- | --- | --- |
| Structural | `teamGroup`, `groupSite`, `teamSite`, `channelSite`, `assignmentOf`, `appRoleAssignmentResource`, `referencesEntraDevice` | Structure: the dependents of the linked object also depend on the resource |
| Membership | `memberOf`, `primaryMemberOf` (nested groups are followed, cycles terminate) | Members |
| Ownership | `ownerOf`, `intunePrimaryUserOf`, `entraRegisteredOwnerOf`, `mailboxOf` | Members |
| Grant | `sitePermission`, `fullAccessTo`, `sendAsOn`, `sendOnBehalfOf`, `appRoleAssignmentPrincipal`, `targetsGroup` | Members |
| Discovery | `channelInTeam` for private and shared channels | Never (outgoing only) |

Candidate resources are the collective objects reachable forward from the
migrating principals. For each one the walk goes backward and counts the
principals on each side. Delegations and ownership are **not transitive**: a
delegate of a mailbox does not become a dependent of the resources the mailbox
reaches. Intune "all users / all devices" assignments are flagged and exclusion
targets are not counted as dependents.

Resource families and classification:

| Family | Types |
| --- | --- |
| Groups | `group`, `mailGroup` |
| Teams | `team` |
| Sites and OneDrive | `site`, `oneDrive` |
| Mailboxes | `mailbox`, `sharedMailbox`, `resourceMailbox` |
| Applications | `servicePrincipal`, `mobileApp`, `deviceConfiguration`, `deviceCompliancePolicy` |
| Devices | `managedDevice`, `device` |

A resource is **Partagée** when at least one remaining principal depends on it;
otherwise **À arbitrer** when a principal to arbitrate depends on it; otherwise
**Plusieurs vagues** when a principal of another wave depends on it; otherwise it
is **Population qui migre uniquement**.

### Volume deduplication and explanations

Volumes are counted **once per resource object ID**. A Teams-connected site
reached through the team (`teamSite`), its backing Microsoft 365 group
(`groupSite`) and a standard channel (`channelSite`) is one site object
(`site/sharepoint.siteId/<site-collection GUID>`), listed once with one volume
and three routes. Team and backing group keep distinct object types even though
they share a GUID; distinct private or shared channel sites stay distinct.

| Type | Volume source (latest non-conflicting published value) |
| --- | --- |
| `site` | `storageUsedBytes` (SharePoint site usage report) |
| `oneDrive` | `quotaUsedBytes`, else `storageUsedBytes`, else `storageUsedMegabytes` |
| Mailboxes | `sizeBytes` plus `archiveSizeBytes` |

Volumes apply to sites and mailboxes only. A resource without a usable value is
counted under **Ressources sans volume connu**, never as zero. **Expliquer** lists the routes
(relation and related object, up to 50) and up to 25 sampled principals per side
with their relation chain and the immediate object they reach the resource
through.

### Analysis coverage

Each family shows the latest published coverage of its sections and an overall
status:

| Family | Sections |
| --- | --- |
| Groups | `ad.groups`, `entra.groups`, `exchange.mailEnabledGroups` |
| Teams | `teams.teams`, `teams.members`, `teams.channels` |
| Sites and OneDrive | `teams.sites`, `sharepoint.groupSites`, `sharepoint.sitePermissions`, `onedrive.accounts` |
| Mailboxes | `exchange.mailboxes`, `exchange.mailboxPermissions` |
| Applications | `applicationsSso.appRoleAssignments`, `intune.appAssignments`, `intune.profileAssignments`, `intune.policyAssignments` |
| Devices | `intune.primaryUsers`, `intune.registeredOwners`, `intune.managedDevices` |

Only sections applicable to the environment kind are listed. The status is
**Non collectée** when no section was published, **Complète** only when every
applicable section was published complete at tenant or domain scope with no
unresolved principal and no uncollected roster, and **Partielle** otherwise.
In particular:

- `sharepoint.sitePermissions` is collected one site at a time (*collecte par
  ressource*) and never proves tenant-wide completeness.
- Foreign-tenant or missing member references stay unresolved and are counted,
  never merged into a local user.
- Private and shared channel rosters are not collected: a private or shared
  channel (and its site) reached from the migrating population is reported as
  *membres non collectés*; its team's members are not credited as its
  dependents.

### Authorization and isolation

Reading requires project read access; computing requires the qualify
permission. `DependencyAnalyses`, `DependencyResources` and `DependencyLinks`
carry `WorkspaceId`, composite foreign keys and row-level security predicates.
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
- A dependency analysis covers one environment. Cross-environment dependencies
  (for example an AD group synchronized to Entra) are only joined through
  validated hybrid mappings of principals, not of resources.
- One analysis reads at most 500,000 relations and lists at most 50,000
  resources; larger environments must be scoped by wave.
- Incoming shared channels and channel-member expansion are not collected, so
  shared-channel dependencies from other tenants are not visible.
