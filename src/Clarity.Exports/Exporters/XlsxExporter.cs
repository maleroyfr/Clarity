using Clarity.Domain.Mappings;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;
using ClosedXML.Excel;

namespace Clarity.Exports.Exporters;

internal sealed class XlsxExporter
{
    private static readonly XLColor HeaderBg    = XLColor.FromHtml("#2563EB");
    private static readonly XLColor HeaderFg    = XLColor.White;

    public Stream Export(
        IReadOnlyList<InventoryObject> objects,
        ExportRequest request,
        ExportMetadata metadata)
    {
        var workbook = new XLWorkbook();

        if (request.IncludeMetadata)
            WriteSummarySheet(workbook, metadata, objects);

        var byWorkload = objects
            .GroupBy(o => WorkloadAreaMapping.GetWorkloadArea(o.ObjectType))
            .OrderBy(g => g.Key.ToString());

        foreach (var group in byWorkload)
        {
            var sheetName = SanitizeSheetName(group.Key.ToString());
            var ws = workbook.Worksheets.Add(sheetName);
            WriteDataSheet(ws, group.ToList());
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private void WriteSummarySheet(
        XLWorkbook workbook,
        ExportMetadata metadata,
        IReadOnlyList<InventoryObject> objects)
    {
        var ws = workbook.Worksheets.Add("Summary");
        ws.Cell(1, 1).Value = "Clarity Export Summary";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        ws.Cell(3, 1).Value = "Snapshot ID";
        ws.Cell(3, 2).Value = metadata.SnapshotId.ToString();
        ws.Cell(4, 1).Value = "Snapshot Name";
        ws.Cell(4, 2).Value = metadata.SnapshotName;
        ws.Cell(5, 1).Value = "Exported At";
        ws.Cell(5, 2).Value = metadata.ExportedAt.ToString("O");
        ws.Cell(6, 1).Value = "Format";
        ws.Cell(6, 2).Value = metadata.Format.ToString();
        ws.Cell(7, 1).Value = "Total Objects";
        ws.Cell(7, 2).Value = metadata.TotalObjectsExported;

        ws.Cell(9, 1).Value = "Object Type";
        ws.Cell(9, 2).Value = "Count";
        StyleHeaderCell(ws.Cell(9, 1));
        StyleHeaderCell(ws.Cell(9, 2));

        var countsByType = objects
            .GroupBy(o => o.ObjectType)
            .OrderBy(g => g.Key.ToString())
            .ToList();

        var row = 10;
        foreach (var group in countsByType)
        {
            ws.Cell(row, 1).Value = group.Key.ToString();
            ws.Cell(row, 2).Value = group.Count();
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void WriteDataSheet(IXLWorksheet ws, IReadOnlyList<InventoryObject> objects)
    {
        if (objects.Count == 0)
        {
            ws.Cell(1, 1).Value = "No data";
            return;
        }

        var allPropertyKeys = objects
            .SelectMany(o => o.Properties.Keys)
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        // Header row
        ws.Cell(1, 1).Value = "ObjectType";
        ws.Cell(1, 2).Value = "ExternalId";
        ws.Cell(1, 3).Value = "DisplayName";
        for (var i = 0; i < allPropertyKeys.Count; i++)
            ws.Cell(1, 4 + i).Value = allPropertyKeys[i];

        // Style header row
        var headerRange = ws.Range(1, 1, 1, 3 + allPropertyKeys.Count);
        foreach (var cell in headerRange.Cells())
            StyleHeaderCell(cell);

        // Data rows
        var row = 2;
        foreach (var obj in objects)
        {
            ws.Cell(row, 1).Value = obj.ObjectType.ToString();
            ws.Cell(row, 2).Value = obj.ExternalId;
            ws.Cell(row, 3).Value = obj.DisplayName ?? string.Empty;
            for (var i = 0; i < allPropertyKeys.Count; i++)
                ws.Cell(row, 4 + i).Value = obj.GetProperty(allPropertyKeys[i]) ?? string.Empty;
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void StyleHeaderCell(IXLCell cell)
    {
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = HeaderBg;
        cell.Style.Font.FontColor = HeaderFg;
    }

    private static string SanitizeSheetName(string name)
    {
        // Excel sheet names cannot exceed 31 chars or contain certain chars
        var invalid = new[] { '/', '\\', '?', '*', '[', ']', ':' };
        foreach (var c in invalid)
            name = name.Replace(c, '_');
        return name.Length > 31 ? name[..31] : name;
    }
}
