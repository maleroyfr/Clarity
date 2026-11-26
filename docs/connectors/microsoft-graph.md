# Microsoft Graph Entra collector 1.0.0

## Deployment and consent

This is a server collector using Microsoft Graph **REST v1.0**, not a local
PowerShell module or a beta API. Its HTTP implementation makes paging, retry and
canonical output explicit; it does not depend on an additional Graph SDK.
Only the public `graph.microsoft.com` cloud and tenant-wide scope are supported.
The catalog marks it `SyntheticValidated`, not live-tenant certified.

An operator must register a separate, multi-tenant Entra application accepting
accounts from other organizational directories. Upload the collector's public
certificate to that registration. Each customer administrator grants application
permissions to its service principal using the tenant-specific admin-consent
flow. Consent to Clarity's interactive sign-in app is **not** collector consent.
Deployment identity, web sign-in application and collector application remain
separate. No Azure deployment or consent is performed automatically.

The private, exportable certificate remains in Azure Key Vault. Grant the running
service's managed identity certificate and backing-secret read access (for example
the appropriately scoped Key Vault Certificate User role), not secret-write or
deployment privileges. The collector downloads it with `ManagedIdentityCredential`,
loads an ephemeral private key and requests a tenant-specific
`https://graph.microsoft.com/.default` token with `ClientCertificateCredential`.
There is no developer-credential or local-file fallback. Rotate the certificate by
publishing a new Key Vault version after registering its public certificate; the
next job resolves the current version. Tokens and private keys are never persisted.

Configure the server through protected configuration:

| Key | Value |
| --- | --- |
| `GraphCollector:ClientId` | Multi-tenant collector application ID |
| `GraphCollector:KeyVaultUri` | HTTPS vault URI |
| `GraphCollector:CertificateName` | Exportable certificate name |
| `GraphCollector:ManagedIdentityClientId` | Optional user-assigned managed identity client ID; otherwise system-assigned |

A Connection stores the fixed configuration reference `collector` and its tenant,
not these credentials. Connection creation/reuse requires an authorized existing
M365 environment and workspace-manager access. Collection and permission tests
require consultant/manager access. Every job rechecks stored membership. Never put
real configuration values or customer records in this public repository.

## Sections and actual endpoints

Every run first reads `GET /v1.0/organization?$select=id` with application permission
**Organization.Read.All** and compares the observed tenant to the authorized
environment. A mismatch blocks staging and publication.

| Section | Additional application permissions | Reads |
| --- | --- | --- |
| Users and guests | `User.Read.All` | `/users/delta`, explicit selected identity, mail and on-premises sync fields |
| Groups | `Group.Read.All`; `Member.Read.Hidden` for hidden membership; `Directory.Read.All` to resolve all returned directory types | `/groups`, `/groups/{id}/members`, `/groups/{id}/owners`; direct, paged links |
| Devices | `Device.Read.All` | `/devices`, selected directory-device metadata |
| Domains | `Domain.Read.All` | `/domains`, verification/default/authentication metadata |
| Licenses | `LicenseAssignment.Read.All`, `User.Read.All` | `/subscribedSkus` and a separate full `/users?$select=id,assignedLicenses` scan |
| Applications | `Application.Read.All` | `/applications` and `/servicePrincipals`, allowlisted basic metadata only |

All paths above are prefixed `/v1.0`. Exact field lists are in `catalog/entra.v1.json`
and `EntraCollector`. No credential properties, mail content, effective licensing,
Intune, conditional access or dependency analysis is collected.

Access tests issue lightweight reads per section and one group's member/owner
reads when a sample exists. Empty groups cannot prove nested permission access.
Successful sample reads cannot prove access to every hidden group. Missing grants
are recorded as explicit section codes. Grant the documented rights and rerun;
the AD script can collect corresponding **AD** data where appropriate, but is not
an interchangeable Entra producer and does not inherit Graph consent.

## Coverage, retry and recovery

All pages, including empty pages with a next link, are followed. Continuations must
remain HTTPS public Graph v1.0 links. HTTP redirects must be disabled by the host.
Page count is capped at 1,000 per endpoint traversal; responses at 2 MiB per page
and 128 MiB per session. ZIP v1's compressed, expanded and line limits also apply.
Collection is intentionally bounded; a larger tenant requires reviewed limits or
a future scoped producer, not a silently truncated Complete result.

429/502/503/504 retry at most four HTTP attempts. `Retry-After` seconds/date is
honored up to 60 seconds; a longer delay stops with `RetryAfterLimit`, rather than
retrying early. Without a header the waits are 1, 2 and 4 seconds. Durable job
leases serialize a tenant/provider across processes; unrelated tenants can use
the two worker lanes. Section failures preserve conforming records and emit
Partial/Failed coverage and sanitized remediation codes.

Only **users** use delta. Tokens persist in workspace-isolated SQL, not raw ZIPs,
browser responses or logs. Changed scope/selected fields/producer version, a
six-day cursor age, HTTP 410 or `syncStateNotFound` forces a full read. Duplicate
user appearances are reduced by key, preserving earlier fields omitted from a
later fragment. Explicit `@removed` signals become `deleted=true`; absence from a
result never implies deletion. Incremental results are marked Partial with
`IncrementalChanges`: even an empty delta is not a known-zero tenant.
License scans are full because user delta does not track every licensing change.

The v1.0 group members endpoint can omit service principals. Group coverage is
therefore explicitly Partial with a limitation warning, even after successful
pages. Minimal/unresolved directory references stay references, not invented
complete objects.

The resulting package passes the same schemas, catalog, Blob/SHA verification,
environment validation and atomic publisher as a ZIP upload. An authorized Graph
launch supplies confirmation; uploaded files still need a preview and explicit
confirmation. A durable prepared package is reused after interruption. Published
sections are idempotent; cursor advancement follows publication under a fenced
job lease. Failure before advancement replays the previous cursor safely.
Prior observations remain available during pending, failed and partial work.

## Verification and operator pilot

The automated suite uses .NET 11, synthetic Graph HTTP fixtures, the actual ZIP
reader and SQL Server 2022 in CI. It covers paging, replay, empty versus unknown,
throttling, unsafe links, expired/changed cursors, missing permissions, all six
section shapes, environment mismatch and interruption after actual publication.
Azure Key Vault Certificates 4.10.0 APIs are compiled; live certificate retrieval,
admin consent, actual tenant rights and real Graph behavior are **not** certified
by mocks. Complete an operator pilot before production use.

References:
- [User delta](https://learn.microsoft.com/graph/api/user-delta?view=graph-rest-1.0)
- [Paging](https://learn.microsoft.com/graph/paging)
- [Throttling](https://learn.microsoft.com/graph/throttling)
- [Group members and v1.0 limits](https://learn.microsoft.com/graph/api/group-list-members?view=graph-rest-1.0)
- [Application admin consent](https://learn.microsoft.com/entra/identity-platform/v2-admin-consent)
