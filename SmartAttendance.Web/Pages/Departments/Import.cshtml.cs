using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.MasterDataImports.Services;
using SmartAttendance.Application.MasterDataImports.ViewModels;

namespace SmartAttendance.Web.Pages.Departments;

public class ImportModel : PageModel
{
    private const string FixedImportType = "Departments";

    private readonly IMasterDataImportService _masterDataImportService;
    private readonly IWebHostEnvironment _environment;

    public ImportModel(
        IMasterDataImportService masterDataImportService,
        IWebHostEnvironment environment)
    {
        _masterDataImportService = masterDataImportService;
        _environment = environment;
    }

    [BindProperty]
    public IFormFile? ImportFile { get; set; }

    [BindProperty]
    public string? PastedData { get; set; }

    public string ImportType { get; private set; } = FixedImportType;

    public string PageTitle { get; private set; } = "Department Data Import";

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
    // NEXORA_FIX22A_TEMPLATE_HELPERS
    private static byte[] BuildTemplateWorkbook(string sheetName, string[] headers, string[] sample)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            AddZipEntry(archive, "[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
</Types>");

            AddZipEntry(archive, "_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");

            AddZipEntry(archive, "xl/workbook.xml", $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets>
    <sheet name=""{EscapeXml(sheetName)}"" sheetId=""1"" r:id=""rId1""/>
  </sheets>
</workbook>");

            AddZipEntry(archive, "xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
</Relationships>");

            AddZipEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(headers, sample));
        }

        return memory.ToArray();
    }

    private static string BuildWorksheetXml(IReadOnlyList<string> headers, IReadOnlyList<string> sample)
    {
        var builder = new StringBuilder();
        builder.Append(@"<?xml version=""1.0"" encoding=""UTF-8""?><worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetData>");
        builder.Append(BuildRowXml(1, headers));
        builder.Append(BuildRowXml(2, sample));
        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static string BuildRowXml(int rowIndex, IReadOnlyList<string> values)
    {
        var builder = new StringBuilder();
        builder.Append($"<row r=\"{rowIndex}\">");

        for (var i = 0; i < values.Count; i++)
        {
            var cellRef = $"{GetColumnName(i + 1)}{rowIndex}";
            builder.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{EscapeXml(values[i])}</t></is></c>");
        }

        builder.Append("</row>");
        return builder.ToString();
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string GetColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }

        return name;
    }

    private static string EscapeXml(string? value)
    {
        return System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
    }

    // NEXORA_FIX22C_TEMPLATE_HANDLER_ONLY
    public IActionResult OnGetTemplate()
    {
        var bytes = BuildTemplateWorkbook("Departments", new[] { "Name" }, new[] { "Human Resources" });
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "NEXORA_Departments_Template.xlsx");
    }
}
