using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126175339_DurableCollectionJobs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CollectionJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ConcurrencyKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                Attempt = table.Column<int>(type: "int", nullable: false),
                Progress = table.Column<int>(type: "int", nullable: false),
                CancellationRequested = table.Column<bool>(type: "bit", nullable: false),
                LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                AvailableAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CollectionJobs", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_CollectionJobs_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_CollectionJobs_Projects_WorkspaceId_ProjectId",
                    columns: x => new { x.WorkspaceId, x.ProjectId },
                    principalTable: "Projects",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_CollectionJobs_ConcurrencyKey_State_LeaseExpiresAtUtc",
            table: "CollectionJobs",
            columns: new[] { "ConcurrencyKey", "State", "LeaseExpiresAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_CollectionJobs_State_AvailableAtUtc_LeaseExpiresAtUtc",
            table: "CollectionJobs",
            columns: new[] { "State", "AvailableAtUtc", "LeaseExpiresAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_CollectionJobs_WorkspaceId_ProjectId_CreatedAtUtc",
            table: "CollectionJobs",
            columns: new[] { "WorkspaceId", "ProjectId", "CreatedAtUtc" });
        migrationBuilder.Sql("""
            CREATE FUNCTION Security.JobAccess(@WorkspaceId uniqueidentifier)
            RETURNS TABLE WITH SCHEMABINDING AS RETURN
            SELECT 1 AS Allowed
            WHERE CONVERT(bit, SESSION_CONTEXT(N'clarity.jobDispatcher')) = 1
               OR CHARINDEX('|' + CONVERT(varchar(36), @WorkspaceId) + '|',
                    CONVERT(varchar(8000), SESSION_CONTEXT(N'clarity.workspaces'))) > 0;
            """);
        migrationBuilder.Sql("""
            CREATE SECURITY POLICY Security.CollectionJobsBoundary
            ADD FILTER PREDICATE Security.JobAccess(WorkspaceId) ON dbo.CollectionJobs,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.CollectionJobs AFTER INSERT,
            ADD BLOCK PREDICATE Security.JobAccess(WorkspaceId) ON dbo.CollectionJobs AFTER UPDATE
            WITH (STATE = ON, SCHEMABINDING = ON);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY Security.CollectionJobsBoundary; DROP FUNCTION Security.JobAccess;");
        migrationBuilder.DropTable(
            name: "CollectionJobs");
    }
}
