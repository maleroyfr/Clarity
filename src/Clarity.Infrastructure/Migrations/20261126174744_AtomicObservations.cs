using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126174744_AtomicObservations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InventoryObjects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryObjects", x => new { x.WorkspaceId, x.Id });
                table.UniqueConstraint("AK_InventoryObjects_WorkspaceId_EnvironmentId_Id", x => new { x.WorkspaceId, x.EnvironmentId, x.Id });
                table.ForeignKey(
                    name: "FK_InventoryObjects_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_InventoryObjects_Environments_WorkspaceId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "RelationshipObservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SectionRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RelationType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                FromJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ToJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                PropertiesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                IdentityHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ObservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RelationshipObservations", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_RelationshipObservations_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_RelationshipObservations_SectionRuns_WorkspaceId_SectionRunId",
                    columns: x => new { x.WorkspaceId, x.SectionRunId },
                    principalTable: "SectionRuns",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "ExternalKeys",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Namespace = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                ExternalId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, collation: "Latin1_General_100_BIN2")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExternalKeys", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_ExternalKeys_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ExternalKeys_InventoryObjects_WorkspaceId_EnvironmentId_ObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "ObjectObservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SectionRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                AttributesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                UserPrincipalName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Smtp = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Status = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                DisplayName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ObjectObservations", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_ObjectObservations_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ObjectObservations_InventoryObjects_WorkspaceId_ObjectId",
                    columns: x => new { x.WorkspaceId, x.ObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_ObjectObservations_SectionRuns_WorkspaceId_SectionRunId",
                    columns: x => new { x.WorkspaceId, x.SectionRunId },
                    principalTable: "SectionRuns",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_ExternalKeys_WorkspaceId_EnvironmentId_Namespace_Type_ExternalId",
            table: "ExternalKeys",
            columns: new[] { "WorkspaceId", "EnvironmentId", "Namespace", "Type", "ExternalId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ExternalKeys_WorkspaceId_EnvironmentId_ObjectId",
            table: "ExternalKeys",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_ObjectObservations_WorkspaceId_ObjectId_ObservedAtUtc",
            table: "ObjectObservations",
            columns: new[] { "WorkspaceId", "ObjectId", "ObservedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ObjectObservations_WorkspaceId_SectionRunId_ObjectId",
            table: "ObjectObservations",
            columns: new[] { "WorkspaceId", "SectionRunId", "ObjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ObjectObservations_WorkspaceId_Smtp",
            table: "ObjectObservations",
            columns: new[] { "WorkspaceId", "Smtp" });

        migrationBuilder.CreateIndex(
            name: "IX_ObjectObservations_WorkspaceId_Status_ObservedAtUtc",
            table: "ObjectObservations",
            columns: new[] { "WorkspaceId", "Status", "ObservedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ObjectObservations_WorkspaceId_UserPrincipalName",
            table: "ObjectObservations",
            columns: new[] { "WorkspaceId", "UserPrincipalName" });

        migrationBuilder.CreateIndex(
            name: "IX_RelationshipObservations_WorkspaceId_SectionRunId_IdentityHash",
            table: "RelationshipObservations",
            columns: new[] { "WorkspaceId", "SectionRunId", "IdentityHash" },
            unique: true);
        foreach (var table in new[] { "InventoryObjects", "ExternalKeys", "ObjectObservations", "RelationshipObservations" })
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
        foreach (var table in new[] { "InventoryObjects", "ExternalKeys", "ObjectObservations", "RelationshipObservations" })
        {
            migrationBuilder.Sql($"DROP SECURITY POLICY Security.{table}Boundary;");
        }
        migrationBuilder.DropTable(
            name: "ExternalKeys");

        migrationBuilder.DropTable(
            name: "ObjectObservations");

        migrationBuilder.DropTable(
            name: "RelationshipObservations");

        migrationBuilder.DropTable(
            name: "InventoryObjects");
    }
}
