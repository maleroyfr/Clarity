# Applications and SSO collector 1.0.0

This Lot 4 family adds Graph metadata, explicit app-role assignments and a
consultant SSO qualification on application/service-principal object sheets.
It does not establish effective SSO, effective access or migration readiness.
The original French specification remains authoritative.

## Authentication and prerequisites

Use the existing [server Graph certificate flow](microsoft-graph.md). The
collector application, interactive sign-in application and deployment identity
remain separate. No new credential store, consent flow or local fallback exists.
`clarity.graph.applicationsSso` has its own workspace/tenant Connection, section
selection and access checks; it shares the configured collector identity and
the Microsoft.Graph tenant concurrency key with Entra and Intune.

An administrator grants **application**, not delegated, permissions:

| Operation | Least required read-only application permission |
| --- | --- |
| Verify the actual tenant using `GET /v1.0/organization?$select=id` | `Organization.Read.All` |
| All three applications/SSO sections below | `Application.Read.All` |

Neither `Directory.Read.All`, `AppRoleAssignment.ReadWrite.All` nor any write
grant is requested by this family. Users/groups may remain unresolved without
separate Entra collection permissions; those permissions are not prerequisites
for retaining typed assignment references. The collector's `.default` token
reflects actual administrator consent; adding a Connection does not grant consent.

Discovery access tests read each section's exact selected metadata, then one
sample resource's assignment navigation. An empty parent sample is explicitly
*not tested* for assignments, not successful assignment access. A successful
sample cannot certify all resources. A tenant mismatch prevents collection.

## Exact endpoint and coverage matrix

Only public-cloud `https://graph.microsoft.com` **REST v1.0** GET requests are used.
The following paths include every initial query's `$select` fields. Paging
follows Graph-provided `@odata.nextLink` within the bounded v1.0 transport.

| Stable section | Actual initial endpoints | Canonical observations and relationships |
| --- | --- | --- |
| `applicationsSso.applications` | `/v1.0/applications?$select=id,displayName,appId,signInAudience` | `application`, primary `microsoft.graph` object ID; reported client `appId` is an alternate reference |
| `applicationsSso.servicePrincipals` | `/v1.0/servicePrincipals?$select=id,displayName,appId,servicePrincipalType,accountEnabled,appRoleAssignmentRequired,preferredSingleSignOnMode` | `servicePrincipal`, its own primary `microsoft.graph` object ID; `servicePrincipalForApplication` points to typed `application` / `appId` |
| `applicationsSso.appRoleAssignments` | `/v1.0/servicePrincipals?$select=id`, then `/v1.0/servicePrincipals/{id}/appRoleAssignedTo?$select=id,appRoleId,principalId,principalType,resourceId,createdDateTime` | `appRoleAssignment` with opaque assignment ID and listed fields; `appRoleAssignmentPrincipal` points to the typed principal; `appRoleAssignmentResource` points to the resource service principal |

Role-assignment nodes preserve multiple assignments between the same endpoints.
`User`, `Group`, and `ServicePrincipal` map to distinct canonical types. Unknown
or null principal type retains `directoryObject` plus the original reported value,
and makes the section Partial. Missing/invalid mandatory IDs reject that row;
a resource ID different from the parent produces a sanitized section error.
The all-zero `appRoleId` is preserved as a default assignment, not interpreted as
an unknown ID. Role definitions/names and requested permissions are not collected.
No group expansion or transitive access relationship is generated.

GUID-valued IDs are normalized; assignment IDs remain opaque. Application object
ID, service-principal object ID and client appId are never interchangeable.
The existing typed alternate-key projection normalizes appId and can resolve a
stable appId reference when the local registration arrives later. Resolution
never crosses environments or workspaces. A foreign-home-tenant app registration
stays unresolved; it is not created from a service principal's client ID.
The earlier `entra.applications` basic section remains compatible and selectable.
It does not acquire this family's richer SSO coverage or permissions checks.

Missing metadata is not synthesized. Explicit null remains null, notably for
`preferredSingleSignOnMode`: older SAML apps and OIDC apps can have no mode
reported. `accountEnabled` and `appRoleAssignmentRequired` describe configuration,
not successful sign-in. No `passwordCredentials`, `keyCredentials`, certificate
data, tokens, SAML settings/payloads, URLs with query secrets, delegated OAuth
grants, sign-in logs, conditional-access policies or content are selected or
retained, even when unsolicited response fields contain them.

## Publication and incomplete coverage

Each section uses full paged reads, not delta. A child failure preserves valid
assignment observations and continues with other visible resource principals.
Errors are deduplicated sanitized codes, without response bodies. Complete means
the declared metadata traversal completed, not effective access or visibility
outside the queried scope. Empty successful sections report zero; failed sections
report unknown counts. Every result carries `SsoMetadataOnly`, preventing omission
from becoming negative evidence, including assignment removals (which can suffer
Graph replication delays).

The existing transport enforces four HTTP attempts, a maximum 60-second
Retry-After, 1,000 pages per traversal, 2 MiB per page and 128 MiB per session.
This collector additionally bounds parents/objects to 100,000 and relationships
to 150,000; canonical ZIP limits still apply. No beta fallback is attempted.

Authorized durable jobs recheck membership and actual tenant, stage allowlisted
canonical data in private Blob storage and publish via atomic `PublicationService`.
Prepared-run retry and publication idempotency are unchanged. Partial/failed
sections retain prior valid observations and links. The shared projection still
prefers complete observations over incomplete ones for a previously known field;
partial observations remain visible in history and can fill unknown fields.

## Consultant qualification journey

Open an application or service-principal in **Inventaire** and use **Qualification
des dépendances SSO**. A consultant/manager selects a dependency assessment
(to qualify, retain/adapt, or no dependency retained), a qualified mode and a
required justification describing the systems/populations/dependencies. Add an
available ticket/workshop reference and optionally select a published observation
of this same object. Evidence references are text only: no URL fetch, external
content upload or trust assertion. A missing reference is displayed explicitly.

The specification's `Assessment` entity is implemented as append-only numbered
revisions, using the existing project authorization, project transaction lock,
reason validation and revision-conflict conventions. It is not a second Graph
producer or an inventory relationship. Each revision retains workspace, project,
object, environment, stable author tenant/object IDs, server UTC date, value,
protocol, justification and optional proof. Current state and SQL-paged history
reauthorize Read; mutations reauthorize Qualify and require an active dossier.
Readers cannot write, and platform administration confers no inventory access.
Evidence must belong to a published observation of the same object/environment.
Operation IDs deduplicate exact retries and reject changed payloads or actors.
Stale revisions or metadata fingerprints reject the mutation atomically.

A fingerprint of current appId, audience, service-principal type, enabled state,
assignment requirement, configured SSO mode, field conflicts and object presence
flags the current assessment for review when those values change. Identical
refreshes do not reopen it. Revisions and proof are never overwritten by Graph.
Assignment/population changes and external dependencies still require consultant
review: this is not an automated access assessment. Manual qualifications never
enter Graph coverage counters, current Graph property projections, association
validation or effective-access calculations.

Apply the additive `SsoAssessments` migration explicitly; existing data is not
rewritten. It includes composite workspace/project/environment/object/proof
foreign keys, global EF filtering and SQL filter/block predicates. Azure deployment
remains gated by `vars.AZURE_DEPLOY_ENABLED == 'true'`.

## Validation and live-pilot limits

Tested versions: .NET/EF Core 11, Microsoft Graph REST v1.0 **synthetic HTTP
fixtures only**. `GraphTests.ApplicationsSso.cs` supplies synthetic registrations,
service principals, typed assignments, unknown types, null modes, forbidden pages
and unsafe continuations. Its SQL cases drive Graph jobs through Blob staging,
publication and later reference resolution, partial and empty refreshes, authored
assessment history, replay/stale conflicts, proof validation, reader/revoked/admin
denials and SQL isolation even with EF filters disabled. Web tests exercise the
French sheet and consultant versus reader controls.

```powershell
dotnet test --project tests\Clarity.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~GraphTests.Sso|FullyQualifiedName~DiscoveryModelTests'
dotnet test --project tests\Clarity.Web.Tests -c Release --filter 'FullyQualifiedName~SsoQualificationTests'
```

SQL cases explicitly skip locally without `CLARITY_TEST_SQL`; CI executes them
against disposable SQL Server and checks the migration bundle twice. No tenant
consent, live response completeness, sovereign cloud, real sign-in, app-role
replication timing, Key Vault rotation or pilot-scale throughput is certified.
The user-led pilot must consent the exact read grants, verify representative
SAML/OIDC/managed-identity and group/application assignment responses against the
tenant, review empty/denied scopes, and inspect publication/qualification proof.
Keep all real tenant data and credentials out of this public repository.

Official API references:
[applications](https://learn.microsoft.com/graph/api/application-list?view=graph-rest-1.0),
[service principals](https://learn.microsoft.com/graph/api/serviceprincipal-list?view=graph-rest-1.0),
[SSO mode semantics](https://learn.microsoft.com/graph/api/resources/serviceprincipal?view=graph-rest-1.0),
[resource app-role assignments](https://learn.microsoft.com/graph/api/serviceprincipal-list-approleassignedto?view=graph-rest-1.0).
