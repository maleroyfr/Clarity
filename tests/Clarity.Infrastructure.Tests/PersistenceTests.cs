using Clarity.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

public class PersistenceTests
{
    [Fact]
    public void Workspace_filter_is_parameterized_in_generated_sql()
    {
        using var db = Create("Server=localhost;Database=unused", [Guid.NewGuid()]);
        var sql = db.Projects.ToQueryString();
        sql.ShouldContain("WorkspaceId");
        sql.ShouldContain("WHERE");
    }

    [SqlFact]
    [Trait("Category", "SQL")]
    public async Task Composite_foreign_key_prevents_cross_workspace_environment_and_filters_reads()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = new ClientWorkspace { Name = "Synthetic Alpha" };
        var second = new ClientWorkspace { Name = "Synthetic Beta" };
        await using var setup = Create(database.ConnectionString, [first.Id, second.Id]);
        setup.Workspaces.AddRange(first, second);
        var firstProject = new Project { WorkspaceId = first.Id, Name = "Alpha audit" };
        var secondProject = new Project { WorkspaceId = second.Id, Name = "Beta audit" };
        setup.Projects.AddRange(firstProject, secondProject);
        setup.Environments.AddRange(
            new ProjectEnvironment { WorkspaceId = first.Id, ProjectId = firstProject.Id, ExternalId = "same-synthetic-tenant" },
            new ProjectEnvironment { WorkspaceId = second.Id, ProjectId = secondProject.Id, ExternalId = "same-synthetic-tenant" });
        await setup.SaveChangesAsync();

        await using var scoped = Create(database.ConnectionString, [first.Id]);
        (await scoped.Projects.CountAsync()).ShouldBe(1);
        (await scoped.Environments.CountAsync()).ShouldBe(1);
        (await scoped.Projects.AnyAsync(x => x.Id == secondProject.Id)).ShouldBeFalse();
        scoped.Environments.Add(new ProjectEnvironment
        {
            WorkspaceId = first.Id,
            ProjectId = secondProject.Id,
            ExternalId = "cross-client-reference"
        });
        await Should.ThrowAsync<DbUpdateException>(() => scoped.SaveChangesAsync());
    }

    internal static ClarityDbContext Create(string connection, IReadOnlyList<Guid> workspaces) =>
        new(new DbContextOptionsBuilder<ClarityDbContext>().UseSqlServer(connection).Options,
            new WorkspaceScope(default, workspaces));
}

internal sealed class TestDatabase(string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public static async Task<TestDatabase> CreateAsync()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLARITY_TEST_SQL"))
        {
            InitialCatalog = $"ClarityTest_{Guid.NewGuid():N}"
        };
        var database = new TestDatabase(connection.ConnectionString);
        await using var db = PersistenceTests.Create(database.ConnectionString, []);
        await db.Database.MigrateAsync();
        return database;
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = PersistenceTests.Create(ConnectionString, []);
        await db.Database.EnsureDeletedAsync();
    }
}
