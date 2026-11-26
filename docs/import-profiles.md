# Versioned report import profiles

Specification §5 "Autres imports" requires administrative and tooling exports to
enter through **versioned import profiles**. This slice implements the framework
and three Microsoft 365 usage-report profiles documented in
[connectors/microsoft-365-usage-reports.md](connectors/microsoft-365-usage-reports.md).
No Quest or other third-party layout is supported: none has a verified sample.
Population lists (Lot 5 wizard) are a separate journey and do not use these profiles.

## Profile definitions

A profile is a repository file `catalog/report-profiles/{id}.v{version}.json`,
validated by `report-profile.schema.json` and embedded in `Clarity.Contracts`.
It declares:

| Element | Meaning |
| --- | --- |
| `id`, `version`, `readiness` | Stable identity; only `SyntheticValidated` profiles are selectable |
| `producerId`, `capabilityId`, `environmentKind`, `scopeKind` | Catalog producer (channel `Import`) feeding exactly one section, for one environment kind and scope unit |
| `format` | Accepted file: `csv`, UTF-8, comma delimiter, `.csv`, `yyyy-MM-dd` dates |
| `columns` | Exact header, canonical field, value type (`date`, `boolean`, `bytes`, `count`, `days`, `string`, `upn`, `guid`, `url`), unit, required/optional and `concealable` |
| `refreshDateColumn`, `periodColumn` | Report snapshot date (the observation date) and period length in days |
| `object` | Canonical type, ordered identity columns with key namespace, alternate URL keys |
| `relations` | Typed relation, source column, direction and endpoint type/namespace |
| `unsupportedVariants` | Recognizable layouts that must be refused, such as megabyte exports |
| `coverage` | `tenantExhaustive` (always `false` here), statement and coded limits |
| `source` | Graph endpoints, cmdlets, permissions, roles and documentation links |

`ReportProfiles.Load(catalog)` rejects at startup any profile that fails the
schema or diverges from the catalog: a duplicate ID/version or producer; a producer
that is not `Import`, not at the same version or readiness, or in preview; a
different environment kind, section or scope; profile fields that differ from the
section's attributes, types or reference producer; byte fields not named `…Bytes`
with unit `bytes`; relations that differ from the section; identities without a
required column; concealable GUID identities; variant headers that overlap
supported headers; and coverage that claims tenant exhaustiveness or omits the
`NotTenantExhaustive` limit.

`ReportProfiles.Default.Require(id, version)` returns a supported profile or
throws `ReportProfileUnavailable`, so unknown IDs, versions and planned profiles
are explicit errors. `RequireBestMatch` refuses a file that carries every required
column of a more specific profile (for example a SharePoint site report submitted
as OneDrive) with `ReportProfileMismatch`.

## Reusable adapter API

The adapter lives in `Clarity.Contracts` and has no persistence or HTTP dependency:

```csharp
var profile = ReportProfiles.Default.Require("m365.sharePointSiteUsageDetail", "1.0.0");
var context = new ReportContext(tenantId, new CollectionScope("tenant", tenantId), sha256Hex,
    ScopeObserved: false, SourceId: ReportAdapter.UploadSourceId);
ReportConversion result = ReportAdapter.Convert(bytes, profile, context, ReportLimits.Default);
// result.Package: canonical DiscoveryPackage (manifest, objects, relationships, one section)
// result.Diagnostics: recognized/unrecognized columns, counts, sanitized row issues, refresh date
```

`CsvReportReader.Read(bytes, limits)` is the single bounded RFC 4180 reader;
`ReportAdapter.Convert(CsvTable, profile, context)` converts an already-read table.
`ReportDiagnostics.Identities` lists the column/namespace/format mappings used by
usable rows in profile priority order (available candidates when no row is usable).
A later Graph report download must call the same `Convert` with the tenant it
authenticated, `ScopeObserved: true` and its own `SourceId`, then hand the package
to the common publication path. It must not reimplement CSV parsing.

Conventions the next collection families must keep:

| Item | Convention |
| --- | --- |
| Section ID | `{capabilityId}:{tenantId lower-case}` |
| Observation date | Report refresh date at 00:00 UTC for every object and relationship; never the upload date |
| Run ID | Deterministic SHA-256 of profile, version, tenant, scope, content hash, scope source and source ID |
| Manifest | `producerVersion` = profile version; `moduleVersions` `reportProfile` (`id@version`), `reportAdapter` (`ReportAdapter.Version`, `1.0.0`), `scopeSource` (`consultantSupplied` or `observed`) |
| Primary keys | First usable declared identity per row: immutable ID when supplied (`sharepoint.siteId`, canonical GUID `D` form); otherwise a provisional report namespace (`m365report.upn` lower-case, `m365report.siteUrl` normalized) |
| Alternate keys | `url` namespace with the normalized HTTPS URL, resolved by dated alternate-key periods |
| Relation endpoints | `user` in namespace `upn`, resolved only through a user UPN valid at the observation date; otherwise pending |
| Mailbox types | `mailbox` keyed `m365report.upn`, relation `mailboxOf` (mailbox → user) |
| OneDrive / site types | `oneDrive` and `site`, relation `ownerOf` (user → object) |

Provisional keys never share a namespace with Graph or Exchange identifiers, so a
report row is never merged with an object solely by display name or by a guessed
GUID. Merging a provisional mailbox with a later Exchange mailbox requires the
reviewable association path of [consolidation](consolidation.md), not the adapter.

## Interpretation rules

- Headers match case-insensitively and in any order; unknown columns are listed
  and ignored. Duplicate headers, missing required columns, megabyte variants,
  header-only files, malformed quoting, UTF-16 or invalid UTF-8 are file errors.
- An empty cell is unknown (`null`); `0` is a measured zero. Bytes and counts accept
  unsigned base-10 integers only, booleans `True`/`False`, dates `yyyy-MM-dd`.
  `12 MB`, `Unlimited`, `-1` or `1,024` reject the row instead of being coerced.
- A row is converted whole or rejected whole with a sanitized issue: physical line
  (header is line 1), profile column header and stable code. Cell values never
  appear in issues. Duplicate namespace/identifier pairs reject every duplicated row.
- OneDrive rows with an absent or blank `Site Id` use their normalized `Site URL`;
  rows with a valid ID retain `sharepoint.siteId` even in the same file. A nonblank
  invalid ID still rejects the whole row. Preview identities and relationship
  endpoints retain each selected column's namespace; no URL is treated as a GUID.
- A 32-hex-character value in a concealable column is a Microsoft concealed name.
  A concealed identity skips the row and counts it; concealed names/URLs in other
  columns are omitted and counted. Nothing is unhashed, guessed or matched.
- Mixed refresh dates, mixed periods or several SharePoint roots in one file are
  file errors (`ReportMixedRefreshDates`, `ReportMixedPeriods`,
  `ReportEnvironmentConflict`).
- Status is `Complete` only when every row is converted without concealment,
  `Partial` when rows are rejected or concealed, and `Failed` (no observations,
  unknown counts) with `AllIdentifiersConcealed` or `NoUsableRows` otherwise.
  Every section carries `ReportCoverageLimited`; consultant uploads also carry
  `ScopeSuppliedByConsultant`. Because of those warnings a report section never
  proves absence under the [Lot 3 rules](consolidation.md#current-state).

## Upload, preview, confirmation and job

`ImportService.UploadReportAsync(projectId, environmentId, profileId, version,
scope, scopeAttested, stream, fileName)` and
`POST /api/projects/{projectId}/discovery/{environmentId}/reports/{profileId}/{version}?scopeKind=tenant&scopeValue={tenantId}&attested=true`
(antiforgery-protected, raw CSV body) check in order: Import permission on the
project, the environment belonging to that project, a supported profile and
enabled producer, an environment of the profile kind with a verified tenant GUID,
the consultant's attestation (`ReportScopeNotAttested`) and a scope equal to that
environment's tenant (`ReportScopeMismatch`). The file is streamed to the private
`raw-artifacts` Blob container with the ZIP limit (16 MiB), then read with
`ReportLimits.Default`: 150,000 data rows, 100 fields per row and 4,096 characters
per field. A ZIP whose manifest names an `Import` producer is refused with
`ReportRequiresProfileUpload`.

Before recording, the server re-validates the canonical package and context and
compares the report's own evidence with the environment: UPN domains outside
verified Graph domains already collected for the tenant, or a SharePoint root
different from previously observed site URLs, block it with
`ReportEnvironmentConflict`. Without prior evidence the scope remains recorded as
consultant-supplied, not as confirmed.

The run is recorded with channel `Report`, the producer and profile version, the
manifest, the uploader identity, the upload time and the raw-file SHA-256 under the
existing locks: an identical file deduplicates (including concurrent uploads), a
reused run ID with other bytes or the same bytes under another profile/version is
a conflict, and rejected or duplicate uploads delete their Blob.

Preview, confirmation and the import job re-read the retained Blob, verify its
hash and re-run the recorded profile; nothing is trusted from the browser.
Confirmation refuses `Failed` or empty conversions with `ReportNoUsableRows` and
creates the single job; no observation exists before it. The job rechecks the
actor's membership and Import permission, then publishes through
`PublicationService`. Job retry and section markers make re-delivery idempotent.
Raw downloads return the original CSV as `text/csv` with a sanitized file name.

The proof panel shows channel `ReportImport` ("Import de rapport administratif"),
never Microsoft Graph, with profile/adapter versions, scope source, refresh date,
uploader and upload time.

## Current values and absence

A `Partial` report section whose only errors are `RejectedRows` keeps its converted
values comparable, because rows are all-or-nothing. They follow the existing
precedence (reference producer, verified connection, complete coverage, then
observation date), so a later partial report updates only the rows it contains.
Report sections are never exhaustive: missing, rejected or concealed rows leave
earlier values, presence and human mapping decisions unchanged.

## Discovery screen

For an existing M365 tenant environment the Discovery panel offers a French
"Importer un rapport administratif" card listing only supported profiles with
their version, required columns, identity column and namespace, coverage statement
and declared limits. The consultant enters the tenant ID and ticks the attestation
before the file picker is enabled. The preview shows the profile, refresh date,
recognized, unrecognized and missing optional columns, data/usable/rejected/
concealed counts, up to 50 actionable row issues, domains/roots found, samples and
the section outcome; confirmation is a separate explicit action. Without SQL/Blob
configuration the card states that storage is required instead of offering upload.

## Acceptance matrix

| Journey | Executable evidence |
| --- | --- |
| Only validated versioned profiles load; unknown versions and dishonest definitions (exhaustive coverage, catalog drift, byte naming) are rejected | `ReportProfileTests.OffersOnlyValidatedVersionedProfilesAndRejectsUnknownOrDishonestDefinitions` |
| BOM, quoted commas/quotes, embedded line breaks and malformed CSV | `ReportProfileTests.ReaderHandlesBomQuotesAndEmbeddedLineBreaksAndRejectsMalformedInput` |
| Mailbox schema: measured zero versus blank versus invalid bytes, concealed rows, deletion flag without presence change | `ReportProfileTests.MailboxFixtureKeepsMeasuredZeroBlankUnknownInvalidAndConcealedRowsDistinct` |
| OneDrive schema: Site ID preferred, provisional URL fallback with an absent column or mixed populated/blank/whitespace cells; invalid and concealed keys stay distinct; duplicates use the selected key | `ReportProfileTests.OneDriveFixturePrefersSiteIdAndFallsBackToAProvisionalUrlKey`, `OneDriveRowsChooseTheirOwnIdentityWhenSiteIdIsBlank`, `OneDriveFallbackDoesNotHideInvalidMissingOrConcealedIdentities`, `OneDriveDuplicateRowsReportTheSelectedIdentityColumn` |
| SharePoint schema: reordered headers, unknown columns, blank owner not guessed | `ReportProfileTests.SharePointFixtureAcceptsReorderedHeadersAndReportsUnknownColumnsWithoutGuessingOwners` |
| Missing columns, MB variant, header-only, mixed dates/periods/roots, limits | `ReportProfileTests.FileLevelDefectsAreExplicitErrorsInsteadOfEmptySuccess` |
| Rejected rows without leaked values; all-concealed and all-invalid fail honestly | `ReportProfileTests.RowLevelDefectsRejectRowsWithoutLeakingValuesAndConcealedReportsFailHonestly` |
| Parallel identical uploads, no observation before confirmation, concurrent confirmation, job publication, refresh-date observation, report provenance and raw proof | `ReportImportTests.MailboxReportPublishesOnlyAfterConfirmationThroughTheCommonJobPathWithReportProvenance` (SQL) |
| Mixed OneDrive Site ID and provisional URL rows survive preview, confirmation, publication, relationship resolution, provenance, duplicate parallel uploads and re-delivery | `ReportImportTests.MixedOneDriveIdentitiesPublishThroughTheCommonJobPathWithoutDuplicates` (SQL) |
| Owner links through observed UPNs, pending otherwise; contradictory domain/root blocks publication | `ReportImportTests.SiteReportsResolveOwnersThroughObservedUpnsAndContradictoryTenantEvidenceBlocksPublication` (SQL) |
| Unattested/mismatched scope, unknown profile/version, Quest ID, wrong environment, missing required column, wrong profile, header-only, malformed, oversize, ZIP bypass, guessed IDs and another workspace | `ReportImportTests.UnsupportedProfilesScopesFilesAndUnauthorizedOrGuessedIdentifiersAreRejectedAtTheServerBoundary` (SQL) |
| Concealed-only and all-invalid files state `Failed` and cannot be confirmed | `ReportImportTests.ConcealedOnlyAndAllInvalidReportsStateTheirFailureAndCannotBeConfirmed` (SQL) |
| A later partial report keeps prior values, presence and a validated human decision | `ReportImportTests.PartialReportKeepsPriorValuesPresenceAndValidatedHumanDecisions` (SQL) |
| Job-time membership recheck, explicit retry publishing exactly once, retry conflict | `ReportImportTests.JobTimeAuthorizationIsRecheckedAndRetryPublishesTheReportExactlyOnce` (SQL) |
| French card lists supported profiles, columns and limits; hidden for AD | `DiscoveryEndpointTests.FrenchReportCardOffersOnlySupportedTenantProfilesWithTheirColumnsAndDeclaredLimits` |
| HTTP stream → preview → confirm → job → coverage, raw download, inventory sheet; sanitized 400 | `DiscoveryEndpointTests.SqlReportJourneyStreamsPreviewsConfirmsAndPublishesAPartialReportThroughTheHttpBoundary` (SQL) |
| Report endpoint requires dossier authorization (403) | `DiscoveryEndpointTests` unauthorized endpoint loop |
| French preview of columns, rejected and concealed rows, origin and limits; labels for every profile/limit | `ComponentTests.Report_preview_shows_recognized_columns_rejected_rows_origin_and_declared_limits_in_french`, `ComponentTests.Every_supported_report_profile_and_declared_limit_has_a_french_label` |
| Mixed OneDrive preview shows each selected identity column with its correct namespace | `ComponentTests.Report_preview_shows_both_identity_namespaces_for_mixed_onedrive_rows` |

SQL cases run in CI against disposable SQL Server and are skipped locally without
`CLARITY_TEST_SQL`. Fixtures in `catalog/report-profiles/examples` are synthetic
(`contoso.example`, `contoso.sharepoint.com`). Downloading live reports from a
customer tenant and validating real exports remain an operator pilot.
