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
