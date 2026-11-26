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

Lot 2 enables only AD and Graph Entra as their implementations land.
Exchange and SharePoint entries illustrate future Lot 4 extensions, explicitly
unavailable and untested. The remaining planned families below are not selectable:

| Family | Intended producer / data | Required future verification |
| --- | --- | --- |
| OneDrive | Graph plus SharePoint/PnP or administrative report | Personal-site provisioning, byte volumes, masked IDs, separate local permissions |
| Teams | Graph teams, channels and associated sites | Endpoint permissions and hidden/shared channel scope |
| Intune / Autopilot | Graph management metadata | v1.0 versus opt-in preview by section; licensing and application rights |
| Microsoft 365 Apps / GPO | Local Office / GroupPolicy scripts | Windows and module versions, OU/computer scope and read rights |
| Power BI | Power BI script or administrative export | API/report schema, tenant admin/scanner rights and resource scope |
| Azure / infrastructure | Az and targeted local scripts/imports | Subscription RBAC, OS/modules, servers/DNS/shares/services metadata boundaries |

No future family is presented as tested or ready. New supported families require
their own versioned file, exact endpoint/cmdlet and permissions documentation,
tested-version evidence, limits, reference fields, and canonical-package tests.
