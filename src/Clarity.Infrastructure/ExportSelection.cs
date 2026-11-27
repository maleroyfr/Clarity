using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record ExportUnit(Guid SectionId, Guid EnvironmentId, string Environment, string Capability, string ScopeJson, CoverageStatus Status,
    DateTimeOffset Date, Guid RunId);
public sealed record ExportGap(Guid EnvironmentId, string Environment, string Capability);

/// <summary>
/// Fingerprint of everything an export reads besides the coverage units: publications, scope decisions, mappings, review flags, populations,
/// target comparisons and dependency analyses of the project. Generation fails rather than mixing data published or decided after launch.
/// </summary>
public sealed record ExportDigest(int PublishedSections, DateTimeOffset? LastPublishedAtUtc, int ScopeDecisions, long ScopeRevisions,
    DateTimeOffset? LastScopeDecisionAtUtc, int Mappings, long MappingRevisions, int ReviewFlags, long ReviewFlagRevisions,
    DateTimeOffset? LastFlaggedAtUtc, IReadOnlyList<string> Populations, IReadOnlyList<string> Comparisons, IReadOnlyList<string> Analyses);

/// <summary>The dated observation selection of an export: the latest published coverage unit per capability and scope, the capabilities never collected, and the digest.</summary>
public sealed record ExportSelection(IReadOnlyList<ExportUnit> Units, IReadOnlyList<ExportGap> Uncollected, ExportDigest Digest)
{
    public string Serialize() => JsonSerializer.Serialize(this, ContractJson.Options);

    public static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    public static ExportSelection Read(string json) =>
        JsonSerializer.Deserialize<ExportSelection>(json, ContractJson.Options) ?? throw new ContractException("JobPayload", "export");

    internal static async Task<ExportSelection> CaptureAsync(ClarityDbContext db, Project project, ExportFilter filter, DiscoveryCatalog catalog,
        CancellationToken token)
    {
        var ws = project.WorkspaceId;
        var environments = await db.Environments.AsNoTracking()
            .Where(e => e.WorkspaceId == ws && e.ProjectId == project.Id && e.Role == filter.Side
                && (filter.EnvironmentId == null || e.Id == filter.EnvironmentId))
            .Select(e => new { e.Id, e.ExternalId, e.Kind }).ToListAsync(token);
        var ids = environments.Select(e => e.Id).ToList();
        var sections = from s in db.SectionRuns
                       join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                       where s.WorkspaceId == ws && r.ProjectId == project.Id && ids.Contains(r.EnvironmentId) && s.PublishedAtUtc != null
                       select new { s.Id, r.EnvironmentId, s.CapabilityId, s.ScopeHash, s.ScopeJson, s.Coverage, r.CompletedAtUtc, r.ExternalRunId, s.RunId };
        var latest = await sections.GroupBy(s => new { s.EnvironmentId, s.CapabilityId, s.ScopeHash })
            .Select(g => g.OrderByDescending(s => s.CompletedAtUtc).ThenByDescending(s => s.ExternalRunId).First()).ToListAsync(token);
        var names = environments.ToDictionary(e => e.Id, e => e.ExternalId);
        var units = latest.Select(s => new ExportUnit(s.Id, s.EnvironmentId, names[s.EnvironmentId], s.CapabilityId, s.ScopeJson, s.Coverage,
                s.CompletedAtUtc, s.RunId))
            .OrderBy(u => u.Environment, StringComparer.Ordinal).ThenBy(u => u.Capability, StringComparer.Ordinal)
            .ThenBy(u => u.ScopeJson, StringComparer.Ordinal).ThenBy(u => u.SectionId).ToList();
        var collected = await (from s in db.SectionRuns
                               join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                               where s.WorkspaceId == ws && r.ProjectId == project.Id && ids.Contains(r.EnvironmentId)
                               select new { r.EnvironmentId, s.CapabilityId }).Distinct().ToListAsync(token);
        var gaps = environments.SelectMany(e => catalog.Families.Where(f => f.EnvironmentKind == e.Kind.ToString()).SelectMany(f => f.Sections)
                .Where(s => s.ProducerIds.Any(id => catalog.Producers[id].Readiness == "SyntheticValidated"))
                .Select(s => s.Id).Distinct(StringComparer.Ordinal)
                .Where(c => !collected.Any(x => x.EnvironmentId == e.Id && x.CapabilityId == c))
                .Select(c => new ExportGap(e.Id, e.ExternalId, c)))
            .OrderBy(g => g.Environment, StringComparer.Ordinal).ThenBy(g => g.Capability, StringComparer.Ordinal).ThenBy(g => g.EnvironmentId).ToList();
        return new(units, gaps, await DigestAsync(db, project, token));
    }

    private static async Task<ExportDigest> DigestAsync(ClarityDbContext db, Project project, CancellationToken token)
    {
        var ws = project.WorkspaceId;
        var published = from s in db.SectionRuns
                        join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                        where s.WorkspaceId == ws && r.ProjectId == project.Id && s.PublishedAtUtc != null
                        select s.PublishedAtUtc;
        var decisions = db.ScopeDecisions.Where(d => d.WorkspaceId == ws && d.ProjectId == project.Id);
        var mappings = db.MappingDecisions.Where(m => m.WorkspaceId == ws && m.ProjectId == project.Id);
        var flags = db.ReviewFlags.Where(f => f.WorkspaceId == ws && f.ProjectId == project.Id);
        var populations = await db.Populations.Where(p => p.WorkspaceId == ws && p.ProjectId == project.Id)
            .Select(p => new { p.Id, p.Name, p.MemberCount, p.EvaluatedAtUtc }).ToListAsync(token);
        var comparisons = await db.TargetComparisons.Where(c => c.WorkspaceId == ws && c.ProjectId == project.Id)
            .Select(c => new { c.Id, c.ComputedAtUtc }).ToListAsync(token);
        var analyses = await db.DependencyAnalyses.Where(a => a.WorkspaceId == ws && a.ProjectId == project.Id)
            .Select(a => new { a.Id, a.ComputedAtUtc }).ToListAsync(token);
        return new(await published.CountAsync(token), await published.MaxAsync(token),
            await decisions.CountAsync(token), await decisions.SumAsync(d => d.Revision, token), await decisions.MaxAsync(d => (DateTimeOffset?)d.UpdatedAtUtc, token),
            await mappings.CountAsync(token), await mappings.SumAsync(m => m.Revision, token),
            await flags.CountAsync(token), await flags.SumAsync(f => f.DecisionRevision, token), await flags.MaxAsync(f => (DateTimeOffset?)f.FlaggedAtUtc, token),
            populations.OrderBy(p => p.Id).Select(p => $"{p.Id:N}:{p.MemberCount}:{p.EvaluatedAtUtc.UtcTicks}:{p.Name}").ToList(),
            comparisons.OrderBy(c => c.Id).Select(c => $"{c.Id:N}:{c.ComputedAtUtc.UtcTicks}").ToList(),
            analyses.OrderBy(a => a.Id).Select(a => $"{a.Id:N}:{a.ComputedAtUtc.UtcTicks}").ToList());
    }
}
