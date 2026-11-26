using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class ZipImportTests
{
    [Fact]
    public async Task AdvertisedEntrySizesCannotBypassTheActualExpandedByteLimit()
    {
        var bytes = await BytesAsync(DiscoveryFixture.Package());
        var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(bytes.Length - 6));
        for (var entry = 0; entry < 4; entry++)
        {
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)).ShouldBe(0x02014b50u);
            var local = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 42));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(local + 22), 1);
            offset += 46 + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28))
                + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 30))
                + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 32));
        }
        var reader = new ZipPackageReader(new PackageValidator(), new PackageLimits(UncompressedBytes: 100));
        await Should.ThrowAsync<ContractException>(() => reader.ReadAsync(new MemoryStream(bytes)));
    }

    [Fact]
    public async Task ZipReaderRejectsUnsafeEntriesEncodingAndEveryResourceLimit()
    {
        var bytes = await BytesAsync(DiscoveryFixture.Package());
        var reader = new ZipPackageReader(new PackageValidator(), new PackageLimits());
        (await reader.ReadAsync(new MemoryStream(bytes), cancellationToken: TestContext.Current.CancellationToken)).Package.Objects.ShouldHaveSingleItem();
        foreach (var name in new[] { "../manifest.json", "MANIFEST.JSON", "folder/manifest.json", "script.ps1" })
        {
            var invalid = ReplaceEntry(bytes, "manifest.json", name, "{}"u8.ToArray());
            await Should.ThrowAsync<ContractException>(() => reader.ReadAsync(new MemoryStream(invalid)));
        }
        await Should.ThrowAsync<ContractException>(() => reader.ReadAsync(
            new MemoryStream(ReplaceEntry(bytes, "manifest.json", "manifest.json", [255]))));
        foreach (var limits in new[]
        {
            new PackageLimits(CompressedBytes: bytes.Length - 1),
            new PackageLimits(UncompressedBytes: 100),
            new PackageLimits(EntryBytes: 100),
            new PackageLimits(Lines: 0),
            new PackageLimits(LineCharacters: 10)
        })
        {
            await Should.ThrowAsync<ContractException>(() => new ZipPackageReader(new PackageValidator(), limits)
                .ReadAsync(new MemoryStream(bytes)));
        }
        (await Should.ThrowAsync<ContractException>(() => reader.ReadAsync(new MemoryStream(bytes), new string('0', 64))))
            .Code.ShouldBe("ContentHashMismatch");
    }

    [SqlFact]
    public async Task IdenticalParallelSeparateMachineUploadsRequireConfirmationAndIntegrateOnce()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var package = DiscoveryFixture.Package();
        var requestId = Guid.NewGuid();
        package = package with { Manifest = package.Manifest with { RequestId = requestId } };
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            db.CollectionRequests.Add(new CollectionRequest
            {
                Id = requestId,
                WorkspaceId = fixture.Workspace.Id,
                ProjectId = fixture.Project.Id,
                EnvironmentId = fixture.Environment.Id,
                ProducerId = package.Manifest.ProducerId,
                ScriptVersion = package.Manifest.ProducerVersion,
                ScopeJson = DiscoveryScope.Serialize(package.Manifest.Scope),
                ScopeHash = DiscoveryScope.Hash(package.Manifest.Scope),
                SectionsJson = JsonSerializer.Serialize(new[] { "ad.users" }),
                ActorTenantId = fixture.Identity.TenantId,
                ActorObjectId = fixture.Identity.ObjectId
            });
            await db.SaveChangesAsync();
        }
        var store = new MemoryArtifactStore();
        var imports = Service(fixture, store);
        var bytes = await BytesAsync(package);
        var ids = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => imports.UploadAsync(
            fixture.Project.Id, fixture.Environment.Id, new MemoryStream(bytes), "separate-machine.zip")));
        ids.Distinct().ShouldHaveSingleItem();
        store.Count.ShouldBe(1);
        var preview = await imports.PreviewAsync(fixture.Project.Id, ids[0]);
        preview.NewObjects.ShouldBe(1);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.ObjectObservations.CountAsync()).ShouldBe(0);
            (await db.CollectionJobs.CountAsync()).ShouldBe(0);
        }
        await Task.WhenAll(imports.ConfirmAsync(fixture.Project.Id, ids[0]), imports.ConfirmAsync(fixture.Project.Id, ids[0]));
        await Processor(fixture, store).ProcessOneAsync(default);
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.ObjectObservations.CountAsync()).ShouldBe(1);
        (await check.CollectionRuns.SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
        (await check.CollectionJobs.SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
        (await check.CollectionRequests.SingleAsync()).State.ShouldBe(CollectionRequestState.Integrated);
        await using var download = await imports.DownloadAsync(fixture.Project.Id, ids[0]);
        using var downloaded = new MemoryStream();
        await download.CopyToAsync(downloaded);
        downloaded.ToArray().ShouldBe(bytes);
    }

    [SqlFact]
    public async Task ConflictingRunWrongEnvironmentRequestAndWorkspaceAreRejectedBeforePublication()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var store = new MemoryArtifactStore();
        var imports = Service(fixture, store);
        var package = DiscoveryFixture.Package();
        var bytes = await BytesAsync(package);
        await imports.UploadAsync(fixture.Project.Id, fixture.Environment.Id, new MemoryStream(bytes), "valid.zip");
        var different = await BytesAsync(package with
        {
            Objects = [package.Objects[0] with
        {
            Attributes = new Dictionary<string, JsonElement> { ["displayName"] = JsonSerializer.SerializeToElement("Changed") }
        }]
        });
        await Should.ThrowAsync<ConflictException>(() => imports.UploadAsync(fixture.Project.Id, fixture.Environment.Id,
            new MemoryStream(different), "conflict.zip"));
        foreach (var manifest in new[]
        {
            package.Manifest with { RunId = Guid.NewGuid(), EnvironmentExternalId = "wrong.example.test" },
            package.Manifest with { RunId = Guid.NewGuid(), RequestId = Guid.NewGuid() }
        })
        {
            var wrong = await BytesAsync(package with { Manifest = manifest });
            await Should.ThrowAsync<ContractException>(() => imports.UploadAsync(fixture.Project.Id, fixture.Environment.Id,
                new MemoryStream(wrong), "wrong.zip"));
        }
        await Should.ThrowAsync<AccessDeniedException>(() => imports.UploadAsync(Guid.NewGuid(), fixture.Environment.Id,
            new MemoryStream(bytes), "other-workspace.zip"));
        store.Count.ShouldBe(1);
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.ObjectObservations.CountAsync()).ShouldBe(0);
        (await check.CollectionRuns.CountAsync()).ShouldBe(1);
    }

    [SqlFact]
    public async Task PartialPackageUsesCommonPublisherAndKeepsPriorValidData()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var initial = DiscoveryFixture.Package();
        var first = await fixture.StageAsync(initial);
        await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, first.Id, initial, initial.Results[0].SectionId);
        var package = DiscoveryFixture.Package("Partial refresh") with
        {
            Results =
            [
                initial.Results[0] with { Status = CoverageStatus.Partial, Warnings = [new SectionIssue("Limited", "Limited read scope.")] },
                new SectionResult("ad.groups:example.test", "ad.groups", CoverageStatus.Failed, initial.Manifest.Scope,
                    [], [], null, null, [], [new SectionIssue("AccessDenied", "Group read access is missing.")])
            ]
        };
        var store = new MemoryArtifactStore();
        var imports = Service(fixture, store);
        var id = await imports.UploadAsync(fixture.Project.Id, fixture.Environment.Id, new MemoryStream(await BytesAsync(package)), "partial.zip");
        await imports.ConfirmAsync(fixture.Project.Id, id);
        await Processor(fixture, store).ProcessOneAsync(default);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.ObjectObservations.CountAsync()).ShouldBe(2);
        (await db.InventoryObjects.CountAsync()).ShouldBe(1);
        (await db.SectionRuns.CountAsync(x => x.RunId == id && x.PublishedAtUtc != null)).ShouldBe(2);
    }

    internal static DiscoveryCatalog TestCatalog() => new(DiscoveryCatalog.Load().Families.Select(family => family with
    {
        Producers = family.Producers.Select(producer => producer.Id == "clarity.ad"
            ? producer with { Readiness = "SyntheticValidated", TestedVersions = ["Synthetic test fixture only"] } : producer).ToArray()
    }));

    internal static ImportService Service(DiscoveryFixture fixture, IArtifactStore store) => new(
        fixture.Factory, fixture.Access, fixture.Scope, store, new ZipPackageReader(new PackageValidator(), new PackageLimits()),
        new PackageLimits(), TestCatalog());

    internal static CollectionJobProcessor Processor(DiscoveryFixture fixture, IArtifactStore store) => new(
        new SqlJobQueue(fixture.ConnectionString, TimeProvider.System),
        [new ImportJobHandler(store, new ZipPackageReader(new PackageValidator(), new PackageLimits()), new PackageLimits(), TestCatalog(), new PackageValidator())],
        NullLogger<CollectionJobProcessor>.Instance);

    internal static async Task<byte[]> BytesAsync(DiscoveryPackage package)
    {
        using var stream = new MemoryStream();
        await PackageZipWriter.WriteAsync(package, stream);
        return stream.ToArray();
    }

    private static byte[] ReplaceEntry(byte[] source, string oldName, string name, byte[] content)
    {
        using var stream = new MemoryStream();
        stream.Write(source);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry(oldName)!.Delete();
            using var entry = zip.CreateEntry(name).Open();
            entry.Write(content);
        }
        return stream.ToArray();
    }
}

internal sealed class MemoryArtifactStore : IArtifactStore
{
    private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);
    public int Count => files.Count;

    public async Task<StoredArtifact> SaveAsync(Guid workspaceId, Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await source.CopyToAsync(stream, cancellationToken);
        var bytes = stream.ToArray();
        if (bytes.Length > maxBytes) { throw new ContractException("CompressedSizeLimit", "zip"); }
        var name = $"{workspaceId:N}/{Guid.NewGuid():N}.zip";
        files[name] = bytes;
        return new StoredArtifact(name, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);
    }
    public Task<Stream> OpenAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(blobName.StartsWith($"{workspaceId:N}/", StringComparison.Ordinal)
            ? new MemoryStream(files[blobName]) : throw new AccessDeniedException());
    public Task DeleteAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken)
    {
        if (!blobName.StartsWith($"{workspaceId:N}/", StringComparison.Ordinal)) { throw new AccessDeniedException(); }
        files.TryRemove(blobName, out _);
        return Task.CompletedTask;
    }
}
