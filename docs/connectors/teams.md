# Teams collection 1.0.0

The [`teams` catalog family](../../catalog/teams.v1.json) implements the Teams
specification line: "Graph pour équipes, propriétaires, membres, canaux et
résolution de sites associés quand possible." Chat and telephony scope are
consultant qualifications, not inferred collection results.

`clarity.graph.teams` is **SyntheticValidated**. Tests use synthetic REST v1.0
responses only. No live tenant, customer data, production permission grant or
Teams configuration has been validated.

## Discovery journey and permissions

Use the existing [server certificate and tenant consent setup](microsoft-graph.md#deployment-and-consent).
In an authorized dossier, select its declared M365 tenant, choose
"Microsoft Graph — Teams (v1.0)", declare the producer's own connection, test
access, then launch the required sections. An Entra, SharePoint or OneDrive
connection cannot launch Teams sections. The server first verifies
`GET /v1.0/organization?$select=id` using `Organization.Read.All`; a tenant
mismatch blocks staging and publication.

All permissions below are **application** permissions with administrator
consent. Resource-specific consent and delegated sign-in are not implemented.
All requests are GETs on `https://graph.microsoft.com`.

| Section | Actual requests | Required permissions | Collected evidence |
| --- | --- | --- | --- |
| `teams.teams` | `/v1.0/teams?$select=id,displayName,visibility` | `Team.ReadBasic.All` | Team ID, display name, visibility; `teamGroup` to its Microsoft 365 group ID |
| `teams.members` | `/v1.0/teams?$select=id`; `/v1.0/teams/{team-id}/members?$top=999&$select=id,displayName,userId,tenantId,roles` | `Team.ReadBasic.All`, `TeamMember.Read.All` | Opaque membership IDs, names, tenant-qualified user IDs and roles; direct members and owners |
| `teams.channels` | `/v1.0/teams?$select=id`; `/v1.0/teams/{team-id}/channels?$select=id,displayName,membershipType,createdDateTime,webUrl` | `Team.ReadBasic.All`, `Channel.ReadBasic.All` | Hosted standard, private and shared channel identity, name, type, date and Teams URL |
| `teams.sites` | `/v1.0/teams?$select=id`; `/v1.0/groups/{team-id}/sites/root?$select=id,webUrl`; the channels request above; `/v1.0/teams/{team-id}/channels/{channel-id}/filesFolder`; if necessary `/v1.0/drives/{drive-id}/items/{item-id}?$select=sharepointIds,parentReference` | `Team.ReadBasic.All`, `Channel.ReadBasic.All`, `Sites.Read.All` | Site collection IDs, site URLs when provided, `teamsConnected`, group/team/channel site relations |

`Sites.Read.All` authorizes both `filesFolder` and driveItem metadata and is
required for group root sites; `Files.Read.All` alone is insufficient for this
section. No `Group.Read.All` permission is needed merely to use a known team's
backing group ID. Entra group collection has its own permissions and connection.
`filesFolder` supports **no query options**. Its response is used for IDs only;
the collector does not persist raw JSON, download URLs or file content. If it
omits the site identity, a selected driveItem metadata read is attempted using
its returned drive ID and item ID, never a URL supplied by the response.

Access tests add `$top=1` to the teams request and read the actual child endpoint
for one team (and one channel for sites). Empty team/channel lists are explicitly
untested, not successful permission proof. `TeamsSampleOnly` means the sample
succeeded, not that every private/shared channel is accessible.

## Stable identity and unresolved links

The collector uses the [landed SharePoint identity contract](sharepoint-onedrive.md#account-links-masked-identifiers-and-unresolved-links)
and the common canonical publication/relationship machinery:

| Object | Primary identity | Links |
| --- | --- | --- |
| Team | `team/microsoft.graph/<team GUID>` | `teamGroup` to `group/microsoft.graph/<same GUID>`; types remain distinct |
| Membership | `teamMembership/microsoft.teams.membership/<team GUID>/<opaque membership ID>` | `membershipInTeam`, `membershipUser`; `memberOf` and, for an explicit `owner` role, `ownerOf` |
| Channel | `teamChannel/microsoft.teams.channel/<team GUID>/<opaque channel ID>` | `channelInTeam`, `channelSite` |
| Site collection | `site/sharepoint.siteId/<site collection GUID>` | `groupSite`, `teamSite`, `channelSite`; `teamsConnected: true` |

The group ID equals the team ID by the Graph team resource contract. Neither a
group nor a user is invented from display names. Group and user references remain
pending until the corresponding object is collected in the **same workspace and
environment**. A member resolves to `user/microsoft.graph/<userId>` only when the
reported `tenantId` matches the collection tenant. Foreign users use a pending
`user/microsoft.graph.tenantUser/<tenantId>/<userId>` reference, never a local
directory match. Missing tenant/user IDs retain the membership object and its
roles without parsing the membership ID or matching names/emails.

Root site IDs come from the collection GUID in Graph's composite site ID.
Channel site IDs come from `sharepointIds.siteId`, or the collection GUID of
`parentReference.siteId`. A site URL comes only from the root site's `webUrl` or
`sharepointIds.siteUrl`, **never** a folder URL or a guessed channel path.

Multiple standard channels and producers reporting one GUID reuse one site.
Private and shared channels are resolved independently; different GUIDs always
remain different sites, even if labels or URLs match. There is no fallback from
a private/shared channel to the team's root site.

If only a site URL is available, the provisional identity is
`site/sharepoint.siteUrl/<normalized URL>`. URLs are dated `url` alternatives,
not automatic merge keys to immutable identities. If no site identity is known,
the relationship retains a pending endpoint reference:
`site/microsoft.graph.groupSite/<team GUID>` or
`site/microsoft.teams.filesFolder/<team GUID>/<channel ID>`.
These denote **a resolution request to verify**, not proof that a site exists.
No placeholder site object is created. The object sheet and provenance show
pending endpoints; the coverage is partial.

## Consultant qualifications

Each collected team's object sheet has separate French panels for chat scope
and telephony scope. The consultant records **Unknown**, **Included** or
**Excluded**, a mandatory reason describing populations/dependencies, an optional
external evidence reference and an optional published observation of that team.
These are dossier-specific framing statements, not a tenant-wide chat inventory
or a claim that telephony configuration belongs to the team.

The existing `Assessment` mechanism records independent histories by kind,
revision, author tenant/object IDs, UTC date and observation reference. The
nullable `ScopeValue` column is added by the explicit backward-compatible
`TeamsScopeAssessments` migration; existing SSO assessments retain their values.
Shared assessment storage enforces authorization, optimistic revision/fingerprint
checks, idempotent operation IDs, scoped evidence references and audit events.
SQL query filters, composite foreign keys and the existing row-level policy
continue to apply. Readers cannot edit; platform administration alone grants
no inventory access.

Recollection never overwrites a qualification. A change in the team's projected
visibility or presence marks both qualifications for review; a rename alone
does not. Human statements never become Graph observations, memberships,
effective-access claims or migration execution instructions.

## Coverage, failures and limits

- Basic team metadata only: list teams does not populate archive/configuration
  fields, so the collector does not record their nulls as facts.
- Hosted channels only. Incoming shared channels hosted elsewhere, channel
  members, tabs, apps, messages, chats, files, recordings, call records, phone
  numbers and telephony configuration are not collected.
- No access inheritance is inferred from team membership. An owner role means
  the role was observed, not effective access to every channel or site.
- Full pagination is used for teams, members and channels, with deduplication.
  At most 20,000 teams are processed per section. The existing transport bounds
  page count, response size, retries and `Retry-After`; cancellation propagates.
- Each team and channel read preserves earlier successful evidence on failure.
  Forbidden reads produce sanitized `MissingPermission` errors, without Graph
  bodies. Missing sites, invalid identities, foreign/missing user references,
  unknown roles/types and limits make coverage partial. An empty successful
  enumeration is a known zero, not a permission failure.
- `TeamsSiteUnresolved`, `TeamsSiteUnavailable`, `TeamsMemberUnresolved`,
  `TeamsMemberRolesUnknown`, `TeamsChannelTypeUnknown` and `TeamsLimit` are
  surfaced with French remediation. Partial/failed reads do not retract the
  last valid snapshot. Complete is relative to the selected fields and hosted
  scope, not all Teams services or effective access.
- Public cloud only. The shared worker authorizes launch and execution,
  tenant-verifies before collection, stages a canonical ZIP privately and
  publishes idempotently with run/section/observation provenance. Logs carry
  correlation IDs, not tokens, raw responses or sensitive fields.

## Synthetic acceptance and deferred pilot checks

`GraphTests.Teams.cs` covers paged metadata/members/channels, explicit owners,
foreign/missing tenant identity, sensitive-field exclusion, distinct private/
shared sites, same-GUID deduplication, driveItem fallback, provisional URLs,
unresolved references, partial failure, zero scope and unsafe continuations.
SQL acceptance covers producer-specific connection/access-test/launch/job,
canonical cross-producer publication, provenance, tenant mismatch,
authorization, independent assessment history and stale/replayed changes.
`TeamsQualificationTests.cs` covers French Discovery and object-sheet rendering,
unknown scope and read-only/foreign-workspace/platform-admin behavior.

Tests run on .NET 11 with synthetic Graph REST **v1.0** HTTP fixtures. SQL tests
skip explicitly without `CLARITY_TEST_SQL`; CI must run them against its
disposable SQL Server before delivery is called complete.

**User-led live checks remain deferred:** administrator-consent combinations;
real `filesFolder`/driveItem `sharepointIds` shapes; tenant-qualified guest
members; provisioning and special-character failures; private channel migration
to root-folder storage; hosted shared-channel visibility; large-tenant
pagination/throttling and response limits. Do not certify these from synthetic
tests. No Azure deployment or deployment-gate change is part of this slice.

References:
[list teams](https://learn.microsoft.com/graph/api/teams-list?view=graph-rest-1.0),
[team/group identity](https://learn.microsoft.com/graph/api/resources/team?view=graph-rest-1.0),
[members](https://learn.microsoft.com/graph/api/team-list-members?view=graph-rest-1.0),
[channels](https://learn.microsoft.com/graph/api/channel-list?view=graph-rest-1.0),
[filesFolder](https://learn.microsoft.com/graph/api/channel-get-filesfolder?view=graph-rest-1.0),
[driveItem metadata](https://learn.microsoft.com/graph/api/driveitem-get?view=graph-rest-1.0).
