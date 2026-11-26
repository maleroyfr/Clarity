using Shouldly;

namespace Clarity.Core.Tests;

public class AuthorizationTests
{
    [Theory]
    [InlineData(WorkspaceRole.Reader, WorkspaceOperation.Read, true)]
    [InlineData(WorkspaceRole.Reader, WorkspaceOperation.Import, false)]
    [InlineData(WorkspaceRole.Consultant, WorkspaceOperation.Export, true)]
    [InlineData(WorkspaceRole.Consultant, WorkspaceOperation.ManageMembers, false)]
    [InlineData(WorkspaceRole.WorkspaceManager, WorkspaceOperation.ManageConnections, true)]
    public void Workspace_permissions_are_explicit(WorkspaceRole role, WorkspaceOperation operation, bool allowed) =>
        WorkspacePolicy.Allows(role, operation).ShouldBe(allowed);

    [Fact]
    public void Platform_admin_has_no_implicit_inventory_access()
    {
        WorkspacePolicy.Allows(null, WorkspaceOperation.Read, true).ShouldBeFalse();
        WorkspacePolicy.Allows(null, WorkspaceOperation.Qualify, true).ShouldBeFalse();
        WorkspacePolicy.Allows(null, WorkspaceOperation.AdministerPlatform, true).ShouldBeTrue();
    }
}
