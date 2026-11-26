# Intune and Windows Autopilot collection

`clarity.graph.intune` 1.0.0 is an application-only Microsoft Graph collector
for one declared M365 tenant. Select **Microsoft Graph — Intune et Autopilot
(v1.0)** in Discovery, declare its connection, test access and launch the
required sections. The Entra connection remains independent; an existing Entra
grant or successful Entra collection does not establish Intune access.

## Prerequisites and actual reads

Use the [server certificate, Key Vault, managed identity and tenant consent
setup](microsoft-graph.md#deployment-and-consent). The customer needs an active
Intune license and must grant the collector **application** permissions, not
delegated or write permissions. Every access test and new collection first
compares `GET /v1.0/organization?$select=id` (`Organization.Read.All`) with the
authorized environment. No credential or identity comes from a report or a
browser-supplied workspace ID.

Every row below was exercised against **synthetic REST v1.0 HTTP responses**,
not a live service. Paths are GET requests relative to `https://graph.microsoft.com/v1.0`.
`DM` = `/deviceManagement`; `DA` = `/deviceAppManagement`. Section IDs in a
package are `<capability>:<lower-case tenant GUID>`. Each run records the
producer/version, `Microsoft.Graph = REST v1.0`, observed tenant, exact scope,
connection, timestamps, section outcome, counts, covered fields and relations.

| Stable capability | Endpoint(s) | Additional application permission | Coverage and limit |
| --- | --- | --- | --- |
| `intune.managedDevices` | `DM/managedDevices` | `DeviceManagementManagedDevices.Read.All` | Device name, serial, OS/version, management agent, ownership **category**, device-level compliance and directory device ID. Not per-setting compliance. |
| `intune.primaryUsers` | `DM/managedDevices`, then `/{id}/users` on that collection | `DeviceManagementManagedDevices.Read.All` | Every returned primary user ID, including no user or multiple users. No inference from `userId`, enrollment UPN or last logon. |
| `intune.registeredOwners` | `/devices`, then `/devices/{id}/registeredOwners` | `Device.Read.All` | Entra directory owners of visible directory devices, not only Intune-managed devices. Only typed IDs are retained. |
| `intune.autopilotIdentities` | `DM/windowsAutopilotDeviceIdentities` | `DeviceManagementServiceConfig.Read.All` | Identity, serial, make/model, enrollment state and exposed managed/directory device IDs. The v1.0 `azureActiveDirectoryDeviceId` field is deprecated; no guessed replacement. No product keys, hardware hashes or imported identities. |
| `intune.apps` | `DA/mobileApps` | `DeviceManagementApps.Read.All` | App ID, display name and publisher; not binaries, configuration, installs or detected software. |
| `intune.profiles` | `DM/deviceConfigurations` | `DeviceManagementConfiguration.Read.All` | Classic device configuration profile ID, name and version; no settings payload. |
| `intune.policies` | `DM/deviceCompliancePolicies` | `DeviceManagementConfiguration.Read.All` | Compliance policy ID, name and version; not all Intune policies or per-device enforcement. |
| `intune.appAssignments` | `DA/mobileApps`, then `/{id}/assignments` | `DeviceManagementApps.Read.All` | Parent app, assignment ID, intent, target type and exposed group ID. Not effective installations. |
| `intune.profileAssignments` | `DM/deviceConfigurations`, then `/{id}/assignments` | `DeviceManagementConfiguration.Read.All` | Parent classic profile, assignment ID and declared target. Not effective settings. |
| `intune.policyAssignments` | `DM/deviceCompliancePolicies`, then `/{id}/assignments` | `DeviceManagementConfiguration.Read.All` | Parent compliance policy, assignment ID and declared target. Not effective compliance. |

The [family catalog](../../catalog/intune.v1.json) declares the exact routes,
fields and relations. Root object reads select `id` and their allowlisted
attributes. Fan-out reads select parent IDs, then child IDs for users/owners or
`id,target` (also `intent` for apps) for assignments. The access test reads the
first parent page and one child page when a parent is present; an empty parent
sample leaves child access **untested**, not verified. A 403 produces
`MissingPermission` for that section, not a zero count. One successful sample
does not establish rights to all objects.

**Beta boundary:** `/beta/deviceManagement/windowsAutopilotDeploymentProfiles`
and `/beta/deviceManagement/configurationPolicies` and their assignments are
**not collected, tested or enabled** by this version. The v1.0 profile row is not
Autopilot deployment-profile or settings catalog coverage. There is no silent
beta fallback; even a beta paging link is rejected.

## Identity, assignments and current state

A managed device, an Autopilot identity and an Entra directory device are
separate objects. Intune object IDs use `microsoft.intune`; Entra primary keys
use `microsoft.graph`. `referencesManagedDevice` uses the exposed managed device
ID. `referencesEntraDevice` points to Entra's **alternate `deviceId`**, not its
directory object `id`. Null, zero and unusable link IDs make no link. Name and
serial are evidence for review, never merge keys. A link can resolve after the
Entra collection arrives; an ambiguous ID stays unresolved.

`intunePrimaryUserOf` connects a user to a **managed device**;
`entraRegisteredOwnerOf` connects a directory object to a **directory device**.
Both are observed edges, not membership, same-person proof or a confirmed
AD association. Follow the separate [same-side AD/Entra decision](../consolidation.md#same-side-associations)
to reach the AD computer; an observed user or owner cannot validate that
decision. No cross-tenant or source-to-target matching is performed here.

Each assignment is a typed object keyed by `parentId:assignmentId`.
`assignmentOf` points to its parent. `targetsGroup` identifies the declared
group of an inclusion **or exclusion**; `targetType` preserves the difference.
All-device and all-licensed-user targets are retained as types without invented
group or user IDs. Targets are not expanded; filters, policy settings, effective
membership and access are not evaluated.

All reads page through the shared bounded Graph transport. There is no delta.
A successful section is complete for its documented visible rows only and
carries `IntuneVisibleScope`: a later omission cannot erase earlier presence
or retire a relationship. A failed first read has unknown counts; a failed later
read keeps already collected evidence and reports partial coverage. Unreported
fields stay absent, explicit nulls stay null. No consent token, raw Graph
response body, configuration payload, product key, bypass code or user email is
written into the normalized ZIP. Publication, retries and proof use the same
[authorized, idempotent pipeline](../discovery.md#atomic-section-publication)
as the Entra collector.

## Verification and operator pilot

`GraphTests.Intune*` checks each v1.0 route with synthetic data, nested paging,
denied reads, typed links and assignments, the beta boundary, and ZIP/catalog
validation. SQL cases run the connection checks and the real job/publisher;
they distinguish Intune and Entra evidence from the validated hybrid AD device
association. No live admin consent, Key Vault certificate retrieval, customer
licensing/RBAC visibility, tenant-size throughput, device enrollment or
production Graph response shape has been certified. Verify those with a
consenting pilot tenant before production use.

Microsoft references: [managed devices and primary users](https://learn.microsoft.com/graph/api/resources/intune-devices-manageddevice?view=graph-rest-1.0),
[registered owners](https://learn.microsoft.com/graph/api/device-list-registeredowners?view=graph-rest-1.0),
[Autopilot identities](https://learn.microsoft.com/graph/api/resources/intune-enrollment-windowsautopilotdeviceidentity?view=graph-rest-1.0),
[mobile apps](https://learn.microsoft.com/graph/api/resources/intune-apps-mobileapp?view=graph-rest-1.0),
[device configurations](https://learn.microsoft.com/graph/api/resources/intune-deviceconfig-deviceconfiguration?view=graph-rest-1.0),
[compliance policies](https://learn.microsoft.com/graph/api/resources/intune-deviceconfig-devicecompliancepolicy?view=graph-rest-1.0).
