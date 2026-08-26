using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

public partial class ProfileModel
{
    private static readonly HashSet<string> AllowedProfileFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".doc",
        ".docx",
        ".xls",
        ".xlsx"
    };

    private static readonly HashSet<string> AllowedProfileFileCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Experience",
        "Certificates",
        "Medical",
        "Training",
        "Education"
    };

    [BindProperty]
    public IFormFile? ProfileAreaFile { get; set; }

    [BindProperty]
    public string? ProfileAreaCategory { get; set; }

    public List<ProfileFileRow> ProfileFiles { get; set; } = new();

    public async Task<IActionResult> OnPostUploadProfileAreaFileAsync(int id)
    {
        await EnsureProfileFilesTableAsync();

        if (id <= 0)
        {
            TempData["ErrorMessage"] = "Employee was not selected.";
            return RedirectToPage("./Index");
        }

        var category = (ProfileAreaCategory ?? string.Empty).Trim();

        if (!AllowedProfileFileCategories.Contains(category))
        {
            TempData["ErrorMessage"] = "Invalid file category.";
            return RedirectToPage(new { id });
        }

        var saved = await SaveProfileAreaFileAsync(id, category, ProfileAreaFile);

        if (string.IsNullOrWhiteSpace(saved))
        {
            TempData["ErrorMessage"] = "Invalid file. Allowed: PDF, images, Word, Excel. Max 10MB.";
        }
        else
        {
            TempData["SuccessMessage"] = "Profile file uploaded successfully.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteProfileAreaFileAsync(int id, int fileId)
    {
        await EnsureProfileFilesTableAsync();

        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1 StoredPath, ISNULL(ProtectedKey, '') AS ProtectedKey
FROM EmployeeProfileFiles
WHERE Id = @FileId AND EmployeeId = @EmployeeId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@FileId", fileId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", id);
            },
            reader => new
            {
                StoredPath = HrmsDatabase.GetString(reader, "StoredPath"),
                ProtectedKey = HrmsDatabase.GetString(reader, "ProtectedKey")
            });

        var row = rows.FirstOrDefault();
        var storedPath = row?.StoredPath ?? string.Empty;
        var protectedKey = row?.ProtectedKey ?? string.Empty;

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "DELETE FROM EmployeeProfileFiles WHERE Id = @FileId AND EmployeeId = @EmployeeId;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@FileId", fileId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", id);
            });

        if (!string.IsNullOrWhiteSpace(protectedKey))
        {
            Infrastructure.Security.ProtectedFileStore.TryDelete(
                Infrastructure.Security.ProtectedFileStore.ResolveRoot(_environment.ContentRootPath),
                protectedKey);
        }
        else if (!string.IsNullOrWhiteSpace(storedPath) &&
                 storedPath.StartsWith("/uploads/employee-profile-files/", StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteProfileAreaPhysicalFile(storedPath);
        }

        TempData["SuccessMessage"] = "Profile file deleted successfully.";
        return RedirectToPage(new { id });
    }

    private async Task EnsureProfileFilesTableAsync()
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF OBJECT_ID(N'[dbo].[EmployeeProfileFiles]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeProfileFiles]
    (
        [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [EmployeeId] int NOT NULL,
        [Category] nvarchar(50) NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [StoredPath] nvarchar(500) NOT NULL,
        [ContentType] nvarchar(120) NULL,
        [SizeBytes] bigint NOT NULL CONSTRAINT DF_EmployeeProfileFiles_SizeBytes DEFAULT 0,
        [UploadedAt] datetime2 NOT NULL CONSTRAINT DF_EmployeeProfileFiles_UploadedAt DEFAULT SYSUTCDATETIME(),
        [UploadedBy] nvarchar(150) NULL,
        [ProtectedKey] nvarchar(400) NULL
    );

    CREATE INDEX IX_EmployeeProfileFiles_Employee_Category
    ON [dbo].[EmployeeProfileFiles] ([EmployeeId], [Category], [UploadedAt]);
END;
""");
    }

    private async Task<string> SaveProfileAreaFileAsync(int employeeId, string category, IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return string.Empty;
        }

        if (file.Length > 10 * 1024 * 1024)
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(file.FileName);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedProfileFileExtensions.Contains(extension))
        {
            return string.Empty;
        }

        if (!await Infrastructure.Security.UploadSignatureValidator.IsValidForExtensionAsync(file, extension))
        {
            return string.Empty;
        }

        var scan = await Infrastructure.Security.FileThreatPolicy.ScanUploadAsync(
            _threatScanner, file, HttpContext.RequestAborted);
        if (!Infrastructure.Security.FileThreatPolicy.CanStore(_malwareOptions, scan))
        {
            return string.Empty;
        }

        // المرحلة 6: الملفات الجديدة تُخزَّن خارج wwwroot باسم مولَّد بالكامل، فلا
        // يخدمها الخادم الثابت ولا يُخمَّن مسارها. القراءة عبر /files فقط.
        var protectedRoot = Infrastructure.Security.ProtectedFileStore.ResolveRoot(
            _environment.ContentRootPath);
        var storageKey = Infrastructure.Security.ProtectedFileStore.BuildStorageKey(
            employeeId, category, extension);

        await using (var source = file.OpenReadStream())
        {
            var stored = await Infrastructure.Security.ProtectedFileStore.SaveAsync(
                protectedRoot, storageKey, source, HttpContext.RequestAborted);

            if (!stored)
            {
                return string.Empty;
            }
        }

        var safeOriginalName = Path.GetFileName(file.FileName);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO EmployeeProfileFiles
(
    EmployeeId,
    Category,
    FileName,
    StoredPath,
    ProtectedKey,
    ContentType,
    SizeBytes,
    UploadedAt,
    UploadedBy
)
VALUES
(
    @EmployeeId,
    @Category,
    @FileName,
    '',
    @ProtectedKey,
    @ContentType,
    @SizeBytes,
    SYSUTCDATETIME(),
    @UploadedBy
);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Category", category);
                HrmsDatabase.AddParameter(command, "@FileName", safeOriginalName);
                HrmsDatabase.AddParameter(command, "@ProtectedKey", storageKey);
                HrmsDatabase.AddParameter(command, "@ContentType", file.ContentType ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@SizeBytes", file.Length);
                HrmsDatabase.AddParameter(command, "@UploadedBy", User?.Identity?.Name ?? string.Empty);
            });

        return storageKey;
    }

    private async Task LoadProfileFilesAsync(int employeeId)
    {
        await EnsureProfileFilesTableAsync();

        ProfileFiles = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 200
    Id,
    Category,
    FileName,
    StoredPath,
    UploadedAt
FROM EmployeeProfileFiles
WHERE EmployeeId = @EmployeeId
ORDER BY UploadedAt DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new ProfileFileRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Category = HrmsDatabase.GetString(reader, "Category"),
                FileName = HrmsDatabase.GetString(reader, "FileName"),
                StoredPath = HrmsDatabase.GetString(reader, "StoredPath"),
                UploadedAt = HrmsDatabase.GetDateTime(reader, "UploadedAt")
            });
    }

    public List<ProfileFileRow> ProfileFilesByCategory(string category)
    {
        return ProfileFiles
            .Where(file => string.Equals(file.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.UploadedAt)
            .ToList();
    }

    private void TryDeleteProfileAreaPhysicalFile(string storedPath)
    {
        try
        {
            var webRoot = _environment.WebRootPath;

            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(_environment.ContentRootPath, "wwwroot");
            }

            var relative = storedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(webRoot, relative));
            var webRootFull = Path.GetFullPath(webRoot);

            if (fullPath.StartsWith(webRootFull, StringComparison.OrdinalIgnoreCase) &&
                System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }
        catch
        {
        }
    }

    public class ProfileFileRow
    {
        public int Id { get; set; }

        public string Category { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string StoredPath { get; set; } = string.Empty;

        public DateTime? UploadedAt { get; set; }

        /// <summary>
        /// المسار الوحيد لفتح الملف: نقطة مصادَقة تفحص صلاحية الموظف المستهدف —
        /// تخدم الملفات المحمية الجديدة والصفوف التاريخية معاً.
        /// </summary>
        public string DownloadUrl => $"/files/employee-profile/{Id}";
    }
}
