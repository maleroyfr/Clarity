using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126163044_InitialFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ClientWorkspaces",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClientWorkspaces", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PlatformAdmins",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlatformAdmins", x => new { x.TenantId, x.ObjectId });
            });

        migrationBuilder.CreateTable(
            name: "AuditEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditEvents", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_AuditEvents_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "Members",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Role = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Members", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_Members_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "Projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Scenario = table.Column<int>(type: "int", nullable: false),
                Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                ScopingParameters = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Projects", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_Projects_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "Environments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Role = table.Column<int>(type: "int", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                ExternalId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                DeclaredScope = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                TargetState = table.Column<int>(type: "int", nullable: false),
                Hypotheses = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Environments", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_Environments_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Environments_Projects_WorkspaceId_ProjectId",
                    columns: x => new { x.WorkspaceId, x.ProjectId },
                    principalTable: "Projects",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_WorkspaceId_OccurredAtUtc",
            table: "AuditEvents",
            columns: new[] { "WorkspaceId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ClientWorkspaces_Status_Name",
            table: "ClientWorkspaces",
            columns: new[] { "Status", "Name" });

        migrationBuilder.CreateIndex(
            name: "IX_Environments_WorkspaceId_ProjectId_Role",
            table: "Environments",
            columns: new[] { "WorkspaceId", "ProjectId", "Role" });

        migrationBuilder.CreateIndex(
            name: "IX_Members_TenantId_ObjectId",
            table: "Members",
            columns: new[] { "TenantId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_Members_WorkspaceId_TenantId_ObjectId",
            table: "Members",
            columns: new[] { "WorkspaceId", "TenantId", "ObjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Projects_WorkspaceId_Status_Name",
            table: "Projects",
            columns: new[] { "WorkspaceId", "Status", "Name" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AuditEvents");

        migrationBuilder.DropTable(
            name: "Environments");

        migrationBuilder.DropTable(
            name: "Members");

        migrationBuilder.DropTable(
            name: "PlatformAdmins");

        migrationBuilder.DropTable(
            name: "Projects");

        migrationBuilder.DropTable(
            name: "ClientWorkspaces");
    }
}
