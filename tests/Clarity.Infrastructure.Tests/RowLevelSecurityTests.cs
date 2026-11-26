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
}
