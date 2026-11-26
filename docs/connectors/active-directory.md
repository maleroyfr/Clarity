# Active Directory Discovery 1.0.0

Download `clarity-ad-1.0.0.zip` and the separate request parameter JSON from the
authorized dossier. Extract the **script bundle** on a Windows machine with RSAT
ActiveDirectory, sign/review scripts according to your organization's policy, and
open an already authorized local read session. Do not disable execution policy.

```powershell
.\ad\Collect-AD.ps1 -ParameterFile .\parameters.json -OutputPath .\results.zip
```

Upload **results.zip**, not the script bundle, from any authorized browser/machine.
No connection secret, token, certificate or local credential is in the parameters.
The immutable bundle embeds the exact AD/common source together; parameters stay
outside it for stable signing. Never change a published script under the same
producer version; publish a new version and retain the old bundle for its requests.

## Actual reads and rights

`Get-ADDomain` / `Get-ADForest` check the observed DNS domain/forest against the
request. OU requests also require `domain` metadata and an actual
`Get-ADOrganizationalUnit -Identity` read inside that domain. `Get-ADObject -Identity`
checks the search base. No domain or module installation is performed automatically.

`Get-ADUser`, `Get-ADComputer`, `Get-ADGroup` and `Get-ADOrganizationalUnit` use
`-Filter * -SearchBase <requested DN> -Server <verified domain> -ResultPageSize 500`
and explicit attribute allowlists. Rights are ordinary authorized reads of those
objects/attributes and membership ranges, not Domain Admin by default.
`Get-ADObject -Properties member;range=<start>-<end>` reads groups in ranges of at
most 1,000, advancing from the actual response boundary until `*`. Direct member
references inside scope are resolved with `Get-ADObject -Identity`; outside-scope
DNs remain unresolved references. There is no transitive/effective-access claim.

objectGUID is the stable primary key; SID is an alternate. UPN, mail,
proxyAddresses, primaryGroupID and `mS-DS-ConsistencyGuid` (base64 bytes) are
preserved without assuming a configured Entra sourceAnchor. Primary-group links
derive the group SID from the account domain SID plus primaryGroupID. They remain
typed SID references until resolved. Missing attributes/references and read errors
produce partial/failed coverage, never a fabricated zero.

## Tested versions and limits

Repository tests execute on PowerShell **7.6.10 Windows** with **Pester 5.7.1**,
and Linux CI with mocked AD cmdlets. Mocks represent ActiveDirectory **1.0.1.0**
property shapes, including 1,001 group members, ranged continuation, out-of-scope
references, primary groups, byte-array anchors and inaccessible sections.
Generated output is validated through the actual C# ZIP v1 ingestion reader.

The script uses PowerShell 5.1-compatible syntax but **Windows PowerShell 5.1,
RSAT and a live AD forest have not been certified**. Linux tests are not a claim
that ActiveDirectory runs there. The operator pilot must verify OS/module version,
AD Web Services connectivity, object/attribute ACLs and large-group behavior.
LDAP can hide values due to ACLs; even an empty returned property is not proof of
unrestricted read rights. Scope/field coverage must be interpreted with that limit.
Concurrent group changes cannot produce a transactionally consistent AD snapshot;
inconsistent ranges/duplicates reduce coverage. No passwords, recovery keys, GPO
settings, file/message contents or effective permission analysis are collected.

References: [Get-ADObject](https://learn.microsoft.com/powershell/module/activedirectory/get-adobject)
and [attribute range retrieval](https://learn.microsoft.com/windows/win32/adsi/attribute-range-retrieval).
