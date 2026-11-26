# Versioned Discovery catalog

Family files are embedded in Clarity.Contracts. `DiscoveryCatalog.Load()` validates
their JSON Schema, stable IDs, producer ownership and every field's reference
producer. UI/server code must call `RequireProducer` before enabling a producer.
`Planned` entries are documentation, not executable alternatives.
`SyntheticValidated` means repository tests ran, **not** a live-customer pilot.
Preview APIs require both `preview: true` and an explicit caller opt-in.

Scope units follow ZIP v1. A complete section covers only its declared scope,
attributes and relations; it does not imply completeness of another family.
Field types define explicit empty values (`null`, empty string or empty array);
unknown fields are rejected at the catalog boundary. Metadata outside this
allowlist (including credentials and file/message contents) is not accepted.

Lot 2 enables only AD and Graph Entra as their implementations land. Lot 4 adds
`SyntheticValidated` report-import producers (channel `Import`) for Exchange
mailbox usage, the new OneDrive family and SharePoint site usage. They are
selectable only through the matching profile in `report-profiles/`, whose loader
also checks that every profile field and relation exists in its family; see
[import profiles](../docs/import-profiles.md). Lot 4 also adds the separate
[Intune / Autopilot Graph v1.0 producer](../docs/connectors/intune-autopilot.md);
its declared beta limits are not selectable endpoints. The Exchange Online PowerShell and
PnP producers remain `planned`, unavailable and untested. The remaining planned
families below are not selectable:

| Family | Intended producer / data | Required future verification |
| --- | --- | --- |
| OneDrive (beyond usage reports) | Graph plus SharePoint/PnP | Personal-site provisioning, separate local permissions, sharing |
| Teams | Graph teams, channels and associated sites | Endpoint permissions and hidden/shared channel scope |
| Microsoft 365 Apps / GPO | Local Office / GroupPolicy scripts | Windows and module versions, OU/computer scope and read rights |
| Power BI | Power BI script or administrative export | API/report schema, tenant admin/scanner rights and resource scope |
| Azure / infrastructure | Az and targeted local scripts/imports | Subscription RBAC, OS/modules, servers/DNS/shares/services metadata boundaries |

No future family is presented as tested or ready. New supported families require
their own versioned file, exact endpoint/cmdlet and permissions documentation,
tested-version evidence, limits, reference fields, and canonical-package tests.
