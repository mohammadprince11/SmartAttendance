using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

public partial class ProfileModel : PageModel
{
    private static readonly HashSet<string> AllowedEmployeePhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public ProfileModel(ApplicationDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EmployeeNo { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    public EmployeeProfileCard? Employee { get; set; }

    public List<AttendanceRow> AttendanceRows { get; set; } = new();

    public List<RequestRow> RequestRows { get; set; } = new();

    public List<DocumentRow> DocumentRows { get; set; } = new();

    public List<ShiftRow> ShiftRows { get; set; } = new();

    public List<AuditRow> AuditRows { get; set; } = new();

    public List<EmployeeProfileDynamicSection> ProfileDynamicSections { get; set; } = new();

    public int AttendanceCount { get; set; }

    public int PresentCount { get; set; }

    public int LateCount { get; set; }

    public int AbsentCount { get; set; }

    public int MissingCheckoutCount { get; set; }

    public int TotalWorkingMinutes { get; set; }

    public int PendingRequests { get; set; }

    public int ApprovedRequests { get; set; }

    public int RejectedRequests { get; set; }

    public string? ErrorMessage { get; set; }

    [BindProperty]
    public IFormFile? EmployeePhoto { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await EmployeeLifecycleSchema.EnsureAsync(_dbContext);
        await EnsureProfileFilesTableAsync();

        if (!FromDate.HasValue)
        {
            FromDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        }

        if (!ToDate.HasValue)
        {
            ToDate = DateOnly.FromDateTime(DateTime.Today);
        }

        Employee = await LoadEmployeeAsync();

        if (Employee != null)
        {
            ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, Employee.Id);
        }

        if (Employee == null)
        {
            ErrorMessage = "لم يتم العثور على الموظف المطلوب.";
            return Page();
        }

        await LoadProfileReassignLookupsAsync();
        await LoadAttendanceAsync(Employee.Id);
        await LoadRequestsAsync(Employee.Id);
        await LoadDocumentsAsync(Employee.Id);
        await LoadProfileFilesAsync(Employee.Id);
        await LoadShiftsAsync(Employee.Id);
        await LoadAuditAsync(Employee.Id);

        return Page();
    }

    public async Task<IActionResult> OnPostReassignFromModalAsync(
        int id,
        string? reassignDate,
        string? reassignPosition,
        string? reassignReason,
        string? reassignNotes,
        string? confirmReassign)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await EmployeeLifecycleSchema.EnsureAsync(_dbContext);

        if (id <= 0)
        {
            TempData["ErrorMessage"] = "\u0644\u0645 \u064a\u062a\u0645 \u062a\u062d\u062f\u064a\u062f \u0627\u0644\u0645\u0648\u0638\u0641.";
            return RedirectToPage("./Index");
        }

        var employee = await LoadProfileReassignEmployeeAsync(id);

        if (employee == null)
        {
            TempData["ErrorMessage"] = "\u0644\u0645 \u064a\u062a\u0645 \u0627\u0644\u0639\u062b\u0648\u0631 \u0639\u0644\u0649 \u0627\u0644\u0645\u0648\u0638\u0641.";
            return RedirectToPage("./Index");
        }

        if (employee.IsActive)
        {
            TempData["ErrorMessage"] = "\u0627\u0644\u0645\u0648\u0638\u0641 \u0641\u0639\u0627\u0644 \u062d\u0627\u0644\u064a\u0627\u064b \u0648\u0644\u0627 \u064a\u062d\u062a\u0627\u062c \u0625\u0644\u0649 \u0625\u0639\u0627\u062f\u0629 \u062a\u0639\u064a\u064a\u0646.";
            return RedirectToPage(new { id });
        }

        if (!DateOnly.TryParse(reassignDate, out var reassignDateValue))
        {
            TempData["ErrorMessage"] = "\u062d\u062f\u062f \u062a\u0627\u0631\u064a\u062e \u0627\u0644\u0645\u0628\u0627\u0634\u0631\u0629 \u0627\u0644\u062c\u062f\u064a\u062f.";
            return RedirectToPage(new { id });
        }

        if (string.IsNullOrWhiteSpace(reassignReason))
        {
            TempData["ErrorMessage"] = "\u0627\u0643\u062a\u0628 \u0633\u0628\u0628 \u0625\u0639\u0627\u062f\u0629 \u0627\u0644\u062a\u0639\u064a\u064a\u0646.";
            return RedirectToPage(new { id });
        }

        var isConfirmed =
            string.Equals(confirmReassign, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(confirmReassign, "on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(confirmReassign, "1", StringComparison.OrdinalIgnoreCase);

        if (!isConfirmed)
        {
            TempData["ErrorMessage"] = "\u064a\u062c\u0628 \u062a\u0623\u0643\u064a\u062f \u0625\u0639\u0627\u062f\u0629 \u0627\u0644\u062a\u0639\u064a\u064a\u0646 \u0642\u0628\u0644 \u0627\u0644\u062d\u0641\u0638.";
            return RedirectToPage(new { id });
        }

        var newPosition = string.IsNullOrWhiteSpace(reassignPosition)
            ? employee.Position
            : reassignPosition.Trim();

        var reason = reassignReason.Trim();
        var notes = BuildProfileReassignNotes(employee.Position, newPosition, reassignNotes);
        var userName = Request.Cookies["SA.UserName"] ?? "System";
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        object? previousHireDateSql = employee.HireDate.HasValue
            ? employee.HireDate.Value.ToDateTime(TimeOnly.MinValue)
            : null;

        var reassignDateSql = reassignDateValue.ToDateTime(TimeOnly.MinValue);

        var oldValues =
            $"IsActive: {employee.IsActive}; HireDate: {DisplayDate(employee.HireDate)}; Position: {employee.Position}; EmploymentStatus: {employee.EmploymentStatus}; ServiceEndDate: {DisplayDate(employee.ServiceEndDate)}";

        var newValues =
            $"IsActive: True; HireDate/ReassignDate: {reassignDateValue:yyyy-MM-dd}; Position: {newPosition}; EmploymentStatus: Active";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
INSERT INTO EmployeeRehires
(
    EmployeeId,
    EmployeeNo,
    EmployeeName,
    PreviousHireDate,
    RehireDate,
    PreviousEmploymentStatus,
    Reason,
    HrNotes,
    CreatedBy,
    IpAddress,
    CreatedAt
)
VALUES
(
    @EmployeeId,
    @EmployeeNo,
    @EmployeeName,
    @PreviousHireDate,
    @RehireDate,
    @PreviousEmploymentStatus,
    @Reason,
    @HrNotes,
    @CreatedBy,
    @IpAddress,
    GETDATE()
);

UPDATE Employees
SET
    IsActive = 1,
    HireDate = @RehireDate,
    Position = @Position,
    EmploymentStatus = N'Active',
    ServiceEndDate = NULL,
    ServiceEndType = NULL,
    ServiceEndReason = NULL,
    ServiceEndNotes = NULL,
    ClearanceStatus = NULL,
    LastRehireDate = @RehireDate,
    RehireReason = @Reason,
    RehireNotes = @HrNotes,
    RehireCount = ISNULL(RehireCount, 0) + 1
WHERE Id = @EmployeeId;

IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
BEGIN
    INSERT INTO AuditLogs
    (
        EntityName,
        EntityId,
        Action,
        OldValues,
        NewValues,
        UserName,
        IpAddress
    )
    VALUES
    (
        'Employee',
        CAST(@EmployeeId AS nvarchar(80)),
        'Employee Reassigned From Profile Modal',
        @OldValues,
        @NewValues,
        @CreatedBy,
        @IpAddress
    );
END;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", id);
                HrmsDatabase.AddParameter(command, "@EmployeeNo", employee.EmployeeNo);
                HrmsDatabase.AddParameter(command, "@EmployeeName", employee.FullName);
                HrmsDatabase.AddParameter(command, "@PreviousHireDate", previousHireDateSql);
                HrmsDatabase.AddParameter(command, "@RehireDate", reassignDateSql);
                HrmsDatabase.AddParameter(command, "@PreviousEmploymentStatus", employee.EmploymentStatus);
                HrmsDatabase.AddParameter(command, "@Position", newPosition);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@HrNotes", string.IsNullOrWhiteSpace(notes) ? null : notes);
                HrmsDatabase.AddParameter(command, "@CreatedBy", userName);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
                HrmsDatabase.AddParameter(command, "@OldValues", oldValues);
                HrmsDatabase.AddParameter(command, "@NewValues", newValues);
            });

        TempData["SuccessMessage"] = "\u062a\u0645\u062a \u0625\u0639\u0627\u062f\u0629 \u062a\u0639\u064a\u064a\u0646 \u0627\u0644\u0645\u0648\u0638\u0641 \u0648\u062d\u0641\u0638 \u0627\u0644\u062d\u0631\u0643\u0629 \u0641\u064a \u0627\u0644\u0633\u062c\u0644 \u0627\u0644\u0648\u0638\u064a\u0641\u064a.";
        return RedirectToPage(new { id });
    }

    private async Task<ProfileReassignEmployeeRow?> LoadProfileReassignEmployeeAsync(int employeeId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 1
    e.Id,
    e.EmployeeNo,
    e.FullName,
    e.HireDate,
    e.IsActive,
    ISNULL(e.Position, '') AS Position,
    ISNULL(e.EmploymentStatus, '') AS EmploymentStatus,
    e.ServiceEndDate,
    ISNULL(e.ServiceEndReason, '') AS ServiceEndReason
FROM Employees e
WHERE e.Id = @EmployeeId;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new ProfileReassignEmployeeRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                ServiceEndDate = HrmsDatabase.GetDateOnly(reader, "ServiceEndDate"),
                ServiceEndReason = HrmsDatabase.GetString(reader, "ServiceEndReason")
            });

        return rows.FirstOrDefault();
    }

    private static string BuildProfileReassignNotes(string previousPosition, string newPosition, string? hrNotes)
    {
        var items = new List<string>();

        if (!string.Equals(previousPosition?.Trim(), newPosition?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            items.Add($"Position changed from '{previousPosition}' to '{newPosition}'.");
        }

        if (!string.IsNullOrWhiteSpace(hrNotes))
        {
            items.Add(hrNotes.Trim());
        }

        return string.Join(" | ", items);
    }

    private class ProfileReassignEmployeeRow
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public DateOnly? HireDate { get; set; }

        public bool IsActive { get; set; }

        public string Position { get; set; } = string.Empty;

        public string EmploymentStatus { get; set; } = string.Empty;

        public DateOnly? ServiceEndDate { get; set; }

        public string ServiceEndReason { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostUploadProfilePhotoAsync(int id)
    {
        await EmployeeLifecycleSchema.EnsureAsync(_dbContext);

        if (id <= 0)
        {
            TempData["ErrorMessage"] = "لم يتم تحديد الموظف.";
            return RedirectToPage("./Index");
        }

        var result = await SaveEmployeePhotoAsync(id, EmployeePhoto);

        if (string.IsNullOrWhiteSpace(result))
        {
            TempData["ErrorMessage"] = "اختر صورة صحيحة بصيغة PNG أو JPG أو WEBP وبحجم لا يتجاوز 5MB.";
        }
        else
        {
            TempData["SuccessMessage"] = "تم تحديث صورة الموظف بنجاح.";
        }

        return RedirectToPage(new { id });
    }

    private async Task<string> SaveEmployeePhotoAsync(int employeeId, IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedEmployeePhotoExtensions.Contains(extension))
        {
            return string.Empty;
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            return string.Empty;
        }

        var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-photos");
        Directory.CreateDirectory(uploadsRoot);

        var fileName = $"employee_{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(uploadsRoot, fileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = $"/uploads/employee-photos/{fileName}";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "UPDATE Employees SET PhotoPath = @PhotoPath WHERE Id = @EmployeeId;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PhotoPath", relativePath);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });

        return relativePath;
    }

    private async Task<EmployeeProfileCard?> LoadEmployeeAsync()
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 1
    e.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(e.NationalId, '') AS NationalId,
    ISNULL(e.Phone, '') AS Phone,
    ISNULL(e.Email, '') AS Email,
    e.HireDate,
    e.BirthDate,
    ISNULL(e.MaritalStatus, '') AS MaritalStatus,
    e.IsActive,
    ISNULL(e.Position, '') AS Position,
    ISNULL(e.PhotoPath, '') AS PhotoPath,
    ISNULL(e.Gender, '') AS Gender,
    ISNULL(e.Nationality, '') AS Nationality,
    ISNULL(e.Country, '') AS Country,
    ISNULL(e.ContractType, '') AS ContractType,
    e.ContractEndDate,
    ISNULL(e.EmploymentStatus, '') AS EmploymentStatus,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(c.Name, '') AS CompanyName,
    ISNULL(m.FullName, '') AS DirectManager
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
LEFT JOIN Companies c ON b.CompanyId = c.Id
LEFT JOIN Employees m ON e.DirectManagerId = m.Id
WHERE
    (@Id > 0 AND e.Id = @Id)
    OR
    (@Id = 0 AND @EmployeeNo <> '' AND e.EmployeeNo = @EmployeeNo);",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", Id);
                HrmsDatabase.AddParameter(command, "@EmployeeNo", EmployeeNo ?? "");
            },
            reader => new EmployeeProfileCard
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                NationalId = HrmsDatabase.GetString(reader, "NationalId"),
                Phone = HrmsDatabase.GetString(reader, "Phone"),
                Email = HrmsDatabase.GetString(reader, "Email"),
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate"),
                BirthDate = HrmsDatabase.GetDateOnly(reader, "BirthDate"),
                MaritalStatus = HrmsDatabase.GetString(reader, "MaritalStatus"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                PhotoPath = HrmsDatabase.GetString(reader, "PhotoPath"),
                Gender = HrmsDatabase.GetString(reader, "Gender"),
                Nationality = HrmsDatabase.GetString(reader, "Nationality"),
                Country = HrmsDatabase.GetString(reader, "Country"),
                ContractType = HrmsDatabase.GetString(reader, "ContractType"),
                ContractEndDate = HrmsDatabase.GetDateOnly(reader, "ContractEndDate"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                CompanyName = HrmsDatabase.GetString(reader, "CompanyName"),
                DirectManager = HrmsDatabase.GetString(reader, "DirectManager")
            });

        return rows.FirstOrDefault();
    }

    private async Task LoadAttendanceAsync(int employeeId)
    {
        AttendanceCount = await CountAsync(
            @"SELECT COUNT(*) FROM AttendanceRecords WHERE EmployeeId = @EmployeeId AND AttendanceDate BETWEEN @FromDate AND @ToDate",
            employeeId);

        PresentCount = await CountAsync(
            @"SELECT COUNT(*) FROM AttendanceRecords WHERE EmployeeId = @EmployeeId AND AttendanceDate BETWEEN @FromDate AND @ToDate AND Status = 1",
            employeeId);

        LateCount = await CountAsync(
            @"SELECT COUNT(*) FROM AttendanceRecords WHERE EmployeeId = @EmployeeId AND AttendanceDate BETWEEN @FromDate AND @ToDate AND Status = 2",
            employeeId);

        AbsentCount = await CountAsync(
            @"SELECT COUNT(*) FROM AttendanceRecords WHERE EmployeeId = @EmployeeId AND AttendanceDate BETWEEN @FromDate AND @ToDate AND Status = 3",
            employeeId);

        MissingCheckoutCount = await CountAsync(
            @"SELECT COUNT(*) FROM AttendanceRecords WHERE EmployeeId = @EmployeeId AND AttendanceDate BETWEEN @FromDate AND @ToDate AND CheckOut IS NULL",
            employeeId);

        TotalWorkingMinutes = await CountAsync(
            @"SELECT ISNULL(SUM(CASE WHEN CheckOut IS NOT NULL THEN DATEDIFF(minute, CheckIn, CheckOut) ELSE 0 END), 0)
              FROM AttendanceRecords
              WHERE EmployeeId = @EmployeeId AND AttendanceDate BETWEEN @FromDate AND @ToDate",
            employeeId);

        AttendanceRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 60
    ar.AttendanceDate,
    ar.CheckIn,
    ar.CheckOut,
    ar.Status,
    ar.Source,
    ISNULL(ar.Notes, '') AS Notes,
    ISNULL(dv.Name, '') AS DeviceName
FROM AttendanceRecords ar
LEFT JOIN Devices dv ON ar.DeviceId = dv.Id
WHERE ar.EmployeeId = @EmployeeId
  AND ar.AttendanceDate BETWEEN @FromDate AND @ToDate
ORDER BY ar.AttendanceDate DESC, ar.CheckIn DESC;",
            command =>
            {
                AddEmployeeDateParameters(command, employeeId);
            },
            reader => new AttendanceRow
            {
                AttendanceDate = HrmsDatabase.GetDateOnly(reader, "AttendanceDate"),
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut"),
                Status = HrmsDatabase.GetInt(reader, "Status"),
                Source = HrmsDatabase.GetInt(reader, "Source"),
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                DeviceName = HrmsDatabase.GetString(reader, "DeviceName")
            });
    }

    private async Task LoadRequestsAsync(int employeeId)
    {
        PendingRequests = await CountRequestAsync(employeeId, "Pending");
        ApprovedRequests = await CountRequestAsync(employeeId, "Approved");
        RejectedRequests = await CountRequestAsync(employeeId, "Rejected");

        RequestRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 25
    Id,
    RequestType,
    RequestDate,
    FromDate,
    ToDate,
    CONVERT(varchar(5), StartTime, 108) AS StartTimeText,
    CONVERT(varchar(5), EndTime, 108) AS EndTimeText,
    ISNULL(Status, '') AS Status,
    ISNULL(CurrentStep, '') AS CurrentStep,
    ISNULL(Reason, '') AS Reason,
    CreatedAt
FROM SelfServiceRequests
WHERE EmployeeId = @EmployeeId
ORDER BY CreatedAt DESC;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new RequestRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                RequestDate = HrmsDatabase.GetDateOnly(reader, "RequestDate"),
                FromDate = HrmsDatabase.GetDateOnly(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateOnly(reader, "ToDate"),
                StartTime = HrmsDatabase.GetString(reader, "StartTimeText"),
                EndTime = HrmsDatabase.GetString(reader, "EndTimeText"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    private async Task LoadDocumentsAsync(int employeeId)
    {
        DocumentRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 25
    Id,
    DocumentType,
    FileName,
    StoredPath,
    ExpiryDate,
    ISNULL(Notes, '') AS Notes,
    UploadedAt,
    ISNULL(UploadedBy, '') AS UploadedBy
FROM EmployeeDocuments
WHERE EmployeeId = @EmployeeId
ORDER BY UploadedAt DESC;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new DocumentRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                DocumentType = HrmsDatabase.GetString(reader, "DocumentType"),
                FileName = HrmsDatabase.GetString(reader, "FileName"),
                StoredPath = HrmsDatabase.GetString(reader, "StoredPath"),
                ExpiryDate = HrmsDatabase.GetDateOnly(reader, "ExpiryDate"),
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                UploadedAt = HrmsDatabase.GetDateTime(reader, "UploadedAt"),
                UploadedBy = HrmsDatabase.GetString(reader, "UploadedBy")
            });
    }

    private async Task LoadShiftsAsync(int employeeId)
    {
        ShiftRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 15
    es.Id,
    s.Code,
    s.Name,
    CONVERT(varchar(5), s.StartTime, 108) AS StartTimeText,
    CONVERT(varchar(5), s.EndTime, 108) AS EndTimeText,
    s.WorkingHours,
    es.EffectiveFrom,
    es.EffectiveTo,
    es.IsCurrent,
    ISNULL(es.WeeklyOffDays, '') AS WeeklyOffDays
FROM EmployeeShifts es
INNER JOIN Shifts s ON es.ShiftId = s.Id
WHERE es.EmployeeId = @EmployeeId
ORDER BY es.IsCurrent DESC, es.EffectiveFrom DESC;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new ShiftRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Code = HrmsDatabase.GetString(reader, "Code"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                StartTime = HrmsDatabase.GetString(reader, "StartTimeText"),
                EndTime = HrmsDatabase.GetString(reader, "EndTimeText"),
                WorkingHours = HrmsDatabase.GetString(reader, "WorkingHours"),
                EffectiveFrom = HrmsDatabase.GetDateOnly(reader, "EffectiveFrom"),
                EffectiveTo = HrmsDatabase.GetDateOnly(reader, "EffectiveTo"),
                IsCurrent = HrmsDatabase.GetBool(reader, "IsCurrent"),
                WeeklyOffDays = HrmsDatabase.GetString(reader, "WeeklyOffDays")
            });
    }

    private async Task LoadAuditAsync(int employeeId)
    {
        AuditRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 25
    EntityName,
    EntityId,
    Action,
    ISNULL(OldValues, '') AS OldValues,
    ISNULL(NewValues, '') AS NewValues,
    ISNULL(UserName, '') AS UserName,
    ISNULL(IpAddress, '') AS IpAddress,
    CreatedAt
FROM AuditLogs
WHERE EntityName = 'Employee'
  AND EntityId = CAST(@EmployeeId AS nvarchar(80))
ORDER BY CreatedAt DESC;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new AuditRow
            {
                EntityName = HrmsDatabase.GetString(reader, "EntityName"),
                EntityId = HrmsDatabase.GetString(reader, "EntityId"),
                Action = HrmsDatabase.GetString(reader, "Action"),
                OldValues = HrmsDatabase.GetString(reader, "OldValues"),
                NewValues = HrmsDatabase.GetString(reader, "NewValues"),
                UserName = HrmsDatabase.GetString(reader, "UserName"),
                IpAddress = HrmsDatabase.GetString(reader, "IpAddress"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    private async Task<int> CountAsync(string sql, int employeeId)
    {
        return await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            sql,
            command => AddEmployeeDateParameters(command, employeeId));
    }

    private async Task<int> CountRequestAsync(int employeeId, string status)
    {
        return await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            @"SELECT COUNT(*) FROM SelfServiceRequests WHERE EmployeeId = @EmployeeId AND Status = @Status",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Status", status);
            });
    }

    private void AddEmployeeDateParameters(System.Data.Common.DbCommand command, int employeeId)
    {
        HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
        HrmsDatabase.AddParameter(command, "@FromDate", FromDate);
        HrmsDatabase.AddParameter(command, "@ToDate", ToDate);
    }

    public string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    public string DisplayDate(DateOnly? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";
    }

    public string DisplayDateTime(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : "-";
    }

    public string DisplayTime(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("HH:mm") : "-";
    }

    public string StatusText(int status)
    {
        return status switch
        {
            1 => "حاضر",
            2 => "متأخر",
            3 => "غائب",
            4 => "إجازة",
            5 => "راحة / عطلة",
            _ => "غير محدد"
        };
    }

    public string SourceText(int source)
    {
        return source switch
        {
            1 => "جهاز",
            2 => "استيراد",
            3 => "تعديل يدوي",
            _ => "-"
        };
    }

    public string RequestStatusText(string? status)
    {
        return status switch
        {
            "Pending" => "معلق",
            "Approved" => "مقبول",
            "Rejected" => "مرفوض",
            _ => Display(status)
        };
    }

    public decimal TotalWorkingHours => Math.Round(TotalWorkingMinutes / 60m, 2);

    public int PayrollRiskItems => AbsentCount + MissingCheckoutCount + PendingRequests + ExpiredDocumentsCount + ExpiringDocumentsCount;

    public int AttendanceRiskItems => AbsentCount + LateCount + MissingCheckoutCount;

    public int ExpiredDocumentsCount => DocumentRows.Count(x => x.ExpiryDate.HasValue && x.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today));

    public int ExpiringDocumentsCount => DocumentRows.Count(x =>
        x.ExpiryDate.HasValue &&
        x.ExpiryDate.Value >= DateOnly.FromDateTime(DateTime.Today) &&
        x.ExpiryDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(30)));

    public string PayrollReadinessClass => PayrollRiskItems == 0 ? "ok" : "warn";

    public string PayrollReadinessText => PayrollRiskItems == 0
        ? "جاهز مبدئياً لإغلاق الراتب"
        : "يحتاج مراجعة قبل إغلاق الراتب";

    public string AttendanceRiskClass => AttendanceRiskItems == 0 ? "ok" : "warn";

    
    
    public int AttendanceExceptionsCount => AttendanceRows.Count(x =>
        x.Status == 2 ||
        x.Status == 3 ||
        !x.CheckIn.HasValue ||
        !x.CheckOut.HasValue ||
        !string.IsNullOrWhiteSpace(x.Notes));

    public List<AttendanceRow> AttendanceExceptionRows => AttendanceRows
        .Where(x =>
            x.Status == 2 ||
            x.Status == 3 ||
            !x.CheckIn.HasValue ||
            !x.CheckOut.HasValue ||
            !string.IsNullOrWhiteSpace(x.Notes))
        .Take(20)
        .ToList();

    public string AttendanceExceptionClass => AttendanceExceptionsCount == 0 ? "ok" : "warn";
public int Employee360HealthScore
    {
        get
        {
            var score = 100;

            score -= AbsentCount * 8;
            score -= MissingCheckoutCount * 10;
            score -= PendingRequests * 6;
            score -= ExpiredDocumentsCount * 12;
            score -= ExpiringDocumentsCount * 4;

            if (score < 0)
            {
                return 0;
            }

            return score;
        }
    }

    public string Employee360HealthClass => Employee360HealthScore >= 85
        ? "ok"
        : Employee360HealthScore >= 60
            ? "warn"
            : "danger";

    public string Employee360HealthText => Employee360HealthScore >= 85
        ? "مستقر"
        : Employee360HealthScore >= 60
            ? "يحتاج متابعة"
            : "خطر تشغيلي";

    public bool HasPayrollBlockingIssues => AbsentCount > 0 || MissingCheckoutCount > 0 || PendingRequests > 0 || ExpiredDocumentsCount > 0;
    public string AttendanceStatusName(int status)
    {
        return status switch
        {
            1 => "حاضر",
            2 => "متأخر",
            3 => "غياب",
            4 => "بصمة ناقصة",
            5 => "عطلة",
            _ => "غير محدد"
        };
    }
    public string AttendanceSourceName(int source)
    {
        return source switch
        {
            1 => "جهاز بصمة",
            2 => "إدخال يدوي",
            3 => "استيراد",
            4 => "طلب",
            5 => "نظام",
            _ => "غير محدد"
        };
    }


public class EmployeeProfileCard
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string NationalId { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public DateOnly? HireDate { get; set; }

        public DateOnly? BirthDate { get; set; }

        
        public string MaritalStatus { get; set; } = string.Empty;
public bool IsActive { get; set; }

        public string Position { get; set; } = string.Empty;

        public string PhotoPath { get; set; } = string.Empty;

        public string Gender { get; set; } = string.Empty;

        public string Nationality { get; set; } = string.Empty;

        public string Country { get; set; } = string.Empty;

        public string ContractType { get; set; } = string.Empty;

        public DateOnly? ContractEndDate { get; set; }

        public string EmploymentStatus { get; set; } = string.Empty;

        public string DepartmentName { get; set; } = string.Empty;

        public string BranchName { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;

        public string DirectManager { get; set; } = string.Empty;
    }

    public class AttendanceRow
    {
        public DateOnly? AttendanceDate { get; set; }

        public DateTime? CheckIn { get; set; }

        public DateTime? CheckOut { get; set; }

        public int Status { get; set; }

        public int Source { get; set; }

        public string DeviceName { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;
    }

    public class RequestRow
    {
        public int Id { get; set; }

        public string RequestType { get; set; } = string.Empty;

        public DateOnly? RequestDate { get; set; }

        public DateOnly? FromDate { get; set; }

        public DateOnly? ToDate { get; set; }

        public string StartTime { get; set; } = string.Empty;

        public string EndTime { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string CurrentStep { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }
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

        public string UploadedBy { get; set; } = string.Empty;
    }

    public class ShiftRow
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string StartTime { get; set; } = string.Empty;

        public string EndTime { get; set; } = string.Empty;

        public string WorkingHours { get; set; } = string.Empty;

        public DateOnly? EffectiveFrom { get; set; }

        public DateOnly? EffectiveTo { get; set; }

        public bool IsCurrent { get; set; }

        public string WeeklyOffDays { get; set; } = string.Empty;
    }

    public class AuditRow
    {
        public string EntityName { get; set; } = string.Empty;

        public string EntityId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string OldValues { get; set; } = string.Empty;

        public string NewValues { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string IpAddress { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }
    }
}







