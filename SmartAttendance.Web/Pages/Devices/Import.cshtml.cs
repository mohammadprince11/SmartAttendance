using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.MasterDataImports.Services;
using SmartAttendance.Application.MasterDataImports.ViewModels;

namespace SmartAttendance.Web.Pages.Devices;

public class ImportModel : PageModel
{
    private const string FixedImportType = "Devices";

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

    public string PageTitle { get; private set; } = "استيراد بيانات الأجهزة";

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
}
