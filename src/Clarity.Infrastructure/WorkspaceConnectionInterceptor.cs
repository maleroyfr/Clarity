using System.Data;
using System.Data.Common;
using Clarity.Core;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clarity.Infrastructure;

public sealed class WorkspaceConnectionInterceptor(IWorkspaceContext scope) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var workspaces = "|" + string.Join('|', scope.WorkspaceIds.Distinct().Select(x => x.ToString("D"))) + "|";
        if (workspaces.Length > 8000)
        {
            throw new InvalidOperationException("Authorized workspace scope exceeds SQL session context capacity.");
        }
        var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key=N'clarity.workspaces', @value=@workspaces;
            EXEC sys.sp_set_session_context @key=N'clarity.tenant', @value=@tenant;
            EXEC sys.sp_set_session_context @key=N'clarity.subject', @value=@subject;
            EXEC sys.sp_set_session_context @key=N'clarity.platformAdmin', @value=@admin;
            EXEC sys.sp_set_session_context @key=N'clarity.jobDispatcher', @value=@dispatcher;
            """;
        Add(command, "@workspaces", DbType.AnsiString, workspaces);
        Add(command, "@tenant", DbType.Guid, scope.Identity.TenantId);
        Add(command, "@subject", DbType.Guid, scope.Identity.ObjectId);
        Add(command, "@admin", DbType.Boolean, scope.IsPlatformAdmin);
        Add(command, "@dispatcher", DbType.Boolean, scope is JobDispatchScope);
        return command;
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
