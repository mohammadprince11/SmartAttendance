using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قارئ جداول قائم بذاته (xlsx + csv) → صفوف من الخلايا النصية. يدعم SharedStrings
/// و inlineStr ومراجع الأعمدة (r="B2") لتفادي إزاحة الأعمدة عند الخلايا الفارغة.
/// قابل لإعادة الاستخدام بأي استيراد (حركات الرواتب، الحضور، ...).
/// </summary>
public static class SpreadsheetReader
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static List<string[]> Read(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".csv" => ReadCsv(stream),
            ".xlsx" => ReadXlsx(stream),
            _ => throw new InvalidOperationException("صيغة غير مدعومة — ارفع ملف .xlsx أو .csv")
        };
    }

    // ---------------- CSV ----------------
    private static List<string[]> ReadCsv(Stream stream)
    {
        var rows = new List<string[]>();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var sep = line.Contains('\t') ? '\t' : (line.Contains(';') && !line.Contains(',') ? ';' : ',');
            rows.Add(SplitCsv(line, sep));
        }
        return rows;
    }

    private static string[] SplitCsv(string line, char sep)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == sep && !inQuotes) { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result.Select(x => x.Trim()).ToArray();
    }

    // ---------------- XLSX ----------------
    private static List<string[]> ReadXlsx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var shared = ReadSharedStrings(archive);
        var sheetEntry = archive.GetEntry(FirstSheetPath(archive))
            ?? throw new InvalidOperationException("لا توجد ورقة عمل داخل الملف.");

        using var s = sheetEntry.Open();
        var doc = XDocument.Load(s);
        var rows = new List<string[]>();

        foreach (var row in doc.Descendants(S + "row"))
        {
            var map = new Dictionary<int, string>();
            var maxCol = -1;
            foreach (var c in row.Elements(S + "c"))
            {
                var col = ColIndex((string?)c.Attribute("r") ?? "");
                if (col < 0) continue;
                var t = (string?)c.Attribute("t");
                string value;
                if (t == "s")
                {
                    value = int.TryParse(c.Element(S + "v")?.Value, out var idx) && idx >= 0 && idx < shared.Count
                        ? shared[idx] : "";
                }
                else if (t == "inlineStr")
                {
                    value = c.Element(S + "is")?.Element(S + "t")?.Value ?? "";
                }
                else
                {
                    value = c.Element(S + "v")?.Value ?? "";
                }
                map[col] = value.Trim();
                if (col > maxCol) maxCol = col;
            }
            var arr = new string[maxCol + 1];
            for (var i = 0; i <= maxCol; i++) arr[i] = map.TryGetValue(i, out var v) ? v : "";
            rows.Add(arr);
        }
        return rows;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var list = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return list;
        using var s = entry.Open();
        var doc = XDocument.Load(s);
        foreach (var si in doc.Descendants(S + "si"))
            list.Add(string.Concat(si.Descendants(S + "t").Select(t => t.Value)));
        return list;
    }

    private static string FirstSheetPath(ZipArchive archive)
    {
        if (archive.GetEntry("xl/worksheets/sheet1.xml") != null) return "xl/worksheets/sheet1.xml";
        var first = archive.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        return first?.FullName ?? "xl/worksheets/sheet1.xml";
    }

    /// <summary>مرجع خلية (B2) → فهرس العمود (A=0).</summary>
    private static int ColIndex(string cellRef)
    {
        var col = 0; var i = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i]))
        {
            col = col * 26 + (char.ToUpperInvariant(cellRef[i]) - 'A' + 1);
            i++;
        }
        return col - 1;
    }
}
