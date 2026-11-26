namespace Clarity.Core;

public sealed class GraphCursor : WorkspaceEntity
{
    public Guid EnvironmentId { get; init; }
    public Guid ConnectionId { get; init; }
    public required string CapabilityId { get; init; }
    public required string ScopeHash { get; set; }
    public required string DeltaLink { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
}
