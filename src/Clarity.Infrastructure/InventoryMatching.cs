using System.Security.Cryptography;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed class InventoryMatching(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope)
{
    public async Task<SyncBinding?> GetBindingAsync(Guid projectId, Guid adId, Guid entraId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.SyncBindings.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.ProjectId == projectId && x.AdEnvironmentId == adId && x.EntraEnvironmentId == entraId, token);
    }

    public async Task VerifyAnchorAsync(Guid projectId, Guid adId, Guid entraId, SourceAnchor? anchor, string reason, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        ValidateReason(reason);
        if (anchor.HasValue && !Enum.IsDefined(anchor.Value)) { throw new ArgumentOutOfRangeException(nameof(anchor)); }
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await LockProjectAsync(db, project, token);
        var ad = await ImportService.EnvironmentAsync(db, project, adId, token);
        var entra = await ImportService.EnvironmentAsync(db, project, entraId, token);
        if (ad.Kind != EnvironmentKind.AdForest || entra.Kind != EnvironmentKind.M365Tenant || ad.Role != entra.Role)
        { throw new ArgumentException("Expected an AD forest and Entra tenant on the same side."); }
        var binding = await db.SyncBindings.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.ProjectId == projectId
            && x.AdEnvironmentId == adId && x.EntraEnvironmentId == entraId, token);
        if (binding == null)
        {
            binding = new SyncBinding { WorkspaceId = project.WorkspaceId, ProjectId = projectId, AdEnvironmentId = adId, EntraEnvironmentId = entraId, Reason = reason };
            db.SyncBindings.Add(binding);
        }
        binding.Anchor = anchor;
        binding.Reason = reason.Trim();
        binding.VerifiedAtUtc = DateTimeOffset.UtcNow;
        binding.VerifiedByTenantId = scope.Identity.TenantId;
        binding.VerifiedByObjectId = scope.Identity.ObjectId;
        await db.SaveChangesAsync(token);
        await RefreshAsync(db, project, token);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    internal static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) { throw new ArgumentException("A justification of 1 to 1000 characters is required."); }
    }

    internal static Task LockProjectAsync(ClarityDbContext db, Project project, CancellationToken token) => db.Database.ExecuteSqlAsync($"""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource = {$"clarity:match:{project.WorkspaceId:N}:{project.Id:N}"},
            @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @result < 0 THROW 51000, 'Inventory association lock unavailable.', 1;
        """, token);

    private sealed record Fact(string Name, string Json, Guid Observation, bool Connected, bool Conflict);
    private sealed record Identity(InventoryObject Item, ProjectEnvironment Environment, ExternalKey Key, Dictionary<string, Fact> Facts)
    {
        public string? Text(string name) => Facts.TryGetValue(name, out var f) && JsonSerializer.Deserialize<JsonElement>(f.Json) is
        { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    }
    private sealed record Match(Identity Ad, Identity Entra, AssociationRule Rule, bool Strong, bool Check, SyncBinding? Binding);

    internal static async Task RefreshAsync(ClarityDbContext db, Project project, CancellationToken token)
    {
        var workspace = project.WorkspaceId;
        var environments = await db.Environments.AsNoTracking().Where(x => x.WorkspaceId == workspace && x.ProjectId == project.Id
            && x.TargetState == TargetState.Existing).ToDictionaryAsync(x => x.Id, token);
        if (!environments.Values.Any(e => e.Kind == EnvironmentKind.AdForest) || !environments.Values.Any(e => e.Kind == EnvironmentKind.M365Tenant)) { return; }
        var envIds = environments.Keys.ToArray();
        var items = db.InventoryObjects.Where(x => x.WorkspaceId == workspace && envIds.Contains(x.EnvironmentId)
            && x.Presence == InventoryPresence.Observed && (x.Type == "user" || x.Type == "device" || x.Type == "computer"));
        var fieldNames = new[] { "userPrincipalName", "mail", "proxyAddresses", "objectSid", "msDSConsistencyGuid", "onPremisesImmutableId",
            "onPremisesSecurityIdentifier", "onPremisesSyncEnabled", "deviceId", "trustType", "serialNumber", "displayName", "enabled", "accountEnabled" };
        var facts = (await (from i in items
                            join v in db.CurrentValues on new { i.WorkspaceId, ObjectId = i.Id } equals new { v.WorkspaceId, v.ObjectId }
                            join o in db.ObjectObservations on new { v.WorkspaceId, Id = v.ObservationId } equals new { o.WorkspaceId, o.Id }
                            join s in db.SectionRuns on new { o.WorkspaceId, Id = o.SectionRunId } equals new { s.WorkspaceId, s.Id }
                            join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                            where fieldNames.Contains(v.Field)
                            select new { i.Id, Value = new Fact(v.Field, v.ValueJson, v.ObservationId, r.ConnectionId != null, v.Conflicting) })
            .ToListAsync(token)).ToLookup(x => x.Id, x => x.Value);
        var rows = await (from i in items.AsNoTracking()
                          join k in db.ExternalKeys on new { i.WorkspaceId, ObjectId = i.Id } equals new { k.WorkspaceId, k.ObjectId }
                          where k.IsPrimary
                          select new { Item = i, Key = k }).ToListAsync(token);
        var identities = rows.Select(x => new Identity(x.Item, environments[x.Item.EnvironmentId], x.Key,
            facts[x.Item.Id].ToDictionary(f => f.Name))).ToList();
        var bindings = await db.SyncBindings.AsNoTracking().Where(x => x.WorkspaceId == workspace && x.ProjectId == project.Id)
            .ToDictionaryAsync(x => (x.AdEnvironmentId, x.EntraEnvironmentId), token);
        var matches = new List<Match>();
        foreach (var side in identities.GroupBy(x => x.Environment.Role))
        {
            var users = side.Where(x => x.Environment.Kind == EnvironmentKind.M365Tenant && x.Item.Type == "user").ToArray();
            var devices = side.Where(x => x.Environment.Kind == EnvironmentKind.M365Tenant && x.Item.Type == "device").ToArray();
            var byAnchor = users.ToLookup(x => Bytes(x.Text("onPremisesImmutableId")));
            var bySid = users.ToLookup(x => x.Text("onPremisesSecurityIdentifier"), StringComparer.OrdinalIgnoreCase);
            var byUpn = users.ToLookup(x => x.Text("userPrincipalName"), StringComparer.OrdinalIgnoreCase);
            var byMail = users.SelectMany(x => Addresses(x).Select(a => (a, x))).ToLookup(p => p.a, p => p.x, StringComparer.OrdinalIgnoreCase);
            var byDevice = devices.ToLookup(x => x.Text("deviceId"), StringComparer.OrdinalIgnoreCase);
            var byName = devices.ToLookup(x => x.Text("displayName"), StringComparer.OrdinalIgnoreCase);
            var bySerial = devices.ToLookup(x => x.Text("serialNumber"), StringComparer.OrdinalIgnoreCase);
            foreach (var ad in side.Where(x => x.Environment.Kind == EnvironmentKind.AdForest && x.Key.Namespace == "ad.objectGUID"))
            {
                var candidates = new Dictionary<Guid, Match>();
                void Add(Identity cloud, AssociationRule rule, bool strong, bool check, SyncBinding? binding = null)
                {
                    if (!candidates.TryGetValue(cloud.Item.Id, out var existing) || rule < existing.Rule)
                    { candidates[cloud.Item.Id] = new(ad, cloud, rule, strong, check, binding); }
                }
                if (ad.Item.Type == "user")
                {
                    var sid = ad.Text("objectSid");
                    foreach (var binding in bindings.Values.Where(b => b.AdEnvironmentId == ad.Environment.Id && b.Anchor != null))
                    {
                        var anchor = binding.Anchor == SourceAnchor.ObjectGuid ? GuidBytes(ad.Key.ExternalId) : Bytes(ad.Text("msDSConsistencyGuid"));
                        if (anchor == null) { continue; }
                        foreach (var cloud in byAnchor[anchor].Where(x => x.Environment.Id == binding.EntraEnvironmentId))
                        {
                            var otherSid = cloud.Text("onPremisesSecurityIdentifier");
                            var sidAgrees = sid == null || otherSid == null || string.Equals(sid, otherSid, StringComparison.OrdinalIgnoreCase);
                            var verified = cloud.Facts.TryGetValue("onPremisesSyncEnabled", out var sync) && sync.Json == "true"
                                && sync.Connected && !sync.Conflict && cloud.Facts["onPremisesImmutableId"] is { Connected: true, Conflict: false }
                                && !(ad.Facts.GetValueOrDefault("msDSConsistencyGuid")?.Conflict == true && binding.Anchor == SourceAnchor.ConsistencyGuid)
                                && cloud.Facts.GetValueOrDefault("onPremisesSecurityIdentifier")?.Conflict != true && ad.Facts.GetValueOrDefault("objectSid")?.Conflict != true;
                            Add(cloud, AssociationRule.ConfiguredAnchor, verified && sidAgrees, !verified || !sidAgrees, binding);
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(sid)) { foreach (var c in bySid[sid]) { Add(c, AssociationRule.SecurityIdentifier, false, true); } }
                    if (ad.Text("userPrincipalName") is { Length: > 0 } upn) { foreach (var c in byUpn[upn]) { Add(c, AssociationRule.ExactUpn, false, false); } }
                    foreach (var mail in Addresses(ad)) { foreach (var c in byMail[mail]) { Add(c, AssociationRule.ExactSmtp, false, false); } }
                }
                if (ad.Item.Type == "computer")
                {
                    if (Guid.TryParse(ad.Key.ExternalId, out var guid))
                    {
                        foreach (var c in byDevice[guid.ToString()])
                        {
                            var verified = c.Text("trustType") == "ServerAd" && c.Facts["deviceId"] is { Connected: true, Conflict: false }
                                && c.Facts["trustType"] is { Connected: true, Conflict: false };
                            Add(c, AssociationRule.DeviceIdentifier, verified, !verified);
                        }
                    }
                    if (ad.Text("displayName") is { Length: > 0 } name) { foreach (var c in byName[name]) { Add(c, AssociationRule.DeviceHint, false, true); } }
                    if (ad.Text("serialNumber") is { Length: > 0 } serial) { foreach (var c in bySerial[serial]) { Add(c, AssociationRule.DeviceHint, false, true); } }
                }
                matches.AddRange(candidates.Values);
            }
        }
        var decisions = await db.MappingDecisions.Where(x => x.WorkspaceId == workspace && x.ProjectId == project.Id)
            .ToDictionaryAsync(x => (x.AdObjectId, x.EntraObjectId), token);
        var matched = matches.Select(m => (m.Ad.Item.Id, m.Entra.Item.Id)).ToHashSet();
        var byId = identities.ToDictionary(i => i.Item.Id);
        foreach (var qualified in decisions.Values.Where(d => d.ConsultantQualified))
        {
            if (!matched.Contains((qualified.AdObjectId, qualified.EntraObjectId)) && byId.TryGetValue(qualified.AdObjectId, out var ad)
                && byId.TryGetValue(qualified.EntraObjectId, out var entra))
            {
                matches.Add(new(ad, entra, AssociationRule.ConsultantQualified, false, true, null));
            }
        }
        var forAd = matches.ToLookup(x => x.Ad.Item.Id, x => x.Entra.Item.Id);
        var forCloud = matches.ToLookup(x => x.Entra.Item.Id, x => x.Ad.Item.Id);
        foreach (var match in matches)
        {
            var a = match.Ad.Item.Id;
            var b = match.Entra.Item.Id;
            var competing = forAd[a].Concat(forCloud[b]).Order().ToArray();
            static IEnumerable<Fact> Critical(Identity i) => i.Facts.Values.Where(f => f.Name != "displayName" || i.Item.Type != "user")
                .OrderBy(f => f.Name, StringComparer.Ordinal);
            var critical = Critical(match.Ad).Concat(Critical(match.Entra)).ToArray();
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                match.Rule,
                match.Strong,
                match.Check,
                match.Binding?.Anchor,
                competing,
                fields = critical.Select(f => new { f.Name, f.Json, f.Connected, f.Conflict })
            })));
            var proof = JsonSerializer.Serialize(new
            {
                rule = match.Rule.ToString(),
                match.Strong,
                match.Check,
                candidates = competing,
                observations = critical.Select(f => f.Observation).Distinct().Order(),
                anchor = match.Binding?.Anchor?.ToString(),
                match.Binding?.VerifiedAtUtc,
                match.Binding?.VerifiedByObjectId,
                match.Binding?.VerifiedByTenantId,
                match.Binding?.Reason
            });
            if (!decisions.Remove((a, b), out var decision))
            {
                decision = new MappingDecision
                {
                    WorkspaceId = workspace,
                    ProjectId = project.Id,
                    AdEnvironmentId = match.Ad.Environment.Id,
                    EntraEnvironmentId = match.Entra.Environment.Id,
                    AdObjectId = a,
                    EntraObjectId = b,
                    EvidenceJson = proof,
                    Fingerprint = fingerprint,
                    Rule = match.Rule
                };
                db.MappingDecisions.Add(decision);
                Record(db, decision, competing.Length > 2 || match.Check ? MappingState.ToReview : match.Strong ? MappingState.Validated : MappingState.Proposed,
                    "CandidateObserved", null);
            }
            else if (decision.Fingerprint != fingerprint)
            {
                decision.Rule = match.Rule;
                decision.Fingerprint = fingerprint;
                decision.EvidenceJson = proof;
                Record(db, decision, MappingState.ToReview, "AssociationEvidenceChanged", null);
            }
        }
        foreach (var lost in decisions.Values.Where(x => x.Rule != AssociationRule.NoCurrentCandidate))
        {
            lost.Fingerprint = "";
            lost.Rule = AssociationRule.NoCurrentCandidate;
            Record(db, lost, MappingState.ToReview, "CandidateNoLongerObserved", null);
        }
    }

    internal static void Record(ClarityDbContext db, MappingDecision decision, MappingState state, string reason, UserIdentity? actor)
    {
        decision.State = state;
        db.MappingRevisions.Add(new MappingRevision
        {
            WorkspaceId = decision.WorkspaceId,
            DecisionId = decision.Id,
            Number = ++decision.Revision,
            State = state,
            Rule = decision.Rule,
            EvidenceJson = decision.EvidenceJson,
            Reason = reason,
            ActorTenantId = actor?.TenantId,
            ActorObjectId = actor?.ObjectId
        });
    }

    private static string? GuidBytes(string value) => Guid.TryParse(value, out var guid) && guid != Guid.Empty ? Convert.ToBase64String(guid.ToByteArray()) : null;
    private static string? Bytes(string? value)
    {
        Span<byte> buffer = stackalloc byte[16];
        return value != null && Convert.TryFromBase64String(value, buffer, out var length) && length == 16 && new Guid(buffer) != Guid.Empty
            ? Convert.ToBase64String(buffer) : null;
    }
    private static IEnumerable<string> Addresses(Identity identity)
    {
        if (identity.Text("mail") is { Length: > 0 } mail) { yield return mail.Trim(); }
        if (identity.Facts.TryGetValue("proxyAddresses", out var proxies) && JsonSerializer.Deserialize<JsonElement>(proxies.Json) is
            { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var value in array.EnumerateArray())
            {
                if (value.GetString() is { } smtp && smtp.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase) && smtp.Length > 5) { yield return smtp[5..].Trim(); }
            }
        }
    }
}
