# SharePoint and OneDrive collection 1.0.0

Two catalog families, [`sharepoint`](../../catalog/sharepoint.v1.json) and
[`onedrive`](../../catalog/onedrive.v1.json), implement these specification lines:

- SharePoint: "Graph pour sites, identifiants, groupes associés, ressources Teams
  liées et rapports disponibles." and "PnP.PowerShell pour les détails de sites,
  bibliothèques, permissions et partages selon les capacités et les droits. Module
  Microsoft.Online.SharePoint.PowerShell ou profil de rapport administratif lorsque
  cette source correspond à la donnée attendue."
- OneDrive: "Graph pour l’association au compte, les métadonnées et rapports
  accessibles." and "Exports administratifs ou script SharePoint/PnP pour compléter
  les sites personnels, volumes et contrôles de provisionnement. Signaler les
  identifiants masqués ou les liaisons impossibles."

Every producer is `SyntheticValidated`: repository tests used synthetic HTTP
responses, mocked cmdlets and synthetic CSV files only. No live tenant, live
module version or customer export has certified them.

| Producer | Channel | Sections | Scope |
| --- | --- | --- | --- |
| `clarity.graph.sharepoint` "Microsoft Graph — sites SharePoint (v1.0)" | Graph | `sharepoint.sites`, `sharepoint.groupSites`, server download of `sharepoint.siteUsage` | tenant |
| `clarity.sharepoint` "SharePoint — script PnP.PowerShell (site ciblé)" | PowerShell 7.4 | `sharepoint.siteDetails`, `sharepoint.libraries`, `sharepoint.sitePermissions`, `sharepoint.siteSharing` | one site collection URL (`resource`) |
| `report.m365.sharePointSiteUsageDetail` | Import | `sharepoint.siteUsage` | tenant |
| `clarity.graph.onedrive` "Microsoft Graph — comptes OneDrive (v1.0)" | Graph | `onedrive.accounts`, server download of `onedrive.accountUsage` | tenant |
| `clarity.onedrive` "OneDrive — script SharePoint Online (sites personnels)" | Windows PowerShell 5.1 | `onedrive.personalSites` | tenant |
| `report.m365.oneDriveUsageAccountDetail` | Import | `onedrive.accountUsage` | tenant |

Each producer is independent: Graph consent does not authorize PnP.PowerShell or
SharePoint Online Management Shell, and the Entra connection does not grant
SharePoint or OneDrive Graph access.

## Graph collectors

Use the [server certificate, Key Vault, managed identity and tenant consent
setup](microsoft-graph.md#deployment-and-consent). In Discovery, declare a Graph
connection for `clarity.graph.sharepoint` or `clarity.graph.onedrive` on an
existing M365 tenant environment, test access, then launch. Every access test and
collection first compares `GET /v1.0/organization?$select=id`
(`Organization.Read.All`) with the authorized environment; a mismatch blocks
staging and publication. Paths are GET requests on `https://graph.microsoft.com`.

| Section | Requests actually sent | Application permissions | Coverage |
| --- | --- | --- | --- |
| `sharepoint.sites` | `/v1.0/sites/getAllSites` (no query options, as documented), paged | `Sites.Read.All` | One `site` per site collection keyed by the collection GUID of the composite Graph ID; personal sites (`isPersonalSite` or a `-my.sharepoint.com` host) are skipped. Name, display name, URL, host, data location, created and modified dates. |
| `sharepoint.groupSites` | `/v1.0/groups?$filter=groupTypes/any(c:c eq 'Unified')&$select=id,resourceProvisioningOptions`, then `/v1.0/groups/{id}/sites/root?$select=id,webUrl` per group | `Group.Read.All`, `Sites.Read.All` | Microsoft 365 group → root site relation `groupSite`; `teamsConnected` comes from `resourceProvisioningOptions` containing `Team`. |
| `onedrive.accounts` | `/v1.0/users?$top=999&$select=id,userPrincipalName`, then `/v1.0/users/{id}/drives?$select=id,driveType,webUrl,createdDateTime,lastModifiedDateTime,quota,owner,sharepointIds` per user | `User.Read.All`; `Files.Read.All` or `Sites.Read.All` | `business` drives only: drive ID, site URL, dates and quota used/total/state, linked to the Entra user with `ownerOf`. |

Access tests read `getAllSites`, one group and its root site, or one user and its
drives. An empty tenant cannot prove the second read and is reported as
`GroupSitesNotTestedEmptyScope` or `DrivesNotTestedEmptyScope`.

### Server report downloads

`sharepoint.siteUsage` and `onedrive.accountUsage` can be downloaded by the server
through the matching Graph connection, in addition to consultant uploads:

- `GET /v1.0/reports/getSharePointSiteUsageDetail(period='D30')` through
  `clarity.graph.sharepoint`;
- `GET /v1.0/reports/getOneDriveUsageAccountDetail(period='D30')` through
  `clarity.graph.onedrive`.

Both need `Reports.Read.All` (application). Graph answers `302`, `303` or `307`
to a short-lived pre-authenticated URL. The redirect target is fetched **without**
the Graph bearer token and only when it is HTTPS on the default port, has no user
info or fragment, is at most 4,096 characters and its host matches
`^reports[a-z0-9-]{0,20}\.office\.com$`; any other target fails with
`ReportDownloadHostRejected`. The body is bounded by the import size limit. The
access test follows the redirect only to validate the host and does not download
the report. Neither the URL nor the CSV is logged.

The bytes go through the same versioned profile as an upload
(`ReportProfiles.Default.Require` and `ReportAdapter.Convert`). The tenant
verified on the connection is recorded as observed (`ScopeObserved: true`) with
source "Microsoft Graph reports API (Clarity connection)", the run is bound to the
connection and job, and the raw CSV is kept in private Blob storage as proof. The
explicit launch is the confirmation, as for other Graph sections: the same leased
job publishes the run through the common publication path. Identical bytes reuse
the deterministic report run only when it came from the same connection and
producer; a download whose bytes equal an earlier consultant upload fails with a
conflict instead of reusing the upload. Report
interpretation, concealed identities and their limits are described in
[Microsoft 365 usage reports](microsoft-365-usage-reports.md); every report
section carries `ReportCoverageLimited` and never proves absence.

## PnP.PowerShell script (targeted site)

Choose "SharePoint — script PnP.PowerShell (site ciblé)", enter the site collection
URL and select sections. The server normalizes the URL (HTTPS, `*.sharepoint.com`,
lower-case host, no trailing slash), rejects `-admin` and `-my` hosts, binds the
request to the producer, the exact section set, the normalized URL, the tenant and
the script version, and replays the same operation idempotently. Download
`Clarity.Discovery.SharePoint.1.0.0.zip` and the separate `parameters.json`, then
on a workstation with PowerShell 7.4 or newer and PnP.PowerShell installed by an
administrator:

```powershell
.\sharepoint\Collect-SharePoint.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip
```

The script prompts for the client ID of the Entra app registration that the
customer authorized for PnP.PowerShell interactive sign-in. The GUID is used only
for `Connect-PnPOnline -Url <site> -Interactive -ClientId <id>` and is written to
neither the package nor the parameters; Clarity stores no client ID, password,
token or certificate. Before writing any section the script requires
`Get-PnPTenantId` to equal the request's tenant and `Get-PnPSite -Includes Id,Url`
to return the requested site collection root. It disconnects in all cases.

| Section | Cmdlets actually used | Rights of the signed-in account |
| --- | --- | --- |
| `sharepoint.siteDetails` | `Get-PnPWeb -Includes Title,WebTemplate,Configuration,Language,LastItemUserModifiedDate`, `Get-PnPSite -Includes Usage` | Read access to the site |
| `sharepoint.libraries` | `Get-PnPList -Includes RootFolder,HasUniqueRoleAssignments` (BaseTemplate 101, not hidden) | Read access to the site |
| `sharepoint.sitePermissions` | `Get-PnPProperty -ClientObject (Get-PnPWeb) -Property RoleAssignments`, `Get-PnPProperty -Property Member,RoleDefinitionBindings`, `Get-PnPGroupMember -Group <id>` | Site collection administrator or permission to enumerate permissions |
| `sharepoint.siteSharing` | `Get-PnPTenantSite -Identity <site URL>` | SharePoint administrator |

The delegated rights are those of the signed-in account combined with the
permissions granted to the customer's app registration; the catalog does not
prescribe that registration's permissions. A cmdlet that fails makes only its
section `Failed` with `PnPReadFailed` or `SharingReadFailed` and a fixed message.

Objects: the site is `site`/`sharepoint.siteId`/`<site collection GUID>` with
alternate `url`, the same immutable key as the Graph collector. Libraries are
`documentLibrary`/`sharepoint.listId` with `partOf` → site. Root-web role
assignments become `sitePermission` (principal → site, property `roles`) from
Entra users (`user`/`upn` parsed from the `i:0#.f|membership|` claim), Entra
groups (`group`/`microsoft.graph` from `c:0t.c|tenant|` or
`c:0o.c|federateddirectoryclaimprovider|` claims) and SharePoint groups
(`sharepointGroup`/`sharepoint.siteGroup`/`<site GUID>/<group ID>`). Direct
SharePoint group members produce `memberOf`. Other claims (for example `_o` owner
claims, "everyone" claims, guests without a UPN claim, nested SharePoint groups)
are counted as `PrincipalUnresolved`, never invented. Sharing produces
`sharingCapability`, `sharingDomainRestrictionMode`, `defaultSharingLinkType`,
`defaultLinkPermission` and `lockState` only.

## SharePoint Online Management Shell script (personal sites)

Choose "OneDrive — script SharePoint Online (sites personnels)". The request is
bound to the tenant itself. Download `Clarity.Discovery.OneDrive.1.0.0.zip`, then
in **Windows PowerShell 5.1** on Windows with Microsoft.Online.SharePoint.PowerShell
installed by an administrator:

```powershell
.\onedrive\Collect-OneDrive.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip
```

The script prompts for the tenant admin URL `https://<name>-admin.sharepoint.com`.
Before any sign-in it reads the public
`https://login.microsoftonline.com/<name>.onmicrosoft.com/v2.0/.well-known/openid-configuration`
and requires its issuer tenant ID to equal the request; a mismatch or network
failure writes no package. It then runs `Connect-SPOService -Url <admin URL>`
(interactive SharePoint administrator sign-in), reads
`Get-SPOSite -IncludePersonalSite $true -Limit All -Filter "Url -like '-my.sharepoint.com/personal/'"`
and disconnects in all cases. A read failure makes the section `Failed` with
`SpoReadFailed`.

Each personal site of the tenant's own `-my.sharepoint.com` host becomes
`oneDrive`/`sharepoint.siteUrl`/`<normalized URL>` with alternate `url`, carrying
URL, owner UPN, used and quota storage in the module's **megabyte** integers,
last content modification, lock state and status. A UPN owner produces `ownerOf`
from `user`/`upn`.

## Account links, masked identifiers and unresolved links

| Evidence | Key | Account link |
| --- | --- | --- |
| Graph drive | `oneDrive`/`sharepoint.siteId` (immutable), alternate `url` | `ownerOf` from `user`/`microsoft.graph` (Entra object ID) |
| Usage report | `sharepoint.siteId` when the row has a valid `Site Id`, otherwise provisional `m365report.siteUrl`; alternate `url` | `ownerOf` from `user`/`upn` resolved with the UPN valid at the report date |
| SPO personal site | provisional `sharepoint.siteUrl`, alternate `url` | `ownerOf` from `user`/`upn` |

Provisional URL keys are never equated with immutable site IDs automatically;
alternate keys are evidence for the reviewable association path in
[consolidation](../consolidation.md). The following are counted explicitly, make
the section `Partial` and are never guessed:

- `OneDriveNotProvisioned`: the user has no readable `business` drive. Graph does
  not distinguish never provisioned, unlicensed and inaccessible.
- `SiteIdUnavailable`: a drive without `sharepointIds.siteId` is not converted
  rather than keyed from its URL.
- `OwnerUnresolved` (empty owner) and `OwnerMasked` (owner that is not a UPN):
  the personal site is published without an account link.
- `SiteIdentifierUnavailable`, `DuplicateSiteCollection`, `GroupSiteUnavailable`,
  `UserLimit`, `LibraryIdentifierUnavailable`, `LibraryAttributesMissing`,
  `PersonalSiteAttributesMissing`, `SiteUsageUnavailable` and
  `PrincipalUnresolved`: unreadable or unusable items are counted, not filled.
- Concealed report names and URLs follow the report profiles and are not
  converted.

## Coverage and limits

- Site metadata only. No file or list item content, item-level, folder-level or
  library-level permission expansion (unique permissions are only flagged),
  individual sharing links, external users or effective access.
- `getAllSites` returns site collections and possibly webs; only one object per
  site collection is kept, preferring the entry Graph marks as root. Subsites are
  not collected. Teams channels and their independently resolved private/shared sites are
  covered by the separate [Teams producer](teams.md), not this site enumeration.
- The PnP script covers one site collection per request and its root web only.
  `storageUsedBytes` stays unobserved when usage is unreadable.
- `onedrive.accounts` reads at most 20,000 users per run (`UserLimit`, then
  `Partial`) and one drives request per user; large tenants need a maintenance
  window and may meet throttling.
- Graph collectors use the public cloud only. Multi-geo report files are
  rejected by the profiles.
- A section is `Complete` only when every read succeeded. Absence is inferred
  only from a later complete snapshot of the same fields; reports never prove
  absence.

## Privacy and logging

Packages contain identifiers, URLs, names, UPNs, dates, sizes, role names and the
relations above. They never contain file content, credentials, tokens, client
IDs, raw Graph responses or raw cmdlet output. Script messages and warnings are
fixed strings; exception text is not copied. Server-side, connection, request,
download, upload, job launch and job execution are authorized against the
workspace and project, idempotent, and logged with correlation IDs only.

## Tested versions

- Graph: .NET 11 with synthetic Microsoft Graph REST v1.0 HTTP fixtures
  (`GraphTests.SharePoint.cs`), including composite IDs, duplicate and personal
  sites, groups without sites, drives without site IDs, allow-listed and rejected
  report redirects and the Graph bearer token never sent to the download host.
  Connection, probe, publication and download staging run as SQL tests in CI.
- PnP script: PowerShell 7 with Pester 5.7.1 and mocked PnP.PowerShell cmdlet
  shapes (`scripts/sharepoint/SharePoint.Tests.ps1`).
- SPO script: Pester 5.7.1 with mocked Microsoft.Online.SharePoint.PowerShell
  cmdlet shapes on PowerShell 7 and on Windows PowerShell 5.1
  (`scripts/onedrive/OneDrive.Tests.ps1`).
- Generated packages are validated by the C# ZIP v1 reader and catalog
  (`PowerShellContractTests`); request scoping and bundles by
  `SharePointScriptTests` (SQL cases run in CI and skip locally without
  `CLARITY_TEST_SQL`).

**No live tenant, live PnP.PowerShell or Microsoft.Online.SharePoint.PowerShell
version was used.** Pilot checks still required:

- the report redirect host family and `Location` shape;
- whether `getAllSites` returns subwebs, and whether the Unified `groupTypes`
  filter works without `ConsistencyLevel`;
- whether the report `Site Id` and PnP `Site.Id` equal Graph's
  `sharepointIds.siteId` and composite site collection GUID;
- `403` versus `404` behaviour of `/users/{id}/drives` for unlicensed users;
- `Get-PnPTenantId` and `Get-PnPTenantSite` under interactive delegated sign-in,
  and the permissions the customer's app registration needs;
- CSOM and SPO `DateTime` kinds, the `Get-SPOSite -Filter` syntax and owner
  formats in a real tenant;
- the OpenID tenant check fails closed when the admin URL prefix differs from the
  tenant's initial `onmicrosoft.com` name (for example after a tenant rename).

References:
[getAllSites](https://learn.microsoft.com/graph/api/site-getallsites?view=graph-rest-1.0),
[group root site](https://learn.microsoft.com/graph/api/site-get?view=graph-rest-1.0),
[list drives](https://learn.microsoft.com/graph/api/drive-list?view=graph-rest-1.0),
[PnP.PowerShell](https://pnp.github.io/powershell/),
[Get-SPOSite](https://learn.microsoft.com/powershell/module/sharepoint-online/get-sposite).
