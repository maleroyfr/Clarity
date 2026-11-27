using Microsoft.Data.SqlClient;

namespace Clarity.Tests;

/// <summary>
/// A disposable SQL login mapped to the <c>[clarity-app]</c> database user and given exactly the application statements of
/// <c>infra/bootstrap-sql.sql</c>, so SQL tests run the application as its least-privileged deployed principal rather than as the
/// CI administrator. The deployed user comes from Entra (<c>FROM EXTERNAL PROVIDER</c>); here it is mapped to a SQL login with a
/// random password that exists only for the test.
/// </summary>
internal sealed class ApplicationLogin : IAsyncDisposable
{
    internal const string ExternalUser = "CREATE USER [clarity-app] FROM EXTERNAL PROVIDER;";
    private readonly string administrator;
    private readonly string login;

    private ApplicationLogin(string administrator, string login, string connectionString)
    {
        this.administrator = administrator;
        this.login = login;
        ConnectionString = connectionString;
    }

    /// <summary>The same server and database, authenticated as the disposable application login.</summary>
    internal string ConnectionString { get; }

    /// <summary>The application statements of the bootstrap script: from <see cref="ExternalUser"/> to the next blank line, without comments.</summary>
    internal static IReadOnlyList<string> Statements()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "bootstrap-sql.sql"));
        var start = Array.IndexOf(lines, ExternalUser);
        if (start < 0) { throw new InvalidDataException($"bootstrap-sql.sql must contain the line {ExternalUser}"); }
        return [.. lines.Skip(start).TakeWhile(line => line.Length > 0).Where(line => !line.StartsWith("--", StringComparison.Ordinal))];
    }

    /// <summary>Creates the login and applies the bootstrap statements in the database of <paramref name="administratorConnection"/>.</summary>
    internal static async Task<ApplicationLogin> CreateAsync(string administratorConnection)
    {
        var login = $"clarity_app_{Guid.NewGuid():N}";
        var password = $"{Guid.NewGuid():N}-Aa1";
        var script = string.Join('\n', Statements()).Replace(ExternalUser, $"CREATE USER [clarity-app] FOR LOGIN [{login}];", StringComparison.Ordinal);
        await using var connection = new SqlConnection(administratorConnection);
        await connection.OpenAsync();
        await using (var command = new SqlCommand("""
            DECLARE @create nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''')
                + N', CHECK_POLICY = OFF;';
            EXEC sys.sp_executesql @create;
            """, connection))
        {
            command.Parameters.AddWithValue("@login", login);
            command.Parameters.AddWithValue("@password", password);
            await command.ExecuteNonQueryAsync();
        }
        await using (var command = new SqlCommand("EXEC sys.sp_executesql @script;", connection))
        {
            command.Parameters.AddWithValue("@script", script);
            await command.ExecuteNonQueryAsync();
        }
        var application = new SqlConnectionStringBuilder(administratorConnection) { UserID = login, Password = password, IntegratedSecurity = false };
        return new(administratorConnection, login, application.ConnectionString);
    }

    /// <summary>Drops the login; drop its database first.</summary>
    public async ValueTask DisposeAsync()
    {
        await using (var pooled = new SqlConnection(ConnectionString)) { SqlConnection.ClearPool(pooled); }
        var master = new SqlConnectionStringBuilder(administrator) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            DECLARE @kill nvarchar(max) = N'';
            SELECT @kill += N'KILL ' + CAST(session_id AS nvarchar(10)) + N';' FROM sys.dm_exec_sessions WHERE login_name = @login;
            EXEC (@kill);
            DECLARE @drop nvarchar(max) = N'DROP LOGIN ' + QUOTENAME(@login) + N';';
            EXEC sys.sp_executesql @drop;
            """, connection);
        command.Parameters.AddWithValue("@login", login);
        await command.ExecuteNonQueryAsync();
    }
}
