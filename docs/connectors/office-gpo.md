# Office / GPO Discovery 1.0.0

This separate PowerShell family inventories GPO metadata, direct OU links,
permission summaries and **six Office 16.0 user ADMX-backed macro settings**.
It does not replace the Active Directory family or enumerate AD users, computers,
groups or group membership. It does not establish effective policy.

## Execution and prerequisites

In Discovery, select an AD forest, the Office/GPO family, a domain or OU scope,
and sections. Download the immutable script bundle and separate request parameters.
Extract the bundle on an authorized Windows workstation or server.

```powershell
.\officegpo\Collect-OfficeGpo.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip
```

Upload the **result** ZIP through the authorized dossier, review its coverage,
then confirm publication. The script/common module travel together; parameters
are not embedded in `Clarity.Discovery.OfficeGpo.1.0.0.zip`. Existing workspace authorization, private upload,
request binding, idempotent publication and provenance apply unchanged.

Required local tools are RSAT Group Policy Management (`GroupPolicy` 1.0.0.0)
and AD DS tools (`ActiveDirectory` 1.0.1.0). Run **Windows PowerShell 5.1**
(`powershell.exe`), not `pwsh`: version 1.0.0 of this collector targets native
RSAT property types, not deserialized compatibility proxies. The script checks
the OS, PowerShell edition/version, module presence and exact import versions,
and records the loaded versions. It never installs tools, changes execution
policy or opens a login.
Review/sign the source according to local policy.

Run with an already authorized Windows identity with domain trust/connectivity,
AD Web Services access and access to the selected domain controller and SYSVOL.
No Graph consent is involved. No credentials, tokens, certificates or passwords
belong in request metadata. `Get-ADDomain` / `Get-ADForest` verify the actual DNS
domain and forest; an OU must be read with `-Identity` and belong to that domain.
Subsequent reads pin the observed domain's `PDCEmulator`. A mismatch or an
unverifiable environment stops before producing a package.

## Actual reads and permissions

| Section | Actual commands and source properties | Read rights |
| --- | --- | --- |
| Selection | `Get-ADDomain -Server`: DNSRoot, Forest, DistinguishedName, PDCEmulator; `Get-ADForest -Server`: Name. `Get-ADOrganizationalUnit -Identity` or `-Filter * -SearchBase -SearchScope Subtree -ResultPageSize 500 -Server`: ObjectGUID, DistinguishedName | Read domain/forest metadata; List Contents and Read Property on the requested OU subtree |
| `officeGpo.policies` | `Get-GPO -All -Domain -Server` for domain scope, or `-Guid` for distinct directly linked GPOs in OU scope. Id, DisplayName, DomainName, GpoStatus, CreationTime, ModificationTime, WmiFilter presence | GPO Read on the AD policy containers and SYSVOL files; no Domain Admin requirement |
| `officeGpo.links` | `Get-GPInheritance -Target -Domain -Server`: Path, GpoInheritanceBlocked, **GpoLinks** with GpoId, Target, Enabled, Enforced, Order | Read the selected OUs' gPLink/gPOptions and identity attributes |
| `officeGpo.securityFiltering` | `Get-GPPermission -Guid -All -DomainName -Server`: Trustee.Sid (string or Value), Trustee.SidType, Permission, Inherited | GPO Read and READ_CONTROL for permission summaries; no Apply Group Policy, edit or Modify Security permission required |
| `officeGpo.officeSettings` | `Get-GPRegistryValue -Guid -Key -Domain -Server`: FullKeyPath, ValueName, HasValue, PolicyState, Type, Value | GPO Read, including the GPO's registry policy files in SYSVOL |

The registry keys are exactly
`HKEY_CURRENT_USER\Software\Policies\Microsoft\office\16.0\word\security`,
`...\excel\security` and `...\powerpoint\security`. Only `VBAWarnings` and
`blockcontentexecutionfrominternet` DWORD values are emitted. These correspond to
Office's VBA Macro Notification Settings and Block macros from running in Office
files from the Internet policies. The original numeric value is retained, not
translated into a guessed client outcome. `PolicyState=Delete` records a removal
instruction with a null value; it is not a deletion of the inventory object.
Other value names and subkey descriptors are discarded. Strings, arbitrary
registry content, raw reports and full security descriptors are not persisted.
The script does not read or certify installed ADMX/ADML files or their versions.

## Scope, relationships and incomplete evidence

Domain scope selects all **readable** domain GPOs. Link collection visits all
readable OUs in that domain, but excludes the domain root and sites. OU scope
selects that exact OU plus descendants and only GPOs **directly linked** there,
including disabled links. Both modes preserve the exact requested `{kind,value}`
in every section. `InheritedGpoLinks` is deliberately not used: it cannot prove
computer-specific policy application. This is not a snapshot across a forest.

GPO identity is `groupPolicyObject / ad.gpoId / GUID`. `gpoLinkedToOu` targets an
`organizationalUnit / ad.objectGUID` reference, retaining order, enabled/enforced,
target DN and inheritance-blocked metadata on its observation. The collector
does not create placeholder OU inventory. Later AD observations can resolve
these GUIDs even after an OU rename.

`GpoApply` permission summaries produce **gpoSecurityFilter** edges. Other supported
summaries produce distinct **gpoPermission** edges, retaining the permission and
inherited flag. Group/Alias/WellKnownGroup, User and Computer SID types become
group, user and computer references in `ad.objectSid`; other SID types remain
`directoryObject` references. Unknown principals stay pending rather than being
matched by display name. `GpoCustom` is retained as permission evidence but makes
the section partial; individual deny ACEs are not interpreted.

Each Office setting is a distinct `officePolicySetting` with a stable composite
GPO/application/value-name identity and **officeSettingInGpo** relationship.
Different GPOs remain independent even when they set the same Office value.
The existing relationship projection resolves endpoints only inside the same
workspace/environment. None of these relationships is transitive membership,
an inferred computer assignment or proof of effective access.

A successfully read empty scope has Complete coverage and zero counts. Enumeration,
per-OU and per-GPO failures retain successful observations and sanitized issue codes.
A section with no successfully read unit and no usable observations is Failed
with unknown counts. Mixed success and failure yields Partial, even when a
successfully read unit was empty. Every section warns about
ACL-filtered visibility, preventing absence inference from a seemingly complete
listing. Failed refreshes do not erase existing evidence.

`Get-GPRegistryValue` reports errors for unconfigured keys as well as access or
connectivity problems. Version 1.0.0 conservatively treats **all such errors as
incomplete coverage**, rather than parsing localized error text or claiming that
missing means not configured. A GPO without any of the three keys can therefore
have failed Office settings coverage while its metadata, links and permissions
remain usable. Missing-key discrimination requires live-module pilot evidence.

## Validation and pilot limits

Synthetic tests run with **Windows PowerShell 5.1.26100.10068** and **PowerShell
7.6.10 Windows**, Pester **5.7.1**, mocked GroupPolicy **1.0.0.0** and
ActiveDirectory **1.0.1.0** property shapes; Linux CI runs the same mocks. The
PowerShell 7 tests do not imply supported live collection. Generated ZIPs are checked by the actual ZIP v1 reader and
catalog. SQL acceptance publishes these outputs, resolves later OU/group/computer
evidence, retains unknown principals, checks idempotency and cross-workspace RLS,
and verifies that failed refreshes preserve current relationships.

```powershell
$env:CLARITY_SCRIPT_FIXTURES = Join-Path (Get-Location) 'artifacts\script-fixtures'
Import-Module Pester -RequiredVersion 5.7.1
Invoke-Pester -Path scripts -CI
dotnet test --project tests\Clarity.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~OfficeGpoTests|FullyQualifiedName~PowerShellContractTests'
```

SQL tests require `CLARITY_TEST_SQL` and explicitly skip when absent locally.
CI uses disposable SQL Server and checks the migration bundle twice.
No live RSAT/domain session or Office template revision is certified by these
mocks. The user-led pilot must confirm
those versions, GPPermission trustee shapes, per-object/attribute ACL visibility,
missing-registry-key behavior, SYSVOL/AD replication and representative volumes.

Excluded: effective policy/RSoP, security-token or nested-group expansion, WMI
evaluation, loopback processing, domain/site/inherited links, computer-to-OU
containment inventory, Group Policy Preferences, Office Cloud Policy/Intune,
machine-side Office settings, non-macro settings and non-16.0 templates.
Collection does not modify a GPO or run a migration.

References: [Get-GPO](https://learn.microsoft.com/powershell/module/grouppolicy/get-gpo),
[Get-GPInheritance](https://learn.microsoft.com/powershell/module/grouppolicy/get-gpinheritance),
[Get-GPPermission](https://learn.microsoft.com/powershell/module/grouppolicy/get-gppermission),
[Get-GPRegistryValue](https://learn.microsoft.com/powershell/module/grouppolicy/get-gpregistryvalue),
[Office macro policies](https://learn.microsoft.com/deployoffice/security/internet-macros-blocked).
