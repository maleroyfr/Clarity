using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class DiscoveryModelTests
{
    [Fact]
    public void EveryWorkspaceEntityHasScopedKeysFiltersAndForeignKeys()
    {
        using var db = PersistenceTests.Create("Server=unused;Database=unused", []);
        foreach (var entity in db.Model.GetEntityTypes().Where(x => typeof(WorkspaceEntity).IsAssignableFrom(x.ClrType)))
        {
            entity.FindPrimaryKey()!.Properties.Select(x => x.Name).ShouldBe(["WorkspaceId", "Id"]);
            entity.GetDeclaredQueryFilters().ShouldNotBeEmpty();
            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.Properties.Select(x => x.Name).ShouldContain("WorkspaceId");
            }
        }
        var run = db.Model.FindEntityType(typeof(CollectionRun))!;
        run.GetIndexes().ShouldContain(x => x.IsUnique && x.Properties.Any(p => p.Name == "ContentHash"));
        run.GetIndexes().ShouldContain(x => x.IsUnique && x.Properties.Any(p => p.Name == "ExternalRunId"));
    }

    [SqlFact]
    [Trait("Category", "SQL")]
    public async Task CollectionMetadataIsIsolatedEvenWithoutEfFilters()
    {
        await using var database = await TestDatabase.CreateAsync();
        var alpha = new ClientWorkspace { Name = "Synthetic Alpha" };
        var beta = new ClientWorkspace { Name = "Synthetic Beta" };
        await using (var setup = PersistenceTests.Create(database.ConnectionString, [alpha.Id, beta.Id]))
        {
            setup.Workspaces.AddRange(alpha, beta);
            setup.Connections.AddRange(NewConnection(alpha.Id), NewConnection(beta.Id));
            await setup.SaveChangesAsync();
        }
        await using var db = PersistenceTests.Create(database.ConnectionString, [alpha.Id]);
        (await db.Connections.IgnoreQueryFilters().ToListAsync()).ShouldHaveSingleItem().WorkspaceId.ShouldBe(alpha.Id);
        db.Connections.Add(NewConnection(beta.Id));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static Connection NewConnection(Guid workspaceId) => new()
    {
        WorkspaceId = workspaceId,
        Name = "Synthetic Graph",
        TenantId = Guid.NewGuid(),
        ProducerId = "clarity.graph.entra",
        ConfigurationReference = "collector"
    };
}
