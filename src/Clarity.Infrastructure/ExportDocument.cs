using System.Globalization;
using System.Text;
using System.Xml;
using Clarity.Contracts;
using Clarity.Core;
using ClosedXML.Excel;

namespace Clarity.Infrastructure;

public enum ExportExceptionCategory
{
    CoverageGap, ConflictingValue, ReviewFlag, ToArbitrate, IncludedNotObserved, MappingPending, TargetComparison, Collision, SharedDependency
}

public sealed record ExportInventoryRow(Guid Id, string Environment, string Type, string? DisplayName, string? Upn, string? Smtp, string? Domain,
    string? Site, bool? Enabled, InventoryPresence Presence, DateTimeOffset? LastObservedAtUtc, string? SourceCapability, DateTimeOffset? SourceDate,
    ScopeValue? Decision, string? Wave, string? Population, string? Reason, DateTimeOffset? DecidedAtUtc, bool NeedsReview, bool Conflicting);

public sealed record ExportExceptionRow(ExportExceptionCategory Category, string? Environment, string? Type, string? Label, Guid? ObjectId,
    string Detail, DateTimeOffset? Date);

public sealed record ExportMappingRow(Guid Id, MappingKind Kind, MappingState State, AssociationRule Rule, string LeftEnvironment,
    Guid LeftObjectId, string? LeftLabel, string RightEnvironment, Guid RightObjectId, string? RightLabel, long Revision, bool ConsultantQualified,
    string? Reason, DateTimeOffset? Date);

public sealed record ExportHeader(string Project, ProjectScenario Scenario, EnvironmentRole Side, IReadOnlyList<(string Label, string Value)> Filters,
    string Author, DateTimeOffset AsOfUtc, DateTimeOffset GeneratedAtUtc, string SelectionHash);

/// <summary>Everything one export writes, already filtered and ordered; the writers only format it.</summary>
public sealed record ExportDocument(ExportHeader Header, IReadOnlyList<ExportUnit> Units, IReadOnlyList<ExportGap> Uncollected,
    IReadOnlyList<ExportInventoryRow> Inventory, IReadOnlyList<ExportExceptionRow> Exceptions, IReadOnlyList<ExportMappingRow> Mappings)
{
    public int CoverageGaps => Exceptions.Count(e => e.Category == ExportExceptionCategory.CoverageGap);
}

/// <summary>
/// Writes the Excel workbook (summary, useful inventory, exceptions) and the semicolon-separated CSV lists. Text that a spreadsheet would
/// evaluate is never written as a formula: workbook cells keep the verbatim text with an explicit text prefix and CSV fields are neutralized.
/// </summary>
public static class ExportWriter
{
    public const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string CsvContentType = "text/csv";
    private const string DateFormat = "yyyy-mm-dd hh:mm:ss";
    private const int MaxCellText = 32767;

    private sealed record Column<T>(string Key, Func<T, object?> Value, double Width = 18);

    private static Column<ExportInventoryRow>[] InventoryColumns(IExportText text) =>
    [
        new("Export_Col_Environment", r => r.Environment),
        new("Export_Col_ObjectType", r => Label(text, "ObjectType_", r.Type)),
        new("Export_Col_DisplayName", r => r.DisplayName, 32),
        new("Export_Col_Upn", r => r.Upn, 32),
        new("Export_Col_Smtp", r => r.Smtp, 32),
        new("Export_Col_Domain", r => r.Domain),
        new("Export_Col_Site", r => r.Site),
        new("Export_Col_Enabled", r => r.Enabled),
        new("Export_Col_Presence", r => text[r.Presence.ToString()]),
        new("Export_Col_LastObserved", r => r.LastObservedAtUtc, 20),
        new("Export_Col_SourceCapability", r => r.SourceCapability, 24),
        new("Export_Col_SourceDate", r => r.SourceDate, 20),
        new("Export_Col_Decision", r => Decision(text, r.Decision)),
        new("Export_Col_Wave", r => r.Wave),
        new("Export_Col_Population", r => r.Population, 24),
        new("Export_Col_Reason", r => r.Reason, 40),
        new("Export_Col_DecidedAt", r => r.DecidedAtUtc, 20),
        new("Export_Col_NeedsReview", r => r.NeedsReview),
        new("Export_Col_Conflicting", r => r.Conflicting),
        new("Export_Col_ObjectId", r => r.Id.ToString(), 38)
    ];

    private static Column<ExportExceptionRow>[] ExceptionColumns(IExportText text) =>
    [
        new("Export_Col_Category", r => text["ExportException_" + r.Category], 28),
        new("Export_Col_Environment", r => r.Environment),
        new("Export_Col_ObjectType", r => r.Type is null ? null : Label(text, "ObjectType_", r.Type)),
        new("Export_Col_DisplayName", r => r.Label, 32),
        new("Export_Col_Detail", r => r.Detail, 60),
        new("Export_Col_Date", r => r.Date, 20),
        new("Export_Col_ObjectId", r => r.ObjectId?.ToString(), 38)
    ];

    private static Column<ExportMappingRow>[] MappingColumns(IExportText text) =>
    [
        new("Export_Col_MappingKind", r => text["Export_MappingKind_" + r.Kind]),
        new("Export_Col_State", r => text[r.State.ToString()]),
        new("Export_Col_Rule", r => text[r.Rule.ToString()]),
        new("Export_Col_LeftEnvironment", r => r.LeftEnvironment),
        new("Export_Col_LeftLabel", r => r.LeftLabel),
        new("Export_Col_RightEnvironment", r => r.RightEnvironment),
        new("Export_Col_RightLabel", r => r.RightLabel),
        new("Export_Col_Revision", r => r.Revision),
        new("Export_Col_ConsultantQualified", r => r.ConsultantQualified),
        new("Export_Col_Reason", r => r.Reason),
        new("Export_Col_Date", r => r.Date),
        new("Export_Col_LeftObjectId", r => r.LeftObjectId.ToString()),
        new("Export_Col_RightObjectId", r => r.RightObjectId.ToString()),
        new("Export_Col_MappingId", r => r.Id.ToString())
    ];

    public static void WriteWorkbook(ExportDocument document, IExportText text, Stream target)
    {
        using var workbook = new XLWorkbook();
        Summary(workbook.Worksheets.Add(SheetName(text["Export_Sheet_Summary"])), document, text);
        Table(workbook.Worksheets.Add(SheetName(text["Export_Sheet_Inventory"])), InventoryColumns(text), document.Inventory, text);
        Table(workbook.Worksheets.Add(SheetName(text["Export_Sheet_Exceptions"])), ExceptionColumns(text), document.Exceptions, text);
        workbook.SaveAs(target);
    }

    public static void WriteInventoryCsv(ExportDocument document, IExportText text, Stream target) =>
        Csv(target, InventoryColumns(text), document.Inventory, text);

    public static void WriteMappingsCsv(ExportDocument document, IExportText text, Stream target) =>
        Csv(target, MappingColumns(text), document.Mappings, text);

    /// <summary>The reconciliation checks shown in the summary: every total equals the sum of its breakdown and the number of written rows.</summary>
    public static IReadOnlyList<(string Key, bool Reconciled)> Reconciliation(ExportDocument document)
    {
        var count = document.Inventory.Count;
        return
        [
            ("Export_Check_Decisions", Enum.GetValues<ScopeValue>().Sum(v => document.Inventory.Count(r => r.Decision == v))
                + document.Inventory.Count(r => r.Decision is null) == count),
            ("Export_Check_Presence", Enum.GetValues<InventoryPresence>().Sum(p => document.Inventory.Count(r => r.Presence == p)) == count),
            ("Export_Check_Exceptions", Enum.GetValues<ExportExceptionCategory>().Sum(c => document.Exceptions.Count(e => e.Category == c))
                == document.Exceptions.Count),
            ("Export_Check_Coverage", document.CoverageGaps == document.Units.Count(u => u.Status is CoverageStatus.Partial or CoverageStatus.Failed)
                + document.Uncollected.Count)
        ];
    }

    private static void Summary(IXLWorksheet sheet, ExportDocument document, IExportText text)
    {
        var header = document.Header;
        var row = 1;
        void Line(string label, object? value)
        {
            Set(sheet.Cell(row, 1), label, text);
            sheet.Cell(row, 1).Style.Font.Bold = true;
            Set(sheet.Cell(row, 2), value, text);
            row++;
        }
        void Title(string key)
        {
            row++;
            Set(sheet.Cell(row, 1), text[key], text);
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 1).Style.Font.FontSize = 13;
            row++;
        }
        Set(sheet.Cell(row, 1), text["Export_Title"], text);
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 15;
        row += 2;
        Line(text["Export_Project"], header.Project);
        Line(text["Scenario"], text[header.Scenario.ToString()]);
        Line(text["Export_Side"], text[header.Side.ToString()]);
        Line(text["Export_Author"], header.Author);
        Line(text["Export_AsOf"], header.AsOfUtc);
        Line(text["Export_GeneratedAt"], header.GeneratedAtUtc);
        Line(text["Export_SelectionHash"], header.SelectionHash);

        Title("Export_Filters");
        foreach (var (label, value) in header.Filters) { Line(label, value); }

        Title("Export_Counters");
        Line(text["Export_Objects"], document.Inventory.Count);
        foreach (var value in Enum.GetValues<ScopeValue>())
        {
            Line(Decision(text, value), document.Inventory.Count(r => r.Decision == value));
        }
        Line(Decision(text, null), document.Inventory.Count(r => r.Decision is null));
        foreach (var presence in Enum.GetValues<InventoryPresence>())
        {
            Line(text[presence.ToString()], document.Inventory.Count(r => r.Presence == presence));
        }
        Line(text["Export_ExceptionTotal"], document.Exceptions.Count);
        foreach (var category in Enum.GetValues<ExportExceptionCategory>())
        {
            Line(text["ExportException_" + category], document.Exceptions.Count(e => e.Category == category));
        }
        Line(text["Export_Mappings"], document.Mappings.Count);
        Line(text["Export_InventoryCsvRows"], document.Inventory.Count);
        Line(text["Export_MappingsCsvRows"], document.Mappings.Count);
        foreach (var (key, reconciled) in Reconciliation(document))
        {
            Line(text[key], reconciled ? text["Export_Reconciled"] : text["Export_NotReconciled"]);
        }

        Title("Coverage");
        Set(sheet.Cell(row++, 1), text["Export_PartialNote"], text);
        string[] coverageHeaders = ["Export_Col_Environment", "Export_Col_Capability", "Export_Col_Coverage", "Export_Col_Date", "Export_Col_Run", "Export_Col_Scope"];
        for (var i = 0; i < coverageHeaders.Length; i++)
        {
            Set(sheet.Cell(row, i + 1), text[coverageHeaders[i]], text);
            sheet.Cell(row, i + 1).Style.Font.Bold = true;
        }
        row++;
        foreach (var unit in document.Units)
        {
            Set(sheet.Cell(row, 1), unit.Environment, text);
            Set(sheet.Cell(row, 2), unit.Capability, text);
            Set(sheet.Cell(row, 3), text[unit.Status.ToString()], text);
            Set(sheet.Cell(row, 4), unit.Date, text);
            Set(sheet.Cell(row, 5), unit.RunId.ToString(), text);
            Set(sheet.Cell(row, 6), unit.ScopeJson, text);
            row++;
        }
        foreach (var gap in document.Uncollected)
        {
            Set(sheet.Cell(row, 1), gap.Environment, text);
            Set(sheet.Cell(row, 2), gap.Capability, text);
            Set(sheet.Cell(row, 3), text["Export_NotCollected"], text);
            row++;
        }
        sheet.Column(1).Width = 44;
        sheet.Column(2).Width = 40;
        for (var i = 3; i <= 6; i++) { sheet.Column(i).Width = 22; }
    }

    private static void Table<T>(IXLWorksheet sheet, Column<T>[] columns, IReadOnlyList<T> rows, IExportText text)
    {
        for (var c = 0; c < columns.Length; c++)
        {
            Set(sheet.Cell(1, c + 1), text[columns[c].Key], text);
            sheet.Cell(1, c + 1).Style.Font.Bold = true;
            sheet.Column(c + 1).Width = columns[c].Width;
        }
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Length; c++)
            {
                Set(sheet.Cell(r + 2, c + 1), columns[c].Value(rows[r]), text);
            }
        }
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, Math.Max(rows.Count + 1, 2), columns.Length).SetAutoFilter();
    }

    private static void Set(IXLCell cell, object? value, IExportText text)
    {
        switch (value)
        {
            case null:
                return;
            case DateTimeOffset date:
                cell.Value = date.UtcDateTime;
                cell.Style.DateFormat.Format = DateFormat;
                return;
            case bool flag:
                Set(cell, flag ? text["Export_Yes"] : text["Export_No"], text);
                return;
            case int number:
                cell.Value = number;
                return;
            case long number:
                cell.Value = number;
                return;
            case string textValue:
                var clean = CellText(textValue);
                cell.Value = clean;
                if (SpreadsheetText.IsFormulaLike(clean)) { cell.Style.IncludeQuotePrefix = true; }
                return;
            default:
                throw new ArgumentException("Unsupported export cell value.");
        }
    }

    private static void Csv<T>(Stream target, Column<T>[] columns, IReadOnlyList<T> rows, IExportText text)
    {
        using var writer = new StreamWriter(target, new UTF8Encoding(true), 65536, leaveOpen: true);
        writer.Write(SpreadsheetText.CsvLine(columns.Select(c => text[c.Key])));
        foreach (var row in rows)
        {
            writer.Write(SpreadsheetText.CsvLine(columns.Select(c => c.Value(row) switch
            {
                null => null,
                DateTimeOffset date => date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                bool flag => flag ? text["Export_Yes"] : text["Export_No"],
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString()
            })));
        }
        writer.Flush();
    }

    private static string Label(IExportText text, string prefix, string value)
    {
        var key = prefix + value;
        var label = text[key];
        return label == key ? value : label;
    }

    internal static string Decision(IExportText text, ScopeValue? value) =>
        value is { } decision ? text["ScopeDecision_" + decision] : text["Export_Undecided"];

    private static string SheetName(string name)
    {
        var clean = new string(name.Where(c => c is not (':' or '\\' or '/' or '?' or '*' or '[' or ']')).ToArray()).Trim('\'');
        return clean.Length is 0 ? "Sheet" : clean[..Math.Min(31, clean.Length)];
    }

    /// <summary>Workbook cells hold XML text: characters XML cannot carry are replaced and text is limited to the Excel cell maximum.</summary>
    internal static string CellText(string value)
    {
        if (value.Length > MaxCellText) { value = value[..MaxCellText]; }
        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (XmlConvert.IsXmlChar(c)) { builder?.Append(c); continue; }
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], c))
            {
                builder?.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }
            builder ??= new StringBuilder(value.Length).Append(value, 0, i);
            builder.Append('\uFFFD');
        }
        return builder?.ToString() ?? value;
    }
}
