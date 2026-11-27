using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261127014518_TargetComparisons : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Kind",
            table: "MappingDecisions",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "TargetComparisons",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceEnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetEnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Data = table.Column<int>(type: "int", nullable: false),
                OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ComputedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Matched = table.Column<int>(type: "int", nullable: false),
                Ambiguous = table.Column<int>(type: "int", nullable: false),
                Absent = table.Column<int>(type: "int", nullable: false),
                Unknown = table.Column<int>(type: "int", nullable: false),
                NotCompared = table.Column<int>(type: "int", nullable: false),
                Collisions = table.Column<int>(type: "int", nullable: false),
                RulesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CoverageJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TargetComparisons", x => new { x.WorkspaceId, x.Id });
                table.UniqueConstraint("AK_TargetComparisons_WorkspaceId_SourceEnvironmentId_TargetEnvironmentId_Id", x => new { x.WorkspaceId, x.SourceEnvironmentId, x.TargetEnvironmentId, x.Id });
                table.ForeignKey(
                    name: "FK_TargetComparisons_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_TargetComparisons_Environments_WorkspaceId_ProjectId_SourceEnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.SourceEnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
                table.ForeignKey(
                    name: "FK_TargetComparisons_Environments_WorkspaceId_ProjectId_TargetEnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.TargetEnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "TargetMappingRules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                SourceField = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                TargetField = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                TargetDomain = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: true),
                Priority = table.Column<int>(type: "int", nullable: false),
                Justification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedByTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedByObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RetiredByTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RetiredByObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RetiredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RetirementReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TargetMappingRules", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_TargetMappingRules_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_TargetMappingRules_Projects_WorkspaceId_ProjectId",
                    columns: x => new { x.WorkspaceId, x.ProjectId },
                    principalTable: "Projects",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "TargetComparisonItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ComparisonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceEnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetEnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                TargetObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OtherObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Outcome = table.Column<int>(type: "int", nullable: true),
                Collision = table.Column<int>(type: "int", nullable: true),
                ObjectType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Label = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                TargetLabel = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                OtherLabel = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                Key = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                CandidateCount = table.Column<int>(type: "int", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TargetComparisonItems", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_TargetComparisonItems_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_TargetComparisonItems_InventoryObjects_WorkspaceId_SourceEnvironmentId_OtherObjectId",
                    columns: x => new { x.WorkspaceId, x.SourceEnvironmentId, x.OtherObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_TargetComparisonItems_InventoryObjects_WorkspaceId_SourceEnvironmentId_SourceObjectId",
                    columns: x => new { x.WorkspaceId, x.SourceEnvironmentId, x.SourceObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_TargetComparisonItems_InventoryObjects_WorkspaceId_TargetEnvironmentId_TargetObjectId",
                    columns: x => new { x.WorkspaceId, x.TargetEnvironmentId, x.TargetObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_TargetComparisonItems_TargetComparisons_WorkspaceId_SourceEnvironmentId_TargetEnvironmentId_ComparisonId",
                    columns: x => new { x.WorkspaceId, x.SourceEnvironmentId, x.TargetEnvironmentId, x.ComparisonId },
                    principalTable: "TargetComparisons",
                    principalColumns: new[] { "WorkspaceId", "SourceEnvironmentId", "TargetEnvironmentId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_TargetComparisonItems_WorkspaceId_ComparisonId_Collision_Key",
            table: "TargetComparisonItems",
            columns: new[] { "WorkspaceId", "ComparisonId", "Collision", "Key" });

        migrationBuilder.CreateIndex(
            name: "IX_TargetComparisonItems_WorkspaceId_ComparisonId_Outcome_Label",
            table: "TargetComparisonItems",
            columns: new[] { "WorkspaceId", "ComparisonId", "Outcome", "Label" });

        migrationBuilder.CreateIndex(
            name: "IX_TargetComparisonItems_WorkspaceId_SourceEnvironmentId_OtherObjectId",
            table: "TargetComparisonItems",
            columns: new[] { "WorkspaceId", "SourceEnvironmentId", "OtherObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_TargetComparisonItems_WorkspaceId_SourceEnvironmentId_SourceObjectId",
            table: "TargetComparisonItems",
            columns: new[] { "WorkspaceId", "SourceEnvironmentId", "SourceObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_TargetComparisonItems_WorkspaceId_SourceEnvironmentId_TargetEnvironmentId_ComparisonId",
            table: "TargetComparisonItems",
            columns: new[] { "WorkspaceId", "SourceEnvironmentId", "TargetEnvironmentId", "ComparisonId" });

        migrationBuilder.CreateIndex(
            name: "IX_TargetComparisonItems_WorkspaceId_TargetEnvironmentId_TargetObjectId",
            table: "TargetComparisonItems",
            columns: new[] { "WorkspaceId", "TargetEnvironmentId", "TargetObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_TargetComparisons_WorkspaceId_OperationId",
            table: "TargetComparisons",
            columns: new[] { "WorkspaceId", "OperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TargetComparisons_WorkspaceId_ProjectId_SourceEnvironmentId_TargetEnvironmentId",
            table: "TargetComparisons",
            columns: new[] { "WorkspaceId", "ProjectId", "SourceEnvironmentId", "TargetEnvironmentId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TargetComparisons_WorkspaceId_ProjectId_TargetEnvironmentId",
            table: "TargetComparisons",
            columns: new[] { "WorkspaceId", "ProjectId", "TargetEnvironmentId" });

        migrationBuilder.CreateIndex(
            name: "IX_TargetMappingRules_WorkspaceId_OperationId",
            table: "TargetMappingRules",
            columns: new[] { "WorkspaceId", "OperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TargetMappingRules_WorkspaceId_ProjectId_RetiredAtUtc",
            table: "TargetMappingRules",
            columns: new[] { "WorkspaceId", "ProjectId", "RetiredAtUtc" });

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

    private static readonly string[] Tables = ["TargetMappingRules", "TargetComparisons", "TargetComparisonItems"];

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables) { migrationBuilder.Sql($"DROP SECURITY POLICY Security.{table}Boundary;"); }

        migrationBuilder.DropTable(
            name: "TargetComparisonItems");

        migrationBuilder.DropTable(
            name: "TargetMappingRules");

        migrationBuilder.DropTable(
            name: "TargetComparisons");

        migrationBuilder.DropColumn(
            name: "Kind",
            table: "MappingDecisions");
    }
}
