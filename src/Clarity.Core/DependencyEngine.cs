namespace Clarity.Core;

public sealed record DependencyNode(Guid Id, string Type, bool Deleted);
public sealed record DependencyEdge(Guid Id, string Relation, Guid? From, Guid? To);
public sealed record DependencyGraphInput(IReadOnlyCollection<DependencyNode> Nodes, IReadOnlyCollection<DependencyEdge> Edges,
    IReadOnlyDictionary<Guid, DependencySide> Sides, IReadOnlySet<Guid> AllTargets, IReadOnlySet<Guid> ExcludedTargets,
    IReadOnlyDictionary<Guid, string> ChannelKinds);
public sealed record DependencyRoute(Guid ObjectId, string Relation, Guid RelationshipId);
public sealed record DependencySample(Guid UserId, DependencySide Side, string Route, Guid? Via);
public sealed record DependencyFinding(Guid ObjectId, string Type, DependencyFamily Family, int Migrating, int OtherWave, int Arbitrate, int Remaining,
    IReadOnlyList<DependencyRoute> Routes, int RouteCount, IReadOnlyList<DependencySample> Samples, int Unresolved, bool MembersNotCollected, bool AllUsers)
{
    public DependencyClassification Classification => DependencyRules.Classify(OtherWave, Arbitrate, Remaining);
}
/// <summary>A resource reachable from the migrating population whose dependents were not collected (private or shared channel roster).</summary>
public sealed record DependencyGap(Guid ObjectId, string Type, DependencyFamily Family);
public sealed record DependencyGraphResult(IReadOnlyList<DependencyFinding> Findings, int Migrating, int OtherWave, int Arbitrate, int Remaining,
    IReadOnlyList<DependencyGap> Gaps);

/// <summary>
/// Pure dependency traversal. Each relation is read as "dependent → resource" with a category: structural links between
/// collective objects (team ↔ backing group, group/team/channel → site, assignment → app or policy, Intune ↔ Entra device),
/// membership, ownership and grants (site permissions, mailbox delegations, app role and Intune group assignments).
/// From each candidate resource the walk goes backward: structural links keep the structural mode; membership, ownership and
/// grants switch to member mode, which only follows nested membership, the team/group pairing and the owner of an identity-bearing
/// object (mailbox or device). Delegation and ownership are therefore not transitive through other resources.
/// </summary>
public static class DependencyEngine
{
    private enum Category { Structural, Membership, Ownership, Grant, Discovery }
    private enum Mode { Structural, Members }
    private readonly record struct Incoming(Guid Dependent, Category Category, string Relation, Guid EdgeId);

    public static readonly string[] Relations =
    [
        "teamGroup", "groupSite", "teamSite", "channelSite", "channelInTeam", "assignmentOf", "appRoleAssignmentResource", "referencesEntraDevice",
        "memberOf", "primaryMemberOf", "ownerOf", "intunePrimaryUserOf", "entraRegisteredOwnerOf", "mailboxOf", "sitePermission", "fullAccessTo",
        "sendAsOn", "sendOnBehalfOf", "appRoleAssignmentPrincipal", "targetsGroup"
    ];

    private static readonly HashSet<string> IdentityBearing =
        ["mailbox", "sharedMailbox", "resourceMailbox", "managedDevice", "device"];

    public static DependencyGraphResult Analyze(DependencyGraphInput input, long budget = DependencyRules.MaxTraversal)
    {
        var nodes = input.Nodes.ToDictionary(n => n.Id);
        var incoming = new Dictionary<Guid, List<Incoming>>();
        var outgoing = new Dictionary<Guid, List<Guid>>();
        var unresolvedMembers = new Dictionary<Guid, int>();
        var unresolvedOther = new Dictionary<Guid, int>();
        var partial = new HashSet<Guid>();
        foreach (var edge in input.Edges)
        {
            foreach (var (dependent, resource, category) in Map(edge, input, partial))
            {
                if (resource is not { } r || !nodes.ContainsKey(r)) { continue; }
                if (dependent is not { } d || !nodes.ContainsKey(d))
                {
                    if (category == Category.Membership) { unresolvedMembers[r] = unresolvedMembers.GetValueOrDefault(r) + 1; }
                    else if (category is not (Category.Structural or Category.Discovery)) { unresolvedOther[r] = unresolvedOther.GetValueOrDefault(r) + 1; }
                    continue;
                }
                if (category != Category.Discovery)
                {
                    if (!incoming.TryGetValue(r, out var into)) { incoming[r] = into = []; }
                    into.Add(new(d, category, edge.Relation, edge.Id));
                }
                if (!outgoing.TryGetValue(d, out var from)) { outgoing[d] = from = []; }
                from.Add(r);
            }
        }

        var totals = new int[4];
        foreach (var node in nodes.Values.Where(n => n.Type == "user" && !n.Deleted))
        {
            totals[(int)input.Sides.GetValueOrDefault(node.Id, DependencySide.Remaining)]++;
        }

        // Forward closure from migrating principals bounds the resources worth a backward walk.
        var candidates = new HashSet<Guid>();
        var frontier = new Queue<Guid>(nodes.Values.Where(n => n.Type == "user" && !n.Deleted
            && input.Sides.GetValueOrDefault(n.Id, DependencySide.Remaining) == DependencySide.Migrating).Select(n => n.Id));
        if (totals[(int)DependencySide.Migrating] > 0)
        {
            foreach (var target in input.AllTargets.Where(nodes.ContainsKey)) { frontier.Enqueue(target); }
        }
        while (frontier.TryDequeue(out var node))
        {
            foreach (var next in outgoing.GetValueOrDefault(node) ?? [])
            {
                if (candidates.Add(next)) { frontier.Enqueue(next); }
            }
        }

        long steps = 0;
        var findings = new List<DependencyFinding>();
        var gaps = new List<DependencyGap>();
        foreach (var root in candidates.Order())
        {
            var item = nodes[root];
            if (item.Deleted) { continue; }
            if (partial.Contains(root)) { gaps.Add(new(root, item.Type, DependencyFamily.Teams)); continue; }
            if (DependencyRules.Family(item.Type) is not { } family) { continue; }
            var best = new Dictionary<Guid, Mode> { [root] = Mode.Structural };
            var parent = new Dictionary<Guid, (Guid Next, string Relation)>();
            var queue = new Queue<(Guid Node, Mode Mode)>();
            queue.Enqueue((root, Mode.Structural));
            var counted = new Dictionary<Guid, Mode>();
            var sides = new int[4];
            var samples = new List<DependencySample>();
            var unresolved = 0;
            var membersNotCollected = false;
            var allUsers = false;
            while (queue.TryDequeue(out var step))
            {
                if (best[step.Node] != step.Mode) { continue; }
                if (++steps > budget)
                {
                    throw new ArgumentException("The dependency graph is too large to analyze in one pass; narrow the population or the wave.");
                }
                var type = nodes[step.Node].Type;
                if (step.Node != root)
                {
                    if (type == "user")
                    {
                        if (nodes[step.Node].Deleted || counted.ContainsKey(step.Node)) { continue; }
                        counted[step.Node] = step.Mode;
                        var side = input.Sides.GetValueOrDefault(step.Node, DependencySide.Remaining);
                        if (sides[(int)side]++ < DependencyRules.SamplesPerSide) { samples.Add(Sample(step.Node, side, root, parent)); }
                        continue;
                    }
                    if (type == "teamMembership")
                    {
                        if (counted.TryAdd(step.Node, step.Mode)) { unresolved++; }
                        continue;
                    }
                }
                if (step.Mode == Mode.Structural && input.AllTargets.Contains(step.Node)) { allUsers = true; }
                if (partial.Contains(step.Node)) { membersNotCollected = true; }
                if (!counted.TryGetValue(step.Node, out var already))
                {
                    unresolved += unresolvedMembers.GetValueOrDefault(step.Node)
                        + (step.Mode == Mode.Structural ? unresolvedOther.GetValueOrDefault(step.Node) : 0);
                    counted[step.Node] = step.Mode;
                }
                else if (already == Mode.Members && step.Mode == Mode.Structural)
                {
                    unresolved += unresolvedOther.GetValueOrDefault(step.Node);
                    counted[step.Node] = step.Mode;
                }
                foreach (var link in incoming.GetValueOrDefault(step.Node) ?? [])
                {
                    Mode? next = step.Mode == Mode.Structural
                        ? link.Category == Category.Structural ? Mode.Structural : Mode.Members
                        : link.Category == Category.Membership || link.Relation == "teamGroup"
                            || IdentityBearing.Contains(type) && (link.Category == Category.Ownership || link.Relation == "referencesEntraDevice")
                            ? Mode.Members : null;
                    if (next is not { } mode || best.TryGetValue(link.Dependent, out var seen) && seen <= mode) { continue; }
                    best[link.Dependent] = mode;
                    parent[link.Dependent] = (step.Node, link.Relation);
                    queue.Enqueue((link.Dependent, mode));
                }
            }
            if (allUsers) { sides = [.. totals]; }
            if (sides[(int)DependencySide.Migrating] == 0)
            {
                if (membersNotCollected) { gaps.Add(new(root, item.Type, family)); }
                continue;
            }
            var direct = (incoming.GetValueOrDefault(root) ?? []).Where(x => nodes[x.Dependent].Type != "user")
                .DistinctBy(x => (x.Dependent, x.Relation)).ToList();
            findings.Add(new(root, item.Type, family, sides[0], sides[1], sides[2], sides[3],
                direct.Take(DependencyRules.MaxRoutes).Select(x => new DependencyRoute(x.Dependent, x.Relation, x.EdgeId)).ToList(), direct.Count,
                samples, unresolved, membersNotCollected, allUsers));
            if (findings.Count > DependencyRules.MaxResources)
            {
                throw new ArgumentException("The analysis exceeds the supported number of dependent resources; narrow the population or the wave.");
            }
        }
        return new(findings, totals[0], totals[1], totals[2], totals[3], gaps);
    }

    private static DependencySample Sample(Guid user, DependencySide side, Guid root, Dictionary<Guid, (Guid Next, string Relation)> parent)
    {
        var relations = new List<string>();
        Guid? via = null;
        for (var node = user; node != root; node = parent[node].Next)
        {
            relations.Add(parent[node].Relation);
            if (parent[node].Next != root) { via = parent[node].Next; }
        }
        var route = string.Join(" › ", relations);
        return new(user, side, route.Length <= 1000 ? route : route[..999] + "…", via);
    }

    private static IEnumerable<(Guid? Dependent, Guid? Resource, Category Category)> Map(DependencyEdge edge, DependencyGraphInput input, HashSet<Guid> partial)
    {
        switch (edge.Relation)
        {
            case "teamGroup" or "referencesEntraDevice":
                yield return (edge.From, edge.To, Category.Structural);
                yield return (edge.To, edge.From, Category.Structural);
                break;
            case "groupSite" or "teamSite" or "channelSite" or "assignmentOf" or "appRoleAssignmentResource":
                yield return (edge.From, edge.To, Category.Structural);
                break;
            case "channelInTeam":
                // Standard channel members are the team members; private and shared channel rosters are not collected, so such a
                // channel is only discovered from its team and reported as a coverage gap, never credited with the team members.
                if (edge.From is { } channel && input.ChannelKinds.GetValueOrDefault(channel) != "standard")
                {
                    partial.Add(channel);
                    yield return (edge.To, edge.From, Category.Discovery);
                }
                else { yield return (edge.To, edge.From, Category.Structural); }
                break;
            case "memberOf" or "primaryMemberOf":
                yield return (edge.From, edge.To, Category.Membership);
                break;
            case "ownerOf" or "intunePrimaryUserOf" or "entraRegisteredOwnerOf":
                yield return (edge.From, edge.To, Category.Ownership);
                break;
            case "mailboxOf":
                yield return (edge.To, edge.From, Category.Ownership);
                break;
            case "sitePermission" or "fullAccessTo" or "sendAsOn" or "sendOnBehalfOf":
                yield return (edge.From, edge.To, Category.Grant);
                break;
            case "appRoleAssignmentPrincipal":
                yield return (edge.To, edge.From, Category.Grant);
                break;
            case "targetsGroup":
                if (edge.From is not { } assignment || !input.ExcludedTargets.Contains(assignment))
                {
                    yield return (edge.To, edge.From, Category.Grant);
                }
                break;
        }
    }
}
