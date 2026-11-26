using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126192117_EvidencedRelationships : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InventoryRelationships",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FromObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ToObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                FromJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ToJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                RelationType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                FromMethod = table.Column<int>(type: "int", nullable: false),
                ToMethod = table.Column<int>(type: "int", nullable: false),
                Origin = table.Column<int>(type: "int", nullable: false),
                IsCurrent = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryRelationships", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_InventoryRelationships_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_InventoryRelationships_Environments_WorkspaceId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_InventoryRelationships_InventoryObjects_WorkspaceId_EnvironmentId_FromObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.FromObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_InventoryRelationships_InventoryObjects_WorkspaceId_EnvironmentId_ToObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.ToObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "RelationshipSupports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RelationshipSupports", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_RelationshipSupports_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_RelationshipSupports_InventoryRelationships_WorkspaceId_RelationshipId",
                    columns: x => new { x.WorkspaceId, x.RelationshipId },
                    principalTable: "InventoryRelationships",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_RelationshipSupports_RelationshipObservations_WorkspaceId_ObservationId",
                    columns: x => new { x.WorkspaceId, x.ObservationId },
                    principalTable: "RelationshipObservations",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryRelationships_WorkspaceId_EnvironmentId_FromObjectId_Origin_IsCurrent",
            table: "InventoryRelationships",
            columns: new[] { "WorkspaceId", "EnvironmentId", "FromObjectId", "Origin", "IsCurrent" });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryRelationships_WorkspaceId_EnvironmentId_ToObjectId_Origin_IsCurrent",
            table: "InventoryRelationships",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ToObjectId", "Origin", "IsCurrent" });

        migrationBuilder.CreateIndex(
            name: "IX_RelationshipSupports_WorkspaceId_ObservationId",
            table: "RelationshipSupports",
            columns: new[] { "WorkspaceId", "ObservationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RelationshipSupports_WorkspaceId_RelationshipId",
            table: "RelationshipSupports",
            columns: new[] { "WorkspaceId", "RelationshipId" });
        foreach (var table in new[] { "InventoryRelationships", "RelationshipSupports" })
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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY Security.RelationshipSupportsBoundary; DROP SECURITY POLICY Security.InventoryRelationshipsBoundary;");
        migrationBuilder.DropTable(
            name: "RelationshipSupports");

        migrationBuilder.DropTable(
            name: "InventoryRelationships");
    }
}
