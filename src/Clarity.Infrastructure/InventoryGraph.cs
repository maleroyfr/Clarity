using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record MembershipPath(Guid ObjectId, IReadOnlyList<Guid> RelationshipIds, string? Name);

public sealed class InventoryGraph(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access)
{
    public async Task<IReadOnlyList<MembershipPath>> MembershipsAsync(Guid projectId, Guid objectId, bool reverse, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var environment = await (from i in db.InventoryObjects
                                 join e in db.Environments on new { i.WorkspaceId, Id = i.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                                 where i.WorkspaceId == project.WorkspaceId && e.ProjectId == projectId && i.Id == objectId
                                 select (Guid?)e.Id).SingleOrDefaultAsync(token) ?? throw new AccessDeniedException();
        var parents = new Dictionary<Guid, (Guid Node, Guid Edge)>();
        var frontier = new List<Guid> { objectId };
        while (frontier.Count > 0)
        {
            var next = new List<Guid>();
            foreach (var ids in frontier.Chunk(500))
            {
                var query = db.InventoryRelationships.Where(e => e.WorkspaceId == project.WorkspaceId && e.EnvironmentId == environment
                    && e.Origin == RelationshipOrigin.Membership && e.IsCurrent && e.FromObjectId != null && e.ToObjectId != null);
                var edges = await query.Where(e => ids.Contains(reverse ? e.ToObjectId!.Value : e.FromObjectId!.Value))
                    .OrderBy(e => e.Id).Select(e => new { e.Id, From = e.FromObjectId!.Value, To = e.ToObjectId!.Value }).ToListAsync(token);
                foreach (var edge in edges)
                {
                    var child = reverse ? edge.From : edge.To;
                    if (child != objectId && parents.TryAdd(child, (reverse ? edge.To : edge.From, edge.Id))) { next.Add(child); }
                }
            }
            frontier = next;
        }
        var objectIds = parents.Keys.ToArray();
        var names = await db.InventoryObjects.Where(i => i.WorkspaceId == project.WorkspaceId && i.EnvironmentId == environment && objectIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.DisplayName ?? i.UserPrincipalName, token);
        return parents.Keys.Select(node =>
        {
            var path = new List<Guid>();
            for (var step = node; step != objectId; step = parents[step].Node) { path.Add(parents[step].Edge); }
            path.Reverse();
            return new MembershipPath(node, path, names.GetValueOrDefault(node));
        }).ToList();
    }
}
