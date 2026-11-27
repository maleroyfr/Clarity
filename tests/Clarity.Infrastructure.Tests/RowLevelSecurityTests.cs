using Clarity.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public class RowLevelSecurityTests
{
    [SqlFact]
    [Trait("Category", "SQL")]
    public async Task Bypassing_EF_filters_cannot_read_or_mutate_other_clients_and_pooled_scope_is_reset()
    {
        await using var database = await TestDatabase.CreateAsync();
        var alpha = new ClientWorkspace { Name = "Synthetic Alpha" };
        var beta = new ClientWorkspace { Name = "Synthetic Beta" };
        var connection = new SqlConnectionStringBuilder(database.ConnectionString) { MaxPoolSize = 1 }.ConnectionString;
        await using (var setup = PersistenceTests.Create(connection, [alpha.Id, beta.Id]))
        {
            setup.Workspaces.AddRange(alpha, beta);
            setup.Projects.AddRange(new Project { WorkspaceId = alpha.Id, Name = "Alpha audit" },
                new Project { WorkspaceId = beta.Id, Name = "Beta audit" });
            await setup.SaveChangesAsync();
        }

        short sessionId;
        await using (var first = PersistenceTests.Create(connection, [alpha.Id]))
        {
            await first.Database.OpenConnectionAsync();
            sessionId = await first.Database.SqlQueryRaw<short>("SELECT @@SPID AS Value").SingleAsync();
            var visible = await first.Projects.IgnoreQueryFilters().ToListAsync();
            visible.ShouldHaveSingleItem().WorkspaceId.ShouldBe(alpha.Id);
            (await first.Projects.IgnoreQueryFilters().Where(x => x.WorkspaceId == beta.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Name, "Forbidden"))).ShouldBe(0);
            first.Projects.Add(new Project { WorkspaceId = beta.Id, Name = "Forbidden insert" });
            await Should.ThrowAsync<DbUpdateException>(() => first.SaveChangesAsync());
        }

        await using (var second = PersistenceTests.Create(connection, [beta.Id]))
        {
            await second.Database.OpenConnectionAsync();
            (await second.Database.SqlQueryRaw<short>("SELECT @@SPID AS Value").SingleAsync()).ShouldBe(sessionId);
            (await second.Projects.IgnoreQueryFilters().ToListAsync()).ShouldHaveSingleItem().WorkspaceId.ShouldBe(beta.Id);
        }

        await using var anonymous = PersistenceTests.Create(connection, []);
        (await anonymous.Projects.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [SqlFact]
    [Trait("Category", "SQL")]
    [Trait("Recette", "R09")]
    public async Task Every_workspace_table_has_an_enabled_filter_and_insert_update_block_predicates()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var tables = new SortedSet<string>(StringComparer.Ordinal) { "ClientWorkspaces" };
        await using (var command = new SqlCommand("""
            SELECT t.name FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id
            WHERE SCHEMA_NAME(t.schema_id) = N'dbo' AND c.name = N'WorkspaceId'
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken)) { tables.Add(reader.GetString(0)); }
        }
        using (var model = PersistenceTests.Create("Server=unused;Database=unused", []))
        {
            foreach (var entity in model.Model.GetEntityTypes().Where(x => typeof(WorkspaceEntity).IsAssignableFrom(x.ClrType)))
            {
                tables.ShouldContain(entity.GetTableName()!);
            }
        }
        var predicates = new List<(string Table, string Kind, string Function)>();
        await using (var command = new SqlCommand("""
            SELECT OBJECT_NAME(p.target_object_id), p.predicate_type_desc + ISNULL(N' ' + p.operation_desc, N''), p.predicate_definition
            FROM sys.security_predicates p JOIN sys.security_policies s ON s.object_id = p.object_id
            WHERE s.is_enabled = 1 AND s.is_schema_bound = 1
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                predicates.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }
        tables.Count.ShouldBeGreaterThan(30);
        foreach (var table in tables)
        {
            var kinds = predicates.Where(x => x.Table == table).Select(x => x.Kind).Order(StringComparer.Ordinal).ToArray();
            kinds.ShouldBe(["BLOCK AFTER INSERT", "BLOCK AFTER UPDATE", "FILTER"], $"{table} must have enabled, schema-bound row-level security.");
            predicates.Where(x => x.Table == table).ShouldAllBe(x => x.Function.Contains("[Security].", StringComparison.Ordinal),
                $"{table} predicates must use the Security schema functions.");
        }
    }
}
