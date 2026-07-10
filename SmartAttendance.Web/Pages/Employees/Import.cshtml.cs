using System.Xml.Linq;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.MasterDataImports.Services;
using SmartAttendance.Application.MasterDataImports.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
namespace SmartAttendance.Web.Pages.Employees;

public class ImportModel : PageModel
{
    private const string FixedImportType = "Employees";

    private readonly IMasterDataImportService _masterDataImportService;
    private readonly IWebHostEnvironment _environment;
    private readonly ApplicationDbContext _dbContext;
public ImportModel(
        IMasterDataImportService masterDataImportService,
        IWebHostEnvironment environment,
        ApplicationDbContext dbContext)
    {
        _masterDataImportService = masterDataImportService;
        _environment = environment;
        _dbContext = dbContext;
    }

    [BindProperty]
    public IFormFile? ImportFile { get; set; }

    [BindProperty]
    public string? PastedData { get; set; }

    public string ImportType { get; private set; } = FixedImportType;

    public string PageTitle { get; private set; } = "Employee Data Import";

    public List<string> RequiredColumns { get; set; } = new();

    public MasterDataImportPreviewViewModel? Preview { get; set; }

    public MasterDataImportResultViewModel? ImportResult { get; set; }

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
        LoadRequiredColumns();
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        LoadRequiredColumns();

        var hasFile = ImportFile != null && ImportFile.Length > 0;
        var hasPastedData = !string.IsNullOrWhiteSpace(PastedData);

        if (!hasFile && !hasPastedData)
        {
            ErrorMessage = "Please upload an Excel / CSV file or paste data copied from Excel.";
            return Page();
        }

        try
        {
            var token = Guid.NewGuid().ToString("N");
            string filePath;
            string originalFileName;

            Directory.CreateDirectory(GetImportFolder());

            if (hasFile)
            {
                var extension = Path.GetExtension(ImportFile!.FileName).ToLowerInvariant();

                if (extension is not ".xlsx" and not ".csv")
                {
                    ErrorMessage = "Unsupported file type. Please upload .xlsx or .csv file.";
                    return Page();
                }

                originalFileName = ImportFile.FileName;
                var safeFileName = MakeSafeFileName(Path.GetFileName(ImportFile.FileName));
                var storedFileName = $"{token}_{safeFileName}";
                filePath = Path.Combine(GetImportFolder(), storedFileName);

                await using var stream = System.IO.File.Create(filePath);
                await ImportFile.CopyToAsync(stream);
            }
            else
            {
                originalFileName = $"Pasted_{FixedImportType}.csv";
                var storedFileName = $"{token}_{originalFileName}";
                filePath = Path.Combine(GetImportFolder(), storedFileName);

                var csvContent = ConvertExcelPasteToCsv(PastedData!);
                await System.IO.File.WriteAllTextAsync(filePath, csvContent, Encoding.UTF8);
            }

            Preview = await _masterDataImportService.PreviewAsync(
                filePath,
                token,
                originalFileName,
                FixedImportType,
                previewLimit: 500);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync(string token)
    {
        LoadRequiredColumns();

        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "Import token is missing.";
            return Page();
        }

        var filePath = FindFileByToken(token);

        if (filePath == null)
        {
            ErrorMessage = "Uploaded or pasted data was not found. Please preview it again.";
            return Page();
        }

        try
        {
            var originalFileName = GetOriginalFileNameFromStoredPath(filePath, token);

            ImportResult = await _masterDataImportService.ImportAsync(
                filePath,
                originalFileName,
                FixedImportType);

            var nxrDynamicImportResult = await ImportEmployeeDynamicFieldsFromFileAsync(filePath);
            if (nxrDynamicImportResult.MatchedColumns > 0)
            {
                TempData["SuccessMessage"] =
                    $"Employee import completed. Dynamic custom fields saved: {nxrDynamicImportResult.SavedValues}. Matched custom columns: {nxrDynamicImportResult.MatchedColumns}. Skipped rows: {nxrDynamicImportResult.SkippedRows}.";
            }

            TempData["SuccessMessage"] = ImportResult.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    private void LoadRequiredColumns()
    {
        RequiredColumns = _masterDataImportService.GetRequiredColumns(FixedImportType);
    }

    private string GetImportFolder()
    {
        return Path.Combine(_environment.ContentRootPath, "App_Data", "PageImports", FixedImportType);
    }

    private string? FindFileByToken(string token)
    {
        var folder = GetImportFolder();

        if (!Directory.Exists(folder))
            return null;

        return Directory
            .GetFiles(folder, $"{token}_*")
            .FirstOrDefault();
    }

    private static string GetOriginalFileNameFromStoredPath(string filePath, string token)
    {
        var storedFileName = Path.GetFileName(filePath);
        var prefix = $"{token}_";

        if (storedFileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return storedFileName[prefix.Length..];

        return storedFileName;
    }

    private static string MakeSafeFileName(string fileName)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '_');

        return fileName;
    }

    private static string ConvertExcelPasteToCsv(string pastedData)
    {
        var lines = pastedData
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            var cells = line.Split('\t');
            builder.AppendLine(string.Join(",", cells.Select(ToCsvCell)));
        }

        return builder.ToString();
    }

    private static string ToCsvCell(string value)
    {
        value = value.Trim();

        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            value = $"\"{value}\"";

        return value;
    }

    // NEXORA_FIX18A_DYNAMIC_TEMPLATE_START
    public async Task<IActionResult> OnGetTemplateAsync()
    {
        LoadRequiredColumns();

        var columns = await BuildEmployeeTemplateColumnsAsync();
        var workbookBytes = BuildEmployeeTemplateWorkbook(columns);
        var fileName = $"NEXORA_Employees_Import_Template_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

        return File(
            workbookBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    public async Task<IActionResult> OnGetTemplateColumnsAsync()
    {
        var columns = await BuildEmployeeTemplateColumnsAsync();

        return new JsonResult(new
        {
            columns
        });
    }
    private async Task<List<string>> BuildEmployeeTemplateColumnsAsync()
    {
        var columns = new List<string>
        {
            "EmployeeNo",
            "FullName",
            "DepartmentCode",
            "HireDate",
            "NationalId",
            "Phone",
            "Email",
            "BirthDate",
            "IsActive",
            "Position",
            "Nationality",
            "Country",
            "Gender",
            "MaritalStatus",
            "ContractType",
            "DirectManagerEmployeeNo"
        };

        await EmployeeProfileDynamicFields.EnsureSchemaAsync(_dbContext);

        var dynamicFields = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    FieldKey,
    FieldLabel,
    SectionKey,
    SortOrder
FROM EmployeeProfileFieldDefinitions
WHERE IsActive = 1
ORDER BY
    CASE SectionKey
        WHEN 'basic' THEN 10
        WHEN 'personal' THEN 20
        WHEN 'job' THEN 30
        WHEN 'financial' THEN 40
        WHEN 'additional' THEN 50
        ELSE 99
    END,
    SortOrder,
    Id;
""",
            command => { },
            reader => new EmployeeImportTemplateDynamicField
            {
                FieldKey = HrmsDatabase.GetString(reader, "FieldKey"),
                FieldLabel = HrmsDatabase.GetString(reader, "FieldLabel"),
                SectionKey = HrmsDatabase.GetString(reader, "SectionKey"),
                SortOrder = HrmsDatabase.GetInt(reader, "SortOrder")
            });

        var used = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);

        foreach (var field in dynamicFields)
        {
            var label = string.IsNullOrWhiteSpace(field.FieldLabel)
                ? field.FieldKey
                : field.FieldLabel.Trim();

            var header = $"Custom: {label}";

            if (!used.Add(header))
            {
                header = $"Custom: {label} [{field.FieldKey}]";
                used.Add(header);
            }

            columns.Add(header);
        }

        return columns;
    }

    private static byte[] BuildEmployeeTemplateWorkbook(List<string> columns)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(
                archive,
                "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>");

            AddZipEntry(
                archive,
                "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            AddZipEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>");

            AddZipEntry(
                archive,
                "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"Employees\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "</workbook>");

            AddZipEntry(
                archive,
                "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills>" +
                "<borders count=\"1\"><border/></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>" +
                "</styleSheet>");

            AddZipEntry(archive, "xl/worksheets/sheet1.xml", BuildEmployeeTemplateWorksheet(columns));
        }

        return stream.ToArray();
    }

    private static string BuildEmployeeTemplateWorksheet(List<string> columns)
    {
        var maxColumn = Math.Max(columns.Count, 1);
        var endReference = GetCellReference(1, maxColumn);

        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        builder.Append("<dimension ref=\"A1:");
        builder.Append(endReference);
        builder.Append("\"/>");
        builder.Append("<sheetViews><sheetView workbookViewId=\"0\" rightToLeft=\"1\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        builder.Append("<sheetFormatPr defaultRowHeight=\"18\"/>");
        builder.Append("<sheetData><row r=\"1\">");

        for (var i = 0; i < columns.Count; i++)
        {
            builder.Append(BuildInlineCell(1, i + 1, columns[i]));
        }

        builder.Append("</row></sheetData>");
        builder.Append("</worksheet>");

        return builder.ToString();
    }

    private static string BuildInlineCell(int row, int column, string value)
    {
        return $"<c r=\"{GetCellReference(row, column)}\" t=\"inlineStr\"><is><t>{Xml(value)}</t></is></c>";
    }

    private static string GetCellReference(int row, int column)
    {
        var dividend = column;
        var columnName = string.Empty;

        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar(65 + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName + row.ToString();
    }

    private static string Xml(string? value)
    {
        return System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
    }

    private static void AddZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed class EmployeeImportTemplateDynamicField
    {
        public string FieldKey { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string SectionKey { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }
    // NEXORA_FIX18A_DYNAMIC_TEMPLATE_END

    // NEXORA_FIX18C_DYNAMIC_EMPLOYEE_IMPORT_START
    private async Task<NexoraEmployeeDynamicImportResult> ImportEmployeeDynamicFieldsFromFileAsync(string filePath)
    {
        var result = new NexoraEmployeeDynamicImportResult();

        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            return result;
        }

        await EmployeeProfileDynamicFields.EnsureSchemaAsync(_dbContext);

        var definitions = await LoadEmployeeDynamicImportDefinitionsAsync();
        if (definitions.Count == 0)
        {
            return result;
        }

        var rows = await ReadEmployeeImportRowsAsync(filePath);
        if (rows.Count == 0)
        {
            return result;
        }

        var headerMap = BuildDynamicImportHeaderMap(rows, definitions);
        result.MatchedColumns = headerMap.Count;

        if (headerMap.Count == 0)
        {
            return result;
        }

        foreach (var row in rows)
        {
            var employeeNo = GetImportRowValue(row, "EmployeeNo");
            if (string.IsNullOrWhiteSpace(employeeNo))
            {
                result.SkippedRows++;
                continue;
            }

            var employeeId = await FindEmployeeIdByEmployeeNoAsync(employeeNo);
            if (employeeId <= 0)
            {
                result.SkippedRows++;
                continue;
            }

            var rowSaved = 0;

            foreach (var pair in headerMap)
            {
                var rawValue = GetImportRowValue(row, pair.Key);

                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    continue;
                }

                await SaveEmployeeDynamicImportFieldAsync(
                    employeeId,
                    pair.Value.FieldKey,
                    pair.Value.FieldLabel,
                    rawValue.Trim());

                rowSaved++;
            }

            if (rowSaved > 0)
            {
                result.SavedRows++;
                result.SavedValues += rowSaved;
            }
        }

        return result;
    }

    private async Task<List<NexoraEmployeeDynamicImportDefinition>> LoadEmployeeDynamicImportDefinitionsAsync()
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    FieldKey,
    FieldLabel
FROM EmployeeProfileFieldDefinitions
WHERE IsActive = 1
ORDER BY SortOrder, Id;
""",
            command => { },
            reader => new NexoraEmployeeDynamicImportDefinition
            {
                FieldKey = HrmsDatabase.GetString(reader, "FieldKey"),
                FieldLabel = HrmsDatabase.GetString(reader, "FieldLabel")
            });
    }

    private static Dictionary<string, NexoraEmployeeDynamicImportDefinition> BuildDynamicImportHeaderMap(
        List<Dictionary<string, string>> rows,
        List<NexoraEmployeeDynamicImportDefinition> definitions)
    {
        var map = new Dictionary<string, NexoraEmployeeDynamicImportDefinition>(StringComparer.OrdinalIgnoreCase);

        if (rows.Count == 0)
        {
            return map;
        }

        var headers = rows[0].Keys.ToList();

        foreach (var header in headers)
        {
            if (!TryResolveDynamicImportHeader(header, definitions, out var definition))
            {
                continue;
            }

            if (!map.ContainsKey(header))
            {
                map.Add(header, definition);
            }
        }

        return map;
    }

    private static bool TryResolveDynamicImportHeader(
        string header,
        List<NexoraEmployeeDynamicImportDefinition> definitions,
        out NexoraEmployeeDynamicImportDefinition definition)
    {
        definition = NexoraEmployeeDynamicImportDefinition.Empty;

        var normalizedHeader = NormalizeDynamicImportHeader(header);
        if (string.IsNullOrWhiteSpace(normalizedHeader))
        {
            return false;
        }

        var customPrefix = "custom:";
        if (normalizedHeader.StartsWith(customPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalizedHeader = normalizedHeader.Substring(customPrefix.Length).Trim();
        }
        else
        {
            return false;
        }

        var bracketKey = ExtractBracketKey(normalizedHeader);
        if (!string.IsNullOrWhiteSpace(bracketKey))
        {
            var byBracketKey = definitions.FirstOrDefault(item =>
                SameImportText(item.FieldKey, bracketKey));

            if (byBracketKey != null)
            {
                definition = byBracketKey;
                return true;
            }

            normalizedHeader = RemoveBracketKey(normalizedHeader);
        }

        var byLabel = definitions.FirstOrDefault(item =>
            SameImportText(item.FieldLabel, normalizedHeader));

        if (byLabel != null)
        {
            definition = byLabel;
            return true;
        }

        var byKey = definitions.FirstOrDefault(item =>
            SameImportText(item.FieldKey, normalizedHeader));

        if (byKey != null)
        {
            definition = byKey;
            return true;
        }

        return false;
    }

    private async Task<int> FindEmployeeIdByEmployeeNoAsync(string employeeNo)
    {
        var list = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1 Id
FROM Employees
WHERE LOWER(EmployeeNo) = LOWER(@EmployeeNo);
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeNo", employeeNo.Trim()),
            reader => HrmsDatabase.GetInt(reader, "Id"));

        return list.FirstOrDefault();
    }

    private async Task SaveEmployeeDynamicImportFieldAsync(
        int employeeId,
        string fieldKey,
        string fieldLabel,
        string fieldValue)
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF EXISTS
(
    SELECT 1
    FROM EmployeeCustomFields
    WHERE EmployeeId = @EmployeeId
      AND FieldKey = @FieldKey
)
BEGIN
    UPDATE EmployeeCustomFields
    SET FieldLabel = @FieldLabel,
        FieldValue = @FieldValue,
        UpdatedAt = SYSUTCDATETIME()
    WHERE EmployeeId = @EmployeeId
      AND FieldKey = @FieldKey;
END
ELSE
BEGIN
    INSERT INTO EmployeeCustomFields
    (
        EmployeeId,
        FieldKey,
        FieldLabel,
        FieldValue,
        UpdatedAt
    )
    VALUES
    (
        @EmployeeId,
        @FieldKey,
        @FieldLabel,
        @FieldValue,
        SYSUTCDATETIME()
    );
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@FieldKey", fieldKey);
                HrmsDatabase.AddParameter(command, "@FieldLabel", fieldLabel);
                HrmsDatabase.AddParameter(command, "@FieldValue", fieldValue ?? string.Empty);
            });
    }

    private static async Task<List<Dictionary<string, string>>> ReadEmployeeImportRowsAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension == ".xlsx")
        {
            return await Task.FromResult(ReadXlsxImportRows(filePath));
        }

        if (extension == ".csv")
        {
            return await Task.FromResult(ReadDelimitedImportRows(filePath, ','));
        }

        return new List<Dictionary<string, string>>();
    }

    private static List<Dictionary<string, string>> ReadXlsxImportRows(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var sharedStrings = ReadXlsxSharedStrings(archive);
        var worksheetEntry =
            archive.GetEntry("xl/worksheets/sheet1.xml") ??
            archive.Entries.FirstOrDefault(entry =>
                entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        if (worksheetEntry == null)
        {
            return new List<Dictionary<string, string>>();
        }

        using var stream = worksheetEntry.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var rowElements = document.Descendants(ns + "row").ToList();
        var table = new List<List<string>>();

        foreach (var rowElement in rowElements)
        {
            var cells = new Dictionary<int, string>();

            foreach (var cell in rowElement.Elements(ns + "c"))
            {
                var reference = cell.Attribute("r")?.Value;
                var columnIndex = GetXlsxColumnIndex(reference);

                if (columnIndex <= 0)
                {
                    columnIndex = cells.Count + 1;
                }

                cells[columnIndex] = ReadXlsxCellValue(cell, sharedStrings);
            }

            if (cells.Count == 0)
            {
                table.Add(new List<string>());
                continue;
            }

            var maxColumn = cells.Keys.Max();
            var values = new List<string>();

            for (var column = 1; column <= maxColumn; column++)
            {
                values.Add(cells.TryGetValue(column, out var value) ? value : string.Empty);
            }

            table.Add(values);
        }

        return ConvertTableToImportRows(table);
    }

    private static List<string> ReadXlsxSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null)
        {
            return new List<string>();
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;

        return document
            .Descendants(ns + "si")
            .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string ReadXlsxCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var ns = cell.Name.Namespace;
        var cellType = cell.Attribute("t")?.Value ?? string.Empty;

        if (cellType.Equals("inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants(ns + "t").Select(text => text.Value)).Trim();
        }

        var rawValue = cell.Element(ns + "v")?.Value ?? string.Empty;

        if (cellType.Equals("s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex) &&
            sharedStringIndex >= 0 &&
            sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex].Trim();
        }

        return rawValue.Trim();
    }

    private static int GetXlsxColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return 0;
        }

        var letters = new string(cellReference.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        var result = 0;

        foreach (var letter in letters)
        {
            result *= 26;
            result += letter - 'A' + 1;
        }

        return result;
    }

    private static List<Dictionary<string, string>> ReadDelimitedImportRows(string filePath, char delimiter)
    {
        var text = System.IO.File.ReadAllText(filePath, Encoding.UTF8);
        var lines = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => ParseDelimitedLine(line, delimiter))
            .ToList();

        return ConvertTableToImportRows(lines);
    }

    private static List<string> ParseDelimitedLine(string line, char delimiter)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                values.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        values.Add(builder.ToString().Trim());
        return values;
    }

    private static List<Dictionary<string, string>> ConvertTableToImportRows(List<List<string>> table)
    {
        var result = new List<Dictionary<string, string>>();

        var headerRow = table.FirstOrDefault(row => row.Any(value => !string.IsNullOrWhiteSpace(value)));
        if (headerRow == null)
        {
            return result;
        }

        var headers = headerRow
            .Select(value => value.Trim())
            .ToList();

        var startIndex = table.IndexOf(headerRow) + 1;

        for (var rowIndex = startIndex; rowIndex < table.Count; rowIndex++)
        {
            var row = table[rowIndex];

            if (row.All(value => string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                var header = headers[columnIndex];
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                var value = columnIndex < row.Count ? row[columnIndex] : string.Empty;
                dictionary[header] = value?.Trim() ?? string.Empty;
            }

            if (dictionary.Count > 0)
            {
                result.Add(dictionary);
            }
        }

        return result;
    }

    private static string GetImportRowValue(Dictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out var value) ? value?.Trim() ?? string.Empty : string.Empty;
    }

    private static bool SameImportText(string? left, string? right)
    {
        return string.Equals(
            NormalizeDynamicImportHeader(left),
            NormalizeDynamicImportHeader(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDynamicImportHeader(string? value)
    {
        return (value ?? string.Empty)
            .Replace('\u00A0', ' ')
            .Trim();
    }

    private static string ExtractBracketKey(string value)
    {
        var start = value.LastIndexOf('[');
        var end = value.LastIndexOf(']');

        if (start >= 0 && end > start)
        {
            return value.Substring(start + 1, end - start - 1).Trim();
        }

        return string.Empty;
    }

    private static string RemoveBracketKey(string value)
    {
        var start = value.LastIndexOf('[');
        var end = value.LastIndexOf(']');

        if (start >= 0 && end > start)
        {
            return value.Remove(start, end - start + 1).Trim();
        }

        return value.Trim();
    }

    private sealed class NexoraEmployeeDynamicImportDefinition
    {
        public static NexoraEmployeeDynamicImportDefinition Empty { get; } = new();

        public string FieldKey { get; set; } = string.Empty;

        public string FieldLabel { get; set; } = string.Empty;
    }

    private sealed class NexoraEmployeeDynamicImportResult
    {
        public int MatchedColumns { get; set; }

        public int SavedRows { get; set; }

        public int SavedValues { get; set; }

        public int SkippedRows { get; set; }
    }
    // NEXORA_FIX18C_DYNAMIC_EMPLOYEE_IMPORT_END
}
