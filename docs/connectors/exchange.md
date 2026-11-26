# Exchange Online collection 1.0.0

Producer `clarity.exchange` 1.0.0 (channel PowerShell, `M365Tenant` environments,
readiness `SyntheticValidated`) implements the specification line "Module
ExchangeOnlineManagement. Destinataires, boîtes utilisateur, partagées et
ressources, archives, tailles, alias, délégations, groupes mail. Examiner au
besoin les domaines acceptés, connecteurs et règles utiles au cadrage."
Microsoft Graph already covers the Entra identities, domains and the Graph usage
reports; this producer only adds what the ExchangeOnlineManagement module reads.

## Running it

In the discovery panel of a Microsoft 365 environment, choose the producer
"Exchange Online — script PowerShell", select sections and create a script
request. Download `Clarity.Discovery.Exchange.1.0.0.zip` (deterministic; AD and
Exchange bundles are separate files and embed the exact common source) and the
separate `parameters.json`. On a machine that has the module installed, open a
read-only session yourself, then run the script in the same PowerShell window:

```powershell
Connect-ExchangeOnline -ShowBanner:$false   # operator-owned session, read-only roles
.\exchange\Collect-Exchange.ps1 -ParameterFile .\parameters.json -OutputPath .\results.zip
```

Upload `results.zip`, not the script bundle. Clarity stores no password, token,
certificate or consent for this script, and the parameters contain none. The
script never connects, installs a module or elevates a role. It runs
`Get-ConnectionInformation` and refuses to read anything unless there is exactly
one connected session whose `TenantID` equals the request's
`expectedEnvironmentExternalId`; this check happens before any package is written.
The server accepts a request only for an environment of the same tenant: the
scope must be the tenant itself (case-insensitive GUID match) and carry no domain.
The request is bound to the producer, the exact section set and the script
version, so an Exchange result cannot be imported against an AD request or the
reverse, and replaying the same operation is idempotent.

## Actual cmdlets and rights

All calls are read-only. Cmdlet names are the only data sources; nothing is
parsed from message, calendar or file content.

| Section | Cmdlets actually used |
| --- | --- |
| all | `Get-ConnectionInformation` (tenant check) |
| `exchange.recipients` | `Get-EXORecipient -ResultSize Unlimited -Properties Alias,DisplayName,EmailAddresses,ExternalDirectoryObjectId,Guid,PrimarySmtpAddress,RecipientType,RecipientTypeDetails` |
| `exchange.mailboxes` | `Get-EXOMailbox -ResultSize Unlimited -Properties Alias,ArchiveState,ArchiveStatus,DisplayName,EmailAddresses,ExchangeGuid,ExternalDirectoryObjectId,Guid,PrimarySmtpAddress,RecipientTypeDetails,UserPrincipalName` |
| `exchange.mailboxStatistics` | `Get-EXOMailbox` as above, then `Get-EXOMailboxStatistics -UserPrincipalName <UPN>` per mailbox and `-Archive` only when `ArchiveStatus` is `Active` |
| `exchange.mailboxPermissions` | `Get-EXOMailbox … -Properties GrantSendOnBehalfTo`, `Get-EXOMailboxPermission -ExternalDirectoryObjectId <id>`, `Get-EXORecipientPermission -ExternalDirectoryObjectId <id>`, `Get-EXORecipient` to resolve trustees |
| `exchange.mailEnabledGroups` | `Get-DistributionGroup -ResultSize Unlimited`, `Get-DistributionGroupMember -Identity <group GUID> -ResultSize Unlimited` |
| `exchange.acceptedDomains` (optional) | `Get-AcceptedDomain` |
| `exchange.connectors` (optional) | `Get-InboundConnector`, `Get-OutboundConnector` |
| `exchange.transportRules` (optional) | `Get-TransportRule` |

Rights: the operator's own session needs read access to these cmdlets only. The
catalog names the Exchange role families **View-Only Recipients** (recipient,
mailbox, statistics, permission and group sections) and **View-Only
Configuration** (accepted domains, connectors, transport rules). The built-in
role group *View-Only Organization Management* contains both; Entra *Global
Reader* is another read-only route. These role-to-cmdlet mappings come from
Microsoft's documentation and have **not** been verified in a live tenant:
the pilot must confirm that each section returns data with the chosen role. A
cmdlet the role cannot run fails only its section, with the fixed error code
`ExchangeReadFailed`, and previously published data of other sections is kept.
Recipients and permissions outside the session's RBAC scope are invisible, and
absence is never inferred from an unreadable value.

## Objects, identities and links

Identity namespace `exchange.guid` (the Exchange object GUID) is the primary key;
`externalDirectoryObjectId`, UPN, primary SMTP and every `smtp:` address are
alternate keys. Object types: `mailbox` (UserMailbox), `sharedMailbox`,
`resourceMailbox` (Room and Equipment), `mailGroup`, `recipient`,
`acceptedDomain`, `connector`, `transportRule`. **Shared and resource mailboxes
are independent objects:** they are never merged with a user and never receive
`mailboxOf`.

A user mailbox emits `mailboxOf` to the user with this precedence, resolved by
the common relationship projection:

1. `ExternalDirectoryObjectId` → `user/microsoft.graph/<id>` (Entra object ID);
2. otherwise an exact UPN → `user/upn/<UPN>`;
3. otherwise an exact primary SMTP address → `user/smtp/<address>`.

The weaker keys travel in `properties.fallbackTo` (at most 8, same object type).
A weaker key is tried only while the stronger one has no observed user yet; a
stronger key that matches several users stays *Ambiguous* and is never overridden
or skipped, and an ambiguous match is never guessed. A pending link stays pending
until the user is observed (Graph identity section) and produces the sanitized
`UnlinkedMailbox` warning when a mailbox has no usable key at all. Comparisons
are exact (case-insensitive); no fuzzy display-name matching exists.

Delegations are `fullAccessTo`, `sendAsOn` and `sendOnBehalfOf` edges from the
trustee to the mailbox and are shown as observed delegations. Only explicit,
non-inherited allows are reported. A Deny entry suppresses the same trustee's
grant; `NT AUTHORITY\SELF`, SIDs and orphaned entries are ignored. A trustee that
is not exactly one observed recipient (for example a non-mail-enabled security
group) is counted in `UnresolvedTrustees` and never invented, making the section
Partial. An edge proves an observed grant, not effective access. Group
membership is `memberOf` from direct members only (no nested expansion).

Graph usage-report mailboxes (`report.m365.mailboxUsageDetail`, section
`exchange.mailboxUsage`) live in the provisional `m365report.upn` namespace.
They are **not** Exchange GUID identities and are never auto-merged with objects
from this producer: associating them goes through the reviewable consolidation
path described in `docs/consolidation.md`.

## Coverage and limits

- Sizes parse the documented `n (n bytes)` value and stay empty when unreadable
  (`UnreadableSize`). Archive size covers the primary archive only, not
  auto-expanding archives or recoverable-items detail. Statistics can lag live
  content.
- Soft-deleted and inactive mailboxes, Microsoft 365 group mailboxes (group
  membership stays in Graph), dynamic distribution groups (visible only as
  recipients), folder permissions, calendar delegates and application access
  policies are out of scope.
- Accepted domains do not prove DNS/MX ownership; connectors omit smart hosts,
  TLS settings, certificates and IP addresses; transport rules carry name,
  state, mode, priority and last change only, never conditions, actions or
  comments. A rule name can still be sensitive: review the package before
  sharing it.
- A section is **Complete** only when every per-item call succeeded; otherwise
  Partial with sanitized warning codes `MissingAttributes`, `MissingIdentity`,
  `UnreadableSize`, `UnreadableMailbox`, `UnreadableArchive`, `UnreadableGroup`,
  `UnresolvedMembers`, `UnresolvedTrustees`, `UnlinkedMailbox`. Absence is
  inferred only from a later complete snapshot of the same fields.
- Statistics, permissions and group members are read one object at a time and
  without retry. Large tenants (thousands of mailboxes) need a maintenance
  window and may meet Exchange Online throttling; a throttled call marks its
  section Partial rather than inventing values.

## Privacy and logging

Output contains identifiers, names, addresses, sizes, dates and the permission
relations above. It never contains mail, calendar or file content, credentials,
tokens or raw cmdlet output. Messages and warnings are fixed strings; exception
text is not copied. Server-side, the import is authorized against the workspace
and project, replayed idempotently and logged only with correlation IDs.

## Tested versions

Tested on PowerShell **7.6.10** with **Pester 5.7.1** and mocked
ExchangeOnlineManagement **3.x** cmdlet shapes (including 1,001-member groups,
Deny and inherited entries, SID/orphan trustees, ambiguous trustees, inactive
archives, unreadable sizes and per-section failures). Generated packages are
validated by the real C# ZIP v1 reader; the SQL linking test
(`ExchangeLinkingTests`) and request tests run in CI against a disposable SQL
Server and skip locally without `CLARITY_TEST_SQL`.

The script keeps PowerShell 5.1-compatible syntax. **No live Exchange Online
tenant, Windows PowerShell 5.1 host or certified module version was used.**
Pilot checks still required: real output shapes (`TotalItemSize`,
`GrantSendOnBehalfTo`, trustee formats), role-to-cmdlet rights, module version,
`-Archive` statistics lookup by UPN, throttling on a large tenant, and the
interpretation of dynamic groups through recipients only.

References:
[Get-EXOMailbox](https://learn.microsoft.com/powershell/module/exchange/get-exomailbox),
[Get-EXOMailboxStatistics](https://learn.microsoft.com/powershell/module/exchange/get-exomailboxstatistics),
[Get-EXOMailboxPermission](https://learn.microsoft.com/powershell/module/exchange/get-exomailboxpermission),
[Get-EXORecipientPermission](https://learn.microsoft.com/powershell/module/exchange/get-exorecipientpermission),
[Exchange Online PowerShell](https://learn.microsoft.com/powershell/exchange/exchange-online-powershell).
