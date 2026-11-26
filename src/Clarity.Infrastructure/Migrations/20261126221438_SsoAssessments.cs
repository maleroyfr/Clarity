using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126221438_SsoAssessments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Assessments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                Value = table.Column<int>(type: "int", nullable: false),
                Protocol = table.Column<int>(type: "int", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                EvidenceReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                EvidenceObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                MetadataFingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                AuthorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AuthorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Assessments", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_Assessments_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Assessments_Environments_WorkspaceId_ProjectId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
                table.ForeignKey(
                    name: "FK_Assessments_InventoryObjects_WorkspaceId_EnvironmentId_ObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_Assessments_ObjectObservations_WorkspaceId_ObjectId_EvidenceObservationId",
                    columns: x => new { x.WorkspaceId, x.ObjectId, x.EvidenceObservationId },
                    principalTable: "ObjectObservations",
                    principalColumns: new[] { "WorkspaceId", "ObjectId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_Assessments_WorkspaceId_EnvironmentId_ObjectId",
            table: "Assessments",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_Assessments_WorkspaceId_ObjectId_EvidenceObservationId",
            table: "Assessments",
            columns: new[] { "WorkspaceId", "ObjectId", "EvidenceObservationId" });

        migrationBuilder.CreateIndex(
            name: "IX_Assessments_WorkspaceId_ProjectId_EnvironmentId",
            table: "Assessments",
            columns: new[] { "WorkspaceId", "ProjectId", "EnvironmentId" });

        migrationBuilder.CreateIndex(
            name: "IX_Assessments_WorkspaceId_ProjectId_ObjectId_Kind_Revision",
            table: "Assessments",
            columns: new[] { "WorkspaceId", "ProjectId", "ObjectId", "Kind", "Revision" },
            unique: true);
        migrationBuilder.Sql("""
            CREATE SECURITY POLICY Security.AssessmentsBoundary
            ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.Assessments,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.Assessments AFTER INSERT,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.Assessments AFTER UPDATE
            WITH (STATE = ON, SCHEMABINDING = ON);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY Security.AssessmentsBoundary;");
        migrationBuilder.DropTable(
            name: "Assessments");
    }
}
