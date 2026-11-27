# Pilot checklist

The specification ([section 9](specification.md#9-ordre-de-réalisation-pour-lagent))
defines the last lot as follows:

> 6. **Pilote**. Valider le parcours avec un cas représentatif anonymisé, vérifier l’isolation réelle, le redémarrage et les temps d’usage. Prioriser ensuite les enrichissements en fonction des opérations qui prennent encore du temps aux consultants.

This page separates what the repository has already shown on synthetic data
from what only the user-led pilot can show, and lists the pilot steps with a
place to record their results.

## Status

- **Synthetic readiness: complete.** Every row of the recette table, client
  isolation, restarts, throttling, a 50,000-user volume run, retention and
  controlled deletion are covered by automated tests on generated, anonymized
  data and a disposable SQL Server. The evidence is listed below.
- **Pilot: not started.** No step in the [pilot](#pilot-steps-user-led) section
  has been executed. Nothing has been deployed: `AZURE_DEPLOY_ENABLED` is unset,
  and no real tenant, forest, customer export or credential has been used.
  Running the pilot, and enabling deployment for it, are the user's decisions.

## Synthetic readiness evidence

Each slice was pushed to `main` and verified by a green main CI run that
includes the SQL integration tests.

| Area | Evidence | Commit | Main CI |
|---|---|---|---|
| Recette R01–R14 on a generated dossier | [Reference acceptance](recette.md) | `9439522` | [45941575853](https://github.com/maleroyfr/Clarity/actions/runs/45941575853) |
| Cross-client reads, downloads and mutations with guessed identifiers; RLS; audit trail | [Isolation audit](isolation-audit.md) | `29ae66c` | [45948824522](https://github.com/maleroyfr/Clarity/actions/runs/45948824522) |
| Restarts, lease loss, throttling, export supersession; 50,000 users and 500,020 memberships | [Resilience and performance](resilience-performance.md) (full-size run [45990976175](https://github.com/maleroyfr/Clarity/actions/runs/45990976175)) | `17303bc` | [45992958834](https://github.com/maleroyfr/Clarity/actions/runs/45992958834) |
| Retention per category, project archival, controlled SQL and Blob deletion | [Retention and deletion](retention-deletion.md) | `0e214a7` | [46000074019](https://github.com/maleroyfr/Clarity/actions/runs/46000074019) |
| Least-privilege SQL grants enforced by tests; operations runbook; this checklist | [Operations](operations.md), [deployment](deployment.md) | The commit adding this page | Its main CI run |

What this evidence does not show:

- Microsoft Graph consent, permissions and throttling against a real tenant
  (Graph is exercised through a synthetic HTTP handler).
- Real tenant and forest identifiers, and the scripts' behaviour with the
  documented rights on customer machines.
- The current format of each administrative report from Microsoft.
- Timings on the chosen App Service plan and Azure SQL tier with real data
  shapes. The volume run used a 4-processor GitHub runner and SQL Server in a
  container.
- Live Entra sign-in, private networking, RBAC propagation, Key Vault references,
  alerting, backups and the deployment workflow itself.
- How long consultants actually spend on each operation.

## Before the pilot

- [ ] Provision the environment as in [deployment](deployment.md) sections 1–3,
      including the [runtime choice](deployment.md#runtime-choice) check for the
      App Service plan and OS.
- [ ] Apply every statement of `infra/bootstrap-sql.sql` after the migrations,
      including the `GRANT` lines for all tables, and compare the result with
      the query in [SQL permission errors](operations.md#sql-permission-errors).
- [ ] Populate `clarity-oidc` and check the Key Vault reference status.
- [ ] Choose and record the `Retention__*` values; the specification requires
      it: "Paramétrer la durée de conservation par catégorie de fichier et de
      donnée avant le pilote réel." Align App Service log, Application Insights
      and SQL backup retention with them
      ([retention and deletion](retention-deletion.md#retention-per-category)).
- [ ] Configure alerts on the log messages listed in
      [operations](operations.md#logs-and-telemetry).
- [ ] Decide whether to enable the deployment gate and name the environment
      reviewers ([deployment](deployment.md#4-configure-the-deployment-gate)).
- [ ] After the first release: both health endpoints return 200, Entra sign-in
      works, and a guessed identifier from another workspace is refused.
- [ ] Create the pilot client workspaces and assign members explicitly. Platform
      administrators get no inventory access unless they are assigned.
- [ ] Prepare the anonymized representative case outside this repository. The
      repository and its tests stay synthetic; never commit pilot data.
- [ ] Grant Graph consent per tenant, limited to the enabled collections
      ([Microsoft Graph connector](connectors/microsoft-graph.md) and the other
      connector documents).
- [ ] Brief pilot users on the documented limits:
      [discovery](discovery.md), [consolidation](consolidation.md#overview-and-acceptance-run),
      [comparisons](comparison.md#limits), [decisions](decisions.md#limits),
      [exports](exports.md#limits), [scenarios](scenarios.md#limits),
      [retention and deletion](retention-deletion.md#limits) and
      [volume](resilience-performance.md#known-limits-at-this-volume).

## Pilot steps (user-led)

1. **Representative journey.** On the anonymized case, run the consultant
   journey end to end: collect and import the source, explore, review mappings,
   build populations, decide scope, compare with the target, export. Check each
   recette row against its expected result in [recette](recette.md).
2. **Real environment.**
   - R03: Graph collection with the consented permissions, including throttled
     periods.
   - R05: a package from the wrong tenant or forest is blocked before
     publication, with the real identifiers.
   - R07: each administrative report in its current format imports through its
     profile; a changed format needs a new profile version.
   - R04: scripts run on a separate machine with the documented rights and
     tested module versions, and the ZIP imports from another machine.
3. **Real isolation.** With two pilot workspaces and distinct members, a member
   of one copies project, object, export and job identifiers of the other into
   URLs and API calls, then attempts reads, downloads and mutations. Expect the
   responses in [isolation audit](isolation-audit.md#expected-responses), review
   both audit trails, and confirm `Security.ClientBoundary` is enabled.
4. **Restart.** Restart the App Service during a Graph collection, a ZIP or
   report import, and an export. Collections and imports resume or can be
   relaunched without duplicates and with an understandable job state (R08). An
   export completes or fails `SelectionSuperseded` without mixed-date files.
5. **Usage times.** Time the operations listed in
   [measured timings](resilience-performance.md#measured-timings) on the real
   volume. Record the App Service plan, SQL tier, object and relationship counts.
   Compare with the budgets: 5 seconds for interactive reads, 3 minutes for
   population, scope, recomputation and export operations.
6. **Retention and deletion rehearsal.** On a synthetic project in the deployed
   environment, archive it, request deletion with its exact name, then confirm
   its SQL rows and Blob files are gone, Blob versions and soft delete still allow
   recovery, the audit events remain, and the retention sweep runs in the logs.
7. **Operations rehearsal.** Following [operations](operations.md): trace a
   failure from its correlation identifier to its job and project, confirm an
   alert fires, and roll back to the previous artifact as described in
   [deployment](deployment.md#schema-changes-and-rollback).
8. **Prioritize enrichments.** List the operations that still take consultants
   time, with measured duration and frequency, and use them to order the next
   enrichments.

## Sign-off

| Step | Owner | Date | Result and evidence |
|---|---|---|---|
| Before the pilot | | | |
| 1. Representative journey | | | |
| 2. Real environment | | | |
| 3. Real isolation | | | |
| 4. Restart | | | |
| 5. Usage times | | | |
| 6. Retention and deletion rehearsal | | | |
| 7. Operations rehearsal | | | |
| 8. Prioritized enrichments | | | |

Record results without customer data: counts, timings, outcomes and correlation
identifiers only.
