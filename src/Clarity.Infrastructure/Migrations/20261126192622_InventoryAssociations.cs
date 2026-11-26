using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126192622_InventoryAssociations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MappingDecisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AdEnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EntraEnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AdObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EntraObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                Rule = table.Column<int>(type: "int", nullable: false),
                Fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MappingDecisions", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_MappingDecisions_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_MappingDecisions_Environments_WorkspaceId_ProjectId_AdEnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.AdEnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
                table.ForeignKey(
                    name: "FK_MappingDecisions_Environments_WorkspaceId_ProjectId_EntraEnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EntraEnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
                table.ForeignKey(
                    name: "FK_MappingDecisions_InventoryObjects_WorkspaceId_AdEnvironmentId_AdObjectId",
                    columns: x => new { x.WorkspaceId, x.AdEnvironmentId, x.AdObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_MappingDecisions_InventoryObjects_WorkspaceId_EntraEnvironmentId_EntraObjectId",
                    columns: x => new { x.WorkspaceId, x.EntraEnvironmentId, x.EntraObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "SyncBindings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AdEnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EntraEnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Anchor = table.Column<int>(type: "int", nullable: true),
                VerifiedByTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VerifiedByObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SyncBindings", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_SyncBindings_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_SyncBindings_Environments_WorkspaceId_ProjectId_AdEnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.AdEnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
                table.ForeignKey(
                    name: "FK_SyncBindings_Environments_WorkspaceId_ProjectId_EntraEnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EntraEnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "MappingRevisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Number = table.Column<long>(type: "bigint", nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                Rule = table.Column<int>(type: "int", nullable: false),
                EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                ActorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ActorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MappingRevisions", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_MappingRevisions_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_MappingRevisions_MappingDecisions_WorkspaceId_DecisionId",
                    columns: x => new { x.WorkspaceId, x.DecisionId },
                    principalTable: "MappingDecisions",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_MappingDecisions_WorkspaceId_AdEnvironmentId_AdObjectId",
            table: "MappingDecisions",
            columns: new[] { "WorkspaceId", "AdEnvironmentId", "AdObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_MappingDecisions_WorkspaceId_EntraEnvironmentId_EntraObjectId",
            table: "MappingDecisions",
            columns: new[] { "WorkspaceId", "EntraEnvironmentId", "EntraObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_MappingDecisions_WorkspaceId_ProjectId_AdEnvironmentId",
            table: "MappingDecisions",
            columns: new[] { "WorkspaceId", "ProjectId", "AdEnvironmentId" });

        migrationBuilder.CreateIndex(
            name: "IX_MappingDecisions_WorkspaceId_ProjectId_AdObjectId_EntraObjectId",
            table: "MappingDecisions",
            columns: new[] { "WorkspaceId", "ProjectId", "AdObjectId", "EntraObjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MappingDecisions_WorkspaceId_ProjectId_EntraEnvironmentId",
            table: "MappingDecisions",
            columns: new[] { "WorkspaceId", "ProjectId", "EntraEnvironmentId" });

        migrationBuilder.CreateIndex(
            name: "IX_MappingDecisions_WorkspaceId_ProjectId_State",
            table: "MappingDecisions",
            columns: new[] { "WorkspaceId", "ProjectId", "State" });

        migrationBuilder.CreateIndex(
            name: "IX_MappingRevisions_WorkspaceId_DecisionId_Number",
            table: "MappingRevisions",
            columns: new[] { "WorkspaceId", "DecisionId", "Number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SyncBindings_WorkspaceId_ProjectId_AdEnvironmentId_EntraEnvironmentId",
            table: "SyncBindings",
            columns: new[] { "WorkspaceId", "ProjectId", "AdEnvironmentId", "EntraEnvironmentId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SyncBindings_WorkspaceId_ProjectId_EntraEnvironmentId",
            table: "SyncBindings",
            columns: new[] { "WorkspaceId", "ProjectId", "EntraEnvironmentId" });
        foreach (var table in new[] { "SyncBindings", "MappingDecisions", "MappingRevisions" })
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
        migrationBuilder.Sql("DROP SECURITY POLICY Security.MappingRevisionsBoundary; DROP SECURITY POLICY Security.MappingDecisionsBoundary; DROP SECURITY POLICY Security.SyncBindingsBoundary;");
        migrationBuilder.DropTable(
            name: "MappingRevisions");

        migrationBuilder.DropTable(
            name: "SyncBindings");

        migrationBuilder.DropTable(
            name: "MappingDecisions");
    }
}
