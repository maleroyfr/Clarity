# Clarity contributor instructions

- `docs/specification.md` is the authoritative product reference; preserve its
  original French text. Record local choices in English in README/ADRs.
- Code, identifiers and documentation are English. Business UI text comes from
  `.resx` resources with `fr-FR` as default. Keep Discovery and Microsoft names.
- Use .NET 11, EF Core 11, Fluent UI Blazor v5 and central package versions.
  Nullable, analyzers and warnings-as-errors are enabled; keep formatting clean.
- Keep each commit focused on one verifiable journey and include the exact
  validation commands and results in its body. Never claim a skipped test passed.
- Public repository: synthetic data only; no real client names, tenant IDs,
  credentials, tokens or raw customer exports.
- Every workspace-owned table has `WorkspaceId`. Use composite foreign keys
  `(WorkspaceId, Id)`, EF global query filters and SQL row-level security.
  Set SQL `SESSION_CONTEXT` on every connection open, including pooled reuse.
- Authorize every server-side read, mutation, download, upload, job launch and job
  execution. Derive workspace context from identity plus an authorized project,
  never solely from a workspace ID supplied by a client. Workers re-scope per job.
- Platform administration does not imply inventory access. Use explicit membership.
- Implement authorization, idempotency and provenance with the feature they protect.
- Use one DbContext per operation through `IDbContextFactory`. Grid filtering,
  sorting and paging happen in the database, not after loading all rows.
- Logs carry correlation IDs. Never log tokens, raw Graph responses or sensitive
  fields. Report failures explicitly without leaking underlying sensitive data.
- For every connector, script or import profile, document expected data, actual
  endpoints/cmdlets, rights, tested versions and coverage limits.
- Fake authentication is registered only in Development/Testing. Do not silently
  fall back from production identity or persistence to local substitutes.
- SQL integration tests require `CLARITY_TEST_SQL`; skip clearly when absent
  locally. CI runs them using a disposable SQL Server service.
- Azure deployment is gated by `vars.AZURE_DEPLOY_ENABLED == 'true'`. Keep deployment
  and service identities separate. Migrations are explicit and backward-compatible.
