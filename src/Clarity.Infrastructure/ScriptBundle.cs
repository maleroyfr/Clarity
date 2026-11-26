using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using Clarity.Contracts;

namespace Clarity.Infrastructure;

public sealed record ScriptBundleFile(byte[] Content, string FileName);

/// <summary>
/// Discovers the downloadable PowerShell collector bundles from embedded <c>Scripts/&lt;folder&gt;/…</c> resources
/// (see <c>scripts/README.md</c>). Producer <c>clarity.&lt;folder&gt;</c> owns the folder <c>scripts/&lt;folder&gt;</c>, which must hold
/// exactly one <c>Collect-&lt;Name&gt;.ps1</c> entry script. The shared <c>Clarity.Discovery.Common</c> module is added to every bundle.
/// </summary>
public sealed partial class ScriptBundleRegistry
{
    public const string ResourcePrefix = "Scripts/";
    public const string CommonFolder = "Clarity.Discovery.Common";
    private static readonly DateTimeOffset Timestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record Family(string Folder, string Name, IReadOnlyList<(string Resource, string Entry)> Files);

    private readonly IReadOnlyDictionary<string, Family> families;
    private readonly Func<string, Stream> open;

    public ScriptBundleRegistry(IEnumerable<string> resourceNames, Func<string, Stream> open)
    {
        this.open = open;
        var files = resourceNames
            .Select(x => (Resource: x, Path: x.Replace('\\', '/')))
            .Where(x => x.Path.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(x => (x.Resource, Path: x.Path[ResourcePrefix.Length..]))
            .Where(x => x.Path.IndexOf('/') > 0)
            .ToArray();
        var common = files.Where(x => x.Path.StartsWith(CommonFolder + "/", StringComparison.Ordinal))
            .OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var registered = new Dictionary<string, Family>(StringComparer.Ordinal);
        foreach (var folder in files.GroupBy(x => x.Path[..x.Path.IndexOf('/')], StringComparer.Ordinal)
            .Where(x => x.Key != CommonFolder && FolderName().IsMatch(x.Key)))
        {
            var entries = folder.Select(x => (x.Resource, x.Path, Match: EntryScript().Match(x.Path))).Where(x => x.Match.Success).ToArray();
            if (entries.Length != 1) { continue; }
            var entry = entries[0];
            registered.Add("clarity." + folder.Key, new Family(folder.Key, entry.Match.Groups["name"].Value,
                [(entry.Resource, entry.Path), .. folder.Where(x => x.Path != entry.Path).OrderBy(x => x.Path, StringComparer.Ordinal), .. common]));
        }
        families = registered;
    }

    public static ScriptBundleRegistry FromAssembly(Assembly assembly) =>
        new(assembly.GetManifestResourceNames(), name => assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("A published script component is missing."));

    public IReadOnlyCollection<string> ProducerIds => families.Keys.Order(StringComparer.Ordinal).ToArray();

    public bool Supports(string producerId) => families.ContainsKey(producerId);

    public string? Command(string producerId) => families.TryGetValue(producerId, out var family)
        ? $@".\{family.Folder}\Collect-{family.Name}.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip" : null;

    public byte[]? Create(string producerId) => families.TryGetValue(producerId, out var family) ? Zip(family) : null;

    // The caller owns the version check: the bundle always contains the scripts embedded in this build.
    public ScriptBundleFile? Build(string producerId, string version) => families.TryGetValue(producerId, out var family)
        ? new(Zip(family), $"Clarity.Discovery.{family.Name}.{version}.zip") : null;

    /// <summary>The validated PowerShell producers with a published bundle for an environment kind.</summary>
    public IReadOnlyList<CatalogProducer> Producers(DiscoveryCatalog catalog, string environmentKind) =>
        catalog.Families.Where(x => x.EnvironmentKind == environmentKind).SelectMany(x => x.Producers)
            .Where(x => x.Channel == "PowerShell" && x.Readiness == "SyntheticValidated" && !x.Preview && Supports(x.Id)).ToArray();

    /// <summary>The single producer that covers every requested section; sections of different families cannot be mixed.</summary>
    public CatalogProducer Select(DiscoveryCatalog catalog, string environmentKind, IReadOnlyCollection<string> sections)
    {
        var candidates = sections.Count == 0 ? [] : Producers(catalog, environmentKind)
            .Where(x => sections.All(id => catalog.Sections.TryGetValue(id, out var section) && section.ProducerIds.Contains(x.Id))).ToArray();
        return candidates.Length == 1 ? candidates[0] : throw new ArgumentException("Invalid script request scope or sections.");
    }

    private byte[] Zip(Family family)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (resource, name) in family.Files)
            {
                var entry = zip.CreateEntry(name);
                entry.LastWriteTime = Timestamp;
                using var source = open(resource);
                using var destination = entry.Open();
                source.CopyTo(destination);
            }
        }
        return output.ToArray();
    }

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex FolderName();

    [GeneratedRegex(@"^[a-z0-9]+/Collect-(?<name>[A-Za-z0-9]+)\.ps1$")]
    private static partial Regex EntryScript();
}

public static class ScriptBundle
{
    public static ScriptBundleRegistry Registry { get; } = ScriptBundleRegistry.FromAssembly(typeof(ScriptBundle).Assembly);

    public static bool Supports(string producerId) => Registry.Supports(producerId);

    public static ScriptBundleFile? Build(string producerId, string version) => Registry.Build(producerId, version);

    public static string? Command(string producerId) => Registry.Command(producerId);

    public static byte[] BuildAd() => Registry.Create("clarity.ad") ?? throw new InvalidOperationException("The AD script bundle is missing.");
}
