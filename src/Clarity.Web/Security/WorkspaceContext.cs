using Clarity.Core;

namespace Clarity.Web.Security;

public sealed class WorkspaceContext : IWorkspaceContext
{
    public UserIdentity Identity { get; internal set; }
    public IReadOnlyList<Guid> WorkspaceIds { get; internal set; } = [];
    public bool IsPlatformAdmin { get; internal set; }
}
