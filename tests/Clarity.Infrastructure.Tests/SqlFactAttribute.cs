using System.Runtime.CompilerServices;

namespace Clarity.Infrastructure.Tests;

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLARITY_TEST_SQL")))
        {
            Skip = "SQL integration test skipped: set CLARITY_TEST_SQL to a disposable SQL Server (provided by CI).";
        }
    }
}

/// <summary>A SQL test that also needs <c>CLARITY_TEST_VOLUME</c>, the synthetic users per AD domain shard (see docs/resilience-performance.md).</summary>
public sealed class VolumeFactAttribute : FactAttribute
{
    public VolumeFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLARITY_TEST_SQL"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLARITY_TEST_VOLUME")))
        {
            Skip = "Volume test skipped: set CLARITY_TEST_SQL to a disposable SQL Server and CLARITY_TEST_VOLUME to the users per domain shard.";
        }
    }
}
