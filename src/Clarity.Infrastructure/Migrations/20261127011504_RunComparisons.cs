using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261127011504_RunComparisons : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RunComparisons",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BaselineRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LaterRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ComputedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Added = table.Column<int>(type: "int", nullable: false),
                Modified = table.Column<int>(type: "int", nullable: false),
                Removed = table.Column<int>(type: "int", nullable: false),
                NewlyObserved = table.Column<int>(type: "int", nullable: false),
                NotObserved = table.Column<int>(type: "int", nullable: false),
                NotCompared = table.Column<int>(type: "int", nullable: false),
                Unchanged = table.Column<int>(type: "int", nullable: false),
                RelationsAdded = table.Column<int>(type: "int", nullable: false),
                RelationsRemoved = table.Column<int>(type: "int", nullable: false),
                RelationsUnknown = table.Column<int>(type: "int", nullable: false),
                UnitsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RunComparisons", x => new { x.WorkspaceId, x.Id });
                table.UniqueConstraint("AK_RunComparisons_WorkspaceId_EnvironmentId_Id", x => new { x.WorkspaceId, x.EnvironmentId, x.Id });
                table.ForeignKey(
                    name: "FK_RunComparisons_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_RunComparisons_CollectionRuns_WorkspaceId_BaselineRunId",
                    columns: x => new { x.WorkspaceId, x.BaselineRunId },
                    principalTable: "CollectionRuns",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_RunComparisons_CollectionRuns_WorkspaceId_LaterRunId",
                    columns: x => new { x.WorkspaceId, x.LaterRunId },
                    principalTable: "CollectionRuns",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_RunComparisons_Environments_WorkspaceId_ProjectId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "RunComparisonItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ComparisonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                Change = table.Column<int>(type: "int", nullable: false),
                Tombstone = table.Column<bool>(type: "bit", nullable: false),
                ChangedFields = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ScopeReview = table.Column<bool>(type: "bit", nullable: false),
                MappingReview = table.Column<bool>(type: "bit", nullable: false),
                AssessmentReview = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RunComparisonItems", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_RunComparisonItems_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_RunComparisonItems_InventoryObjects_WorkspaceId_EnvironmentId_ObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_RunComparisonItems_RunComparisons_WorkspaceId_EnvironmentId_ComparisonId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.ComparisonId },
                    principalTable: "RunComparisons",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_RunComparisonItems_WorkspaceId_ComparisonId_Change_DisplayName",
            table: "RunComparisonItems",
            columns: new[] { "WorkspaceId", "ComparisonId", "Change", "DisplayName" });

        migrationBuilder.CreateIndex(
            name: "IX_RunComparisonItems_WorkspaceId_ComparisonId_ObjectId",
            table: "RunComparisonItems",
            columns: new[] { "WorkspaceId", "ComparisonId", "ObjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RunComparisonItems_WorkspaceId_EnvironmentId_ComparisonId",
            table: "RunComparisonItems",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ComparisonId" });

        migrationBuilder.CreateIndex(
            name: "IX_RunComparisonItems_WorkspaceId_EnvironmentId_ObjectId",
            table: "RunComparisonItems",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_RunComparisons_WorkspaceId_BaselineRunId",
            table: "RunComparisons",
            columns: new[] { "WorkspaceId", "BaselineRunId" });

        migrationBuilder.CreateIndex(
            name: "IX_RunComparisons_WorkspaceId_LaterRunId",
            table: "RunComparisons",
            columns: new[] { "WorkspaceId", "LaterRunId" });

        migrationBuilder.CreateIndex(
            name: "IX_RunComparisons_WorkspaceId_OperationId",
            table: "RunComparisons",
            columns: new[] { "WorkspaceId", "OperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RunComparisons_WorkspaceId_ProjectId_EnvironmentId_BaselineRunId_LaterRunId",
            table: "RunComparisons",
            columns: new[] { "WorkspaceId", "ProjectId", "EnvironmentId", "BaselineRunId", "LaterRunId" },
            unique: true);

        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"""
                CREATE SECURITY POLICY Security.{table}Boundary
                ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.{table},
                ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.{table} AFTER INSERT,
                ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.{table} AFTER UPDATE
                WITH (STATE = ON, SCHEMABINDING = ON);
                """);
        }
    }

    private static readonly string[] Tables = ["RunComparisons", "RunComparisonItems"];

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables) { migrationBuilder.Sql($"DROP SECURITY POLICY Security.{table}Boundary;"); }

        migrationBuilder.DropTable(
            name: "RunComparisonItems");

        migrationBuilder.DropTable(
            name: "RunComparisons");
    }
}
