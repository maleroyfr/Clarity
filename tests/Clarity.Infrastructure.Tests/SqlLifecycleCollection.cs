namespace Clarity.Infrastructure.Tests;

// EF database deletion clears SQL pools process-wide; do not race pooled-reuse assertions with cleanup.
[CollectionDefinition("SQL lifecycle", DisableParallelization = true)]
public sealed class SqlLifecycleCollection;
