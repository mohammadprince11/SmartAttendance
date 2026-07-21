using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeDocuments;

/// <summary>
/// مركز وثائق الموظفين: رفع/عرض/انتهاء صلاحية الوثائق مع تنبيهات الانتهاء —
/// يعمل فوق جدول EmployeeDocuments.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".xls", ".xlsx"
    };

    public IndexModel(ApplicationDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    [BindProperty]
    public DocumentInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int EmployeeId { get; set; }

    public List<EmployeeOption> Employees { get; set; } = new();

    public List<DocumentRow> Documents { get; set; } = new();

    public SelectedEmployeeInfo? SelectedEmployee { get; set; }

    public List<DocumentRequirementRow> RequiredDocuments { get; set; } = new();

    public string? Message { get; set; }

    public bool IsError { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        if (EmployeeId > 0)
        {
            Input.EmployeeId = EmployeeId;
        }

        await LoadAsync();
    }

    public async Task<IActionResult> OnPostUploadAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        if (Input.EmployeeId <= 0)
        {
            return await FailAsync("اختر الموظف أولاً.");
        }

        if (Input.File == null || Input.File.Length == 0)
        {
            return await FailAsync("اختر ملف المستند أولاً.");
        }

        if (Input.File.Length > 10 * 1024 * 1024)
        {
            return await FailAsync("حجم الملف أكبر من 10MB.");
        }

        var extension = Path.GetExtension(Input.File.FileName);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return await FailAsync("نوع الملف غير مدعوم. استخدم PDF, JPG, PNG, DOC, DOCX, XLS, XLSX.");
        }

        var employeeExists = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT COUNT(*) FROM Employees WHERE Id = @EmployeeId",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", Input.EmployeeId));

        if (employeeExists == 0)
        {
            return await FailAsync("الموظف المحدد غير موجود.");
        }

        var selectedEmployeeId = Input.EmployeeId;
        var selectedDocumentType = string.IsNullOrWhiteSpace(Input.DocumentType) ? "ID" : Input.DocumentType.Trim();

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-documents");
        Directory.CreateDirectory(uploadRoot);

        var safeOriginalName = Path.GetFileName(Input.File.FileName);
        var storedName = $"{Input.EmployeeId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
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
(@EmployeeId, @DocumentType, @FileName, @StoredPath, @ExpiryDate, @Notes, @UploadedBy);

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeeDocument', CAST(@EmployeeId AS nvarchar(80)), 'Upload Document', @NewValues, @UploadedBy, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", Input.EmployeeId);
                HrmsDatabase.AddParameter(command, "@DocumentType", selectedDocumentType);
                HrmsDatabase.AddParameter(command, "@FileName", safeOriginalName);
                HrmsDatabase.AddParameter(command, "@StoredPath", relativePath);
                HrmsDatabase.AddParameter(command, "@ExpiryDate", Input.ExpiryDate);
                HrmsDatabase.AddParameter(command, "@Notes", Input.Notes ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@UploadedBy", User?.Identity?.Name ?? "HR");
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                    ("DocumentType", selectedDocumentType),
                    ("FileName", safeOriginalName),
                    ("StoredPath", relativePath),
                    ("ExpiryDate", Input.ExpiryDate)));
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        Message = "تم رفع المستند وحفظه بنجاح.";
        EmployeeId = selectedEmployeeId;
        Input = new DocumentInput
        {
            EmployeeId = selectedEmployeeId,
            DocumentType = selectedDocumentType
        };

        await LoadAsync();

        return Page();
    }

    private async Task<IActionResult> FailAsync(string message)
    {
        IsError = true;
        Message = message;
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Employees = await HrmsDatabase.QueryAsync(
            _dbContext,
            "SELECT TOP 1000 Id, EmployeeNo, FullName FROM Employees ORDER BY EmployeeNo",
            null,
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Text = $"{HrmsDatabase.GetString(reader, "EmployeeNo")} - {HrmsDatabase.GetString(reader, "FullName")}"
            });

        if (EmployeeId > 0)
        {
            SelectedEmployee = await LoadSelectedEmployeeAsync(EmployeeId);
        }

        Documents = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 300
    d.Id,
    d.EmployeeId,
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
WHERE (@EmployeeId <= 0 OR d.EmployeeId = @EmployeeId)
ORDER BY d.UploadedAt DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId),
            reader => new DocumentRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                DocumentType = HrmsDatabase.GetString(reader, "DocumentType"),
                FileName = HrmsDatabase.GetString(reader, "FileName"),
                StoredPath = HrmsDatabase.GetString(reader, "StoredPath"),
                ExpiryDate = HrmsDatabase.GetDateOnly(reader, "ExpiryDate"),
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                UploadedAt = HrmsDatabase.GetDateTime(reader, "UploadedAt")
            });

        BuildRequiredDocuments();
    }

    private async Task<SelectedEmployeeInfo?> LoadSelectedEmployeeAsync(int employeeId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    e.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(e.Nationality, '') AS Nationality,
    ISNULL(e.Country, '') AS Country,
    ISNULL(e.Position, '') AS Position,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(c.Name, '') AS CompanyName
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
LEFT JOIN Companies c ON b.CompanyId = c.Id
WHERE e.Id = @EmployeeId;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new SelectedEmployeeInfo
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                Nationality = HrmsDatabase.GetString(reader, "Nationality"),
                Country = HrmsDatabase.GetString(reader, "Country"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                CompanyName = HrmsDatabase.GetString(reader, "CompanyName")
            });

        return rows.FirstOrDefault();
    }

    private void BuildRequiredDocuments()
    {
        RequiredDocuments.Clear();

        if (EmployeeId <= 0 || SelectedEmployee == null)
        {
            return;
        }

        AddRequirement("ID", "هوية / بطاقة وطنية", "مطلوب لكل موظف.");
        AddRequirement("Contract", "عقد عمل", "مطلوب لكل موظف.");

        if (IsSelectedEmployeeExpat)
        {
            AddRequirement("Passport", "جواز سفر", "مطلوب للوافدين.");
            AddRequirement("Visa", "إقامة / فيزا", "مطلوب للوافدين.");
        }

        if (SelectedEmployeeNeedsHealthCard)
        {
            AddRequirement("Health Card", "بطاقة صحية", "مطلوبة للمطاعم، الكافيهات، الفنادق، والمواقع الغذائية.");
        }
    }

    private void AddRequirement(string code, string name, string reason)
    {
        var matchingDocs = Documents
            .Where(d => IsDocumentMatch(d.DocumentType, code))
            .OrderByDescending(d => d.UploadedAt)
            .ToList();

        var latest = matchingDocs.FirstOrDefault();
        var statusKey = "missing";
        var statusText = "ناقص";
        var detail = "لم يتم رفع هذا المستند بعد.";

        if (latest != null)
        {
            if (latest.ExpiryDate.HasValue && latest.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today))
            {
                statusKey = "expired";
                statusText = "منتهي";
                detail = $"تاريخ الانتهاء: {latest.ExpiryDate.Value:yyyy-MM-dd}";
            }
            else if (latest.ExpiryDate.HasValue && latest.ExpiryDate.Value <= DateOnly.FromDateTime(DateTime.Today).AddDays(30))
            {
                statusKey = "expiring";
                statusText = "قرب الانتهاء";
                detail = $"تاريخ الانتهاء: {latest.ExpiryDate.Value:yyyy-MM-dd}";
            }
            else if (!latest.ExpiryDate.HasValue && (code == "Passport" || code == "Visa" || code == "Health Card"))
            {
                statusKey = "review";
                statusText = "يحتاج تاريخ انتهاء";
                detail = "المستند موجود لكن بدون تاريخ انتهاء.";
            }
            else
            {
                statusKey = "complete";
                statusText = "مكتمل";
                detail = latest.ExpiryDate.HasValue ? $"صالح إلى: {latest.ExpiryDate.Value:yyyy-MM-dd}" : "مرفوع بدون تاريخ انتهاء.";
            }
        }

        RequiredDocuments.Add(new DocumentRequirementRow
        {
            Code = code,
            Name = name,
            Reason = reason,
            StatusKey = statusKey,
            StatusText = statusText,
            Detail = detail
        });
    }

    public bool IsSelectedEmployeeExpat
    {
        get
        {
            if (SelectedEmployee == null)
            {
                return false;
            }

            var text = $"{SelectedEmployee.Nationality} {SelectedEmployee.Country}".Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return !(text.Contains("iraq") || text.Contains("iraqi") || text.Contains("العراق") || text.Contains("عراقي") || text.Contains("عراقية"));
        }
    }

    public bool SelectedEmployeeNeedsHealthCard
    {
        get
        {
            if (SelectedEmployee == null)
            {
                return false;
            }

            var context = $"{SelectedEmployee.CompanyName} {SelectedEmployee.BranchName} {SelectedEmployee.DepartmentName} {SelectedEmployee.Position}".ToLowerInvariant();

            return context.Contains("food")
                || context.Contains("restaurant")
                || context.Contains("cafe")
                || context.Contains("coffee")
                || context.Contains("kitchen")
                || context.Contains("hotel")
                || context.Contains("patchi")
                || context.Contains("rixos")
                || context.Contains("movenpick")
                || context.Contains("مطعم")
                || context.Contains("كافيه")
                || context.Contains("مطبخ")
                || context.Contains("فندق")
                || context.Contains("اغذية")
                || context.Contains("أغذية");
        }
    }

    public int MissingRequiredDocumentsCount => RequiredDocuments.Count(d => d.StatusKey == "missing" || d.StatusKey == "expired" || d.StatusKey == "review");

    public string DocumentTypeText(string? type)
    {
        return type switch
        {
            "ID" => "هوية / بطاقة وطنية",
            "Contract" => "عقد عمل",
            "Passport" => "جواز سفر",
            "Visa" => "إقامة / فيزا",
            "Health Card" => "بطاقة صحية",
            "Certificate" => "شهادة / مؤهل",
            "Memo" => "كتاب / مذكرة",
            "Other" => "أخرى",
            _ => string.IsNullOrWhiteSpace(type) ? "غير محدد" : type
        };
    }

    public string DocumentStatusKey(DateOnly? expiryDate)
    {
        if (!expiryDate.HasValue)
        {
            return "no-expiry";
        }

        if (expiryDate.Value < DateOnly.FromDateTime(DateTime.Today))
        {
            return "expired";
        }

        if (expiryDate.Value <= DateOnly.FromDateTime(DateTime.Today).AddDays(30))
        {
            return "expiring";
        }

        return "valid";
    }

    public string DocumentStatusText(DateOnly? expiryDate)
    {
        return DocumentStatusKey(expiryDate) switch
        {
            "expired" => "منتهي",
            "expiring" => "قرب الانتهاء",
            "valid" => "صالح",
            _ => "بدون تاريخ"
        };
    }

    public string DocumentStatusClass(DateOnly? expiryDate)
    {
        return DocumentStatusKey(expiryDate) switch
        {
            "expired" => "danger",
            "expiring" => "warn",
            "valid" => "ok",
            _ => "muted"
        };
    }

    private bool IsDocumentMatch(string? documentType, string requiredCode)
    {
        var value = (documentType ?? string.Empty).Trim();

        if (value.Equals(requiredCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return requiredCode switch
        {
            "ID" => value.Contains("هوية", StringComparison.OrdinalIgnoreCase) || value.Contains("بطاقة", StringComparison.OrdinalIgnoreCase),
            "Contract" => value.Contains("contract", StringComparison.OrdinalIgnoreCase) || value.Contains("عقد", StringComparison.OrdinalIgnoreCase),
            "Passport" => value.Contains("passport", StringComparison.OrdinalIgnoreCase) || value.Contains("جواز", StringComparison.OrdinalIgnoreCase),
            "Visa" => value.Contains("visa", StringComparison.OrdinalIgnoreCase) || value.Contains("اقامة", StringComparison.OrdinalIgnoreCase) || value.Contains("إقامة", StringComparison.OrdinalIgnoreCase),
            "Health Card" => value.Contains("health", StringComparison.OrdinalIgnoreCase) || value.Contains("صحية", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
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

    public class SelectedEmployeeInfo
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Nationality { get; set; } = string.Empty;

        public string Country { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public string DepartmentName { get; set; } = string.Empty;

        public string BranchName { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;
    }

    public class DocumentRow
    {
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;

        public string DocumentType { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string StoredPath { get; set; } = string.Empty;

        public DateOnly? ExpiryDate { get; set; }

        public string Notes { get; set; } = string.Empty;

        public DateTime? UploadedAt { get; set; }
    }

    public class DocumentRequirementRow
    {
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public string StatusKey { get; set; } = string.Empty;

        public string StatusText { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;
    }
}
