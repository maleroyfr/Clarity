# Scenario workflows: tenant-to-tenant, carve-out, carve-in and audit only

This guide covers the Lot 5 scenario-aware overview: how each project scenario
orders the existing working features into a workflow, how each step state is
computed and what the overview does not do. The French product specification
(`docs/specification.md`, the four scenarios at the top of the document) remains
authoritative; this page records the implemented choices.

## Where it appears

The project overview (`/projects/{id}` or `/projects/{id}/overview`) starts with
**Parcours du scénario · &lt;scénario&gt;**. It shows:

- the scenario objective, quoted from the specification;
- the number of key steps that are done;
- the **next step**: the first key step whose state is *À faire* or *À vérifier*;
- the ordered steps. Each step shows its state, whether it is a key or a
  supporting step, a hint and its counters. Each step links to the screen that
  already implements it: environment declaration, Discovery coverage, populations,
  scope decisions, dependencies, decision register, run comparison, source-target
  comparison and mapping rules, or exports.

The same data is available to every project member through
`GET /api/projects/{projectId}/scenario`. The workflow only reads data. It never
creates, changes or approves a decision, and every action stays on its own
screen with its own authorization: a reader sees the workflow but not the
consultant controls of the linked screens.

## Scenario plans

Steps marked **key** carry the scenario objective and drive the next step.
Supporting steps remain visible and usable.

| Step | Tenant-to-tenant | Carve-out | Carve-in | Audit only |
|---|---|---|---|---|
| Declare the source | key | key | key | key |
| Collect the source | key | key | key | key |
| Declare the target | key | supporting | key (existing target expected) | supporting, "add a target later" |
| Collect the target | key | supporting | key | supporting |
| Document mapping rules | key | supporting | key | supporting |
| Compare source and target | key | supporting | key (duplicates and collisions) | supporting |
| Build populations | supporting | key (departing population) | key | supporting |
| Decide the scope | supporting | key | key | supporting |
| Analyse dependencies | supporting | key (resources also used by people who remain) | key (dependencies to keep or adapt) | supporting |
| Compare two collections | supporting | supporting | supporting | supporting |
| Review decisions | key | key | key | supporting |
| Generate exports | key | key | key | key ("publish the synthesis") |

The order of the table rows differs slightly from the on-screen order: carve-out
puts its scoping steps before the target steps, and audit only puts the target
steps last.

Supporting target steps (collect the target, mapping rules, source-target
comparison) are hidden until a target is declared. For audit only, the scoping
steps are hidden until a population, a scope decision or a dependency analysis
exists. A scenario is chosen when the project is created and cannot be changed
afterwards, so an audit-only dossier that later gains a target shows its target
steps once the target is declared.

## Step states

| State | French label | Meaning |
|---|---|---|
| Done | Terminé | The evidence for the step exists and has no point to review |
| Attention | À vérifier | The evidence exists but contains points to review |
| ToDo | À faire | A key step that has not started although its prerequisites are met |
| Waiting | En attente d’un prérequis | A prerequisite is missing; counters are hidden |
| Optional | Facultatif | A supporting step that has not started |

| Step | Done when | Attention when | Waiting when |
|---|---|---|---|
| Declare the source | a source environment exists | — | — |
| Collect the source | sections are published for the source | the latest publication of a section unit has partial or failed coverage | no source is declared |
| Declare the target | a target is declared | carve-in with only planned targets | — |
| Collect the target | sections are published for an existing target | partial or failed coverage | no existing target (a planned target has no data to collect) |
| Build populations | a source population exists | — | the source is not collected |
| Decide the scope | source scope decisions exist | decisions are still *à arbitrer* | no population and no decision |
| Analyse dependencies | a dependency analysis exists | shared resources or unresolved links exist | nothing is included |
| Compare two collections | a run comparison exists | — | no environment has two runs with published sections |
| Document mapping rules | an active rule exists | — | no target is declared |
| Compare source and target | a source-target comparison exists | collisions or ambiguous matches | no target or the source is not collected |
| Review decisions | decisions exist and none needs review | review flags or mappings to review | no decision exists |
| Generate exports | an export completed | exports failed and none completed | the source is not collected |

Absent and unknown objects of a source-target comparison are shown as counters
but do not turn the step into *À vérifier*: absence is only a finding where the
comparison proves it, and unknown never means absent (see
[comparison.md](comparison.md)).

## Counted facts

`ScenarioOverviewService` authorizes one project read, then counts in the
database:

- environments by role and target state;
- section units: the latest published section per environment, capability and
  scope, the same selection as the inventory overview; partial or failed
  coverage counts as *to verify*;
- source populations and current source scope decisions by value;
- the latest dependency analysis per source environment and wave. Shared
  resources are counted **once per resource ID** across those analyses, so a
  site reached through a team and through SharePoint, or shared in two waves,
  counts once. Remaining principals are summed once per environment;
- the latest source-target comparison per environment pair (matched, absent,
  ambiguous, unknown, collisions);
- non-mapping review flags and mapping decisions to review, split by the side
  of their environment;
- consultant decisions: current scope decisions, distinct assessed objects (SSO,
  Teams chat and telephony qualifications) and non-proposed mapping decisions;
- run comparisons, active mapping rules and export jobs by state.

## Limits

- The scenario cannot be changed after project creation.
- Counters are read when the overview loads; reload the page after work in
  another tab or by another consultant.
- Scoping counters concern the source side only.
- Remaining principals are summed per source environment. A person present in
  both Active Directory and Entra ID is counted once in each.
- The latest dependency analysis per environment and wave is used, even when a
  later scope change makes it stale; recompute dependencies after changing the
  scope.
- The workflow does not judge the quality of a decision. It only shows that the
  evidence exists, and it does not replace the consultant review of each
  screen.

Related guides: [scoping.md](scoping.md), [comparison.md](comparison.md),
[decisions.md](decisions.md) and [exports.md](exports.md).
