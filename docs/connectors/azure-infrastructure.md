# Azure and local infrastructure collection

These are two independent Discovery downloads. Neither runs from the web app,
uses Graph consent, signs in, installs modules, elevates privileges or configures
resources. Create one request per target in the authorized dossier. Extract its
versioned script ZIP and put the separately downloaded `parameters.json` beside
the script folders. Run only in an operator-controlled session. Upload **the
result ZIP**, preview its coverage and confirm before publication.

| Producer | Declared environment external ID | Exact `resource` scope | Pilot runtime |
| --- | --- | --- | --- |
| `clarity.azure` 1.0.0 | `AzureScope`: `<tenant-guid>/subscriptions/<subscription-guid>` | `/subscriptions/<subscription-guid>` or that path plus `/resourceGroups/<name>` | PowerShell Core 7.4+, public Azure, existing Az context. Supported [Az 17.0.0 component versions](https://www.powershellgallery.com/packages/Az/17.0.0): Accounts 5.5.5, Resources 10.2.3, OperationalInsights 3.4.1. |
| `clarity.infrastructure` 1.0.0 | `Other`: joined DNS domain, e.g. `example.test` | One local FQDN, e.g. `server.example.test` | Native 64-bit Windows PowerShell 5.1, local domain-joined Windows Server 2019 or later; CimCmdlets and the Windows providers below. No PowerShell 7 compatibility proxies. |

```powershell
.\azure\Collect-Azure.ps1 -ParameterFile .\parameters.json -OutputPath .\azure-result.zip
```

```powershell
.\infrastructure\Collect-Infrastructure.ps1 -ParameterFile .\parameters.json -OutputPath .\server-result.zip
```

**Scope checks precede inventory.** Azure compares the active public-cloud context
and a subscription lookup by **both** tenant and subscription ID to the declared
environment. All reads use that context via `-DefaultProfile`. A group request
also requires one successful read by exact group name and matching ARM ID; it
never falls back to listing the subscription. Use canonical ARM path separators,
`subscriptions` and `resourceGroups`. Group names here support the ASCII subset
of Azure names, not wildcards. Create another request for another subscription.
Lighthouse, sovereign clouds and cross-tenant administration are not supported.

On Windows, the script checks local `Win32_ComputerSystem` domain membership and
DNS host name against the OS networking API's host name and primary DNS suffix,
and checks the server SKU/build with `Win32_OperatingSystem`. The requested
FQDN must match this machine, not an alias. Workgroups, disjoint DNS namespaces,
remote CIM sessions and domain-wide enumeration are unsupported. No DNS lookup
or network scan is necessary. Each request uses `resource`, never an AD domain
or OU selector.

## What is actually read

| Section | Read and output | Required access and limits |
| --- | --- | --- |
| Subscription | [`Get-AzContext`](https://learn.microsoft.com/powershell/module/az.accounts/get-azcontext), [`Get-AzSubscription -TenantId -SubscriptionId`](https://learn.microsoft.com/powershell/module/az.accounts/get-azsubscription): name and state | `Microsoft.Resources/subscriptions/read` for identity verification. Exactly one subscription, not all tenants/subscriptions accessible to the caller. At group scope the subscription section is **NotApplicable**. |
| Resource groups | [`Get-AzResourceGroup`](https://learn.microsoft.com/powershell/module/az.resources/get-azresourcegroup), optionally `-Name`: ARM ID, group name, location, parent subscription | `Microsoft.Resources/subscriptions/resourceGroups/read` within scope; also required to attest any group-scoped request. |
| Log Analytics | [`Get-AzOperationalInsightsWorkspace`](https://learn.microsoft.com/powershell/module/az.operationalinsights/get-azoperationalinsightsworkspace), optionally `-ResourceGroupName`: [workspace](https://learn.microsoft.com/dotnet/api/microsoft.azure.commands.operationalinsights.models.psworkspace) ARM ID, name, location, parent group | `Microsoft.OperationalInsights/workspaces/read` within scope. Management plane only: no queries, events, messages, tables, data-plane roles, workspace/shared keys or tags. |
| Server | `Win32_ComputerSystem`: DNSHostName, Domain, PartOfDomain; `Win32_OperatingSystem`: ProductType, Version, Caption | Read local `root\cimv2` for every request. Publish FQDN, joined domain and OS caption. No AD computer enumeration or machine GUID correlation. |
| DNS | [`MicrosoftDNS_Zone`](https://learn.microsoft.com/windows/win32/dns/microsoftdns-zone): Name, ZoneType | Local DNS role and read access to `root\MicrosoftDNS`. Zone headers only; no DNS records, transfers or secondary-server lists. |
| Shares | [`Win32_Share`](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-share): Name, Type | Local provider read; no path, file listing, content, ACL or share access test. |
| Printers | [`Win32_Printer`](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-printer): Name, filtered to `Local = TRUE AND Network = FALSE` | Local spooler and provider read. Queue name only; no remote queues, jobs, documents or ports. |
| Services | [`Win32_Service`](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-service): Name, State | Local provider/SCM metadata read. No raw service command line, executable path, account or registry value requested. |
| Certificates | [`Get-ChildItem` on the Certificate provider](https://learn.microsoft.com/powershell/module/microsoft.powershell.security/about/about_certificate_provider) at `LocalMachine\My`: Thumbprint, NotBefore, NotAfter | Read public metadata in the machine Personal store only. No private-key access, export, bytes, subject, certificate chain or inferred application binding. |
| Service dependencies | [`Win32_DependentService`](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-dependentservice): Dependent, Antecedent, TypeOfDependency | Local association read. Direction is dependent service → referenced service. The provider's numeric dependency type is retained; a link is not proof of runtime traffic. |
| IIS applications / pools | [`Application`](https://learn.microsoft.com/iis/wmi-provider/application-class): SiteName, Path (virtual), ApplicationPool; [`ApplicationPool`](https://learn.microsoft.com/iis/wmi-provider/applicationpool-class): Name | IIS Management Scripts and Tools provider and authorized reads on local `root\WebAdministration`. A case-sensitive configured pool name is a reference, not proof that the pool exists. No process model, physical paths, site configuration, credentials or connection strings read. |

Windows class reads use [`Get-CimInstance -ClassName -Property`](https://learn.microsoft.com/powershell/module/cimcmdlets/get-ciminstance)
with no remote parameter; the default namespace is `root\cimv2`. Providers have
their own ACLs. The operator must arrange read rights separately; Discovery does
not grant them. An unavailable optional provider or denied store fails that
section without discarding the other sections. Missing scope-attestation reads
instead stop the collection before any result ZIP is created.

## Identity, coverage and proof

Azure uses case-normalized ARM IDs (`azure.armId`), after validating each row's
resource group/subscription path. Local primary keys (`windows.hostResource`)
combine the verified FQDN with the object type and provider key: for instance,
service name, store/thumbprint, or case-preserved IIS site/virtual path. Key
components are escaped separately. These are resource identities, **not**
hardware, AD forest or cross-environment identities: renames/moves can produce
new keys and reuse of a name cannot distinguish a replacement.

A successful read covers only the readable portion of the exact scope. Every
such section includes a visibility warning, including a readable zero, so
omission cannot retire prior evidence. A read interrupted after records were
written is `Partial` and retains them; an unreadable section is `Failed` with
unknown counts. Host containment, ARM containment, direct service dependencies
and explicit IIS pool references are separate typed edges. Missing endpoints
remain pending. No generic application-to-database, network, user, certificate
or cross-environment dependency is inferred.

Import requires the request, version, section set, observed environment and exact
scope to match. Object and relationship identities must stay in the requested
resource boundary (a group may reference its parent subscription). Complete
sections must retain the producer's visibility warning. Relationship metadata
is limited to the documented numeric service dependency type; these producers
do not report fallback identities or deletion signals. These checks
validate consistency, not the signature or truthfulness of a user-uploaded file.
The usual private upload, preview, confirmation, worker reauthorization,
idempotent section publication and dated proof apply.

**Validation matrix:** Pester 5.7.1, Windows PowerShell 5.1 (infrastructure)
and PowerShell 7.6.10 (mocked providers); Linux CI also runs synthetic tests.
Tests generate real ZIPs, check scope and unsafe-field exclusions, and exercise
both imports through the disposable SQL worker. No live host or Azure tenant
was inventoried. A user-led pilot must verify module loading, platform/provider
shapes, least-privilege RBAC and ACL visibility, restricted/empty roles, naming
limits, throughput and package-size limits on representative authorized targets.
