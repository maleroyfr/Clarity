using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261127021746_DecisionHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BasisJson",
            table: "ScopeDecisions",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BasisJson",
            table: "ScopeDecisionRevisions",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BasisJson",
            table: "Assessments",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ReviewFlags",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                DecisionRevision = table.Column<long>(type: "bigint", nullable: false),
                Fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                FlaggedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReviewFlags", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_ReviewFlags_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ReviewFlags_Environments_WorkspaceId_ProjectId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
                table.ForeignKey(
                    name: "FK_ReviewFlags_InventoryObjects_WorkspaceId_EnvironmentId_ObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_ReviewFlags_WorkspaceId_EnvironmentId_ObjectId",
            table: "ReviewFlags",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_ReviewFlags_WorkspaceId_ObjectId",
            table: "ReviewFlags",
            columns: new[] { "WorkspaceId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_ReviewFlags_WorkspaceId_ProjectId_EnvironmentId",
            table: "ReviewFlags",
            columns: new[] { "WorkspaceId", "ProjectId", "EnvironmentId" });

        migrationBuilder.CreateIndex(
            name: "IX_ReviewFlags_WorkspaceId_ProjectId_Kind_ObjectId",
            table: "ReviewFlags",
            columns: new[] { "WorkspaceId", "ProjectId", "Kind", "ObjectId" },
            unique: true);

        migrationBuilder.Sql("""
            CREATE SECURITY POLICY Security.ReviewFlagsBoundary
            ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.ReviewFlags,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.ReviewFlags AFTER INSERT,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.ReviewFlags AFTER UPDATE
            WITH (STATE = ON, SCHEMABINDING = ON);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY Security.ReviewFlagsBoundary;");

        migrationBuilder.DropTable(
            name: "ReviewFlags");

        migrationBuilder.DropColumn(
            name: "BasisJson",
            table: "ScopeDecisions");

        migrationBuilder.DropColumn(
            name: "BasisJson",
            table: "ScopeDecisionRevisions");

        migrationBuilder.DropColumn(
            name: "BasisJson",
            table: "Assessments");
    }
}
