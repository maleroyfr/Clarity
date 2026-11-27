using Clarity.Core;
using Clarity.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
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

    internal static ClarityDbContext Create(string connection, IReadOnlyList<Guid> workspaces)
    {
        var scope = new WorkspaceScope(default, workspaces);
        return new(new DbContextOptionsBuilder<ClarityDbContext>().UseSqlServer(connection)
            .AddInterceptors(new WorkspaceConnectionInterceptor(scope)).Options, scope);
    }
}

internal sealed class TestDatabase(string administratorConnectionString, ApplicationLogin? application) : IAsyncDisposable
{
    /// <summary>The CI administrator, which also applies migrations.</summary>
    public string AdministratorConnectionString { get; } = administratorConnectionString;

    /// <summary>The application login when the database was created for the application, otherwise the administrator.</summary>
    public string ConnectionString => application?.ConnectionString ?? AdministratorConnectionString;

    /// <param name="application">Connect as a disposable login with the reviewed grants of infra/bootstrap-sql.sql.</param>
    public static async Task<TestDatabase> CreateAsync(bool application = false)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLARITY_TEST_SQL"))
        {
            InitialCatalog = $"ClarityTest_{Guid.NewGuid():N}"
        }.ConnectionString;
        var database = new TestDatabase(connection, null);
        try
        {
            await using (var db = PersistenceTests.Create(connection, [])) { await db.Database.MigrateAsync(); }
            return application ? new TestDatabase(connection, await ApplicationLogin.CreateAsync(connection)) : database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await using (var db = PersistenceTests.Create(AdministratorConnectionString, [])) { await db.Database.EnsureDeletedAsync(); }
        if (application is not null) { await application.DisposeAsync(); }
    }
}
