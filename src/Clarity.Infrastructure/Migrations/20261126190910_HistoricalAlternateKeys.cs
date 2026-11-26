using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126190910_HistoricalAlternateKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ExternalKeys_WorkspaceId_EnvironmentId_Namespace_Type_ExternalId",
            table: "ExternalKeys");

        migrationBuilder.AddColumn<string>(
            name: "AlternativeKeysJson",
            table: "ObjectObservations",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsPrimary",
            table: "ExternalKeys",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_ObjectObservations_WorkspaceId_ObjectId_Id",
            table: "ObjectObservations",
            columns: new[] { "WorkspaceId", "ObjectId", "Id" });

        migrationBuilder.CreateTable(
            name: "AlternateKeyPeriods",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Namespace = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                OriginalValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                NormalizedValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, collation: "Latin1_General_100_BIN2"),
                ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ValidUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AlternateKeyPeriods", x => new { x.WorkspaceId, x.Id });
                table.ForeignKey(
                    name: "FK_AlternateKeyPeriods_ClientWorkspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "ClientWorkspaces",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_AlternateKeyPeriods_InventoryObjects_WorkspaceId_EnvironmentId_ObjectId",
                    columns: x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId },
                    principalTable: "InventoryObjects",
                    principalColumns: new[] { "WorkspaceId", "EnvironmentId", "Id" });
                table.ForeignKey(
                    name: "FK_AlternateKeyPeriods_ObjectObservations_WorkspaceId_ObjectId_ObservationId",
                    columns: x => new { x.WorkspaceId, x.ObjectId, x.ObservationId },
                    principalTable: "ObjectObservations",
                    principalColumns: new[] { "WorkspaceId", "ObjectId", "Id" });
            });

        migrationBuilder.CreateIndex(
            name: "IX_ExternalKeys_WorkspaceId_EnvironmentId_Namespace_Type_ExternalId",
            table: "ExternalKeys",
            columns: new[] { "WorkspaceId", "EnvironmentId", "Namespace", "Type", "ExternalId" },
            unique: true,
            filter: "[IsPrimary] = 1");

        migrationBuilder.CreateIndex(
            name: "IX_AlternateKeyPeriods_WorkspaceId_EnvironmentId_Namespace_Type_NormalizedValue",
            table: "AlternateKeyPeriods",
            columns: new[] { "WorkspaceId", "EnvironmentId", "Namespace", "Type", "NormalizedValue" });

        migrationBuilder.CreateIndex(
            name: "IX_AlternateKeyPeriods_WorkspaceId_EnvironmentId_ObjectId",
            table: "AlternateKeyPeriods",
            columns: new[] { "WorkspaceId", "EnvironmentId", "ObjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_AlternateKeyPeriods_WorkspaceId_ObjectId_ObservationId",
            table: "AlternateKeyPeriods",
            columns: new[] { "WorkspaceId", "ObjectId", "ObservationId" });
        migrationBuilder.Sql("""
            CREATE SECURITY POLICY Security.AlternateKeyPeriodsBoundary
            ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.AlternateKeyPeriods,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.AlternateKeyPeriods AFTER INSERT,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.AlternateKeyPeriods AFTER UPDATE
            WITH (STATE = ON, SCHEMABINDING = ON);
            """);
        migrationBuilder.Sql("""
            DECLARE @workspace uniqueidentifier, @scope varchar(38);
            EXEC sys.sp_set_session_context N'clarity.platformAdmin', 1;
            DECLARE workspaces CURSOR LOCAL STATIC FOR SELECT Id FROM dbo.ClientWorkspaces;
            OPEN workspaces;
            EXEC sys.sp_set_session_context N'clarity.platformAdmin', NULL;
            FETCH NEXT FROM workspaces INTO @workspace;
            BEGIN TRY
              WHILE @@FETCH_STATUS = 0
              BEGIN
                SET @scope = '|' + CONVERT(varchar(36), @workspace) + '|';
                EXEC sys.sp_set_session_context N'clarity.workspaces', @scope;
                -- Lot 2 did not record which of multiple keys was the principal.
                -- Classify only the shipped producer shapes and fail closed for custom data.
                IF EXISTS (
                    SELECT ObjectId FROM dbo.ExternalKeys WHERE WorkspaceId = @workspace GROUP BY ObjectId
                    HAVING COUNT(*) > 1 AND (SUM(CASE WHEN Namespace IN ('ad.objectGUID','microsoft.graph') THEN 1 ELSE 0 END) <> 1
                      OR SUM(CASE WHEN Namespace NOT IN ('ad.objectGUID','microsoft.graph','ad.objectSid','microsoft.graph.skuId') THEN 1 ELSE 0 END) > 0))
                  THROW 51003, 'Unclassified legacy external keys: verify the original packages before migrating.', 1;
                UPDATE k SET IsPrimary = 0 FROM dbo.ExternalKeys k
                  WHERE k.WorkspaceId = @workspace AND k.Namespace IN ('ad.objectSid','microsoft.graph.skuId')
                  AND EXISTS (SELECT 1 FROM dbo.ExternalKeys p WHERE p.WorkspaceId = k.WorkspaceId
                    AND p.ObjectId = k.ObjectId AND p.Namespace IN ('ad.objectGUID','microsoft.graph'));
                FETCH NEXT FROM workspaces INTO @workspace;
              END
              EXEC sys.sp_set_session_context N'clarity.workspaces', NULL;
            END TRY
            BEGIN CATCH
              EXEC sys.sp_set_session_context N'clarity.workspaces', NULL;
              THROW;
            END CATCH;
            CLOSE workspaces;
            DEALLOCATE workspaces;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY Security.AlternateKeyPeriodsBoundary;");
        migrationBuilder.DropTable(
            name: "AlternateKeyPeriods");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_ObjectObservations_WorkspaceId_ObjectId_Id",
            table: "ObjectObservations");

        migrationBuilder.DropIndex(
            name: "IX_ExternalKeys_WorkspaceId_EnvironmentId_Namespace_Type_ExternalId",
            table: "ExternalKeys");

        migrationBuilder.DropColumn(
            name: "AlternativeKeysJson",
            table: "ObjectObservations");

        migrationBuilder.DropColumn(
            name: "IsPrimary",
            table: "ExternalKeys");

        migrationBuilder.CreateIndex(
            name: "IX_ExternalKeys_WorkspaceId_EnvironmentId_Namespace_Type_ExternalId",
            table: "ExternalKeys",
            columns: new[] { "WorkspaceId", "EnvironmentId", "Namespace", "Type", "ExternalId" },
            unique: true);
    }
}
