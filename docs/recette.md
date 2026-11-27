# Reference acceptance (recette) on a synthetic dossier

This page maps every row of the specification's *Recette de référence* table
([specification](specification.md), section 9) to the automated tests that
demonstrate it. Rows are numbered R01–R14 in table order; the numbers are a
local convention. Each test carries `[Trait("Recette", "Rxx")]`.

## Synthetic readiness, not the pilot

These tests run Clarity against a generated, anonymized dossier on a disposable
SQL Server database. They show that the product behaves as specified on
representative synthetic data. They are not the pilot. The pilot still requires
the following, and all of it is led by the user:

- an anonymized representative customer case;
- validation against a real tenant and forest, with the documented rights;
- measurement of real isolation, restart behaviour and usage times;
- the decision to enable Azure deployment.

No real tenant, directory, customer export, credential or deployment is used
here.

## The synthetic dossier

`tests/Clarity.Infrastructure.Tests/SyntheticDossier.cs` generates deterministic
packages from a seed. The same seed always yields the same JSON and identifiers.
A smaller dossier with the same seed reuses the same identifiers, which makes it
easy to simulate a later, narrower snapshot.

- **Names and addresses.** These are generated under reserved test domains
  (`example.test`, `forest.example.test`, `target.example`). Users are
  `u00001@example.test`…; a rename changes the prefix to `r` under the same
  object GUID.
- **Identifiers.** Object GUIDs, Entra object IDs and run IDs are SHA-256
  derivations of the seed. No identifier comes from a real system.
- **Shape.** Users are spread over four departments. Department groups are nested
  in an `all-staff` group. Each user belongs to its department and to N project
  groups. Users 2 and 3 share a display name, and every seventh user is disabled.
  The default dossier has 12 users, 8 groups and 40 `memberOf` relationships.
  `new SyntheticDossier(users: 50_000, projectMemberships: 9)` produces about
  500,000 relationships for volume tests.
- **AD variants.** `AdForest(...)` produces the AD forest package (`clarity.ad`,
  sections `ad.users` and `ad.groups`) in these variants:
  - observation day;
  - renamed users;
  - users section omitted;
  - groups section `Complete`, `Partial` (warning), `Failed` (access error),
    `NotApplicable` or omitted;
  - collection request ID;
  - wrong forest.
- **Entra package.** `Entra(...)` produces an Entra tenant package
  (`entra.users` and `entra.domains`). You can limit it to the first N users,
  give it a domain, or override selected mails to create ambiguous hybrid
  candidates.

A non-SQL test checks that every variant passes the discovery catalog and the
package validator and survives the ZIP round trip:
`RecetteTests.SyntheticDossierIsDeterministicAnonymizedAndValidForEveryCoverageVariant`.

## Running the acceptance

```powershell
$env:CLARITY_TEST_SQL = "<connection string of a disposable SQL Server database>"
dotnet test --configuration Release --filter "Recette=R14"   # one row
dotnet test --configuration Release --filter "Recette~R"     # every tagged test
```

Without `CLARITY_TEST_SQL`, SQL tests are reported as **skipped**, not passed. CI
runs them against a disposable SQL Server 2022 service container on every push.

## Row-by-row evidence

The French case names and expected results below are quoted verbatim from the
specification.

| Row | Cas | Résultat attendu | Evidence (test class.method) |
|---|---|---|---|
| R01 | Dossier source seul | Collecte, exploration et synthèse utilisables. La comparaison source/cible s’active quand la cible est renseignée et les données utiles sont présentes. | `RecetteTests.SourceOnlyDossierIsUsableAndTargetComparisonActivatesOnceTargetDataExists`; `TargetComparisonTests.ExistingTargetsProposeReviewableAssociationsWhilePlannedOrEmptyTargetsAreNeverCompared` |
| R02 | Import identique en parallèle | Une seule intégration. Les statuts et fichiers restent associés au bon dossier. | `ZipImportTests.IdenticalParallelSeparateMachineUploadsRequireConfirmationAndIntegrateOnce` |
| R03 | Collecte Graph et import complémentaires | Une même ressource est visible avec les deux provenances. Les attributs et relations propres au script enrichissent cette ressource. | `GraphTests.GraphUsesCommonIngestionSurvivesRestartAndPersistsCursorOnlyAfterPublication` |
| R04 | Script exécuté sur un poste séparé | Le consultant télécharge la collecte, lance le script dans un contexte autorisé et importe le ZIP depuis un autre poste. | `RecetteTests.SeparateMachineScriptWrongEnvironmentAndFailedSectionRetryFollowTheirRequests`; `ScriptRequestTests.ParametersArePortableMetadataAndDownloadsReauthorizeTheProject`; `ZipImportTests.IdenticalParallelSeparateMachineUploadsRequireConfirmationAndIntegrateOnce`; `DiscoveryEndpointTests.SqlSeparateMachineJourneyDownloadsUploadsPreviewsConfirmsAndPreservesCoverage` (HTTP) |
| R05 | Environnement incorrect | La vérification détecte un tenant ou domaine différent de celui du dossier et bloque la publication. | `RecetteTests.SeparateMachineScriptWrongEnvironmentAndFailedSectionRetryFollowTheirRequests`; `ZipImportTests.ConflictingRunWrongEnvironmentRequestAndWorkspaceAreRejectedBeforePublication`; `ReportImportTests.SiteReportsResolveOwnersThroughObservedUpnsAndContradictoryTenantEvidenceBlocksPublication` |
| R06 | Import partiel et relance | Les sections réussies sont disponibles et les dernières observations utiles restent visibles. La section en erreur peut être relancée via son mode de collecte. | `RecetteTests.SeparateMachineScriptWrongEnvironmentAndFailedSectionRetryFollowTheirRequests`; `ZipImportTests.PartialPackageUsesCommonPublisherAndKeepsPriorValidData`; `PublicationTests.AbsenceRequiresAnExhaustiveComparableSnapshot` |
| R07 | Rapport administratif pris en charge | Le profil produit les mêmes observations canoniques que les autres sources, avec les unités, le périmètre et les lignes rejetées visibles. | `ReportImportTests.MailboxReportPublishesOnlyAfterConfirmationThroughTheCommonJobPathWithReportProvenance`; `ReportImportTests.MixedOneDriveIdentitiesPublishThroughTheCommonJobPathWithoutDuplicates`; `ReportImportTests.SiteReportsResolveOwnersThroughObservedUpnsAndContradictoryTenantEvidenceBlocksPublication`; `ReportImportTests.PartialReportKeepsPriorValuesPresenceAndValidatedHumanDecisions` |
| R08 | Redémarrage pendant une collecte | Reprise ou relance sans doublon et état du job compréhensible pour le consultant. | `JobTests.RestartDuringProcessingResumesPublishedSectionWithoutDuplicates`; `GraphTests.GraphUsesCommonIngestionSurvivesRestartAndPersistsCursorOnlyAfterPublication`; `ReportImportTests.JobTimeAuthorizationIsRecheckedAndRetryPublishesTheReportExactlyOnce`; `ResilienceTests.ReclaimedExportRecordsOnlyTheLeaseOwnersFilesAndPurgesInterruptedAttempts` (see [resilience and performance](resilience-performance.md)) |
| R09 | Tentative d’accès depuis un autre client | Refus côté serveur des lectures, téléchargements et mutations. Tester aussi un identifiant appartenant à un autre client. | `ScopeTests.ScopeIsWorkspaceIsolatedAndReadersCannotDecide`; `DependencyTests.DependenciesAreWorkspaceIsolatedAndReadersCannotAnalyze`; `TargetComparisonTests.ComparisonsAreWorkspaceIsolatedAndReadersCannotDocumentOrCompare`; `DecisionHistoryTests.RecomputeFlagsLegacyDecisionsWithUnknownChangesAndIsAuthorizedAndIsolated`; `ReportImportTests.UnsupportedProfilesScopesFilesAndUnauthorizedOrGuessedIdentifiersAreRejectedAtTheServerBoundary`; `ExportTests.ExportGeneratesReconciledFilesAuditsDownloadsAndIsAuthorizedIsolatedAndIdempotent`; `IsolationSweepTests.Every_route_page_and_circuit_service_denies_identifiers_from_another_workspace`; `IsolationSweepTests.Sql_populated_workspaces_resist_guessed_and_copied_identifiers_and_keep_rls_audit_and_storage_intact` (HTTP, pages and circuit services; see [isolation audit](isolation-audit.md)); `RowLevelSecurityTests.Every_workspace_table_has_an_enabled_filter_and_insert_update_block_predicates` |
| R10 | Changement d’UPN et groupe imbriqué | L’objet reste stable et les liens restent explicables. Une correspondance ambiguë est soumise à revue. | `RecetteTests.RenamedNestedMemberKeepsItsIdentityFlagsOnlyItsDecisionAndAmbiguousHybridCandidatesAwaitReview`; `PublicationTests.MembershipPathsResolveLaterArrivalsPreserveProofAndTerminateOnCycles`; `GraphTests.VerifiedConsistencyAnchorAndReviewedUpnSurviveUnrelatedRefreshButNotANewCandidate` |
| R11 | Équipe et site partagés | Un seul site compté dans les volumes, avec les utilisateurs des deux populations et l’origine des liens. | `DependencyTests.SharedTeamAndSiteCountTheSiteVolumeOnceWithBothPopulationsAndLinkOrigins` |
| R12 | Actualisation ciblée | Les faits changés sont visibles. Les décisions valides subsistent et celles affectées restent identifiables. | `RecetteTests.RenamedNestedMemberKeepsItsIdentityFlagsOnlyItsDecisionAndAmbiguousHybridCandidatesAwaitReview`; `DecisionHistoryTests.TargetedRefreshFlagsOnlyChangedDecisionsAndReconfirmationClearsThem`; `DecisionHistoryTests.RecomputeFlagsLegacyDecisionsWithUnknownChangesAndIsAuthorizedAndIsolated` |
| R13 | Source partielle ou rapport masqué | L’écran et l’export indiquent le manque de couverture. Aucun objet absent n’est déduit de ces seules données. | `RecetteTests.PartialSourceIsReportedWithoutAbsenceAndTheExportReflectsFiltersScopeVintageAndProvenance`; `ExportTests.SupersededSelectionFailsWithoutFilesAndPartialCoverageIsReportedNotTreatedAsAbsence`; `PublicationTests.AbsenceRequiresAnExhaustiveComparableSnapshot`; `ReportImportTests.ConcealedOnlyAndAllInvalidReportsStateTheirFailureAndCannotBeConfirmed`; `ReportImportTests.PartialReportKeepsPriorValuesPresenceAndValidatedHumanDecisions` |
| R14 | Export | Le fichier reflète les filtres, le périmètre et le millésime sélectionnés, avec compteurs réconciliables et provenance consultable. | `RecetteTests.PartialSourceIsReportedWithoutAbsenceAndTheExportReflectsFiltersScopeVintageAndProvenance`; `ExportTests.ExportGeneratesReconciledFilesAuditsDownloadsAndIsAuthorizedIsolatedAndIdempotent`; `ExportTests.SupersededSelectionFailsWithoutFilesAndPartialCoverageIsReportedNotTreatedAsAbsence`; `ResilienceTests.DecisionChangedWhileReadingSupersedesTheExportWithoutMixedDateFiles`; `ResilienceTests.ReclaimedExportRecordsOnlyTheLeaseOwnersFilesAndPurgesInterruptedAttempts` |

## What the dossier journeys assert

All four journeys run against SQL, so CI runs them and local runs skip them.

### R01: source-only dossier

1. An AD-only dossier is imported.
2. The scenario overview shows one source with two complete sections and the
   source collection step done.
3. The source/target comparison waits for a target, and the observed population
   counts all 12 users.
4. A user's membership paths include the two-edge nested `all-staff` path.
5. An existing target tenant with a computed-UPN rule is added. Comparing it
   before any target data is collected returns `NoData` and 12 `NotCompared`
   (`TargetDataMissing`).
6. Importing eight target users and comparing again yields 8 matched and
   4 `Unknown` (`TargetNotExhaustive`, never absent), and both scenario steps
   become done.

### R04, R05 and R06: separate machine, wrong environment, failed-section retry

1. A collection request yields portable parameters (request, expected forest,
   domain) and the script bundle.
2. The package generated for that request is uploaded under another file name.
   It is previewed (20 new objects), confirmed and integrated, and the stored
   file downloads byte for byte.
3. A package from another forest is rejected with `EnvironmentOrScopeMismatch`.
   No run, observation or artifact is added.
4. A second request returns a rename together with a failed groups section:
   - the rename is published;
   - every group and membership stays visible from the previous collection;
   - the coverage unit is `Failed` and the collection step needs attention.
5. A third request re-collects only `ad.groups` and restores `Complete`
   coverage.

### R10 and R12: UPN change, nested group and targeted refresh

1. Users 5 and 6 are included in "Vague 1".
2. A refresh renames user 5:
   - the object ID is unchanged;
   - each edge of its nested path has `ad.groups` ZIP provenance.
3. Only user 5's scope decision is flagged, with `mail` and `userPrincipalName`
   changes. User 6's decision stays valid.
4. An Entra source tenant is added, in which one user's mail equals another
   user's UPN. Exactly the three competing hybrid pairs are `ToReview`. The
   renamed user is not matched.

### R13 and R14: partial source and export

1. A later snapshot omits users 11 and 12 and reads groups partially:
   - every object stays `Observed`;
   - the groups unit is `Partial` and needs attention.
2. An export filtered on included objects of "Vague 1" contains exactly the five
   selected users. A "Vague 2" decision is excluded.
3. The export shows each user's real vintage. User 11 keeps its earlier last
   observation, while user 1 has the refresh date.
4. The export reports the `ad.groups — Partial` coverage gap, and all
   reconciliation checks pass.
5. The export's selection timestamp and hash are present, the download hash
   matches the recorded content hash, and request, generation and download are
   audited.

## Coverage limits of this evidence

- **R03 Graph data.** Graph collection is exercised through a synthetic HTTP
  handler, not a live tenant. Real permission consent and throttling behaviour
  must be confirmed during the pilot.
- **R05 forest names.** The forest and domain names in the dossier are
  synthetic. The pilot must confirm the expected tenant and forest identifiers
  of the real dossier.
- **R07 report formats.** Report imports use the versioned synthetic examples of
  each import profile. A newer report format from Microsoft requires a new
  profile version.
- **R11 shared resources.** Shared resources use focused synthetic SharePoint
  and Teams data rather than the generated dossier.
- **Data volume.** The default dossier is deliberately small so that each
  journey stays fast and readable.
- **Retention and deletion.** Retention per category, archival and controlled
  deletion are operations requirements, not rows of the recette table. Their
  tests are listed in [retention and deletion](retention-deletion.md).
