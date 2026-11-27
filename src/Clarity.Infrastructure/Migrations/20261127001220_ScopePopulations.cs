using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261127001220_ScopePopulations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Populations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Criterion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                RootObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ObjectType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                Identifier = table.Column<int>(type: "int", nullable: true),
                MemberCount = table.Column<int>(type: "int", nullable: false),
                EvaluatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                AuthorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AuthorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Populations", x => new { x.WorkspaceId, x.Id });
                table.UniqueConstraint("AK_Populations_WorkspaceId_EnvironmentId_Id", x => new { x.WorkspaceId, x.EnvironmentId, x.Id });
                table.ForeignKey(
                    name: "FK_Populations_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Populations_Environments_WorkspaceId_ProjectId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
                table.ForeignKey(
                    name: "FK_Populations_InventoryObjects_WorkspaceId_EnvironmentId_RootObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.RootObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "PopulationEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PopulationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Line = table.Column<int>(type: "int", nullable: false),
                Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Resolution = table.Column<int>(type: "int", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CandidateCount = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PopulationEntries", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_PopulationEntries_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_PopulationEntries_InventoryObjects_WorkspaceId_ObjectId",
                    columns: x => new { x.WorkspaceId, x.ObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_PopulationEntries_Populations_WorkspaceId_PopulationId",
                    columns: x => new { x.WorkspaceId, x.PopulationId },
                    principalTable: "Populations",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "PopulationMembers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PopulationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PopulationMembers", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_PopulationMembers_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_PopulationMembers_InventoryObjects_WorkspaceId_EnvironmentId_ObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_PopulationMembers_Populations_WorkspaceId_EnvironmentId_PopulationId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.PopulationId },
                    principalTable: "Populations",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "ScopeDecisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Value = table.Column<int>(type: "int", nullable: false),
                Wave = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                PopulationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScopeDecisions", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_ScopeDecisions_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ScopeDecisions_Environments_WorkspaceId_ProjectId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
                table.ForeignKey(
                    name: "FK_ScopeDecisions_InventoryObjects_WorkspaceId_EnvironmentId_ObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_ScopeDecisions_Populations_WorkspaceId_PopulationId",
                    columns: x => new { x.WorkspaceId, x.PopulationId },
                    principalTable: "Populations",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "ScopeDecisionRevisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Number = table.Column<long>(type: "bigint", nullable: false),
                Value = table.Column<int>(type: "int", nullable: false),
                Wave = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                PopulationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                EvidenceObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                ActorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScopeDecisionRevisions", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_ScopeDecisionRevisions_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ScopeDecisionRevisions_ObjectObservations_WorkspaceId_EvidenceObservationId",
                    columns: x => new { x.WorkspaceId, x.EvidenceObservationId },
                    principalTable: "ObjectObservations",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_ScopeDecisionRevisions_Populations_WorkspaceId_PopulationId",
                    columns: x => new { x.WorkspaceId, x.PopulationId },
                    principalTable: "Populations",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_ScopeDecisionRevisions_ScopeDecisions_WorkspaceId_DecisionId",
                    columns: x => new { x.WorkspaceId, x.DecisionId },
                    principalTable: "ScopeDecisions",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_PopulationEntries_WorkspaceId_ObjectId",
            table: "PopulationEntries",
            columns: new[] { "WorkspaceId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_PopulationEntries_WorkspaceId_PopulationId_Line",
            table: "PopulationEntries",
            columns: new[] { "WorkspaceId", "PopulationId", "Line" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PopulationMembers_WorkspaceId_EnvironmentId_ObjectId",
            table: "PopulationMembers",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_PopulationMembers_WorkspaceId_EnvironmentId_PopulationId",
            table: "PopulationMembers",
            columns: new[] { "WorkspaceId", "EnvironmentId", "PopulationId" });

        migrationBuilder.CreateIndex(
            name: "IX_PopulationMembers_WorkspaceId_ObjectId",
            table: "PopulationMembers",
            columns: new[] { "WorkspaceId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_PopulationMembers_WorkspaceId_PopulationId_ObjectId",
            table: "PopulationMembers",
            columns: new[] { "WorkspaceId", "PopulationId", "ObjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Populations_WorkspaceId_EnvironmentId_RootObjectId",
            table: "Populations",
            columns: new[] { "WorkspaceId", "EnvironmentId", "RootObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_Populations_WorkspaceId_ProjectId_CreatedAtUtc",
            table: "Populations",
            columns: new[] { "WorkspaceId", "ProjectId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Populations_WorkspaceId_ProjectId_EnvironmentId",
            table: "Populations",
            columns: new[] { "WorkspaceId", "ProjectId", "EnvironmentId" });

        migrationBuilder.CreateIndex(
            name: "IX_ScopeDecisionRevisions_WorkspaceId_DecisionId_Number",
            table: "ScopeDecisionRevisions",
            columns: new[] { "WorkspaceId", "DecisionId", "Number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ScopeDecisionRevisions_WorkspaceId_EvidenceObservationId",
            table: "ScopeDecisionRevisions",
            columns: new[] { "WorkspaceId", "EvidenceObservationId" });

        migrationBuilder.CreateIndex(
            name: "IX_ScopeDecisionRevisions_WorkspaceId_OperationId",
            table: "ScopeDecisionRevisions",
            columns: new[] { "WorkspaceId", "OperationId" });

        migrationBuilder.CreateIndex(
            name: "IX_ScopeDecisionRevisions_WorkspaceId_PopulationId",
            table: "ScopeDecisionRevisions",
            columns: new[] { "WorkspaceId", "PopulationId" });

        migrationBuilder.CreateIndex(
            name: "IX_ScopeDecisions_WorkspaceId_EnvironmentId_ObjectId",
            table: "ScopeDecisions",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_ScopeDecisions_WorkspaceId_PopulationId",
            table: "ScopeDecisions",
            columns: new[] { "WorkspaceId", "PopulationId" });

        migrationBuilder.CreateIndex(
            name: "IX_ScopeDecisions_WorkspaceId_ProjectId_EnvironmentId",
            table: "ScopeDecisions",
            columns: new[] { "WorkspaceId", "ProjectId", "EnvironmentId" });

        migrationBuilder.CreateIndex(
            name: "IX_ScopeDecisions_WorkspaceId_ProjectId_ObjectId",
            table: "ScopeDecisions",
            columns: new[] { "WorkspaceId", "ProjectId", "ObjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ScopeDecisions_WorkspaceId_ProjectId_Value_Wave",
            table: "ScopeDecisions",
            columns: new[] { "WorkspaceId", "ProjectId", "Value", "Wave" });

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

    private static readonly string[] Tables = ["Populations", "PopulationEntries", "PopulationMembers", "ScopeDecisions", "ScopeDecisionRevisions"];

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables) { migrationBuilder.Sql($"DROP SECURITY POLICY Security.{table}Boundary;"); }

        migrationBuilder.DropTable(
            name: "PopulationEntries");

        migrationBuilder.DropTable(
            name: "PopulationMembers");

        migrationBuilder.DropTable(
            name: "ScopeDecisionRevisions");

        migrationBuilder.DropTable(
            name: "ScopeDecisions");

        migrationBuilder.DropTable(
            name: "Populations");
    }
}
