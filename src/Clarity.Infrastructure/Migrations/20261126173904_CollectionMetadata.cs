using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126173904_CollectionMetadata : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "AK_Environments_WorkspaceId_ProjectId_Id",
            table: "Environments",
            columns: new[] { "WorkspaceId", "ProjectId", "Id" });

        migrationBuilder.CreateTable(
            name: "CollectionRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScopeJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                ScopeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                SectionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ProducerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ScriptVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ActorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CollectionRequests", x => new { x.WorkspaceId, x.Id });
                table.UniqueConstraint("AK_CollectionRequests_WorkspaceId_ProjectId_EnvironmentId_Id", x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId, x.Id });
                table.ForeignKey(
                    name: "FK_CollectionRequests_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_CollectionRequests_Environments_WorkspaceId_ProjectId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "Connections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProducerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ConfigurationReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                TestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                AccessResultsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Connections", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_Connections_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "CollectionRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ExternalRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Channel = table.Column<int>(type: "int", nullable: false),
                ProducerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ProducerVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ScopeJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                ScopeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ActorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CollectionRuns", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_CollectionRuns_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_CollectionRuns_CollectionRequests_WorkspaceId_ProjectId_EnvironmentId_RequestId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId, x.RequestId },
                    principalTable: "CollectionRequests",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_CollectionRuns_Connections_WorkspaceId_ConnectionId",
                    columns: x => new { x.WorkspaceId, x.ConnectionId },
                    principalTable: "Connections",
                    principalColumns: new[] { "WorkspaceId", "Id" });
                table.ForeignKey(
                    name: "FK_CollectionRuns_Environments_WorkspaceId_ProjectId_EnvironmentId",
                    columns: x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId },
                    principalTable: "Environments",
                    principalColumns: new[] { "WorkspaceId", "ProjectId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "RawArtifacts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BlobName = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CompressedBytes = table.Column<long>(type: "bigint", nullable: false),
                UncompressedBytes = table.Column<long>(type: "bigint", nullable: false),
                UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RawArtifacts", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_RawArtifacts_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_RawArtifacts_CollectionRuns_WorkspaceId_RunId",
                    columns: x => new { x.WorkspaceId, x.RunId },
                    principalTable: "CollectionRuns",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "SectionRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SectionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CapabilityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ScopeJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                ScopeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                Coverage = table.Column<int>(type: "int", nullable: false),
                ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ObjectCount = table.Column<long>(type: "bigint", nullable: true),
                RelationshipCount = table.Column<long>(type: "bigint", nullable: true),
                PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SectionRuns", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_SectionRuns_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_SectionRuns_CollectionRuns_WorkspaceId_RunId",
                    columns: x => new { x.WorkspaceId, x.RunId },
                    principalTable: "CollectionRuns",
                    principalColumns: new[] { "WorkspaceId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_CollectionRequests_WorkspaceId_ProjectId_CreatedAtUtc",
            table: "CollectionRequests",
            columns: new[] { "WorkspaceId", "ProjectId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_CollectionRuns_WorkspaceId_ConnectionId",
            table: "CollectionRuns",
            columns: new[] { "WorkspaceId", "ConnectionId" });

        migrationBuilder.CreateIndex(
            name: "IX_CollectionRuns_WorkspaceId_ProjectId_EnvironmentId_ExternalRunId",
            table: "CollectionRuns",
            columns: new[] { "WorkspaceId", "ProjectId", "EnvironmentId", "ExternalRunId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CollectionRuns_WorkspaceId_ProjectId_EnvironmentId_RequestId",
            table: "CollectionRuns",
            columns: new[] { "WorkspaceId", "ProjectId", "EnvironmentId", "RequestId" });

        migrationBuilder.CreateIndex(
            name: "IX_CollectionRuns_WorkspaceId_ProjectId_EnvironmentId_ScopeHash_ContentHash",
            table: "CollectionRuns",
            columns: new[] { "WorkspaceId", "ProjectId", "EnvironmentId", "ScopeHash", "ContentHash" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CollectionRuns_WorkspaceId_ProjectId_UploadedAtUtc",
            table: "CollectionRuns",
            columns: new[] { "WorkspaceId", "ProjectId", "UploadedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Connections_WorkspaceId_TenantId_ProducerId",
            table: "Connections",
            columns: new[] { "WorkspaceId", "TenantId", "ProducerId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RawArtifacts_WorkspaceId_RunId",
            table: "RawArtifacts",
            columns: new[] { "WorkspaceId", "RunId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SectionRuns_WorkspaceId_CapabilityId_ScopeHash_PublishedAtUtc",
            table: "SectionRuns",
            columns: new[] { "WorkspaceId", "CapabilityId", "ScopeHash", "PublishedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_SectionRuns_WorkspaceId_RunId_SectionId",
            table: "SectionRuns",
            columns: new[] { "WorkspaceId", "RunId", "SectionId" },
            unique: true);

        foreach (var table in new[] { "Connections", "CollectionRequests", "CollectionRuns", "SectionRuns", "RawArtifacts" })
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
        foreach (var table in new[] { "Connections", "CollectionRequests", "CollectionRuns", "SectionRuns", "RawArtifacts" })
        {
            migrationBuilder.Sql($"DROP SECURITY POLICY Security.{table}Boundary;");
        }

        migrationBuilder.DropTable(
            name: "RawArtifacts");

        migrationBuilder.DropTable(
            name: "SectionRuns");

        migrationBuilder.DropTable(
            name: "CollectionRuns");

        migrationBuilder.DropTable(
            name: "CollectionRequests");

        migrationBuilder.DropTable(
            name: "Connections");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_Environments_WorkspaceId_ProjectId_Id",
            table: "Environments");
    }
}
