using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261127114305_RetentionAndDeletion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PurgedAtUtc",
            table: "RawArtifacts",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ArchivedAtUtc",
            table: "Projects",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "FilesPurgedAtUtc",
            table: "ExportJobs",
            type: "datetimeoffset",
            nullable: true);
        // Projects archived before this migration get the date of their latest "ProjectArchived" audit event, or the migration time when
        // the event is gone, so the archived-project retention clock starts no earlier than the recorded archival.
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
                UPDATE p SET ArchivedAtUtc = COALESCE((SELECT MAX(a.OccurredAtUtc) FROM dbo.AuditEvents a
                    WHERE a.WorkspaceId = p.WorkspaceId AND a.ResourceId = p.Id AND a.Action = 'ProjectArchived'), SYSDATETIMEOFFSET())
                  FROM dbo.Projects p WHERE p.WorkspaceId = @workspace AND p.Status = 1 AND p.ArchivedAtUtc IS NULL;
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
        // The application may purge audit events past their retention, but SQL itself refuses to delete any event younger than 29 days
        // (the configurable minimum is 30, leaving a day of clock tolerance), so the application identity cannot erase recent activity.
        migrationBuilder.Sql("""
            CREATE FUNCTION Security.AuditRetention(@OccurredAtUtc datetimeoffset)
            RETURNS TABLE WITH SCHEMABINDING AS RETURN
            SELECT 1 AS Allowed WHERE @OccurredAtUtc < DATEADD(day, -29, SYSDATETIMEOFFSET());
            """);
        migrationBuilder.Sql("""
            ALTER SECURITY POLICY Security.ClientBoundary
            ADD BLOCK PREDICATE Security.AuditRetention(OccurredAtUtc) ON dbo.AuditEvents BEFORE DELETE;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER SECURITY POLICY Security.ClientBoundary DROP BLOCK PREDICATE ON dbo.AuditEvents BEFORE DELETE;
            DROP FUNCTION Security.AuditRetention;
            """);

        migrationBuilder.DropColumn(
            name: "PurgedAtUtc",
            table: "RawArtifacts");

        migrationBuilder.DropColumn(
            name: "ArchivedAtUtc",
            table: "Projects");

        migrationBuilder.DropColumn(
            name: "FilesPurgedAtUtc",
            table: "ExportJobs");
    }
}
