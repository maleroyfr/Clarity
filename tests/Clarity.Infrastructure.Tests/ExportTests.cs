using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Clarity.Contracts;
using Clarity.Core;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class ExportTests
{
    private const string FormulaReason = "=HYPERLINK(\"http://example.invalid\")";

    [Fact]
    public void WorkbookKeepsFormulaLikeTextVerbatimAndCsvNeutralizesIt()
    {
        var date = new DateTimeOffset(2026, 2, 1, 8, 30, 0, TimeSpan.Zero);
        var environment = Guid.NewGuid();
        ExportUnit[] units = [new(Guid.NewGuid(), environment, "forest.example.test", "ad.users", "{}", CoverageStatus.Complete, date, Guid.NewGuid()),
            new(Guid.NewGuid(), environment, "forest.example.test", "ad.groups", "{}", CoverageStatus.Partial, date, Guid.NewGuid())];
        ExportGap[] gaps = [new(environment, "forest.example.test", "ad.computers")];
        var id = Guid.NewGuid();
        ExportInventoryRow[] inventory = [new(id, "forest.example.test", "user", "+Synthetic Alice", "alice@example.test", "alice@example.test",
            "example.test", null, true, InventoryPresence.Observed, date, "ad.users", date, ScopeValue.ToArbitrate, "Vague 1", null, FormulaReason, date,
            false, false)];
        ExportExceptionRow[] exceptions = [new(ExportExceptionCategory.CoverageGap, "forest.example.test", null, null, null, "ad.groups — Partial", date),
            new(ExportExceptionCategory.CoverageGap, "forest.example.test", null, null, null, "ad.computers — Export_NotCollected", null),
            new(ExportExceptionCategory.ToArbitrate, "forest.example.test", "user", "+Synthetic Alice", id, FormulaReason, date)];
        var document = new ExportDocument(new ExportHeader("Synthetic audit", ProjectScenario.TenantToTenant, EnvironmentRole.Source,
                [("Export_Col_Wave", "@wave")], "author", date, date, new string('a', 64)), units, gaps, inventory, exceptions, []);
        var text = new KeyText();
        ExportWriter.Reconciliation(document).ShouldAllBe(x => x.Reconciled);

        using var workbookStream = new MemoryStream();
        ExportWriter.WriteWorkbook(document, text, workbookStream);
        workbookStream.Position = 0;
        using var workbook = new XLWorkbook(workbookStream);
        workbook.Worksheets.Select(w => w.Name).ShouldBe(["Export_Sheet_Summary", "Export_Sheet_Inventory", "Export_Sheet_Exceptions"]);
        var sheet = workbook.Worksheet("Export_Sheet_Inventory");
        sheet.LastRowUsed(XLCellsUsedOptions.Contents)!.RowNumber().ShouldBe(2);
        var name = sheet.Cell(2, 3);
        (name.GetString(), name.HasFormula, name.Style.IncludeQuotePrefix).ShouldBe(("+Synthetic Alice", false, true));
        var reason = sheet.Cell(2, 16);
        (reason.GetString(), reason.HasFormula, reason.Style.IncludeQuotePrefix).ShouldBe((FormulaReason, false, true));
        sheet.Cell(2, 13).GetString().ShouldBe("ScopeDecision_ToArbitrate");
        workbook.Worksheet("Export_Sheet_Exceptions").LastRowUsed(XLCellsUsedOptions.Contents)!.RowNumber().ShouldBe(4);
        var summary = SummaryValues(workbook);
        summary["Export_Check_Coverage"].ShouldBe("Export_Reconciled");
        summary["ExportException_CoverageGap"].ShouldBe("2");
        summary["Export_Col_Wave"].ShouldBe("@wave");
        workbook.Worksheets.SelectMany(w => w.CellsUsed(XLCellsUsedOptions.Contents)).ShouldAllBe(c => !c.HasFormula);

        using var csv = new MemoryStream();
        ExportWriter.WriteInventoryCsv(document, text, csv);
        var bytes = csv.ToArray();
        bytes[..3].ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        lines.Length.ShouldBe(3);
        lines[0].ShouldStartWith("Export_Col_Environment;Export_Col_ObjectType;");
        lines[1].ShouldContain(";\"'+Synthetic Alice\";alice@example.test;");
        lines[1].ShouldContain(";\"'=HYPERLINK(\"\"http://example.invalid\"\")\";2026-02-01T08:30:00Z;");
    }

    [SqlFact]
    public async Task ExportGeneratesReconciledFilesAuditsDownloadsAndIsAuthorizedIsolatedAndIdempotent()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var ids = await ScopeTests.PublishAsync(fixture);
        var scope = ScopeTests.Service(fixture);
        var project = fixture.Project.Id;
        await scope.ApplyAsync(project, new(Guid.NewGuid(), ScopeValue.Include, "Vague 1", "Synthetic wave", ObjectIds: [ids["alice"], ids["bob"]]), 2);
        await scope.ApplyAsync(project, new(Guid.NewGuid(), ScopeValue.ToArbitrate, null, FormulaReason, ObjectIds: [ids["carol"]]), 1);
        var store = new MemoryExportStore();
        var exports = Service(fixture, store);
        var request = new ExportRequest(Guid.NewGuid(), new ExportFilter(EnvironmentRole.Source, Type: "user"));

        var id = await exports.LaunchAsync(project, request);
        (await exports.LaunchAsync(project, request)).ShouldBe(id);
        await Should.ThrowAsync<ConflictException>(() => exports.LaunchAsync(project, request with { Filter = new(EnvironmentRole.Source) }));
        (await exports.PageAsync(project, EnvironmentRole.Source, new())).Items.ShouldHaveSingleItem().State.ShouldBe(CollectionRunState.Pending);
        (await ProcessAsync(fixture, store)).ShouldBeTrue();

        var row = (await exports.PageAsync(project, EnvironmentRole.Source, new())).Items.ShouldHaveSingleItem();
        (row.State, row.ErrorCode, row.ObjectCount, row.MappingCount).ShouldBe((CollectionRunState.Completed, (string?)null, (int?)5, (int?)0));
        row.CoverageGaps.ShouldNotBeNull().ShouldBeGreaterThan(0);
        row.ExceptionCount.ShouldBe(row.CoverageGaps + 1);
        row.Artifacts.Select(a => (a.Kind, a.RowCount)).ShouldBe([(ExportArtifactKind.Workbook, 5), (ExportArtifactKind.InventoryCsv, 5),
            (ExportArtifactKind.MappingsCsv, 0)]);
        row.Artifacts.Select(a => a.FileName).ShouldAllBe(n => n.StartsWith("clarity-source-", StringComparison.Ordinal));

        var workbookView = row.Artifacts[0];
        var download = await exports.DownloadAsync(project, id, workbookView.Id);
        download.ContentType.ShouldBe(ExportWriter.WorkbookContentType);
        using var buffer = new MemoryStream();
        await using (download.Content) { await download.Content.CopyToAsync(buffer); }
        Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())).ShouldBe(workbookView.ContentHash);
        buffer.Position = 0;
        using (var workbook = new XLWorkbook(buffer))
        {
            var inventory = workbook.Worksheet("Export_Sheet_Inventory");
            inventory.LastRowUsed(XLCellsUsedOptions.Contents)!.RowNumber().ShouldBe(6);
            var carol = inventory.RowsUsed(XLCellsUsedOptions.Contents).Single(r => r.Cell(3).GetString() == "Carol");
            (carol.Cell(13).GetString(), carol.Cell(16).GetString(), carol.Cell(16).HasFormula).ShouldBe(("ScopeDecision_ToArbitrate", FormulaReason, false));
            inventory.RowsUsed(XLCellsUsedOptions.Contents).Single(r => r.Cell(3).GetString() == "Alice").Cell(14).GetString().ShouldBe("Vague 1");
            var exceptions = workbook.Worksheet("Export_Sheet_Exceptions");
            exceptions.LastRowUsed(XLCellsUsedOptions.Contents)!.RowNumber().ShouldBe(row.ExceptionCount!.Value + 1);
            exceptions.RowsUsed(XLCellsUsedOptions.Contents).Count(r => r.Cell(1).GetString() == "ExportException_ToArbitrate").ShouldBe(1);
            SummaryValues(workbook).Where(x => x.Key.StartsWith("Export_Check_", StringComparison.Ordinal)).ShouldAllBe(x => x.Value == "Export_Reconciled");
        }
        var csv = await exports.DownloadAsync(project, id, row.Artifacts[1].Id);
        using var reader = new StreamReader(csv.Content, Encoding.UTF8);
        (await reader.ReadToEndAsync()).ShouldContain(";\"'=HYPERLINK(\"\"http://example.invalid\"\")\";");

        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        }
        await Should.ThrowAsync<AccessDeniedException>(() => exports.LaunchAsync(project, new(Guid.NewGuid(), new(EnvironmentRole.Source))));
        (await exports.DownloadAsync(project, id, row.Artifacts[2].Id)).FileName.ShouldEndWith("-mappings.csv");

        var other = await fixture.OtherWorkspaceAsync();
        var foreign = new ExportService(other.Factory, other.Access, other.Scope, store, DiscoveryCatalog.Load());
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.DownloadAsync(project, id, workbookView.Id));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.PageAsync(project, EnvironmentRole.Source, new()));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.DownloadAsync(other.Project.Id, id, workbookView.Id));

        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.AuditEvents.CountAsync(x => x.Action == "ExportRequested" && x.ResourceId == id)).ShouldBe(1);
            (await db.AuditEvents.CountAsync(x => x.Action == "ExportGenerated" && x.ResourceId == id)).ShouldBe(1);
            (await db.AuditEvents.CountAsync(x => x.Action == "ExportDownloaded")).ShouldBe(3);
            (await db.ExportJobs.SingleAsync()).AuthorObjectId.ShouldBe(fixture.Identity.ObjectId);
        }
        await using var outside = PersistenceTests.Create(fixture.ConnectionString, []);
        (await outside.ExportJobs.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.ExportArtifacts.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [SqlFact]
    public async Task SupersededSelectionFailsWithoutFilesAndPartialCoverageIsReportedNotTreatedAsAbsence()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var ids = await ScopeTests.PublishAsync(fixture);
        var scope = ScopeTests.Service(fixture);
        var project = fixture.Project.Id;
        var store = new MemoryExportStore();
        var exports = Service(fixture, store);
        await Should.ThrowAsync<AccessDeniedException>(() => exports.LaunchAsync(project,
            new(Guid.NewGuid(), new(EnvironmentRole.Target, fixture.Environment.Id))));

        var stale = await exports.LaunchAsync(project, new(Guid.NewGuid(), new(EnvironmentRole.Source)));
        await scope.ApplyAsync(project, new(Guid.NewGuid(), ScopeValue.Include, "Vague 1", "Synthetic wave", ObjectIds: [ids["alice"], ids["sales"]]), 2);
        (await ProcessAsync(fixture, store)).ShouldBeTrue();
        var failed = (await exports.PageAsync(project, EnvironmentRole.Source, new())).Items.ShouldHaveSingleItem();
        (failed.Id, failed.State, failed.ErrorCode, failed.GeneratedAtUtc, failed.Artifacts.Count)
            .ShouldBe((stale, CollectionRunState.Failed, (string?)"SelectionSuperseded", (DateTimeOffset?)null, 0));
        store.Count.ShouldBe(0);

        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.SectionRuns.Where(s => s.CapabilityId == "ad.groups").ExecuteUpdateAsync(update => update.SetProperty(s => s.Coverage, CoverageStatus.Partial));
        }
        var filtered = await exports.LaunchAsync(project, new(Guid.NewGuid(),
            new(EnvironmentRole.Source, Decision: ExportDecisionFilter.Include, Wave: "Vague 1")));
        (await ProcessAsync(fixture, store)).ShouldBeTrue();
        var row = (await exports.PageAsync(project, EnvironmentRole.Source, new())).Items.Single(x => x.Id == filtered);
        (row.State, row.ObjectCount).ShouldBe((CollectionRunState.Completed, (int?)2));
        await using var content = (await exports.DownloadAsync(project, filtered, row.Artifacts.Single(a => a.Kind == ExportArtifactKind.Workbook).Id)).Content;
        using var workbook = new XLWorkbook(content);
        var inventory = workbook.Worksheet("Export_Sheet_Inventory").RowsUsed(XLCellsUsedOptions.Contents).Skip(1).ToList();
        inventory.Select(r => r.Cell(3).GetString()).Order(StringComparer.Ordinal).ShouldBe(["Alice", "sales"]);
        inventory.ShouldAllBe(r => r.Cell(9).GetString() == "Observed");
        workbook.Worksheet("Export_Sheet_Exceptions").RowsUsed(XLCellsUsedOptions.Contents)
            .Any(r => r.Cell(1).GetString() == "ExportException_CoverageGap" && r.Cell(5).GetString() == "ad.groups — Partial").ShouldBeTrue();
        SummaryValues(workbook)["Export_Col_Wave"].ShouldBe("Vague 1");
    }

    private static ExportService Service(DiscoveryFixture fixture, IExportStore store) =>
        new(fixture.Factory, fixture.Access, fixture.Scope, store, DiscoveryCatalog.Load());

    private static Task<bool> ProcessAsync(DiscoveryFixture fixture, IExportStore store) =>
        new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, TimeProvider.System),
            [new ExportJobHandler(store, new KeyText(), DiscoveryCatalog.Load())], NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default);

    private static Dictionary<string, string> SummaryValues(XLWorkbook workbook) =>
        workbook.Worksheet("Export_Sheet_Summary").RowsUsed(XLCellsUsedOptions.Contents).Where(r => !r.Cell(1).IsEmpty())
            .GroupBy(r => r.Cell(1).GetString()).ToDictionary(g => g.Key, g => g.First().Cell(2).GetString());

    private sealed class KeyText : IExportText
    {
        public string this[string key] => key;
    }
}

internal sealed class MemoryExportStore : IExportStore
{
    private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);
    public int Count => files.Count;
    public bool IsConfigured => true;

    public async Task<StoredArtifact> SaveAsync(Guid workspaceId, Guid exportId, string extension, string contentType, Stream source,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await source.CopyToAsync(stream, cancellationToken);
        var bytes = stream.ToArray();
        var name = $"{workspaceId:N}/{exportId:N}/{Guid.NewGuid():N}{extension}";
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
