using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261127025715_ExportJobs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExportJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Side = table.Column<int>(type: "int", nullable: false),
                FiltersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                SelectionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                SelectionHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                AsOfUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                AuthorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AuthorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                GeneratedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ObjectCount = table.Column<int>(type: "int", nullable: true),
                ExceptionCount = table.Column<int>(type: "int", nullable: true),
                MappingCount = table.Column<int>(type: "int", nullable: true),
                CoverageGaps = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExportJobs", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_ExportJobs_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ExportJobs_CollectionJobs_WorkspaceId_Id",
                    columns: x => new { x.WorkspaceId, x.Id },
                    principalTable: "CollectionJobs",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_ExportJobs_Projects_WorkspaceId_ProjectId",
                    columns: x => new { x.WorkspaceId, x.ProjectId },
                    principalTable: "Projects",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "ExportArtifacts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ExportJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                ContentType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                BlobName = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                Bytes = table.Column<long>(type: "bigint", nullable: false),
                RowCount = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExportArtifacts", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_ExportArtifacts_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ExportArtifacts_ExportJobs_WorkspaceId_ExportJobId",
                    columns: x => new { x.WorkspaceId, x.ExportJobId },
                    principalTable: "ExportJobs",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_ExportArtifacts_WorkspaceId_ExportJobId_Kind",
            table: "ExportArtifacts",
            columns: new[] { "WorkspaceId", "ExportJobId", "Kind" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ExportJobs_WorkspaceId_ProjectId_Side_AsOfUtc",
            table: "ExportJobs",
            columns: new[] { "WorkspaceId", "ProjectId", "Side", "AsOfUtc" });

        migrationBuilder.Sql("""
            CREATE SECURITY POLICY Security.ExportJobsBoundary
            ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.ExportJobs,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.ExportJobs AFTER INSERT,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.ExportJobs AFTER UPDATE
            WITH (STATE = ON, SCHEMABINDING = ON);
            """);
        migrationBuilder.Sql("""
            CREATE SECURITY POLICY Security.ExportArtifactsBoundary
            ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.ExportArtifacts,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.ExportArtifacts AFTER INSERT,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.ExportArtifacts AFTER UPDATE
            WITH (STATE = ON, SCHEMABINDING = ON);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY Security.ExportArtifactsBoundary;");
        migrationBuilder.Sql("DROP SECURITY POLICY Security.ExportJobsBoundary;");
        migrationBuilder.DropTable(
            name: "ExportArtifacts");

        migrationBuilder.DropTable(
            name: "ExportJobs");
    }
}
