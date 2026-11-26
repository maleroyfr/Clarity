using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126164114_WorkspaceRowLevelSecurity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE SCHEMA Security");
        migrationBuilder.Sql("""
            CREATE FUNCTION Security.WorkspaceAccess(@WorkspaceId uniqueidentifier)
            RETURNS TABLE WITH SCHEMABINDING AS RETURN
            SELECT 1 AS Allowed
            WHERE CHARINDEX('|' + CONVERT(varchar(36), @WorkspaceId) + '|',
                CONVERT(varchar(8000), SESSION_CONTEXT(N'clarity.workspaces'))) > 0;
            """);
        migrationBuilder.Sql("""
            CREATE FUNCTION Security.MetadataAccess(@WorkspaceId uniqueidentifier)
            RETURNS TABLE WITH SCHEMABINDING AS RETURN
            SELECT 1 AS Allowed
            WHERE CONVERT(bit, SESSION_CONTEXT(N'clarity.platformAdmin')) = 1
               OR CHARINDEX('|' + CONVERT(varchar(36), @WorkspaceId) + '|',
                    CONVERT(varchar(8000), SESSION_CONTEXT(N'clarity.workspaces'))) > 0;
            """);
        migrationBuilder.Sql("""
            CREATE FUNCTION Security.MemberRead(@WorkspaceId uniqueidentifier, @TenantId uniqueidentifier, @ObjectId uniqueidentifier)
            RETURNS TABLE WITH SCHEMABINDING AS RETURN
            SELECT 1 AS Allowed
            WHERE CONVERT(bit, SESSION_CONTEXT(N'clarity.platformAdmin')) = 1
               OR CHARINDEX('|' + CONVERT(varchar(36), @WorkspaceId) + '|',
                    CONVERT(varchar(8000), SESSION_CONTEXT(N'clarity.workspaces'))) > 0
               OR (@TenantId = CONVERT(uniqueidentifier, SESSION_CONTEXT(N'clarity.tenant'))
                   AND @ObjectId = CONVERT(uniqueidentifier, SESSION_CONTEXT(N'clarity.subject')));
            """);
        migrationBuilder.Sql("""
            CREATE SECURITY POLICY Security.ClientBoundary
            ADD FILTER PREDICATE Security.MetadataAccess(Id) ON dbo.ClientWorkspaces,
            ADD BLOCK PREDICATE Security.MetadataAccess(Id) ON dbo.ClientWorkspaces AFTER INSERT,
            ADD BLOCK PREDICATE Security.MetadataAccess(Id) ON dbo.ClientWorkspaces AFTER UPDATE,
            ADD FILTER PREDICATE Security.MemberRead(WorkspaceId, TenantId, ObjectId) ON dbo.Members,
            ADD BLOCK PREDICATE Security.MetadataAccess(WorkspaceId) ON dbo.Members AFTER INSERT,
            ADD BLOCK PREDICATE Security.MetadataAccess(WorkspaceId) ON dbo.Members AFTER UPDATE,
            ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.Projects,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.Projects AFTER INSERT,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.Projects AFTER UPDATE,
            ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.Environments,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.Environments AFTER INSERT,
            ADD BLOCK PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.Environments AFTER UPDATE,
            ADD FILTER PREDICATE Security.WorkspaceAccess(WorkspaceId) ON dbo.AuditEvents,
            ADD BLOCK PREDICATE Security.MetadataAccess(WorkspaceId) ON dbo.AuditEvents AFTER INSERT,
            ADD BLOCK PREDICATE Security.MetadataAccess(WorkspaceId) ON dbo.AuditEvents AFTER UPDATE
            WITH (STATE = ON, SCHEMABINDING = ON);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP SECURITY POLICY Security.ClientBoundary;
            DROP FUNCTION Security.MemberRead;
            DROP FUNCTION Security.MetadataAccess;
            DROP FUNCTION Security.WorkspaceAccess;
            DROP SCHEMA Security;
            """);
    }
}
