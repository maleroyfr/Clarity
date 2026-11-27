using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clarity.Contracts;

namespace Clarity.Infrastructure.Tests;

/// <summary>
/// Deterministic, anonymized dossier for acceptance and volume tests. Every name, address and identifier is generated under reserved test
/// domains from a seed; nothing comes from a real tenant, directory or customer export. Users are <c>u00001</c>…, spread over four
/// departments; each user belongs to its department group and to <see cref="ProjectMemberships"/> project groups, and department groups are
/// nested in an all-staff group. Users 2 and 3 are homonyms and every seventh user is disabled.
/// </summary>
internal sealed class SyntheticDossier
{
    internal const string Domain = "example.test";
    internal const string Forest = "forest.example.test";
    internal const string UsersSection = "ad.users:example.test";
    internal const string GroupsSection = "ad.groups:example.test";
    internal const string AllStaff = "all-staff";
    internal static readonly DateTimeOffset Epoch = new(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);
    internal static readonly string[] Departments = ["Sales", "Finance", "Operations", "Research"];
    private static readonly string[] UserFields = ["displayName", "userPrincipalName", "mail", "sAMAccountName", "distinguishedName", "enabled"];
    private static readonly string[] GroupFields = ["displayName", "distinguishedName"];
    private readonly int seed;
    private readonly string domain;
    private readonly string usersSection;
    private readonly string groupsSection;
    private readonly string domainComponents;
    private readonly CollectionScope domainScope;

    /// <param name="domain">The AD domain of the users and groups; volume tests shard a forest into several domains, each with its own seed.</param>
    internal SyntheticDossier(int users = 12, int projectMemberships = 2, int seed = 6, string domain = Domain)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(users, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(users, 99_999);
        ArgumentOutOfRangeException.ThrowIfNegative(projectMemberships);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        Users = users;
        ProjectMemberships = projectMemberships;
        ProjectGroups = Math.Max(projectMemberships + 1, users / 25);
        this.seed = seed;
        this.domain = domain;
        usersSection = $"ad.users:{domain}";
        groupsSection = $"ad.groups:{domain}";
        domainComponents = string.Join(',', domain.Split('.').Select(part => $"DC={part}"));
        domainScope = new("domain", domain);
    }

    internal int Users { get; }
    internal int ProjectMemberships { get; }
    internal int ProjectGroups { get; }
    internal int Groups => 1 + Departments.Length + ProjectGroups;
    internal int Relationships => Departments.Length + Users * (1 + ProjectMemberships);

    /// <summary>The <c>ad.objectGUID</c> of user <paramref name="index"/> (1-based).</summary>
    internal string UserId(int index) => Id("user", index);

    /// <summary>The <c>ad.objectGUID</c> of <see cref="AllStaff"/>, a department name or <c>project-N</c> (1-based).</summary>
    internal string GroupId(string name) => Id("group", name);

    /// <summary>The Entra object ID of user <paramref name="index"/> in <paramref name="tenant"/>.</summary>
    internal string EntraUserId(Guid tenant, int index) => Id($"entra:{tenant:D}", index);

    internal static string Upn(int index, bool renamed = false, string domain = Domain) => $"{(renamed ? "r" : "u")}{index:D5}@{domain}";

    internal static string DisplayName(int index) => $"Person {(index == 3 ? 2 : index):D5}";

    internal static string Department(int index) => Departments[(index - 1) % Departments.Length];

    /// <summary>The 1-based project groups of user <paramref name="index"/>.</summary>
    internal IEnumerable<int> ProjectsOf(int index) => Enumerable.Range(0, ProjectMemberships).Select(k => (index - 1 + k) % ProjectGroups + 1);

    /// <summary>
    /// An AD forest package for the dossier's domain observed on <paramref name="day"/>. Renamed users get a new UPN and mail under
    /// the same object GUID. <paramref name="groups"/> null leaves the groups section out; Failed (with an access error) or NotApplicable omit
    /// group data, Partial keeps it with a warning.
    /// </summary>
    internal DiscoveryPackage AdForest(int day = 0, IReadOnlyCollection<int>? renamed = null, bool users = true,
        CoverageStatus? groups = CoverageStatus.Complete, Guid? requestId = null, string forest = Forest)
    {
        if (!users && groups is null) { throw new ArgumentException("A package needs at least one section."); }
        var date = Epoch.AddDays(day);
        renamed ??= [];
        var objects = new List<ObjectRecord>();
        var links = new List<RelationshipRecord>();
        var results = new List<SectionResult>();
        if (users)
        {
            objects.AddRange(Enumerable.Range(1, Users).Select(i =>
            {
                var upn = Upn(i, renamed.Contains(i), domain);
                var name = $"u{i:D5}";
                return new ObjectRecord(usersSection, "user", "ad.objectGUID", UserId(i), date, Attributes(("displayName", DisplayName(i)),
                    ("userPrincipalName", upn), ("mail", upn), ("sAMAccountName", name),
                    ("distinguishedName", $"CN={name},OU={Department(i)},{domainComponents}"), ("enabled", i % 7 != 0)));
            }));
            results.Add(new(usersSection, "ad.users", CoverageStatus.Complete, domainScope, UserFields, [], Users, 0, [], []));
        }
        if (groups is { } status)
        {
            var withData = status is CoverageStatus.Complete or CoverageStatus.Partial;
            if (withData)
            {
                var names = new[] { AllStaff }.Concat(Departments).Concat(Enumerable.Range(1, ProjectGroups).Select(p => $"project-{p}"));
                objects.AddRange(names.Select(name => new ObjectRecord(groupsSection, "group", "ad.objectGUID", GroupId(name), date,
                    Attributes(("displayName", $"Synthetic {name}"), ("distinguishedName", $"CN={name},OU=Groups,{domainComponents}")))));
                links.AddRange(Departments.Select(d => Member("group", GroupId(d), GroupId(AllStaff), date)));
                for (var i = 1; i <= Users; i++)
                {
                    links.Add(Member("user", UserId(i), GroupId(Department(i)), date));
                    links.AddRange(ProjectsOf(i).Select(p => Member("user", UserId(i), GroupId($"project-{p}"), date)));
                }
            }
            SectionIssue[] warnings = status == CoverageStatus.Partial ? [new("Limited", "Synthetic partial group read.")] : [];
            SectionIssue[] errors = status == CoverageStatus.Failed ? [new("AccessDenied", "Synthetic group read failure.")] : [];
            results.Add(new(groupsSection, "ad.groups", status, domainScope, withData ? GroupFields : [], withData ? ["memberOf"] : [],
                withData ? Groups : null, withData ? links.Count : null, warnings, errors));
        }
        var key = $"ad|{forest}|{day}|{users}|{groups}|{requestId}|{string.Join(',', renamed.Order())}{(domain == Domain ? "" : $"|{domain}")}";
        return new(new PackageManifest("1.0", Guid.Parse(Id("run", key)), "clarity.ad", "1.0.0", "AdForest", forest, domainScope, date,
            date.AddMinutes(30), new Dictionary<string, string>(), "Synthetic dossier", requestId), objects, links, results);
    }

    /// <summary>
    /// An Entra tenant package with the first <paramref name="count"/> users under <paramref name="domain"/> and that verified domain.
    /// <paramref name="mails"/> overrides the mail of selected users, for example to create an ambiguous hybrid candidate.
    /// </summary>
    internal DiscoveryPackage Entra(Guid tenant, string domain = Domain, int? count = null, int day = 0, IReadOnlyDictionary<int, string>? mails = null)
    {
        var date = Epoch.AddDays(day);
        var scope = new CollectionScope("tenant", tenant.ToString());
        var total = count ?? Users;
        var users = Enumerable.Range(1, total).Select(i => new ObjectRecord("entra.users", "user", "microsoft.graph", EntraUserId(tenant, i), date,
            Attributes(("displayName", DisplayName(i)), ("userPrincipalName", Upn(i, domain: domain)),
                ("mail", mails?.GetValueOrDefault(i) ?? Upn(i, domain: domain))))).ToList();
        ObjectRecord[] domains = [new("entra.domains", "domain", "microsoft.graph", domain, date, Attributes(("isVerified", true)))];
        var key = $"entra|{tenant:D}|{domain}|{total}|{day}|{string.Join(',', (mails ?? new Dictionary<int, string>()).OrderBy(m => m.Key))}";
        return new(new PackageManifest("1.0", Guid.Parse(Id("run", key)), "clarity.graph.entra", "1.0.0", "M365Tenant", tenant.ToString(), scope,
                date, date.AddMinutes(30), new Dictionary<string, string>(), "Synthetic dossier"),
            [.. users, .. domains], [],
            [
                new SectionResult("entra.users", "entra.users", CoverageStatus.Complete, scope, ["displayName", "userPrincipalName", "mail"], [],
                    users.Count, 0, [], []),
                new SectionResult("entra.domains", "entra.domains", CoverageStatus.Complete, scope, ["isVerified"], [], domains.Length, 0, [], [])
            ]);
    }

    private RelationshipRecord Member(string type, string id, string group, DateTimeOffset date) =>
        new(groupsSection, "memberOf", new ExternalReference(type, "ad.objectGUID", id), new ExternalReference("group", "ad.objectGUID", group), date);

    private static Dictionary<string, JsonElement> Attributes(params (string Name, object Value)[] values) =>
        values.ToDictionary(x => x.Name, x => JsonSerializer.SerializeToElement(x.Value));

    private string Id(string kind, object value) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{kind}:{value}")).AsSpan(0, 16)).ToString("D");
}
