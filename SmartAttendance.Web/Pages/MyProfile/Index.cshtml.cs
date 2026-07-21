using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.MyProfile;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty]
    public ProfileInputModel ProfileInput { get; set; } = new();

    [BindProperty]
    public RequestInputModel RequestInput { get; set; } = new();

    public EmployeeCard? Employee { get; set; }

    public List<AttendanceRow> AttendanceRows { get; set; } = new();

    public List<RequestRow> Requests { get; set; } = new();

    public int TotalAttendance { get; set; }

    public int PresentCount { get; set; }

    public int LateCount { get; set; }

    public int MissingCheckoutCount { get; set; }

    public int RequestsCount { get; set; }

    public int PendingRequestsCount { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var employeeId = GetCurrentEmployeeId();

        if (employeeId <= 0)
        {
            return RedirectToPage("/AccessDenied");
        }

        await LoadAsync(employeeId);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveProfileAsync()
    {
        var employeeId = GetCurrentEmployeeId();

        if (employeeId <= 0)
        {
            return RedirectToPage("/AccessDenied");
        }

        if (string.IsNullOrWhiteSpace(ProfileInput.FullName))
        {
            ErrorMessage = "Ø§Ù„Ø§Ø³Ù… Ù…Ø·Ù„ÙˆØ¨.";
            return RedirectToPage(new { FromDate, ToDate });
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
UPDATE Employees
SET FullName = @FullName,
    Phone = @Phone,
    Email = @Email,
    NationalId = @NationalId,
    BirthDate = @BirthDate,
    Nationality = @Nationality,
    Country = @Country
WHERE Id = @EmployeeId;

IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
BEGIN
    INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
    VALUES ('Employee', CAST(@EmployeeId AS nvarchar(80)), 'Employee Update Own Profile', @NewValues, @UserName, @IpAddress);
END",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@FullName", ProfileInput.FullName.Trim());
                HrmsDatabase.AddParameter(command, "@Phone", EmptyToNull(ProfileInput.Phone));
                HrmsDatabase.AddParameter(command, "@Email", EmptyToNull(ProfileInput.Email));
                HrmsDatabase.AddParameter(command, "@NationalId", EmptyToNull(ProfileInput.NationalId));
                HrmsDatabase.AddParameter(command, "@BirthDate", ProfileInput.BirthDate);
                HrmsDatabase.AddParameter(command, "@Nationality", EmptyToNull(ProfileInput.Nationality));
                HrmsDatabase.AddParameter(command, "@Country", EmptyToNull(ProfileInput.Country));
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                    ("FullName", ProfileInput.FullName),
                    ("Phone", ProfileInput.Phone),
                    ("Email", ProfileInput.Email),
                    ("NationalId", ProfileInput.NationalId),
                    ("BirthDate", ProfileInput.BirthDate),
                    ("Nationality", ProfileInput.Nationality),
                    ("Country", ProfileInput.Country)));
                HrmsDatabase.AddParameter(command, "@UserName", User.Identity?.Name ?? "System");
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        Response.Cookies.Append(
            "SA.DisplayName",
            ProfileInput.FullName.Trim(),
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                MaxAge = TimeSpan.FromDays(30),
                IsEssential = true,
                Path = "/"
            });

        SuccessMessage = "ØªÙ… Ø­ÙØ¸ Ø¨ÙŠØ§Ù†Ø§ØªÙƒ Ø¨Ù†Ø¬Ø§Ø­.";
        return RedirectToPage(new { FromDate, ToDate });
    }

    public async Task<IActionResult> OnPostSubmitRequestAsync()
    {
        var employeeId = GetCurrentEmployeeId();

        if (employeeId <= 0)
        {
            return RedirectToPage("/AccessDenied");
        }

        if (string.IsNullOrWhiteSpace(RequestInput.RequestType))
        {
            ErrorMessage = "Ù†ÙˆØ¹ Ø§Ù„Ø·Ù„Ø¨ Ù…Ø·Ù„ÙˆØ¨.";
            return RedirectToPage(new { FromDate, ToDate });
        }

        try
        {
            var ok = await InsertSelfServiceRequestSafelyAsync(employeeId);

            SuccessMessage = ok
                ? "ØªÙ… ØªÙ‚Ø¯ÙŠÙ… Ø§Ù„Ø·Ù„Ø¨ Ø¨Ù†Ø¬Ø§Ø­."
                : "ØªØ¹Ø°Ø± ØªÙ‚Ø¯ÙŠÙ… Ø§Ù„Ø·Ù„Ø¨ Ù„Ø£Ù† Ø¬Ø¯ÙˆÙ„ Ø§Ù„Ø·Ù„Ø¨Ø§Øª ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯.";
        }
        catch (Exception ex)
        {
            ErrorMessage = "ØªØ¹Ø°Ø± ØªÙ‚Ø¯ÙŠÙ… Ø§Ù„Ø·Ù„Ø¨: " + ex.Message;
        }

        return RedirectToPage(new { FromDate, ToDate });
    }

    private int GetCurrentEmployeeId()
    {
        var employeeIdText = User.FindFirstValue("EmployeeId") ?? "";
        return int.TryParse(employeeIdText, out var employeeId) ? employeeId : 0;
    }

    private async Task LoadAsync(int employeeId)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        if (!FromDate.HasValue)
        {
            FromDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        }

        if (!ToDate.HasValue)
        {
            ToDate = DateOnly.FromDateTime(DateTime.Today);
        }

        var employees = await HrmsDatabase.QueryAsync(
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
    e.IsActive,
    ISNULL(b.Name, '') AS Branch,
    ISNULL(d.Name, '') AS Department,
    ISNULL(e.Position, '') AS Position,
    ISNULL(e.Gender, '') AS Gender,
    ISNULL(e.Nationality, '') AS Nationality,
    ISNULL(e.Country, '') AS Country,
    ISNULL(e.ContractType, '') AS ContractType,
    e.ContractEndDate,
    ISNULL(e.EmploymentStatus, '') AS EmploymentStatus,
    ISNULL(m.FullName, '') AS DirectManager
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
LEFT JOIN Employees m ON e.DirectManagerId = m.Id
WHERE e.Id = @EmployeeId;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeeCard
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
                Branch = HrmsDatabase.GetString(reader, "Branch"),
                Department = HrmsDatabase.GetString(reader, "Department"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                Gender = HrmsDatabase.GetString(reader, "Gender"),
                Nationality = HrmsDatabase.GetString(reader, "Nationality"),
                Country = HrmsDatabase.GetString(reader, "Country"),
                ContractType = HrmsDatabase.GetString(reader, "ContractType"),
                ContractEndDate = HrmsDatabase.GetDateOnly(reader, "ContractEndDate"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                DirectManager = HrmsDatabase.GetString(reader, "DirectManager")
            });

        Employee = employees.FirstOrDefault();

        if (Employee != null)
        {
            ProfileInput = new ProfileInputModel
            {
                FullName = Employee.FullName,
                Phone = Employee.Phone,
                Email = Employee.Email,
                NationalId = Employee.NationalId,
                BirthDate = Employee.BirthDate,
                Nationality = Employee.Nationality,
                Country = Employee.Country
            };
        }

        TotalAttendance = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT COUNT(*) FROM AttendanceRecords WHERE EmployeeId = @EmployeeId",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId));

        PresentCount = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT COUNT(*) FROM AttendanceRecords WHERE EmployeeId = @EmployeeId AND Status = 1",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId));

        LateCount = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT COUNT(*) FROM AttendanceRecords WHERE EmployeeId = @EmployeeId AND Status = 2",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId));

        MissingCheckoutCount = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT COUNT(*) FROM AttendanceRecords WHERE EmployeeId = @EmployeeId AND CheckOut IS NULL",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId));

        AttendanceRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 500
    AttendanceDate,
    CheckIn,
    CheckOut,
    Status,
    Source,
    ISNULL(Notes, '') AS Notes
FROM AttendanceRecords
WHERE EmployeeId = @EmployeeId
  AND AttendanceDate >= @FromDate
  AND AttendanceDate <= @ToDate
ORDER BY AttendanceDate DESC, CheckIn DESC;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@FromDate", FromDate);
                HrmsDatabase.AddParameter(command, "@ToDate", ToDate);
            },
            reader => new AttendanceRow
            {
                AttendanceDate = HrmsDatabase.GetDateOnly(reader, "AttendanceDate"),
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut"),
                Status = HrmsDatabase.GetInt(reader, "Status"),
                Source = HrmsDatabase.GetInt(reader, "Source"),
                Notes = HrmsDatabase.GetString(reader, "Notes")
            });

        Requests = await LoadRequestsSafelyAsync(employeeId);
        RequestsCount = Requests.Count;
        PendingRequestsCount = Requests.Count(x => x.Status.Contains("Pending", StringComparison.OrdinalIgnoreCase) ||
                                                   x.Status.Contains("Ù…Ø¹Ù„Ù‚", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> InsertSelfServiceRequestSafelyAsync(int employeeId)
    {
        var tableExists = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT CASE WHEN OBJECT_ID('SelfServiceRequests', 'U') IS NULL THEN 0 ELSE 1 END");

        if (tableExists == 0)
        {
            return false;
        }

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            @"
DECLARE @columns nvarchar(max) = N'EmployeeId';
DECLARE @values nvarchar(max) = N'@EmployeeId';

IF COL_LENGTH('SelfServiceRequests','RequestType') IS NOT NULL
BEGIN SET @columns += N', RequestType'; SET @values += N', @RequestType'; END

IF COL_LENGTH('SelfServiceRequests','Type') IS NOT NULL
BEGIN SET @columns += N', [Type]'; SET @values += N', @RequestType'; END

IF COL_LENGTH('SelfServiceRequests','StartDate') IS NOT NULL
BEGIN SET @columns += N', StartDate'; SET @values += N', @StartDate'; END

IF COL_LENGTH('SelfServiceRequests','EndDate') IS NOT NULL
BEGIN SET @columns += N', EndDate'; SET @values += N', @EndDate'; END

IF COL_LENGTH('SelfServiceRequests','RequestDate') IS NOT NULL
BEGIN SET @columns += N', RequestDate'; SET @values += N', @StartDate'; END

IF COL_LENGTH('SelfServiceRequests','Date') IS NOT NULL
BEGIN SET @columns += N', [Date]'; SET @values += N', @StartDate'; END

IF COL_LENGTH('SelfServiceRequests','FromTime') IS NOT NULL
BEGIN SET @columns += N', FromTime'; SET @values += N', @FromTime'; END

IF COL_LENGTH('SelfServiceRequests','StartTime') IS NOT NULL
BEGIN SET @columns += N', StartTime'; SET @values += N', @FromTime'; END

IF COL_LENGTH('SelfServiceRequests','ToTime') IS NOT NULL
BEGIN SET @columns += N', ToTime'; SET @values += N', @ToTime'; END

IF COL_LENGTH('SelfServiceRequests','EndTime') IS NOT NULL
BEGIN SET @columns += N', EndTime'; SET @values += N', @ToTime'; END

IF COL_LENGTH('SelfServiceRequests','Reason') IS NOT NULL
BEGIN SET @columns += N', Reason'; SET @values += N', @Reason'; END

IF COL_LENGTH('SelfServiceRequests','Notes') IS NOT NULL
BEGIN SET @columns += N', Notes'; SET @values += N', @Reason'; END

IF COL_LENGTH('SelfServiceRequests','Status') IS NOT NULL
BEGIN SET @columns += N', Status'; SET @values += N', ''Pending'''; END

IF COL_LENGTH('SelfServiceRequests','ApprovalStage') IS NOT NULL
BEGIN SET @columns += N', ApprovalStage'; SET @values += N', ''Manager'''; END

IF COL_LENGTH('SelfServiceRequests','Stage') IS NOT NULL
BEGIN SET @columns += N', Stage'; SET @values += N', ''Manager'''; END

IF COL_LENGTH('SelfServiceRequests','CreatedAt') IS NOT NULL
BEGIN SET @columns += N', CreatedAt'; SET @values += N', SYSUTCDATETIME()'; END

DECLARE @sql nvarchar(max) = N'INSERT INTO SelfServiceRequests (' + @columns + N') VALUES (' + @values + N'); SELECT CAST(SCOPE_IDENTITY() AS int);';

EXEC sp_executesql @sql,
    N'@EmployeeId int, @RequestType nvarchar(100), @StartDate date, @EndDate date, @FromTime nvarchar(20), @ToTime nvarchar(20), @Reason nvarchar(500)',
    @EmployeeId = @EmployeeId,
    @RequestType = @RequestType,
    @StartDate = @StartDate,
    @EndDate = @EndDate,
    @FromTime = @FromTime,
    @ToTime = @ToTime,
    @Reason = @Reason;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@RequestType", RequestInput.RequestType);
                HrmsDatabase.AddParameter(command, "@StartDate", RequestInput.StartDate);
                HrmsDatabase.AddParameter(command, "@EndDate", RequestInput.EndDate ?? RequestInput.StartDate);
                HrmsDatabase.AddParameter(command, "@FromTime", RequestInput.FromTime);
                HrmsDatabase.AddParameter(command, "@ToTime", RequestInput.ToTime);
                HrmsDatabase.AddParameter(command, "@Reason", RequestInput.Reason);
            });

        // سريان الموافقات: حلّ القالب وتجميد خطوات اللجنة على الطلب.
        if (requestId > 0)
        {
            await ApprovalWorkflowEngine.StartAsync(_dbContext, requestId, RequestInput.RequestType ?? string.Empty, employeeId);
        }

        return true;
    }

    private async Task<List<RequestRow>> LoadRequestsSafelyAsync(int employeeId)
    {
        var tableExists = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT CASE WHEN OBJECT_ID('SelfServiceRequests', 'U') IS NULL THEN 0 ELSE 1 END");

        if (tableExists == 0)
        {
            return new List<RequestRow>();
        }

        return await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
DECLARE @RequestTypeExpr nvarchar(max) =
    CASE WHEN COL_LENGTH('SelfServiceRequests','RequestType') IS NOT NULL THEN 'CAST(RequestType AS nvarchar(100))'
         WHEN COL_LENGTH('SelfServiceRequests','Type') IS NOT NULL THEN 'CAST([Type] AS nvarchar(100))'
         ELSE '''Request''' END;

DECLARE @StartDateExpr nvarchar(max) =
    CASE WHEN COL_LENGTH('SelfServiceRequests','StartDate') IS NOT NULL THEN 'CAST(StartDate AS date)'
         WHEN COL_LENGTH('SelfServiceRequests','RequestDate') IS NOT NULL THEN 'CAST(RequestDate AS date)'
         WHEN COL_LENGTH('SelfServiceRequests','Date') IS NOT NULL THEN 'CAST([Date] AS date)'
         ELSE 'CAST(NULL AS date)' END;

DECLARE @EndDateExpr nvarchar(max) =
    CASE WHEN COL_LENGTH('SelfServiceRequests','EndDate') IS NOT NULL THEN 'CAST(EndDate AS date)'
         WHEN COL_LENGTH('SelfServiceRequests','RequestDate') IS NOT NULL THEN 'CAST(RequestDate AS date)'
         WHEN COL_LENGTH('SelfServiceRequests','Date') IS NOT NULL THEN 'CAST([Date] AS date)'
         ELSE 'CAST(NULL AS date)' END;

DECLARE @FromTimeExpr nvarchar(max) =
    CASE WHEN COL_LENGTH('SelfServiceRequests','FromTime') IS NOT NULL THEN 'CAST(FromTime AS nvarchar(20))'
         WHEN COL_LENGTH('SelfServiceRequests','StartTime') IS NOT NULL THEN 'CAST(StartTime AS nvarchar(20))'
         ELSE '''''' END;

DECLARE @ToTimeExpr nvarchar(max) =
    CASE WHEN COL_LENGTH('SelfServiceRequests','ToTime') IS NOT NULL THEN 'CAST(ToTime AS nvarchar(20))'
         WHEN COL_LENGTH('SelfServiceRequests','EndTime') IS NOT NULL THEN 'CAST(EndTime AS nvarchar(20))'
         ELSE '''''' END;

DECLARE @StatusExpr nvarchar(max) =
    CASE WHEN COL_LENGTH('SelfServiceRequests','Status') IS NOT NULL THEN 'CAST(Status AS nvarchar(50))'
         ELSE '''Pending''' END;

DECLARE @ApprovalStageExpr nvarchar(max) =
    CASE WHEN COL_LENGTH('SelfServiceRequests','ApprovalStage') IS NOT NULL THEN 'CAST(ApprovalStage AS nvarchar(50))'
         WHEN COL_LENGTH('SelfServiceRequests','Stage') IS NOT NULL THEN 'CAST(Stage AS nvarchar(50))'
         ELSE '''-''' END;

DECLARE @CreatedAtExpr nvarchar(max) =
    CASE WHEN COL_LENGTH('SelfServiceRequests','CreatedAt') IS NOT NULL THEN 'CAST(CreatedAt AS datetime2)'
         WHEN COL_LENGTH('SelfServiceRequests','RequestDate') IS NOT NULL THEN 'CAST(RequestDate AS datetime2)'
         ELSE 'CAST(NULL AS datetime2)' END;

DECLARE @OrderExpr nvarchar(max) =
    CASE WHEN COL_LENGTH('SelfServiceRequests','CreatedAt') IS NOT NULL THEN 'CreatedAt'
         WHEN COL_LENGTH('SelfServiceRequests','RequestDate') IS NOT NULL THEN 'RequestDate'
         WHEN COL_LENGTH('SelfServiceRequests','Id') IS NOT NULL THEN 'Id'
         ELSE 'EmployeeId' END;

DECLARE @sql nvarchar(max) = N'
SELECT TOP 30
    Id,
    ' + @RequestTypeExpr + N' AS RequestType,
    ' + @StartDateExpr + N' AS StartDate,
    ' + @EndDateExpr + N' AS EndDate,
    ' + @FromTimeExpr + N' AS FromTime,
    ' + @ToTimeExpr + N' AS ToTime,
    ' + @StatusExpr + N' AS Status,
    ' + @ApprovalStageExpr + N' AS ApprovalStage,
    ' + @CreatedAtExpr + N' AS CreatedAt
FROM SelfServiceRequests
WHERE EmployeeId = @EmployeeId
ORDER BY ' + @OrderExpr + N' DESC;';

EXEC sp_executesql @sql, N'@EmployeeId int', @EmployeeId = @EmployeeId;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new RequestRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                StartDate = HrmsDatabase.GetDateOnly(reader, "StartDate"),
                EndDate = HrmsDatabase.GetDateOnly(reader, "EndDate"),
                FromTime = HrmsDatabase.GetString(reader, "FromTime"),
                ToTime = HrmsDatabase.GetString(reader, "ToTime"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                ApprovalStage = HrmsDatabase.GetString(reader, "ApprovalStage"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    private static object? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string StatusText(int status)
    {
        return status switch
        {
            1 => "Ø­Ø§Ø¶Ø±",
            2 => "Ù…ØªØ£Ø®Ø±",
            3 => "ØºÙŠØ§Ø¨",
            4 => "Ø¥Ø¬Ø§Ø²Ø©",
            5 => "Ø¹Ø·Ù„Ø©",
            _ => "-"
        };
    }

    public static string RequestTypeText(string? requestType)
    {
        return requestType switch
        {
            "Missing Punch" => "\u0646\u0633\u064A\u0627\u0646 \u0628\u0635\u0645\u0629",
            "Exit Permission" => "\u0645\u063A\u0627\u062F\u0631\u0629",
            "Overtime" => "\u0639\u0645\u0644 \u0625\u0636\u0627\u0641\u064A",
            "Annual Leave" => "\u0625\u062C\u0627\u0632\u0629 \u0633\u0646\u0648\u064A\u0629",
            "Unpaid Leave" => "\u0625\u062C\u0627\u0632\u0629 \u0628\u062F\u0648\u0646 \u0631\u0627\u062A\u0628",
            "Sick Leave" => "\u0625\u062C\u0627\u0632\u0629 \u0645\u0631\u0636\u064A\u0629",
            "Work Leave" => "\u0625\u062C\u0627\u0632\u0629 \u0639\u0645\u0644",
            _ => string.IsNullOrWhiteSpace(requestType) ? "-" : requestType
        };
    }

    public static string FormatRequestTime(string? fromTime, string? toTime)
    {
        var from = FormatSingleTime(fromTime);
        var to = FormatSingleTime(toTime);

        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
        {
            return "-";
        }

        if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
        {
            return from + " - " + to;
        }

        return !string.IsNullOrWhiteSpace(from) ? from : to;
    }

    private static string FormatSingleTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var clean = value.Trim();

        if (TimeSpan.TryParse(clean, out var timeSpan))
        {
            return timeSpan.ToString(@"hh\:mm");
        }

        if (DateTime.TryParse(clean, out var dateTime))
        {
            return dateTime.ToString("HH:mm");
        }

        if (clean.Length >= 5)
        {
            return clean.Substring(0, 5);
        }

        return clean;
    }
    public class ProfileInputModel
    {
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? NationalId { get; set; }
        public DateOnly? BirthDate { get; set; }
        public string? Nationality { get; set; }
        public string? Country { get; set; }
    }

    public class RequestInputModel
    {
        public string RequestType { get; set; } = string.Empty;
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string? FromTime { get; set; }
        public string? ToTime { get; set; }
        public string? Reason { get; set; }
    }

    public class EmployeeCard
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
        public string Branch { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string ContractType { get; set; } = string.Empty;
        public DateOnly? ContractEndDate { get; set; }
        public string EmploymentStatus { get; set; } = string.Empty;
        public string DirectManager { get; set; } = string.Empty;
    }

    public class AttendanceRow
    {
        public DateOnly? AttendanceDate { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public int Status { get; set; }
        public int Source { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public class RequestRow
    {
        public int Id { get; set; }
        public string RequestType { get; set; } = string.Empty;
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string FromTime { get; set; } = string.Empty;
        public string ToTime { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ApprovalStage { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
    }
}

