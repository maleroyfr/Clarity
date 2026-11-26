using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126182928_GraphCursors : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContinuationJson",
            table: "CollectionRuns",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.CreateTable(
            name: "GraphCursors",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CapabilityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ScopeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                DeltaLink = table.Column<string>(type: "nvarchar(max)", maxLength: 16384, nullable: false),
                PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GraphCursors", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_GraphCursors_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_GraphCursors_Connections_WorkspaceId_ConnectionId",
                    columns: x => new { x.WorkspaceId, x.ConnectionId },
                    principalTable: "Connections",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_GraphCursors_Environments_WorkspaceId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_GraphCursors_WorkspaceId_ConnectionId",
            table: "GraphCursors",
            columns: new[] { "WorkspaceId", "ConnectionId" });

        migrationBuilder.CreateIndex(
            name: "IX_GraphCursors_WorkspaceId_EnvironmentId_ConnectionId_CapabilityId",
            table: "GraphCursors",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ConnectionId", "CapabilityId" },
            unique: true);
        migrationBuilder.Sql("""
            CREATE SECURITY POLICY Security.GraphCursorsBoundary
            ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.GraphCursors,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.GraphCursors AFTER INSERT,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.GraphCursors AFTER UPDATE
            WITH (STATE = ON, SCHEMABINDING = ON);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY Security.GraphCursorsBoundary;");
        migrationBuilder.DropTable(
            name: "GraphCursors");

        migrationBuilder.DropColumn(
            name: "ContinuationJson",
            table: "CollectionRuns");
    }
}
