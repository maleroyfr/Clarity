namespace Clarity.Core;

public enum WorkspaceOperation
{
    Read, Collect, Import, Qualify, Export, ManageMembers, ManageConnections, AdministerPlatform
}

public static class WorkspacePolicy
{
    public static bool Allows(WorkspaceRole? role, WorkspaceOperation operation, bool platformAdmin = false) =>
        operation switch
        {
            WorkspaceOperation.AdministerPlatform => platformAdmin,
            WorkspaceOperation.Read => role is not null,
            WorkspaceOperation.ManageMembers or WorkspaceOperation.ManageConnections => role == WorkspaceRole.WorkspaceManager,
            WorkspaceOperation.Collect or WorkspaceOperation.Import or WorkspaceOperation.Qualify or WorkspaceOperation.Export =>
                role is WorkspaceRole.WorkspaceManager or WorkspaceRole.Consultant,
            _ => false
        };
}

public sealed class AccessDeniedException : Exception
{
    public AccessDeniedException() : base("Access denied to the requested resource.") { }
}

public sealed class ConflictException : Exception
{
    public ConflictException() : base("The operation identifier is already used with different content.") { }
}

public sealed record PageRequest(int Start = 0, int Count = 20, string Search = "", bool Descending = false, string? Sort = null)
{
    public void Validate()
    {
        if (Start < 0 || Count is < 1 or > 100 || Search.Length > 200)
        {
            throw new ArgumentException("Invalid paging parameters.");
        }
    }
}

public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount);
