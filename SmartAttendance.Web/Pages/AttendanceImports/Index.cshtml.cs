using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.AttendanceImports.Services;
using SmartAttendance.Application.AttendanceImports.ViewModels;

namespace SmartAttendance.Web.Pages.AttendanceImports;

public class IndexModel : PageModel
{
    private readonly IAttendanceImportService _attendanceImportService;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(
        IAttendanceImportService attendanceImportService,
        IWebHostEnvironment environment)
    {
        _attendanceImportService = attendanceImportService;
        _environment = environment;
    }

    [BindProperty]
    public IFormFile? AttendanceFile { get; set; }

    public AttendanceImportPreviewViewModel? Preview { get; set; }

    public AttendanceImportResultViewModel? ImportResult { get; set; }

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (AttendanceFile == null || AttendanceFile.Length == 0)
        {
            ErrorMessage = "Please select an Excel or CSV file.";
            return Page();
        }

        var extension = Path.GetExtension(AttendanceFile.FileName).ToLowerInvariant();

        if (extension is not ".xlsx" and not ".csv")
        {
            ErrorMessage = "Unsupported file type. Please upload .xlsx or .csv file.";
            return Page();
        }

        try
        {
            var token = Guid.NewGuid().ToString("N");
            var safeFileName = MakeSafeFileName(Path.GetFileName(AttendanceFile.FileName));
            var storedFileName = $"{token}_{safeFileName}";
            var filePath = Path.Combine(GetImportFolder(), storedFileName);

            Directory.CreateDirectory(GetImportFolder());

            await using (var stream = System.IO.File.Create(filePath))
            {
                await AttendanceFile.CopyToAsync(stream);
            }

            Preview = await _attendanceImportService.PreviewAsync(
                filePath,
                token,
                AttendanceFile.FileName,
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
        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "Import token is missing.";
            return Page();
        }

        var filePath = FindFileByToken(token);

        if (filePath == null)
        {
            ErrorMessage = "Uploaded file was not found. Please upload the file again.";
            return Page();
        }

        try
        {
            var originalFileName = GetOriginalFileNameFromStoredPath(filePath, token);

            ImportResult = await _attendanceImportService.ImportAsync(
                filePath,
                originalFileName);

            TempData["SuccessMessage"] = ImportResult.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    private string GetImportFolder()
    {
        return Path.Combine(_environment.ContentRootPath, "App_Data", "AttendanceImports");
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
}
