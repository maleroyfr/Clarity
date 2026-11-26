using System.Text;

namespace Clarity.Contracts;

/// <summary>Bounds applied to an administrative report file before any interpretation.</summary>
public sealed record ReportLimits(long Bytes, int Rows, int Fields, int FieldCharacters)
{
    public static ReportLimits Default { get; } = new(16 * 1024 * 1024, 150_000, 100, 4096);
}

public sealed record CsvRow(int Line, IReadOnlyList<string> Fields);

public sealed record CsvTable(IReadOnlyList<string> Headers, IReadOnlyList<CsvRow> Rows);

/// <summary>
/// Strict RFC 4180 reader for comma-separated UTF-8 reports. File-level defects throw <see cref="ContractException"/>
/// with the physical line number; cell values are never copied into errors.
/// </summary>
public static class CsvReportReader
{
    public const string FileName = "report.csv";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static CsvTable Read(ReadOnlySpan<byte> content, ReportLimits limits)
    {
        if (content.Length > limits.Bytes) { throw new ContractException("ReportFileTooLarge", FileName); }
        if (content.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) { content = content[3..]; }
        else if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || content.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            throw new ContractException("ReportInvalidEncoding", FileName);
        }
        string text;
        try { text = StrictUtf8.GetString(content); }
        catch (DecoderFallbackException) { throw new ContractException("ReportInvalidEncoding", FileName); }
        if (text.Contains('\0', StringComparison.Ordinal)) { throw new ContractException("ReportInvalidEncoding", FileName); }

        var records = Parse(text, limits);
        if (records.Count == 0) { throw new ContractException("ReportInvalidHeader", FileName, 1); }
        var header = records[0];
        var headers = header.Fields.Select(x => x.Trim()).ToArray();
        if (headers.Any(x => x.Length == 0 || x.Length > 200)) { throw new ContractException("ReportInvalidHeader", FileName, header.Line); }
        var duplicates = headers.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1)
            .Select(x => Truncate(x.Key)).Take(10).ToArray();
        if (duplicates.Length != 0) { throw new ContractException("ReportDuplicateColumns", FileName, header.Line, duplicates); }
        return new CsvTable(headers, records.Skip(1).ToArray());
    }

    internal static string Truncate(string value) => value.Length <= 100 ? value : value[..100];

    private static List<CsvRow> Parse(string text, ReportLimits limits)
    {
        var records = new List<CsvRow>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordLine = 1;
        var inQuotes = false;
        var quoted = false;
        var closed = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { Append('"'); i++; }
                    else { inQuotes = false; closed = true; }
                }
                else
                {
                    if (c == '\n' || (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))) { line++; }
                    Append(c);
                }
                continue;
            }
            switch (c)
            {
                case ',':
                    EndField();
                    break;
                case '\r' or '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { i++; }
                    EndRecord();
                    line++;
                    recordLine = line;
                    break;
                case '"':
                    if (field.Length != 0 || quoted) { throw new ContractException("ReportMalformedCsv", FileName, line); }
                    inQuotes = true;
                    quoted = true;
                    break;
                default:
                    if (closed) { throw new ContractException("ReportMalformedCsv", FileName, line); }
                    Append(c);
                    break;
            }
        }
        if (inQuotes) { throw new ContractException("ReportMalformedCsv", FileName, recordLine); }
        EndRecord();
        return records;

        void Append(char c)
        {
            if (field.Length >= limits.FieldCharacters) { throw new ContractException("ReportFieldTooLong", FileName, line); }
            field.Append(c);
        }

        void EndField()
        {
            if (fields.Count >= limits.Fields) { throw new ContractException("ReportTooManyFields", FileName, recordLine); }
            fields.Add(field.ToString());
            field.Clear();
            quoted = false;
            closed = false;
        }

        void EndRecord()
        {
            var empty = fields.Count == 0 && field.Length == 0 && !quoted;
            if (!empty)
            {
                EndField();
                if (records.Count > limits.Rows) { throw new ContractException("ReportRowLimit", FileName, recordLine); }
                records.Add(new CsvRow(recordLine, fields.ToArray()));
            }
            fields.Clear();
            field.Clear();
            quoted = false;
            closed = false;
        }
    }
}
