using System.Text.RegularExpressions;
using Clarity.Core;
using Clarity.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed partial class DatabaseGrantTests
{
    private static readonly string[] Denials =
    [
        "DENY ALTER ANY SECURITY POLICY TO [clarity-app];",
        "DENY ALTER ANY ROLE TO [clarity-app];",
        "DENY ALTER ON SCHEMA::dbo TO [clarity-app];",
        "DENY ALTER ON SCHEMA::Security TO [clarity-app];"
    ];

    [Fact]
    public void BootstrapGrantsEveryModelTableOnlyTheReviewedPermissions()
    {
        var statements = ApplicationLogin.Statements();
        statements[0].ShouldBe(ApplicationLogin.ExternalUser);
        statements.TakeLast(Denials.Length).ShouldBe(Denials);
        var grants = Grants(statements);
        statements.Count.ShouldBe(1 + grants.Count + Denials.Length, "every application statement is a table GRANT or a reviewed DENY");

        using var db = new ClarityDbContext(new DbContextOptionsBuilder<ClarityDbContext>().UseSqlServer("Server=unused").Options,
            new WorkspaceScope(new UserIdentity(Guid.Empty, Guid.Empty), []));
        var tables = db.Model.GetEntityTypes().ToDictionary(x => x.GetTableName()!, x => x.ClrType, StringComparer.Ordinal);
        grants.Keys.Order(StringComparer.Ordinal).ShouldBe(tables.Keys.Order(StringComparer.Ordinal));
        foreach (var (table, type) in tables)
        {
            grants[table].ShouldBe(Expected(type), table);
        }
    }

    [SqlFact]
    public async Task ApplicationLoginHoldsExactlyTheBootstrapGrantsAndCannotWeakenIsolation()
    {
        await using var database = await TestDatabase.CreateAsync(application: true);
        var expected = Grants(ApplicationLogin.Statements())
            .SelectMany(x => x.Value.Select(permission => $"GRANT {permission} OBJECT_OR_COLUMN dbo.{x.Key}"))
            .Concat([
                "GRANT CONNECT DATABASE ",
                "DENY ALTER ANY SECURITY POLICY DATABASE ",
                "DENY ALTER ANY ROLE DATABASE ",
                "DENY ALTER SCHEMA dbo",
                "DENY ALTER SCHEMA Security"]);
        var actual = new List<string>();
        await using (var administrator = new SqlConnection(database.AdministratorConnectionString))
        {
            await administrator.OpenAsync();
            await using (var command = new SqlCommand("""
                SELECT CONCAT(p.state_desc COLLATE DATABASE_DEFAULT, N' ', p.permission_name COLLATE DATABASE_DEFAULT, N' ',
                    p.class_desc COLLATE DATABASE_DEFAULT, N' ',
                    CASE p.class
                        WHEN 1 THEN (OBJECT_SCHEMA_NAME(p.major_id) + N'.' + OBJECT_NAME(p.major_id)) COLLATE DATABASE_DEFAULT
                        WHEN 3 THEN s.name COLLATE DATABASE_DEFAULT
                        ELSE N'' END)
                FROM sys.database_permissions p
                LEFT JOIN sys.schemas s ON p.class = 3 AND s.schema_id = p.major_id
                WHERE p.grantee_principal_id = DATABASE_PRINCIPAL_ID(N'clarity-app') AND p.minor_id = 0
                """, administrator))
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) { actual.Add(reader.GetString(0)); }
            }
            await using (var command = new SqlCommand("""
                SELECT COUNT(*) FROM sys.database_role_members WHERE member_principal_id = DATABASE_PRINCIPAL_ID(N'clarity-app')
                """, administrator))
            {
                ((int)(await command.ExecuteScalarAsync())!).ShouldBe(0, "the application belongs to no database role");
            }
        }
        actual.Order(StringComparer.Ordinal).ShouldBe(expected.Order(StringComparer.Ordinal));

        await using (var application = new SqlConnection(database.ConnectionString))
        {
            await application.OpenAsync();
            foreach (var refused in new[]
            {
                "UPDATE dbo.AuditEvents SET WorkspaceId = WorkspaceId WHERE 1 = 0;",
                "DELETE FROM dbo.PlatformAdmins WHERE 1 = 0;",
                "DELETE FROM dbo.ClientWorkspaces WHERE 1 = 0;",
                "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;"
            })
            {
                (await Should.ThrowAsync<SqlException>(() => ExecuteAsync(application, refused))).Number.ShouldBe(229, refused);
            }
            foreach (var refused in new[]
            {
                "ALTER SECURITY POLICY Security.ClientBoundary WITH (STATE = OFF);",
                "ALTER SECURITY POLICY Security.ClientBoundary DROP BLOCK PREDICATE ON dbo.AuditEvents BEFORE DELETE;",
                "DROP FUNCTION Security.AuditRetention;",
                "CREATE TABLE dbo.Probe (Id int);"
            })
            {
                await Should.ThrowAsync<SqlException>(() => ExecuteAsync(application, refused));
            }
        }
        await using (var administrator = new SqlConnection(database.AdministratorConnectionString))
        {
            await administrator.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT COUNT(*) FROM sys.security_policies p JOIN sys.security_predicates r ON r.object_id = p.object_id
                WHERE p.name = N'ClientBoundary' AND p.is_enabled = 1 AND r.target_object_id = OBJECT_ID(N'dbo.AuditEvents')
                    AND r.predicate_type_desc = N'BLOCK' AND r.operation_desc = N'BEFORE DELETE'
                """, administrator);
            ((int)(await command.ExecuteScalarAsync())!).ShouldBe(1, "row-level security and the audit retention guard stay in force");
        }
    }

    /// <summary>The reviewed permissions per table: workspace-level tables a project deletion keeps lose what the application never does.</summary>
    private static string[] Expected(Type entity) =>
        entity == typeof(PlatformAdmin) ? ["SELECT"]
        : entity == typeof(AuditEvent) ? ["SELECT", "INSERT", "DELETE"]
        : DeletionJobHandler.WorkspaceLevel.Contains(entity) ? ["SELECT", "INSERT", "UPDATE"]
        : ["SELECT", "INSERT", "UPDATE", "DELETE"];

    private static Dictionary<string, string[]> Grants(IReadOnlyList<string> statements)
    {
        var grants = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var statement in statements.Where(x => x.StartsWith("GRANT ", StringComparison.Ordinal)))
        {
            var match = GrantPattern().Match(statement);
            match.Success.ShouldBeTrue($"only per-table grants to [clarity-app]: {statement}");
            grants.Add(match.Groups["table"].Value, match.Groups["permissions"].Value.Split(", "));
        }
        return grants;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string statement)
    {
        await using var command = new SqlCommand("EXEC sys.sp_executesql @statement;", connection);
        command.Parameters.AddWithValue("@statement", statement);
        await command.ExecuteNonQueryAsync();
    }

    [GeneratedRegex(@"^GRANT (?<permissions>(SELECT|INSERT|UPDATE|DELETE)(, (SELECT|INSERT|UPDATE|DELETE))*) ON dbo\.(?<table>\w+) TO \[clarity-app\];$")]
    private static partial Regex GrantPattern();
}
