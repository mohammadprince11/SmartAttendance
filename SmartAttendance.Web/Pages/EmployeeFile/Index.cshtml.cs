using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeFile;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(ApplicationDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EmployeeNo { get; set; }

    [BindProperty]
    public EmployeeFileInput Input { get; set; } = new();

    [BindProperty]
    public DocumentInput Document { get; set; } = new();

    public EmployeeInfo? Employee { get; set; }

    public List<EmployeeOption> Managers { get; set; } = new();

    public List<DepartmentOption> Departments { get; set; } = new();

    public List<PositionOption> Positions { get; set; } = new();

    public List<DocumentRow> Documents { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await PositionSchema.EnsureAsync(_dbContext);

        await LoadAsync();

        if (Employee == null)
        {
            ErrorMessage = "لم يتم العثور على الموظف.";
            return RedirectToPage("/Employees/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveEmployeeAsync()
    {
        await PositionSchema.EnsureAsync(_dbContext);

        if (Input.Id <= 0)
        {
            ErrorMessage = "اختر موظف صحيح.";
            return RedirectToPage("/Employees/Index");
        }

        if (string.IsNullOrWhiteSpace(Input.EmployeeNo) || string.IsNullOrWhiteSpace(Input.FullName) || Input.DepartmentId <= 0)
        {
            ErrorMessage = "رقم الموظف والاسم والقسم مطلوبة.";
            return RedirectToPage(new { EmployeeNo = Input.OldEmployeeNo });
        }

        if (!string.IsNullOrWhiteSpace(Input.Position))
        {
            var validPosition = await HrmsDatabase.ScalarAsync<int>(
                _dbContext,
                """
DECLARE @PositionCount int = 0;

IF OBJECT_ID(N'dbo.HrJobPositions', N'U') IS NOT NULL
BEGIN
    SELECT @PositionCount = @PositionCount + COUNT(1)
    FROM dbo.HrJobPositions
    WHERE LTRIM(RTRIM(ISNULL(ArabicName, N''))) = @Name
      AND ISNULL(IsActive, 1) = 1;
END;

IF OBJECT_ID(N'dbo.JobPositions', N'U') IS NOT NULL
BEGIN
    SELECT @PositionCount = @PositionCount + COUNT(1)
    FROM dbo.JobPositions
    WHERE LTRIM(RTRIM(ISNULL(Name, N''))) = @Name
      AND ISNULL(IsActive, 1) = 1;
END;

SELECT @PositionCount;
""",
                command => HrmsDatabase.AddParameter(command, "@Name", Input.Position.Trim()));

            if (validPosition <= 0)
            {
                ErrorMessage = "اختر منصب من القائمة المعتمدة.";
                return RedirectToPage(new { EmployeeNo = Input.OldEmployeeNo });
            }
        }

        var duplicate = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT COUNT(*) FROM Employees WHERE EmployeeNo = @EmployeeNo AND Id <> @Id",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeNo", Input.EmployeeNo.Trim());
                HrmsDatabase.AddParameter(command, "@Id", Input.Id);
            });

        if (duplicate > 0)
        {
            ErrorMessage = "رقم الموظف مستخدم لموظف آخر.";
            return RedirectToPage(new { EmployeeNo = Input.OldEmployeeNo });
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE Employees
SET
    EmployeeNo = @EmployeeNo,
    FullName = @FullName,
    NationalId = @NationalId,
    Phone = @Phone,
    Email = @Email,
    HireDate = @HireDate,
    BirthDate = @BirthDate,
    IsActive = @IsActive,
    DepartmentId = @DepartmentId,
    Position = @Position,
    Gender = @Gender,
    Nationality = @Nationality,
    Country = @Country,
    ContractType = @ContractType,
    ContractEndDate = @ContractEndDate,
    EmploymentStatus = @EmploymentStatus,
    DirectManagerId = @DirectManagerId
WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, OldValues, NewValues, UserName, IpAddress)
VALUES ('Employee', CAST(@Id AS nvarchar(80)), 'Update Full Employee File', @OldValues, @NewValues, 'HR', @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", Input.Id);
                HrmsDatabase.AddParameter(command, "@EmployeeNo", Input.EmployeeNo.Trim());
                HrmsDatabase.AddParameter(command, "@FullName", Input.FullName.Trim());
                HrmsDatabase.AddParameter(command, "@NationalId", Input.NationalId);
                HrmsDatabase.AddParameter(command, "@Phone", Input.Phone);
                HrmsDatabase.AddParameter(command, "@Email", Input.Email);
                HrmsDatabase.AddParameter(command, "@HireDate", Input.HireDate);
                HrmsDatabase.AddParameter(command, "@BirthDate", Input.BirthDate);
                HrmsDatabase.AddParameter(command, "@IsActive", Input.IsActive);
                HrmsDatabase.AddParameter(command, "@DepartmentId", Input.DepartmentId);
                HrmsDatabase.AddParameter(command, "@Position", string.IsNullOrWhiteSpace(Input.Position) ? null : Input.Position.Trim());
                HrmsDatabase.AddParameter(command, "@Gender", Input.Gender);
                HrmsDatabase.AddParameter(command, "@Nationality", Input.Nationality);
                HrmsDatabase.AddParameter(command, "@Country", Input.Country);
                HrmsDatabase.AddParameter(command, "@ContractType", Input.ContractType);
                HrmsDatabase.AddParameter(command, "@ContractEndDate", Input.ContractEndDate);
                HrmsDatabase.AddParameter(command, "@EmploymentStatus", Input.EmploymentStatus);
                HrmsDatabase.AddParameter(command, "@DirectManagerId", Input.DirectManagerId);
                HrmsDatabase.AddParameter(command, "@OldValues", HrmsDatabase.JsonLine(("EmployeeNo", Input.OldEmployeeNo)));
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                    ("EmployeeNo", Input.EmployeeNo),
                    ("FullName", Input.FullName),
                    ("DepartmentId", Input.DepartmentId),
                    ("Position", Input.Position),
                    ("Gender", Input.Gender),
                    ("Nationality", Input.Nationality),
                    ("Country", Input.Country),
                    ("ContractType", Input.ContractType),
                    ("ContractEndDate", Input.ContractEndDate),
                    ("EmploymentStatus", Input.EmploymentStatus),
                    ("DirectManagerId", Input.DirectManagerId)));
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        SuccessMessage = "تم حفظ بيانات الموظف الأساسية والمتقدمة.";
        return RedirectToPage(new { EmployeeNo = Input.EmployeeNo.Trim() });
    }

    public async Task<IActionResult> OnPostUploadDocumentAsync()
    {
        await PositionSchema.EnsureAsync(_dbContext);

        if (Document.EmployeeId <= 0)
        {
            ErrorMessage = "اختر موظف صحيح.";
            return RedirectToPage("/Employees/Index");
        }

        if (Document.File == null || Document.File.Length == 0)
        {
            ErrorMessage = "اختر ملف أولاً.";
            return RedirectToPage(new { EmployeeNo = Document.EmployeeNo });
        }

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-documents");
        Directory.CreateDirectory(uploadRoot);

        var extension = Path.GetExtension(Document.File.FileName);
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadRoot, storedName);
        var relativePath = $"/uploads/employee-documents/{storedName}";

        await using (var stream = System.IO.File.Create(physicalPath))
        {
            await Document.File.CopyToAsync(stream);
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO EmployeeDocuments
(EmployeeId, DocumentType, FileName, StoredPath, ExpiryDate, Notes, UploadedBy)
VALUES
(@EmployeeId, @DocumentType, @FileName, @StoredPath, @ExpiryDate, @Notes, 'HR');

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeeDocument', CAST(@EmployeeId AS nvarchar(80)), 'Upload Employee Document', @NewValues, 'HR', @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", Document.EmployeeId);
                HrmsDatabase.AddParameter(command, "@DocumentType", Document.DocumentType);
                HrmsDatabase.AddParameter(command, "@FileName", Document.File.FileName);
                HrmsDatabase.AddParameter(command, "@StoredPath", relativePath);
                HrmsDatabase.AddParameter(command, "@ExpiryDate", Document.ExpiryDate);
                HrmsDatabase.AddParameter(command, "@Notes", Document.Notes);
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                    ("DocumentType", Document.DocumentType),
                    ("FileName", Document.File.FileName),
                    ("ExpiryDate", Document.ExpiryDate)));
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        SuccessMessage = "تم رفع المستند داخل ملف الموظف.";
        return RedirectToPage(new { EmployeeNo = Document.EmployeeNo });
    }

    private async Task LoadAsync()
    {
        Managers = await HrmsDatabase.QueryAsync(
            _dbContext,
            "SELECT TOP 1000 Id, EmployeeNo, FullName FROM Employees WHERE IsActive = 1 ORDER BY EmployeeNo",
            null,
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Text = $"{HrmsDatabase.GetString(reader, "EmployeeNo")} - {HrmsDatabase.GetString(reader, "FullName")}"
            });

        Departments = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    d.Id,
    d.Name AS Department,
    ISNULL(b.Name, '') AS Branch
FROM Departments d
LEFT JOIN Branches b ON d.BranchId = b.Id
ORDER BY b.Name, d.Name;
""",
            null,
            reader => new DepartmentOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Text = $"{HrmsDatabase.GetString(reader, "Branch")} - {HrmsDatabase.GetString(reader, "Department")}"
            });

        Positions = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
CREATE TABLE #PositionOptions
(
    Id int NOT NULL,
    Name nvarchar(400) NOT NULL
);

IF OBJECT_ID(N'dbo.HrJobPositions', N'U') IS NOT NULL
BEGIN
    INSERT INTO #PositionOptions (Id, Name)
    SELECT Id, LTRIM(RTRIM(ArabicName))
    FROM dbo.HrJobPositions
    WHERE LTRIM(RTRIM(ISNULL(ArabicName, N''))) <> N''
      AND ISNULL(IsActive, 1) = 1;
END;

IF OBJECT_ID(N'dbo.JobPositions', N'U') IS NOT NULL
BEGIN
    INSERT INTO #PositionOptions (Id, Name)
    SELECT j.Id, LTRIM(RTRIM(j.Name))
    FROM dbo.JobPositions j
    WHERE LTRIM(RTRIM(ISNULL(j.Name, N''))) <> N''
      AND ISNULL(j.IsActive, 1) = 1
      AND NOT EXISTS
      (
          SELECT 1
          FROM #PositionOptions existing
          WHERE existing.Name = LTRIM(RTRIM(j.Name))
      );
END;

SELECT MIN(Id) AS Id, Name
FROM #PositionOptions
GROUP BY Name
ORDER BY Name;
""",
            null,
            reader => new PositionOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name")
            });

        var employees = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    e.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(e.NationalId, '') AS NationalId,
    ISNULL(e.Phone, '') AS Phone,
    ISNULL(e.Email, '') AS Email,
    e.HireDate,
    e.BirthDate,
    e.IsActive,
    e.DepartmentId,
    ISNULL(b.Name, '') AS Branch,
    ISNULL(d.Name, '') AS Department,
    ISNULL(e.Position, '') AS Position,
    ISNULL(e.Gender, '') AS Gender,
    ISNULL(e.Nationality, '') AS Nationality,
    ISNULL(e.Country, '') AS Country,
    ISNULL(e.ContractType, '') AS ContractType,
    e.ContractEndDate,
    ISNULL(e.EmploymentStatus, '') AS EmploymentStatus,
    ISNULL(e.DirectManagerId, 0) AS DirectManagerId,
    ISNULL(m.FullName, '') AS DirectManager
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON e.BranchId = b.Id
LEFT JOIN Employees m ON e.DirectManagerId = m.Id
WHERE (@Id IS NOT NULL AND e.Id = @Id)
   OR (@EmployeeNo IS NOT NULL AND @EmployeeNo <> '' AND e.EmployeeNo = @EmployeeNo);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", Id);
                HrmsDatabase.AddParameter(command, "@EmployeeNo", EmployeeNo);
            },
            reader => new EmployeeInfo
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                NationalId = HrmsDatabase.GetString(reader, "NationalId"),
                Phone = HrmsDatabase.GetString(reader, "Phone"),
                Email = HrmsDatabase.GetString(reader, "Email"),
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate"),
                BirthDate = HrmsDatabase.GetDateOnly(reader, "BirthDate"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                Branch = HrmsDatabase.GetString(reader, "Branch"),
                Department = HrmsDatabase.GetString(reader, "Department"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                Gender = HrmsDatabase.GetString(reader, "Gender"),
                Nationality = HrmsDatabase.GetString(reader, "Nationality"),
                Country = HrmsDatabase.GetString(reader, "Country"),
                ContractType = HrmsDatabase.GetString(reader, "ContractType"),
                ContractEndDate = HrmsDatabase.GetDateOnly(reader, "ContractEndDate"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                DirectManagerId = HrmsDatabase.GetInt(reader, "DirectManagerId") == 0 ? null : HrmsDatabase.GetInt(reader, "DirectManagerId"),
                DirectManager = HrmsDatabase.GetString(reader, "DirectManager")
            });

        Employee = employees.FirstOrDefault();

        if (Employee == null)
        {
            return;
        }

        EmployeeNo = Employee.EmployeeNo;

        Input = new EmployeeFileInput
        {
            Id = Employee.Id,
            OldEmployeeNo = Employee.EmployeeNo,
            EmployeeNo = Employee.EmployeeNo,
            FullName = Employee.FullName,
            NationalId = Employee.NationalId,
            Phone = Employee.Phone,
            Email = Employee.Email,
            HireDate = Employee.HireDate,
            BirthDate = Employee.BirthDate,
            IsActive = Employee.IsActive,
            DepartmentId = Employee.DepartmentId,
            Position = Employee.Position,
            Gender = Employee.Gender,
            Nationality = Employee.Nationality,
            Country = Employee.Country,
            ContractType = Employee.ContractType,
            ContractEndDate = Employee.ContractEndDate,
            EmploymentStatus = Employee.EmploymentStatus,
            DirectManagerId = Employee.DirectManagerId
        };

        Document = new DocumentInput
        {
            EmployeeId = Employee.Id,
            EmployeeNo = Employee.EmployeeNo
        };

        Documents = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 200
    Id,
    DocumentType,
    FileName,
    StoredPath,
    ExpiryDate,
    ISNULL(Notes, '') AS Notes,
    UploadedAt
FROM EmployeeDocuments
WHERE EmployeeId = @EmployeeId
ORDER BY UploadedAt DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", Employee.Id),
            reader => new DocumentRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                DocumentType = HrmsDatabase.GetString(reader, "DocumentType"),
                FileName = HrmsDatabase.GetString(reader, "FileName"),
                StoredPath = HrmsDatabase.GetString(reader, "StoredPath"),
                ExpiryDate = HrmsDatabase.GetDateOnly(reader, "ExpiryDate"),
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                UploadedAt = HrmsDatabase.GetDateTime(reader, "UploadedAt")
            });
    }

    public class EmployeeFileInput
    {
        public int Id { get; set; }

        public string OldEmployeeNo { get; set; } = string.Empty;

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string? NationalId { get; set; }

        public string? Phone { get; set; }

        public string? Email { get; set; }

        public DateOnly? HireDate { get; set; }

        public DateOnly? BirthDate { get; set; }

        public bool IsActive { get; set; } = true;

        public int DepartmentId { get; set; }

        public string? Position { get; set; }

        public string? Gender { get; set; }

        public string? Nationality { get; set; }

        public string? Country { get; set; }

        public string? ContractType { get; set; }

        public DateOnly? ContractEndDate { get; set; }

        public string? EmploymentStatus { get; set; }

        public int? DirectManagerId { get; set; }
    }

    public class DocumentInput
    {
        public int EmployeeId { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string DocumentType { get; set; } = "ID";

        public IFormFile? File { get; set; }

        public DateOnly? ExpiryDate { get; set; }

        public string? Notes { get; set; }
    }

    public class EmployeeInfo
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string NationalId { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public DateOnly? HireDate { get; set; }

        public DateOnly? BirthDate { get; set; }

        public bool IsActive { get; set; }

        public int DepartmentId { get; set; }

        public string Branch { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public string Gender { get; set; } = string.Empty;

        public string Nationality { get; set; } = string.Empty;

        public string Country { get; set; } = string.Empty;

        public string ContractType { get; set; } = string.Empty;

        public DateOnly? ContractEndDate { get; set; }

        public string EmploymentStatus { get; set; } = string.Empty;

        public int? DirectManagerId { get; set; }

        public string DirectManager { get; set; } = string.Empty;
    }

    public class EmployeeOption
    {
        public int Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    public class DepartmentOption
    {
        public int Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    public class PositionOption
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class DocumentRow
    {
        public int Id { get; set; }

        public string DocumentType { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string StoredPath { get; set; } = string.Empty;

        public DateOnly? ExpiryDate { get; set; }

        public string Notes { get; set; } = string.Empty;

        public DateTime? UploadedAt { get; set; }
    }
}
