# Azure deployment

Deployment is **disabled by default**. No Azure resources are created by CI, and
this repository contains no subscription or client identifiers. An operator must
complete the steps below before setting the repository variable
`AZURE_DEPLOY_ENABLED=true`. The workflow does not provision infrastructure or
grant itself additional permissions.

## Topology and limits

`infra/main.bicep` provisions a Linux B1 App Service plan and app, an application
user-assigned managed identity, Entra-only Azure SQL (S0), a private Storage
account with `raw-artifacts`, `exports` and `script-packages` containers, an RBAC
Key Vault, Log Analytics and Application Insights. SQL, Blob and Key Vault use
private endpoints, private DNS and disabled public access. Shared Storage keys,
public blobs, FTP and basic SCM publishing credentials are disabled. TLS 1.2,
SQL encryption, Blob versioning/7-day soft delete, and vault purge protection
are enabled. These are starter capacities, not an HA or disaster-recovery SLA.
Review region, capacity, backup retention, cost and the `10.42.0.0/16` range before
provisioning. Purge-protected vaults cannot be immediately recreated after deletion.

The web frontend is deliberately reachable over public HTTPS, with server-side
Entra authentication/authorization. Do not enable App Service Easy Auth in front
of the application's OIDC flow. Only anonymous liveness/readiness status is public.
SCM accepts the network's NAT egress IP only. VNet integration routes dependency
and outbound traffic through NAT; a separate runner subnet has no default public
outbound access. NAT and the private endpoints incur costs independently of use.
App Insights ingestion remains a public TLS endpoint; only explicitly allowlisted
Clarity telemetry is exported. Do not turn on App Service auto-instrumentation or
request/body/access logging.

### Runtime choice

The application and EF bundle are **self-contained `linux-x64` .NET 11** artifacts.
Microsoft's [runtime configuration documentation](https://learn.microsoft.com/azure/app-service/configure-language-dotnetcore)
requires `az webapp list-runtimes --os linux` to establish actual stack availability.
No authenticated Azure CLI was available on the implementation machine, and the
documentation did not establish .NET 11 availability in the target region.
Consequently deployment does not depend on a .NET 11 App Service runtime:
`DOTNETCORE|10.0` provides the Linux base image and its native libraries, while the
startup command launches the shipped .NET 11 executable, not `dotnet Clarity.Web.dll`.
Check that base stack in the target subscription during setup. Do not switch to
framework-dependent publishing without validating the selected platform runtime.
CI executes the Linux application and the migration bundle, not just a build.
Publish again after .NET security patches: self-contained runtimes are not patched
by changing the App Service runtime selector.

## 1. Establish identities and the GitHub environment

Use an operator workstation/Cloud Shell with current Azure CLI + Bicep and an
account authorized to create resources and assign RBAC. No Azure CLI installation
is required on the local development machine.

Create three **distinct** identities:

| Identity | Purpose and privileges |
| --- | --- |
| Web OIDC registration | Single-tenant interactive sign-in. Configure a Web redirect URI `https://<app-name>.azurewebsites.net/signin-oidc`, front-channel logout `/signout-oidc`, and a credential stored only in Key Vault. Require assignment to this enterprise application for authorized consultants. Do not grant Graph inventory consent yet. |
| Migration service principal | GitHub OIDC only. Database `db_owner` for explicitly reviewed migrations (including RLS DDL); never used by the web process. Azure Reader at the SQL database resource is sufficient for subscription discovery during login; no resource modification rights. |
| Deployment service principal | GitHub OIDC only. Website Contributor scoped to the web app after provisioning, without SQL or Key Vault data access or RBAC-grant permissions. |

The template creates a fourth identity: the **application managed identity** for
SQL DML, Blob and Key Vault secret reads. It is not a migration identity.

Create GitHub environment `production` (or your chosen name) with required
reviewers, prevent self-review, and restrict deployment branches to `main`.
On **each** migration/deployment app registration, add a federated credential:

```json
{
  "name": "clarity-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:maleroyfr/Clarity:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}
```

Adjust the subject if the repository/environment is renamed. Upload this through
Entra or `az ad app federated-credential create`; no client secret goes into GitHub.
Keep identity IDs in private environment configuration, never committed parameters.
Only the initial provisioning operator needs Contributor plus an RBAC assignment
role on the dedicated resource group.

## 2. Provision infrastructure explicitly

Register the Microsoft.Web, Sql, Storage, Network, ManagedIdentity,
OperationalInsights, Insights and KeyVault resource providers if necessary.
Create a resource group in the selected region:

```powershell
az login
az account set --subscription '<subscription-id>'
az group create --name '<resource-group>' --location '<region>'
az webapp list-runtimes --os linux
az bicep build --file infra\main.bicep
az bicep build-params --file infra\parameters\production.bicepparam
```

The checked-in parameter file is an example and has zero GUIDs. Do not deploy it
unchanged. Pass private values separately; all four identity/name values below
must refer to the same Entra tenant. Use an Entra **group** for SQL administration.

```powershell
$parameters = @(
  'environmentName=production'
  'location=<region>'
  'entraTenantId=<tenant-id>'
  'webClientId=<oidc-registration-client-id>'
  'sqlAdministratorObjectId=<sql-admin-group-object-id>'
  'sqlAdministratorName=<sql-admin-group-display-name>'
)
az deployment group what-if --resource-group '<resource-group>' --template-file infra\main.bicep --parameters $parameters
# Review cost, network changes, identities and the what-if result before applying.
az deployment group create --name clarity-foundation --resource-group '<resource-group>' --template-file infra\main.bicep --parameters $parameters --mode Incremental
az deployment group show --name clarity-foundation --resource-group '<resource-group>' --query properties.outputs
```

Save the non-secret resource outputs privately. Later infrastructure changes also
require reviewed what-if and incremental deployment; CI does not silently apply
network/RBAC changes on every application push. Do not enable any "Allow Azure
services" SQL firewall bypass.

## 3. Private access, SQL bootstrap and OIDC secret

Provide an **ephemeral**, dedicated Linux x64 GitHub runner in the `runners` subnet,
with label `clarity-deploy-private`. It needs Azure CLI, GitHub CLI, curl, unzip,
sha256sum and .NET 11's [native Linux dependencies](https://learn.microsoft.com/dotnet/core/install/linux-ubuntu-install);
it does not need the .NET SDK. It must resolve the private DNS zones and reach
SQL 1433, Blob/Key Vault/SCM 443, Entra, Azure management and GitHub over outbound
HTTPS. The Bicep template intentionally does not install or register a runner.
Use an isolated runner group restricted to the deployment workflow; never run
public pull-request code on it. The ordinary CI job uses GitHub-hosted runners.
An administrator also needs a private-network workstation/runner or VPN for SQL
bootstrap and Key Vault secret provisioning.

Connect to the `Clarity` database as a member of the SQL administrator group using
Entra authentication. Use the staged statements in `infra/bootstrap-sql.sql`:

1. Replace the migration service principal's **display name**, create its external
   database user, and grant `db_owner`. This elevated role is exclusively for
   migrations. The signed-in administrator needs the Entra directory lookup rights
   required by `CREATE USER ... FROM EXTERNAL PROVIDER`; consult
   [Microsoft's principal guidance](https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-service-principal).
   Resolve duplicate display names explicitly; never guess an identity.
2. Download a successful main CI artifact, verify `SHA256SUMS`, and apply its
   `efbundle` from the private network with an authorized migration identity and
   `CLARITY_MIGRATION_SQL` configured as below. Do not run the remaining SQL
   statements until both foundation migrations exist.
3. Replace the app display name with the template's managed-identity name, create
   its external database user, and apply the explicit table grants/denials.
   Never grant it `db_owner`, `db_ddladmin`, schema ownership, arbitrary role
   management or permission to alter RLS. Do not grant audit update/delete.
4. Replace the final zero GUIDs with the first platform administrator's `tid`/`oid`
   and insert that row. This is an operator bootstrap, not startup seeding.
   Platform administration still grants no inventory access: create a client and
   explicitly assign its members through the UI.

The SQL file is a **staged template**, not an unattended one-shot script. Never
commit the substituted file. Future workspace tables need reviewed app-role grants
with their migration; no blanket schema-wide DML permission is given.

Use Key Vault Secrets Officer temporarily, from private access, to populate the
`clarity-oidc` secret with the web registration's credential. Do not echo it in
terminal history, workflow output or command-line arguments. Prefer the portal
on a privately connected workstation or a protected local secret file. Configure
expiry alerts and rotate it; remove the temporary officer assignment afterward.
App Service resolves the versionless Key Vault reference using the app identity.
Validate the reference status in App Service configuration before deployment.
Managed identity and RBAC propagation can take several minutes.

## 4. Configure the deployment gate

Repository variables:

| Variable | Value |
| --- | --- |
| `AZURE_ENVIRONMENT` | `production` (must match federated subjects and environment) |
| `AZURE_DEPLOY_ENABLED` | Leave unset/`false` until all prerequisites are ready; then `true` |

Environment variables:

| Variable | Value |
| --- | --- |
| `AZURE_TENANT_ID` | Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |
| `AZURE_MIGRATION_CLIENT_ID` | Migration app registration client ID |
| `AZURE_DEPLOY_CLIENT_ID` | Separate deployment app registration client ID |
| `AZURE_RESOURCE_GROUP` | Provisioned resource group |
| `AZURE_WEBAPP_NAME` | Template `webAppName` output |
| `SQL_HOST` | Template `sqlHost` output |
| `SQL_DATABASE` | `Clarity` |

The deployment gate is a **repository** variable because GitHub evaluates the job
condition before environment variables are available. Setting an environment-only
gate does not enable deployment. Environment approval is a second, independent gate.
Do not put SQL passwords or web OIDC secrets in any GitHub variable.

## 5. Release execution and validation

On each push/PR, CI restores, builds with warnings as errors, checks formatting,
runs unit/component/HTTP/SQL tests, and builds/lints Bicep and parameter files.
It publishes a self-contained web ZIP and EF bundle. CI applies the bundle twice
against its disposable SQL service and starts the actual Linux executable,
checking live=200, unconfigured ready=503 and the French empty screen.
Successful main pushes retain `clarity-linux-<commit>` with checksums for 90 days.

`deploy.yml` runs only after a successful **push** CI run from this repository's
main branch. It rejects a superseded main commit, downloads that exact run's
artifact (never rebuilds mutable main), and verifies its commit/checksums.
Deployment is serialized per environment without cancelling a migration in flight.
Approval authorizes both schema and application changes for that release.

First, `azure/login` signs in with the migration identity through GitHub OIDC.
The bundle uses:

```text
Server=tcp:<sql-host>,1433;Database=Clarity;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;
```

`AZURE_TOKEN_CREDENTIALS=AzureCliCredential` restricts that credential chain to
the just-authenticated identity, rather than the runner's managed identity or an
unrelated developer credential. The connection is supplied only through
`CLARITY_MIGRATION_SQL`, not a command argument. SqlClient 7's separate Azure
authentication extension is included in both the app and bundle.

After migration, the workflow clears that sign-in, signs in as the deployment
identity, and ZIP-deploys without a server-side build. Health smoke checks require
both `/health/live` and `/health/ready` to return 200; dependency failure is not
treated as successful deployment. The web process uses the app managed identity,
not the CLI credential. Test Entra sign-in, explicit membership, create a synthetic
client/project, and a guessed cross-workspace ID manually before admitting data.
Confirm private DNS, SQL user mapping, Blob/container access, vault resolution and
the absence of auto-instrumentation. A green CI is not evidence that an unconfigured
Azure tenant has been deployed or that live RBAC has propagated.

## Schema changes and rollback

Use expand/contract migrations: add nullable/defaulted fields, deploy compatible
readers/writers, backfill separately, then remove obsolete schema only after the
rollback window. Never run migrations from web startup. Review generated migration
SQL/RLS changes and backup/PITR policy before approving the release. A failed
migration stops application deployment; inspect `__EFMigrationsHistory` privately
before retrying. The bundle is repeatable; it does not automatically downgrade.

To roll back an application:

1. Set `AZURE_DEPLOY_ENABLED=false` and wait for any in-flight deployment to finish.
   Identify the last healthy deployment summary and its upstream CI run/commit.
2. On the trusted private runner/workstation, authenticate to GitHub and download
   that run's `clarity-linux-<commit>` artifact with `gh run download <ci-run-id>
   --name clarity-linux-<commit> --dir <empty-release-directory>`. Check `commit.txt`
   against the chosen commit and run `sha256sum --check SHA256SUMS`.
3. Sign in as the deployment identity and run `az webapp deploy --resource-group
   <group> --name <app> --src-path <release>/web.zip --type zip --clean true
   --restart true`. **Do not execute the old EF bundle or a down migration.**
4. Require both health endpoints to return 200, verify sign-in and one authorized
   journey, and record the rollback. Correct/revert application code on main and
   restore the gate only when the next release is ready.

Preserve approved release artifacts outside the 90-day GitHub retention if a longer
rollback window is required. If a schema change was not backward compatible,
application-only rollback may be unsafe: use the reviewed recovery plan and SQL
point-in-time restore to a separate database, never an automatic destructive down
migration. This foundation does not promise zero-downtime deployment or automatic
data rollback.
