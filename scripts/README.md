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
