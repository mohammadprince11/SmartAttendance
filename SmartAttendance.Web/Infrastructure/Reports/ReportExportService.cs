using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SmartAttendance.Web.Infrastructure.Reports;

/// <summary>عقد تصدير واحد للتقارير؛ لا تعيد كل صفحة بناء CSV/Excel بطريقتها.</summary>
public static class ReportExportService
{
    public sealed record ExportFile(byte[] Bytes, string ContentType, string Extension);
    public sealed record Column(string Key, string Label);

    public static ExportFile Build(
        string format, string title, IReadOnlyList<Column> columns,
        IReadOnlyList<Dictionary<string, string>> rows) =>
        string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? Csv(columns, rows)
            : Xlsx(title, columns, rows);

    private static ExportFile Csv(
        IReadOnlyList<Column> columns, IReadOnlyList<Dictionary<string, string>> rows)
    {
        static string Cell(string value)
        {
            value ??= string.Empty;
            if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
                value = "'" + value;
            return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }

        var text = new StringBuilder();
        text.AppendLine(string.Join(",", columns.Select(column => Cell(column.Label))));
        foreach (var row in rows)
            text.AppendLine(string.Join(",", columns.Select(column => Cell(row.GetValueOrDefault(column.Key, string.Empty)))));
        return new ExportFile(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray(),
            "text/csv; charset=utf-8", "csv");
    }

    private static ExportFile Xlsx(
        string title, IReadOnlyList<Column> columns, IReadOnlyList<Dictionary<string, string>> rows)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Text(zip, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>
""");
            Text(zip, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
            var workbookEntry = zip.CreateEntry("xl/workbook.xml", CompressionLevel.Fastest);
            using (var workbookStream = workbookEntry.Open())
            using (var workbookWriter = XmlWriter.Create(workbookStream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
            {
                workbookWriter.WriteStartDocument(true);
                workbookWriter.WriteStartElement("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                workbookWriter.WriteAttributeString("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                workbookWriter.WriteStartElement("sheets");
                workbookWriter.WriteStartElement("sheet");
                workbookWriter.WriteAttributeString("name", WorksheetName(title));
                workbookWriter.WriteAttributeString("sheetId", "1");
                workbookWriter.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId1");
                workbookWriter.WriteEndElement();
                workbookWriter.WriteEndElement();
                workbookWriter.WriteEndElement();
                workbookWriter.WriteEndDocument();
            }
            Text(zip, "xl/_rels/workbook.xml.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
""");
            Text(zip, "xl/styles.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="2"><font><sz val="11"/><name val="Arial"/></font><font><b/><sz val="11"/><name val="Arial"/></font></fonts>
  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
  <borders count="1"><border/></borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment wrapText="1" vertical="top"/></xf></cellXfs>
</styleSheet>
""");

            var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
            using var stream = entry.Open();
            using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
            writer.WriteStartDocument(true);
            writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            writer.WriteStartElement("sheetViews");
            writer.WriteStartElement("sheetView"); writer.WriteAttributeString("workbookViewId", "0"); writer.WriteAttributeString("rightToLeft", "1");
            writer.WriteStartElement("pane"); writer.WriteAttributeString("ySplit", "1"); writer.WriteAttributeString("topLeftCell", "A2"); writer.WriteAttributeString("state", "frozen"); writer.WriteEndElement();
            writer.WriteEndElement(); writer.WriteEndElement();
            WriteColumns(writer, columns, rows);
            writer.WriteStartElement("sheetData");
            WriteRow(writer, 1, columns.Select(column => column.Label), header: true);
            for (var index = 0; index < rows.Count; index++)
                WriteRow(writer, index + 2, columns.Select(column => rows[index].GetValueOrDefault(column.Key, string.Empty)), header: false);
            writer.WriteEndElement();
            if (columns.Count > 0)
            {
                writer.WriteStartElement("autoFilter");
                writer.WriteAttributeString("ref", $"A1:{ColumnName(columns.Count)}{rows.Count + 1}");
                writer.WriteEndElement();
            }
            writer.WriteEndElement(); writer.WriteEndDocument();
        }

        return new ExportFile(output.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx");
    }

    private static void WriteRow(XmlWriter writer, int number, IEnumerable<string> values, bool header)
    {
        writer.WriteStartElement("row"); writer.WriteAttributeString("r", number.ToString());
        var index = 1;
        foreach (var value in values)
        {
            writer.WriteStartElement("c"); writer.WriteAttributeString("r", ColumnName(index++) + number); writer.WriteAttributeString("t", "inlineStr");
            writer.WriteAttributeString("s", header ? "1" : "2");
            writer.WriteStartElement("is");
            writer.WriteStartElement("t");
            writer.WriteAttributeString("xml", "space", null, "preserve");
            writer.WriteString(Sanitize(value));
            writer.WriteEndElement();
            writer.WriteEndElement(); writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteColumns(
        XmlWriter writer,
        IReadOnlyList<Column> columns,
        IReadOnlyList<Dictionary<string, string>> rows)
    {
        if (columns.Count == 0) return;
        writer.WriteStartElement("cols");
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var longest = rows.Select(row => row.GetValueOrDefault(column.Key, string.Empty)?.Length ?? 0)
                .Append(column.Label.Length)
                .DefaultIfEmpty(8)
                .Max();
            var width = Math.Clamp(longest + 2, 10, 60);
            writer.WriteStartElement("col");
            writer.WriteAttributeString("min", (index + 1).ToString());
            writer.WriteAttributeString("max", (index + 1).ToString());
            writer.WriteAttributeString("width", width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0) { index--; name = (char)('A' + index % 26) + name; index /= 26; }
        return name;
    }

    private static string WorksheetName(string? title)
    {
        var value = new string((title ?? string.Empty)
            .Where(character => character is not '[' and not ']' and not ':' and not '*' and not '?' and not '/' and not '\\')
            .ToArray()).Trim();
        if (value.Length == 0) value = "تقرير";
        return value.Length <= 31 ? value : value[..31];
    }

    private static string Sanitize(string? value) => new((value ?? string.Empty)
        .Where(character => XmlConvert.IsXmlChar(character)).ToArray());

    private static void Text(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
