using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261127004718_ScopeDependencies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DependencyAnalyses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Wave = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ComputedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                MigratingPrincipals = table.Column<int>(type: "int", nullable: false),
                OtherWavePrincipals = table.Column<int>(type: "int", nullable: false),
                ArbitratePrincipals = table.Column<int>(type: "int", nullable: false),
                RemainingPrincipals = table.Column<int>(type: "int", nullable: false),
                Resources = table.Column<int>(type: "int", nullable: false),
                SharedResources = table.Column<int>(type: "int", nullable: false),
                UnresolvedLinks = table.Column<int>(type: "int", nullable: false),
                KnownVolumeBytes = table.Column<long>(type: "bigint", nullable: false),
                SharedVolumeBytes = table.Column<long>(type: "bigint", nullable: false),
                UnknownVolumeResources = table.Column<int>(type: "int", nullable: false),
                CoverageJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DependencyAnalyses", x => new { x.WorkspaceId, x.Id });
                table.UniqueConstraint("AK_DependencyAnalyses_WorkspaceId_EnvironmentId_Id", x => new { x.WorkspaceId, x.EnvironmentId, x.Id });
                table.ForeignKey(
                    name: "FK_DependencyAnalyses_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_DependencyAnalyses_Environments_WorkspaceId_ProjectId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "DependencyLinks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ResourceObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                RelatedObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Side = table.Column<int>(type: "int", nullable: true),
                Route = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                ViaObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DependencyLinks", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_DependencyLinks_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_DependencyLinks_DependencyAnalyses_WorkspaceId_AnalysisId",
                    columns: x => new { x.WorkspaceId, x.AnalysisId },
                    principalTable: "DependencyAnalyses",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_DependencyLinks_InventoryObjects_WorkspaceId_RelatedObjectId",
                    columns: x => new { x.WorkspaceId, x.RelatedObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "DependencyResources",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Family = table.Column<int>(type: "int", nullable: false),
                Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                Classification = table.Column<int>(type: "int", nullable: false),
                MigratingCount = table.Column<int>(type: "int", nullable: false),
                OtherWaveCount = table.Column<int>(type: "int", nullable: false),
                ArbitrateCount = table.Column<int>(type: "int", nullable: false),
                RemainingCount = table.Column<int>(type: "int", nullable: false),
                RouteCount = table.Column<int>(type: "int", nullable: false),
                VolumeApplies = table.Column<bool>(type: "bit", nullable: false),
                VolumeBytes = table.Column<long>(type: "bigint", nullable: true),
                UnresolvedLinks = table.Column<int>(type: "int", nullable: false),
                MembersNotCollected = table.Column<bool>(type: "bit", nullable: false),
                AllUsers = table.Column<bool>(type: "bit", nullable: false),
                Decision = table.Column<int>(type: "int", nullable: true),
                DecisionWave = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DependencyResources", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_DependencyResources_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_DependencyResources_DependencyAnalyses_WorkspaceId_EnvironmentId_AnalysisId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.AnalysisId },
                    principalTable: "DependencyAnalyses",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_DependencyResources_InventoryObjects_WorkspaceId_EnvironmentId_ObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_DependencyAnalyses_WorkspaceId_OperationId",
            table: "DependencyAnalyses",
            columns: new[] { "WorkspaceId", "OperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DependencyAnalyses_WorkspaceId_ProjectId_EnvironmentId_Wave",
            table: "DependencyAnalyses",
            columns: new[] { "WorkspaceId", "ProjectId", "EnvironmentId", "Wave" });

        migrationBuilder.CreateIndex(
            name: "IX_DependencyLinks_WorkspaceId_AnalysisId_ResourceObjectId_Kind",
            table: "DependencyLinks",
            columns: new[] { "WorkspaceId", "AnalysisId", "ResourceObjectId", "Kind" });

        migrationBuilder.CreateIndex(
            name: "IX_DependencyLinks_WorkspaceId_RelatedObjectId",
            table: "DependencyLinks",
            columns: new[] { "WorkspaceId", "RelatedObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_DependencyResources_WorkspaceId_AnalysisId_Classification_Family",
            table: "DependencyResources",
            columns: new[] { "WorkspaceId", "AnalysisId", "Classification", "Family" });

        migrationBuilder.CreateIndex(
            name: "IX_DependencyResources_WorkspaceId_AnalysisId_ObjectId",
            table: "DependencyResources",
            columns: new[] { "WorkspaceId", "AnalysisId", "ObjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DependencyResources_WorkspaceId_EnvironmentId_AnalysisId",
            table: "DependencyResources",
            columns: new[] { "WorkspaceId", "EnvironmentId", "AnalysisId" });

        migrationBuilder.CreateIndex(
            name: "IX_DependencyResources_WorkspaceId_EnvironmentId_ObjectId",
            table: "DependencyResources",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ObjectId" });

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

    private static readonly string[] Tables = ["DependencyAnalyses", "DependencyResources", "DependencyLinks"];

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables) { migrationBuilder.Sql($"DROP SECURITY POLICY Security.{table}Boundary;"); }

        migrationBuilder.DropTable(
            name: "DependencyLinks");

        migrationBuilder.DropTable(
            name: "DependencyResources");

        migrationBuilder.DropTable(
            name: "DependencyAnalyses");
    }
}
