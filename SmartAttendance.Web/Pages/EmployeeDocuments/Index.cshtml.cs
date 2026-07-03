using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeDocuments;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(ApplicationDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    [BindProperty]
    public DocumentInput Input { get; set; } = new();

    public List<EmployeeOption> Employees { get; set; } = new();

    public List<DocumentRow> Documents { get; set; } = new();

    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostUploadAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        if (Input.File == null || Input.File.Length == 0)
        {
            Message = "اختر ملف أولاً.";
            await LoadAsync();
            return Page();
        }

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-documents");
        Directory.CreateDirectory(uploadRoot);

        var extension = Path.GetExtension(Input.File.FileName);
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadRoot, storedName);
        var relativePath = $"/uploads/employee-documents/{storedName}";

        await using (var stream = System.IO.File.Create(physicalPath))
        {
            await Input.File.CopyToAsync(stream);
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO EmployeeDocuments
(EmployeeId, DocumentType, FileName, StoredPath, ExpiryDate, Notes, UploadedBy)
VALUES
(@EmployeeId, @DocumentType, @FileName, @StoredPath, @ExpiryDate, @Notes, 'HR');

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeeDocument', CAST(@EmployeeId AS nvarchar(80)), 'Upload Document', @NewValues, 'HR', @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", Input.EmployeeId);
                HrmsDatabase.AddParameter(command, "@DocumentType", Input.DocumentType);
                HrmsDatabase.AddParameter(command, "@FileName", Input.File.FileName);
                HrmsDatabase.AddParameter(command, "@StoredPath", relativePath);
                HrmsDatabase.AddParameter(command, "@ExpiryDate", Input.ExpiryDate);
                HrmsDatabase.AddParameter(command, "@Notes", Input.Notes);
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                    ("DocumentType", Input.DocumentType),
                    ("FileName", Input.File.FileName),
                    ("ExpiryDate", Input.ExpiryDate)));
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        Message = "تم رفع المستند.";
        await LoadAsync();

        return Page();
    }

    private async Task LoadAsync()
    {
        Employees = await HrmsDatabase.QueryAsync(
            _dbContext,
            "SELECT TOP 500 Id, EmployeeNo, FullName FROM Employees ORDER BY EmployeeNo",
            null,
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Text = $"{HrmsDatabase.GetString(reader, "EmployeeNo")} - {HrmsDatabase.GetString(reader, "FullName")}"
            });

        Documents = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 200
    d.Id,
    e.EmployeeNo,
    e.FullName,
    d.DocumentType,
    d.FileName,
    d.StoredPath,
    d.ExpiryDate,
    ISNULL(d.Notes, '') AS Notes,
    d.UploadedAt
FROM EmployeeDocuments d
INNER JOIN Employees e ON d.EmployeeId = e.Id
ORDER BY d.UploadedAt DESC;
""",
            null,
            reader => new DocumentRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                DocumentType = HrmsDatabase.GetString(reader, "DocumentType"),
                FileName = HrmsDatabase.GetString(reader, "FileName"),
                StoredPath = HrmsDatabase.GetString(reader, "StoredPath"),
                ExpiryDate = HrmsDatabase.GetDateOnly(reader, "ExpiryDate"),
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                UploadedAt = HrmsDatabase.GetDateTime(reader, "UploadedAt")
            });
    }

    public class DocumentInput
    {
        public int EmployeeId { get; set; }

        public string DocumentType { get; set; } = "ID";

        public IFormFile? File { get; set; }

        public DateOnly? ExpiryDate { get; set; }

        public string? Notes { get; set; }
    }

    public class EmployeeOption
    {
        public int Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    public class DocumentRow
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;

        public string DocumentType { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string StoredPath { get; set; } = string.Empty;

        public DateOnly? ExpiryDate { get; set; }

        public string Notes { get; set; } = string.Empty;

        public DateTime? UploadedAt { get; set; }
    }
}
