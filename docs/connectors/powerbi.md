# Power BI collection 1.0.0

Use this collector to inventory **what the signed-in operator can see**, not to
audit an entire tenant. `clarity.powerbi` is a PowerShell producer for an
`M365Tenant` environment. All inventory calls are GETs to Power BI REST v1.0
in the public cloud; it does not use Graph consent, an admin scanner or a server
connection.

## Run a request

1. In Discovery, select **Power BI — script PowerShell** in the Microsoft 365
   environment, choose the sections and create the request. The server binds it
   to the environment tenant, producer version and section set. There is no AD
   domain, individual workspace selector or client-supplied tenant override.
2. Download `Clarity.Discovery.PowerBI.1.0.0.zip` and `parameters.json`
   separately. Unzip the bundle and place the parameters file beside the
   `powerbi` directory. Do not edit it or add credentials.
3. Use a **dedicated PowerShell 7.2+ process** with
   [MicrosoftPowerBIMgmt.Profile 1.3.84](https://www.powershellgallery.com/packages/MicrosoftPowerBIMgmt.Profile/1.3.84)
   already installed by the operator. From the extracted directory run:

   ```powershell
   pwsh -NoProfile -File .\powerbi\Collect-PowerBI.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip
   ```

   Sign in to the requested tenant in the Microsoft prompt. The script uses
   [`Connect-PowerBIServiceAccount -Environment Public`](https://learn.microsoft.com/powershell/module/microsoftpowerbimgmt.profile/connect-powerbiserviceaccount)
   and compares the returned profile's `TenantId` to the request **before any
   REST read or ZIP creation**. It disconnects in `finally`. Use a fresh process
   because connecting replaces its current Power BI profile. No module installs,
   app credentials, token parsing, customer data uploads or rights elevation
   are performed by the script.
4. Review `result.zip` locally, then upload it (not the script bundle) to that
   environment. Check the preview and section issues, confirm, and let the job
   publish the observed objects and links. The inventory proof shows ZIP import,
   `Power BI REST v1.0`, script/module versions, tenant, time, scope, section,
   filename and uploader. Re-importing identical bytes does not publish twice.

## Endpoint, rights and coverage matrix

[`Invoke-PowerBIRestMethod -Method Get`](https://learn.microsoft.com/powershell/module/microsoftpowerbimgmt.profile/invoke-powerbirestmethod)
uses relative URLs under `https://api.powerbi.com/v1.0/myorg/`. Scope names below
are the documented **read** alternatives, not grants made by Clarity. Resource
permissions are also required. `groups` means workspaces, not Entra groups.

| Section (`powerbi.` prefix) | Actual endpoint | Delegated scopes and visibility |
| --- | --- | --- |
| `workspaces` | [groups?$top=100&$skip=N](https://learn.microsoft.com/rest/api/power-bi/groups/get-groups) | `Workspace.Read.All`; user's accessible workspaces only. Excludes My workspace. |
| `reports` | [groups/{groupId}/reports](https://learn.microsoft.com/rest/api/power-bi/reports/get-reports-in-group) | Workspace list plus `Report.Read.All`; visible reports. Paginated reports have no datasetId. |
| `semanticModels`, `owners` | [groups/{groupId}/datasets](https://learn.microsoft.com/rest/api/power-bi/datasets/get-datasets-in-group) | Workspace list plus `Dataset.Read.All`. Read-only access can return just ID and name; full properties, including `configuredBy` (dataset owner), require dataset write permission. No write is attempted. |
| `workspaceRoles` | [groups/{groupId}/users?$top=100&$skip=N](https://learn.microsoft.com/rest/api/power-bi/groups/get-group-users) | `Workspace.Read.All` plus the right to read that workspace's entries. User permission changes can lag. |
| `gateways` | [gateways](https://learn.microsoft.com/rest/api/power-bi/gateways/get-gateways) | `Dataset.Read.All`; gateway administrator only. Cluster ID is the primary gateway's ID; VNet gateways aren't supported. |
| `dataSources` | Gateway list, then [gateways/{gatewayId}/datasources](https://learn.microsoft.com/rest/api/power-bi/gateways/get-datasources) | `Dataset.Read.All` and admin on that gateway. Checks the returned gateway ID against the requested parent. |
| `modelSources` | Workspace and dataset lists, then [groups/{groupId}/datasets/{datasetId}/datasources](https://learn.microsoft.com/rest/api/power-bi/datasets/get-datasources-in-group) | `Workspace.Read.All`, `Dataset.Read.All` and **write permission on the dataset**. Sources without a stable source ID cannot be linked. |

## Reading the inventory

Workspace, report, semantic model, gateway and data source have separate object
types in `powerbi.id`, keyed by their service GUID in this environment. Relations
record workspace containment, report-to-model, model-to-source and
source-to-gateway dependencies. A returned external model/gateway ID can stay
pending. Only the source GUID, associated GUIDs and a documented, allowlisted
source type are retained from data sources. No source name, connection string,
server/database/path, credential type/details, public key, embed URL, query,
report contents or raw response is written. Workspace, report, model and
gateway display names and owner identifiers can still be sensitive.

Model ownership is the returned `configuredBy` address, resolved as an exact
`user/upn` reference; missing or non-address owners are flagged. Direct workspace
roles have a separate `workspaceRoleAssignedTo` link and its observed `role`
property: typed users by address, groups by GUID in `microsoft.graph`. Unobserved
or ambiguous identities are not invented or merged. No role, including Admin,
proves ownership, group membership, effective access, or a report's owner.
App principals and unsupported identifiers are not projected as people.

All successful sections are **Partial**, including zero visible rows. The
catalog rejects a Power BI result labeled Complete. Omission never proves
removal. A denied, throttled, malformed or interrupted list has
`PowerBIReadFailed`; previously written observations survive. A failed read
with no usable records is Failed (unknown counts), not a successful empty list.
`PowerBIUnlinked` flags omitted fields or identities; `PowerBIVisibility`
reminds the reader that visibility is operator-specific. Checks are per request:
there is no retry, snapshot isolation, resume cursor, admin expansion or
inferred deletion. Each section reads its own parents; limit section selection
on large installations. Report, gateway and model detail lists use their
documented collection shapes; a continuation on an unpaged API is not followed.

## Validation boundary

`SyntheticValidated`: PowerShell 7.6.10 and Pester 5.7.1 with a **synthetic**
1.3.84 profile module implementing the documented cmdlet/REST shapes. Generated
packages go through the ZIP v1 reader. The SQL journey executes the downloaded
bundle with server-generated parameters and a fake profile, then uploads,
previews, confirms and checks inventory/provenance. SQL runs in CI, not locally
without `CLARITY_TEST_SQL`. The version pin identifies the pilot target, **not**
a live-certified module. Before production, verify interactive tenant selection
(including guests), actual profile and REST shapes, RBAC for each section,
gateway technology, PowerShell host compatibility, throttling and tenant scale
in an authorized pilot. No live customer tenant is needed for the automated tests.
