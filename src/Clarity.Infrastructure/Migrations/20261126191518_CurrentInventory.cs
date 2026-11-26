using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126191518_CurrentInventory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DisplayName",
            table: "InventoryObjects",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastObservedAtUtc",
            table: "InventoryObjects",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Presence",
            table: "InventoryObjects",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<Guid>(
            name: "PresenceSectionId",
            table: "InventoryObjects",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Smtp",
            table: "InventoryObjects",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "UserPrincipalName",
            table: "InventoryObjects",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CurrentValues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                ValueJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Conflicting = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CurrentValues", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_CurrentValues_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_CurrentValues_ObjectObservations_WorkspaceId_ObjectId_ObservationId",
                    columns: x => new { x.WorkspaceId, x.ObjectId, x.ObservationId },
                    principalTable: "ObjectObservations",
                    principalColumns: new[] { "WorkspaceId", "ObjectId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryObjects_WorkspaceId_EnvironmentId_Presence_Type_DisplayName",
            table: "InventoryObjects",
            columns: new[] { "WorkspaceId", "EnvironmentId", "Presence", "Type", "DisplayName" });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryObjects_WorkspaceId_PresenceSectionId",
            table: "InventoryObjects",
            columns: new[] { "WorkspaceId", "PresenceSectionId" });

        migrationBuilder.CreateIndex(
            name: "IX_CurrentValues_WorkspaceId_ObjectId_Field",
            table: "CurrentValues",
            columns: new[] { "WorkspaceId", "ObjectId", "Field" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CurrentValues_WorkspaceId_ObjectId_ObservationId",
            table: "CurrentValues",
            columns: new[] { "WorkspaceId", "ObjectId", "ObservationId" });

        migrationBuilder.AddForeignKey(
            name: "FK_InventoryObjects_SectionRuns_WorkspaceId_PresenceSectionId",
            table: "InventoryObjects",
            columns: new[] { "WorkspaceId", "PresenceSectionId" },
            principalTable: "SectionRuns",
            principalColumns: new[] { "WorkspaceId", "Id" });
        migrationBuilder.Sql("""
            CREATE SECURITY POLICY Security.CurrentValuesBoundary
            ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.CurrentValues,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.CurrentValues AFTER INSERT,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.CurrentValues AFTER UPDATE
            WITH (STATE = ON, SCHEMABINDING = ON);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY Security.CurrentValuesBoundary;");
        migrationBuilder.DropForeignKey(
            name: "FK_InventoryObjects_SectionRuns_WorkspaceId_PresenceSectionId",
            table: "InventoryObjects");

        migrationBuilder.DropTable(
            name: "CurrentValues");

        migrationBuilder.DropIndex(
            name: "IX_InventoryObjects_WorkspaceId_EnvironmentId_Presence_Type_DisplayName",
            table: "InventoryObjects");

        migrationBuilder.DropIndex(
            name: "IX_InventoryObjects_WorkspaceId_PresenceSectionId",
            table: "InventoryObjects");

        migrationBuilder.DropColumn(
            name: "DisplayName",
            table: "InventoryObjects");

        migrationBuilder.DropColumn(
            name: "LastObservedAtUtc",
            table: "InventoryObjects");

        migrationBuilder.DropColumn(
            name: "Presence",
            table: "InventoryObjects");

        migrationBuilder.DropColumn(
            name: "PresenceSectionId",
            table: "InventoryObjects");

        migrationBuilder.DropColumn(
            name: "Smtp",
            table: "InventoryObjects");

        migrationBuilder.DropColumn(
            name: "UserPrincipalName",
            table: "InventoryObjects");
    }
}
