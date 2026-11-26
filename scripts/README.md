# Discovery script components

`Clarity.Discovery.Common` 1.0.0 writes the four canonical ZIP v1 root files,
UTF-8 without BOM, UTC timestamps, section coverage and record counters. It streams
JSONL and preserves valid observations when a later read fails. Producers must
supply sanitized fixed error messages, not exception text or raw service output.
The shared code targets PowerShell 5.1 syntax; actual validated runtime versions
are recorded in connector documentation as collector tests land.

Parameters are a separate metadata-only JSON file, never embedded credentials.
Unknown properties are rejected. Missing PowerShell/modules give explicit
remediation and **never** trigger installation. The consultant establishes their
own authorized local session. Clarity Graph consent does not apply to it.

Run `Invoke-Pester -Path scripts -CI` with Pester 5.7.1 (pinned in CI). Set
`CLARITY_SCRIPT_FIXTURES` to a scratch directory first; tests place generated ZIPs
there, and `dotnet test` validates those real outputs through the same ZIP reader
used for imports. CI runs Pester before the .NET tests so this is not a dormant job.
With no fixture directory configured, the generated-output .NET check explicitly
skips instead of claiming PowerShell execution.

## Registering a PowerShell collection family

The server, bundle download and Discovery screen are generic: a family is registered
by adding files only, with no change to `ScriptRequestService`, `ScriptBundleRegistry`,
the project file or `DiscoveryPanel`.

1. **Catalog** — `catalog/<family>.v1.json` (embedded automatically) declares a producer
   `clarity.<x>` whose `channel` is `PowerShell`, whose `readiness` is `SyntheticValidated`
   and which is not `preview`, and sections whose `producerIds` and `supportedScopes`
   include it. A producer that is `Planned`, `preview` or has no bundle is never offered.
2. **Bundle** — `scripts/<x>/Collect-<Name>.ps1` (the single entry script of the folder,
   `<x>` lower-case alphanumeric, `<Name>` alphanumeric) plus its `.psm1`/`.psd1`
   files in the same folder. Every `.ps1`/`.psm1`/`.psd1` file except `*.Tests.ps1` is
   embedded and zipped deterministically with `Clarity.Discovery.Common`; the entry script
   comes first. The download is `Clarity.Discovery.<Name>.<catalog producer version>.zip` and
   the command is `.\<x>\Collect-<Name>.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip`.
   A folder without exactly one entry script is ignored.
3. **Texts** — fr-FR keys in `UiStrings.resx`: `Producer_clarity.<x>`, one `Section_<id>` per
   section, and the overrides `CreateScriptRequest_clarity.<x>`, `DownloadScriptBundle_clarity.<x>`,
   `ScriptCommandHint_clarity.<x>` and `LocalScriptConnectionHint_clarity.<x>` (the unsuffixed
   keys are the AD texts). `ObjectType_*` and `Issue_*` keys are added as needed. A web test
   fails when a published family lacks one of them.

The producer is inferred from the requested sections: sections of different producers
cannot be mixed, and an unknown, planned or ambiguous selection is rejected. Sections whose
only supported scope is `tenant` are bound to the environment's tenant; other families get
the scope selector (and the domain for `ou`) in the Discovery screen. The bundle is served
only while the catalog version equals the version recorded in the request.

`exchange/Collect-Exchange.ps1` and its module follow the same rules for the
ExchangeOnlineManagement 3.x cmdlets; it never connects, and requires one
operator-opened session for the requested tenant. See
[Exchange Online](../docs/connectors/exchange.md).
