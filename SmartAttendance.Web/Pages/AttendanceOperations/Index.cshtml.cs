using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.AttendanceImports.Services;
using SmartAttendance.Application.AttendanceImports.ViewModels;
using SmartAttendance.Application.AttendanceProcessing.Services;
using SmartAttendance.Application.AttendanceProcessing.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AttendanceOperations;

/// <summary>
/// عمليات الحضور اليومية: لوحة تجميعية لسجلات الدخول/الخروج مع تصحيح سريع
/// وفلاتر — الواجهة التشغيلية الأولى لمودل الحضور (سيُعاد بناؤه بدراسة كيان).
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAttendanceProcessingService _attendanceProcessingService;
    private readonly IAttendanceImportService _attendanceImportService;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(
        ApplicationDbContext dbContext,
        IAttendanceProcessingService attendanceProcessingService,
        IAttendanceImportService attendanceImportService,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _attendanceProcessingService = attendanceProcessingService;
        _attendanceImportService = attendanceImportService;
        _environment = environment;
    }

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "process";

    [BindProperty(SupportsGet = true)]
    public DateOnly? ProcessFromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ProcessToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ProcessSearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MaxRows { get; set; } = 25;

    [BindProperty(SupportsGet = true)]
    public string? CorrectionEmployeeNo { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? CorrectionFromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? CorrectionToDate { get; set; }

    [BindProperty]
    public IFormFile? AttendanceFile { get; set; }

    [BindProperty]
    public CorrectionInput Correction { get; set; } = new();

    public List<AttendanceProcessingResultViewModel> ProcessedRecords { get; set; } = new();

    public Dictionary<string, string> AttendanceNotes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> AttendanceEditedKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int ProcessTotalResults { get; set; }

    public bool ProcessIsLimited { get; set; }

    public List<CorrectionRow> CorrectionRecords { get; set; } = new();

    public AttendanceImportPreviewViewModel? Preview { get; set; }

    public AttendanceImportResultViewModel? ImportResult { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        NormalizeDefaults();
        await LoadCurrentTabAsync();
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Tab = "import";

        if (AttendanceFile == null || AttendanceFile.Length == 0)
        {
            ErrorMessage = "ÙŠØ±Ø¬Ù‰ Ø§Ø®ØªÙŠØ§Ø± Ù…Ù„Ù Excel Ø£Ùˆ CSV.";
            NormalizeDefaults();
            await LoadCorrectionsAsync();
            await LoadProcessingAsync();
            return Page();
        }

        var extension = Path.GetExtension(AttendanceFile.FileName).ToLowerInvariant();

        if (extension is not ".xlsx" and not ".csv")
        {
            ErrorMessage = "Ù†ÙˆØ¹ Ø§Ù„Ù…Ù„Ù ØºÙŠØ± Ù…Ø¯Ø¹ÙˆÙ…. Ø§Ø³ØªØ®Ø¯Ù… xlsx Ø£Ùˆ csv ÙÙ‚Ø·.";
            NormalizeDefaults();
            await LoadCorrectionsAsync();
            await LoadProcessingAsync();
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

        NormalizeDefaults();
        await LoadCorrectionsAsync();
        await LoadProcessingAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync(string token)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Tab = "import";

        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "Ø±Ù…Ø² Ø§Ù„Ø§Ø³ØªÙŠØ±Ø§Ø¯ ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯.";
            NormalizeDefaults();
            await LoadCurrentTabAsync();
            return Page();
        }

        var filePath = FindFileByToken(token);

        if (filePath == null)
        {
            ErrorMessage = "Ù„Ù… ÙŠØªÙ… Ø§Ù„Ø¹Ø«ÙˆØ± Ø¹Ù„Ù‰ Ø§Ù„Ù…Ù„Ù Ø§Ù„Ù…Ø±ÙÙˆØ¹. Ø§Ø±ÙØ¹ Ø§Ù„Ù…Ù„Ù Ù…Ø±Ø© Ø£Ø®Ø±Ù‰.";
            NormalizeDefaults();
            await LoadCurrentTabAsync();
            return Page();
        }

        try
        {
            var originalFileName = GetOriginalFileNameFromStoredPath(filePath, token);

            ImportResult = await _attendanceImportService.ImportAsync(
                filePath,
                originalFileName);

            SuccessMessage = ImportResult.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        NormalizeDefaults();
        await LoadCorrectionsAsync();
        await LoadProcessingAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUpsertAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Tab = "process";

        if (string.IsNullOrWhiteSpace(Correction.EmployeeNo) || string.IsNullOrWhiteSpace(Correction.Date))
        {
            ErrorMessage = "\u0631\u0642\u0645 \u0627\u0644\u0645\u0648\u0638\u0641 \u0648\u0627\u0644\u062A\u0627\u0631\u064A\u062E \u0645\u0637\u0644\u0648\u0628\u0627\u0646.";
            NormalizeDefaults();
            await LoadProcessingAsync();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Correction.CheckIn) && string.IsNullOrWhiteSpace(Correction.CheckOut))
        {
            ErrorMessage = "\u0623\u062F\u062E\u0644 \u0628\u0635\u0645\u0629 \u062F\u062E\u0648\u0644 \u0623\u0648 \u062E\u0631\u0648\u062C \u0639\u0644\u0649 \u0627\u0644\u0623\u0642\u0644.";
            NormalizeDefaults();
            await LoadProcessingAsync();
            return Page();
        }

        var employeeId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT TOP 1 Id FROM Employees WHERE EmployeeNo = @EmployeeNo",
            command => HrmsDatabase.AddParameter(command, "@EmployeeNo", Correction.EmployeeNo));

        if (employeeId <= 0)
        {
            ErrorMessage = "\u0644\u0645 \u064A\u062A\u0645 \u0627\u0644\u0639\u062B\u0648\u0631 \u0639\u0644\u0649 \u0627\u0644\u0645\u0648\u0638\u0641.";
            NormalizeDefaults();
            await LoadProcessingAsync();
            return Page();
        }

        var date = DateOnly.Parse(Correction.Date);
        var existingId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT TOP 1 Id FROM AttendanceRecords WHERE EmployeeId = @EmployeeId AND AttendanceDate = @AttendanceDate ORDER BY Id DESC",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@AttendanceDate", date);
            });

        var checkIn = BuildDateTime(date, Correction.CheckIn);
        DateTime? checkOut = string.IsNullOrWhiteSpace(Correction.CheckOut) ? null : BuildDateTime(date, Correction.CheckOut);
        var statusValue = NormalizeStatus(Correction.Status);
        var noteText = string.IsNullOrWhiteSpace(Correction.Notes)
            ? "\u062A\u0639\u062F\u064A\u0644 \u064A\u062F\u0648\u064A \u0645\u0646 \u0635\u0641\u062D\u0629 \u0625\u062F\u0627\u0631\u0629 \u0627\u0644\u062D\u0636\u0648\u0631"
            : Correction.Notes.Trim();

        if (existingId > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                @"
UPDATE AttendanceRecords
SET CheckIn = @CheckIn,
    CheckOut = @CheckOut,
    Status = @Status,
    Source = 3,
    Notes = @Notes
WHERE Id = @Id;

IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
BEGIN
    INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
    VALUES ('AttendanceRecord', CAST(@Id AS nvarchar(80)), 'Attendance Edit From Processing', @NewValues, @UserName, @IpAddress);
END;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", existingId);
                    HrmsDatabase.AddParameter(command, "@CheckIn", checkIn);
                    HrmsDatabase.AddParameter(command, "@CheckOut", checkOut);
                    HrmsDatabase.AddParameter(command, "@Status", statusValue);
                    HrmsDatabase.AddParameter(command, "@Notes", noteText);
                    HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                        ("EmployeeNo", Correction.EmployeeNo),
                        ("Date", Correction.Date),
                        ("CheckIn", Correction.CheckIn),
                        ("CheckOut", Correction.CheckOut),
                        ("Status", StatusText(statusValue)),
                        ("Source", "\u064A\u062F\u0648\u064A"),
                        ("Notes", noteText)));
                    HrmsDatabase.AddParameter(command, "@UserName", User.Identity?.Name ?? "HR");
                    HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
                });

            SuccessMessage = "\u062A\u0645 \u062D\u0641\u0638 \u062A\u0639\u062F\u064A\u0644 \u0627\u0644\u062D\u0636\u0648\u0631 \u0648\u062A\u0633\u062C\u064A\u0644 \u0627\u0644\u0645\u0644\u0627\u062D\u0638\u0629.";
        }
        else
        {
            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                @"
INSERT INTO AttendanceRecords
(EmployeeId, AttendanceDate, CheckIn, CheckOut, Source, Status, DeviceId, Notes, CreatedAt)
VALUES
(@EmployeeId, @AttendanceDate, @CheckIn, @CheckOut, 3, @Status, NULL, @Notes, SYSUTCDATETIME());

DECLARE @NewId int = SCOPE_IDENTITY();

IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
BEGIN
    INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
    VALUES ('AttendanceRecord', CAST(@NewId AS nvarchar(80)), 'Attendance Add From Processing', @NewValues, @UserName, @IpAddress);
END;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@AttendanceDate", date);
                    HrmsDatabase.AddParameter(command, "@CheckIn", checkIn);
                    HrmsDatabase.AddParameter(command, "@CheckOut", checkOut);
                    HrmsDatabase.AddParameter(command, "@Status", statusValue);
                    HrmsDatabase.AddParameter(command, "@Notes", noteText);
                    HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                        ("EmployeeNo", Correction.EmployeeNo),
                        ("Date", Correction.Date),
                        ("CheckIn", Correction.CheckIn),
                        ("CheckOut", Correction.CheckOut),
                        ("Status", StatusText(statusValue)),
                        ("Source", "\u064A\u062F\u0648\u064A"),
                        ("Notes", noteText)));
                    HrmsDatabase.AddParameter(command, "@UserName", User.Identity?.Name ?? "HR");
                    HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
                });

            SuccessMessage = "\u062A\u0645\u062A \u0625\u0636\u0627\u0641\u0629 \u0633\u062C\u0644 \u062D\u0636\u0648\u0631 \u064A\u062F\u0648\u064A \u0648\u062D\u0641\u0638 \u0627\u0644\u0645\u0644\u0627\u062D\u0638\u0629.";
        }

        NormalizeDefaults();

        return RedirectToPage("./Index", new
        {
            Tab = "process",
            ProcessFromDate,
            ProcessToDate,
            ProcessSearchTerm,
            MaxRows
        });
    }

    public async Task<IActionResult> OnPostCorrectAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Tab = "corrections";

        if (Correction.Id > 0)
        {
            var oldRows = await HrmsDatabase.QueryAsync(
                _dbContext,
                @"
SELECT TOP 1
    Id,
    AttendanceDate,
    CheckIn,
    CheckOut,
    Status,
    ISNULL(Notes, '') AS Notes
FROM AttendanceRecords
WHERE Id = @Id;",
                command => HrmsDatabase.AddParameter(command, "@Id", Correction.Id),
                reader => new CorrectionRow
                {
                    Id = HrmsDatabase.GetInt(reader, "Id"),
                    Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate")?.ToString("yyyy-MM-dd") ?? "",
                    CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn")?.ToString("HH:mm") ?? "",
                    CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut")?.ToString("HH:mm") ?? "",
                    StatusValue = HrmsDatabase.GetInt(reader, "Status"),
                    Notes = HrmsDatabase.GetString(reader, "Notes")
                });

            var old = oldRows.FirstOrDefault();

            if (old == null || string.IsNullOrWhiteSpace(old.Date))
            {
                ErrorMessage = "Ø§Ù„Ø³Ø¬Ù„ ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯.";
                NormalizeDefaults();
                await LoadCorrectionsAsync();
                return Page();
            }

            var date = DateOnly.Parse(old.Date);
            var checkIn = BuildDateTime(date, Correction.CheckIn);
            DateTime? checkOut = string.IsNullOrWhiteSpace(Correction.CheckOut) ? null : BuildDateTime(date, Correction.CheckOut);
            var statusValue = NormalizeStatus(Correction.Status);

            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                @"
UPDATE AttendanceRecords
SET CheckIn = @CheckIn,
    CheckOut = @CheckOut,
    Status = @Status,
    Notes = @Notes
WHERE Id = @Id;

IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
BEGIN
    INSERT INTO AuditLogs (EntityName, EntityId, Action, OldValues, NewValues, UserName, IpAddress)
    VALUES ('AttendanceRecord', CAST(@Id AS nvarchar(80)), 'Attendance Correction', @OldValues, @NewValues, @UserName, @IpAddress);
END;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", Correction.Id);
                    HrmsDatabase.AddParameter(command, "@CheckIn", checkIn);
                    HrmsDatabase.AddParameter(command, "@CheckOut", checkOut);
                    HrmsDatabase.AddParameter(command, "@Status", statusValue);
                    HrmsDatabase.AddParameter(command, "@Notes", Correction.Notes);
                    HrmsDatabase.AddParameter(command, "@OldValues", HrmsDatabase.JsonLine(
                        ("CheckIn", old.CheckIn),
                        ("CheckOut", old.CheckOut),
                        ("Status", StatusText(old.StatusValue)),
                        ("Notes", old.Notes)));
                    HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                        ("CheckIn", Correction.CheckIn),
                        ("CheckOut", Correction.CheckOut),
                        ("Status", StatusText(statusValue)),
                        ("Notes", Correction.Notes)));
                    HrmsDatabase.AddParameter(command, "@UserName", User.Identity?.Name ?? "HR");
                    HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
                });

            SuccessMessage = "ØªÙ… ØªØµØ­ÙŠØ­ Ø³Ø¬Ù„ Ø§Ù„Ø­Ø¶ÙˆØ± ÙˆØªØ³Ø¬ÙŠÙ„ Ø§Ù„Ø¹Ù…Ù„ÙŠØ© ÙÙŠ Ø³Ø¬Ù„ Ø§Ù„Ø¹Ù…Ù„ÙŠØ§Øª.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Correction.EmployeeNo) || string.IsNullOrWhiteSpace(Correction.Date))
            {
                ErrorMessage = "Ù„Ø§ ÙŠÙ…ÙƒÙ† Ø¥Ø¶Ø§ÙØ© Ø¨ØµÙ…Ø© Ø¨Ø¯ÙˆÙ† Ø±Ù‚Ù… Ù…ÙˆØ¸Ù ÙˆØªØ§Ø±ÙŠØ®.";
                NormalizeDefaults();
                await LoadCorrectionsAsync();
                return Page();
            }

            var employeeId = await HrmsDatabase.ScalarAsync<int>(
                _dbContext,
                "SELECT TOP 1 Id FROM Employees WHERE EmployeeNo = @EmployeeNo",
                command => HrmsDatabase.AddParameter(command, "@EmployeeNo", Correction.EmployeeNo));

            if (employeeId <= 0)
            {
                ErrorMessage = "Ù„Ù… ÙŠØªÙ… Ø§Ù„Ø¹Ø«ÙˆØ± Ø¹Ù„Ù‰ Ø§Ù„Ù…ÙˆØ¸Ù.";
                NormalizeDefaults();
                await LoadCorrectionsAsync();
                return Page();
            }

            var date = DateOnly.Parse(Correction.Date);
            var checkIn = BuildDateTime(date, Correction.CheckIn);
            DateTime? checkOut = string.IsNullOrWhiteSpace(Correction.CheckOut) ? null : BuildDateTime(date, Correction.CheckOut);
            var statusValue = NormalizeStatus(Correction.Status);

            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                @"
INSERT INTO AttendanceRecords
(EmployeeId, AttendanceDate, CheckIn, CheckOut, Source, Status, DeviceId, Notes, CreatedAt)
VALUES
(@EmployeeId, @AttendanceDate, @CheckIn, @CheckOut, 3, @Status, NULL, @Notes, SYSUTCDATETIME());

DECLARE @NewId int = SCOPE_IDENTITY();

IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
BEGIN
    INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
    VALUES ('AttendanceRecord', CAST(@NewId AS nvarchar(80)), 'Manual Attendance Add', @NewValues, @UserName, @IpAddress);
END;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@AttendanceDate", date);
                    HrmsDatabase.AddParameter(command, "@CheckIn", checkIn);
                    HrmsDatabase.AddParameter(command, "@CheckOut", checkOut);
                    HrmsDatabase.AddParameter(command, "@Status", statusValue);
                    HrmsDatabase.AddParameter(command, "@Notes", Correction.Notes);
                    HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                        ("EmployeeNo", Correction.EmployeeNo),
                        ("Date", Correction.Date),
                        ("CheckIn", Correction.CheckIn),
                        ("CheckOut", Correction.CheckOut),
                        ("Status", StatusText(statusValue)),
                        ("Source", "ÙŠØ¯ÙˆÙŠ"),
                        ("Notes", Correction.Notes)));
                    HrmsDatabase.AddParameter(command, "@UserName", User.Identity?.Name ?? "HR");
                    HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
                });

            SuccessMessage = "ØªÙ…Øª Ø¥Ø¶Ø§ÙØ© Ø³Ø¬Ù„ Ø­Ø¶ÙˆØ± ÙŠØ¯ÙˆÙŠ Ù„Ù„ÙŠÙˆÙ… Ø§Ù„Ù…Ø­Ø¯Ø¯.";
        }

        NormalizeDefaults();
        await LoadCorrectionsAsync();

        return Page();
    }
    private async Task LoadCurrentTabAsync()
    {
        if (Tab.Equals("import", StringComparison.OrdinalIgnoreCase))
        {
            await LoadProcessingAsync();
            await LoadCorrectionsAsync();
            return;
        }

        if (Tab.Equals("corrections", StringComparison.OrdinalIgnoreCase))
        {
            await LoadCorrectionsAsync();
            return;
        }

        await LoadProcessingAsync();
    }

    private List<AttendanceProcessingResultViewModel> ApplyProcessingLocalSearch(List<AttendanceProcessingResultViewModel> rows)
    {
        if (string.IsNullOrWhiteSpace(ProcessSearchTerm))
        {
            return rows;
        }

        var term = ProcessSearchTerm.Trim();

        return rows
            .Where(x =>
                (!string.IsNullOrWhiteSpace(x.EmployeeNo) && x.EmployeeNo.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.EmployeeName) && x.EmployeeName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.ShiftName) && x.ShiftName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.ShiftCode) && x.ShiftCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.CalculatedStatus) && x.CalculatedStatus.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
    private List<AttendanceProcessingResultViewModel> CleanProcessFilter(List<AttendanceProcessingResultViewModel> rows)
    {
        if (string.IsNullOrWhiteSpace(ProcessSearchTerm))
        {
            return rows;
        }

        var term = ProcessSearchTerm.Trim();

        return rows
            .Where(x =>
                (!string.IsNullOrWhiteSpace(x.EmployeeNo) && x.EmployeeNo.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.EmployeeName) && x.EmployeeName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.ShiftCode) && x.ShiftCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.ShiftName) && x.ShiftName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.CalculatedStatus) && x.CalculatedStatus.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
    private async Task LoadAttendanceNotesForProcessedAsync()
    {
        AttendanceNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AttendanceEditedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!ProcessedRecords.Any())
        {
            return;
        }

        var fromDate = ProcessedRecords.Min(x => x.AttendanceDate);
        var toDate = ProcessedRecords.Max(x => x.AttendanceDate);

        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT
    e.EmployeeNo,
    ar.AttendanceDate,
    ISNULL(ar.Notes, '') AS Notes,
    ISNULL(ar.Source, 0) AS Source
FROM AttendanceRecords ar
INNER JOIN Employees e ON ar.EmployeeId = e.Id
WHERE ar.AttendanceDate >= @FromDate
  AND ar.AttendanceDate <= @ToDate
  AND ISNULL(ar.Source, 0) = 3
  AND ISNULL(ar.Notes, '') <> ''
  AND ISNULL(ar.Notes, '') NOT LIKE 'Imported%';",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@FromDate", fromDate);
                HrmsDatabase.AddParameter(command, "@ToDate", toDate);
            },
            reader => new AttendanceNoteRow
            {
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                AttendanceDate = HrmsDatabase.GetDateOnly(reader, "AttendanceDate"),
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                Source = HrmsDatabase.GetInt(reader, "Source")
            });

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.EmployeeNo) || row.AttendanceDate == null)
            {
                continue;
            }

            var key = BuildAttendanceNoteKey(row.EmployeeNo, row.AttendanceDate.Value);
            AttendanceEditedKeys.Add(key);

            if (!string.IsNullOrWhiteSpace(row.Notes))
            {
                AttendanceNotes[key] = row.Notes;
            }
        }
    }


    public bool IsAttendanceEdited(string? employeeNo, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(employeeNo))
        {
            return false;
        }

        return AttendanceEditedKeys.Contains(BuildAttendanceNoteKey(employeeNo, date));
    }
    public string GetAttendanceNote(string? employeeNo, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(employeeNo))
        {
            return "-";
        }

        var key = BuildAttendanceNoteKey(employeeNo, date);

        return AttendanceNotes.TryGetValue(key, out var note) && !string.IsNullOrWhiteSpace(note)
            ? note
            : "-";
    }

    private static string BuildAttendanceNoteKey(string employeeNo, DateOnly date)
    {
        return employeeNo.Trim() + "|" + date.ToString("yyyy-MM-dd");
    }

    private class AttendanceNoteRow
    {
        public string EmployeeNo { get; set; } = string.Empty;

        public DateOnly? AttendanceDate { get; set; }

        public string Notes { get; set; } = string.Empty;
    
        public int Source { get; set; }
    }
    private async Task LoadProcessingAsync()
    {
        ProcessFromDate ??= DateOnly.FromDateTime(DateTime.Today);
        ProcessToDate ??= ProcessFromDate;

        if (ProcessToDate < ProcessFromDate)
        {
            ProcessToDate = ProcessFromDate;
        }

        MaxRows = NormalizeMaxRows(MaxRows);

        var processedRecords = await _attendanceProcessingService.GetProcessedRecordsAsync(
            ProcessFromDate,
            ProcessToDate,
            null);

        var materialized = CleanProcessFilter(processedRecords.ToList());

        ProcessTotalResults = materialized.Count;
        ProcessIsLimited = ProcessTotalResults > MaxRows;

        ProcessedRecords = materialized
            .Take(MaxRows)
            .ToList();

        await LoadAttendanceNotesForProcessedAsync();
    }


    private async Task LoadCorrectionsAsync()
    {
        CorrectionFromDate ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        CorrectionToDate ??= DateOnly.FromDateTime(DateTime.Today);

        if (CorrectionToDate < CorrectionFromDate)
        {
            CorrectionToDate = CorrectionFromDate;
        }

        var hasEmployeeFilter = !string.IsNullOrWhiteSpace(CorrectionEmployeeNo);

        if (hasEmployeeFilter)
        {
            var sql = @"
DECLARE @Days int = DATEDIFF(DAY, @FromDate, @ToDate) + 1;

WITH Dates AS
(
    SELECT TOP (@Days)
        DATEADD(DAY, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1, @FromDate) AS AttendanceDate
    FROM sys.all_objects
),
FilteredEmployees AS
(
    SELECT Id, EmployeeNo, FullName
    FROM Employees
    WHERE EmployeeNo LIKE '%' + @EmployeeNo + '%'
)
SELECT TOP 500
    ISNULL(ar.Id, 0) AS Id,
    e.EmployeeNo,
    e.FullName,
    d.AttendanceDate,
    ar.CheckIn,
    ar.CheckOut,
    ISNULL(ar.Status, 0) AS Status,
    ISNULL(ar.Source, 0) AS Source,
    ISNULL(ar.Notes, '') AS Notes
FROM FilteredEmployees e
CROSS JOIN Dates d
LEFT JOIN AttendanceRecords ar
    ON ar.EmployeeId = e.Id
   AND ar.AttendanceDate = CAST(d.AttendanceDate AS date)
ORDER BY d.AttendanceDate DESC, e.EmployeeNo;";

            CorrectionRecords = await HrmsDatabase.QueryAsync(
                _dbContext,
                sql,
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeNo", CorrectionEmployeeNo);
                    HrmsDatabase.AddParameter(command, "@FromDate", CorrectionFromDate);
                    HrmsDatabase.AddParameter(command, "@ToDate", CorrectionToDate);
                },
                reader => new CorrectionRow
                {
                    Id = HrmsDatabase.GetInt(reader, "Id"),
                    EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                    EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                    Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate")?.ToString("yyyy-MM-dd") ?? "",
                    CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn")?.ToString("HH:mm") ?? "",
                    CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut")?.ToString("HH:mm") ?? "",
                    StatusValue = HrmsDatabase.GetInt(reader, "Status"),
                    SourceValue = HrmsDatabase.GetInt(reader, "Source"),
                    Notes = HrmsDatabase.GetString(reader, "Notes")
                });

            return;
        }

        var existingSql = @"
SELECT TOP 200
    ar.Id,
    e.EmployeeNo,
    e.FullName,
    ar.AttendanceDate,
    ar.CheckIn,
    ar.CheckOut,
    ar.Status,
    ar.Source,
    ISNULL(ar.Notes, '') AS Notes
FROM AttendanceRecords ar
INNER JOIN Employees e ON ar.EmployeeId = e.Id
WHERE
    (@FromDate IS NULL OR ar.AttendanceDate >= @FromDate)
    AND (@ToDate IS NULL OR ar.AttendanceDate <= @ToDate)
ORDER BY ar.AttendanceDate DESC, ar.CheckIn DESC;";

        CorrectionRecords = await HrmsDatabase.QueryAsync(
            _dbContext,
            existingSql,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@FromDate", CorrectionFromDate);
                HrmsDatabase.AddParameter(command, "@ToDate", CorrectionToDate);
            },
            reader => new CorrectionRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate")?.ToString("yyyy-MM-dd") ?? "",
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn")?.ToString("HH:mm") ?? "",
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut")?.ToString("HH:mm") ?? "",
                StatusValue = HrmsDatabase.GetInt(reader, "Status"),
                SourceValue = HrmsDatabase.GetInt(reader, "Source"),
                Notes = HrmsDatabase.GetString(reader, "Notes")
            });
    }
    private void NormalizeDefaults()
    {
        if (string.IsNullOrWhiteSpace(Tab))
        {
            Tab = "process";
        }

        Tab = Tab.ToLowerInvariant();

        ProcessFromDate ??= DateOnly.FromDateTime(DateTime.Today);
        ProcessToDate ??= ProcessFromDate;

        CorrectionFromDate ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        CorrectionToDate ??= DateOnly.FromDateTime(DateTime.Today);

        MaxRows = NormalizeMaxRows(MaxRows);
    }

    private string GetImportFolder()
    {
        return Path.Combine(_environment.ContentRootPath, "App_Data", "AttendanceImports");
    }

    private string? FindFileByToken(string token)
    {
        var folder = GetImportFolder();

        if (!Directory.Exists(folder))
        {
            return null;
        }

        return Directory
            .GetFiles(folder, $"{token}_*")
            .FirstOrDefault();
    }

    private static string GetOriginalFileNameFromStoredPath(string filePath, string token)
    {
        var storedFileName = Path.GetFileName(filePath);
        var prefix = $"{token}_";

        if (storedFileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return storedFileName[prefix.Length..];
        }

        return storedFileName;
    }

    private static string MakeSafeFileName(string fileName)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName;
    }

    private static DateTime BuildDateTime(DateOnly date, string? time)
    {
        var parsed = TimeOnly.TryParse(time, out var timeOnly)
            ? timeOnly
            : new TimeOnly(0, 0);

        return date.ToDateTime(parsed);
    }

    private static int NormalizeMaxRows(int value)
    {
        return value switch
        {
            25 => 25,
            50 => 50,
            100 => 100,
            250 => 250,
            500 => 500,
            _ => 25
        };
    }


    private static int NormalizeStatus(int value)
    {
        return value is >= 1 and <= 5 ? value : 1;
    }

    public static string StatusText(int status)
    {
        return status switch
        {
            0 => "\u0644\u0627 \u062A\u0648\u062C\u062F \u0628\u0635\u0645\u0629",
            1 => "\u062D\u0627\u0636\u0631",
            2 => "\u0645\u062A\u0623\u062E\u0631",
            3 => "\u063A\u0627\u0626\u0628",
            4 => "\u0625\u062C\u0627\u0632\u0629",
            5 => "\u0639\u0637\u0644\u0629",
            _ => "-"
        };
    }


    public static string SourceText(int source)
    {
        return source switch
        {
            0 => "-",
            1 => "\u062C\u0647\u0627\u0632",
            2 => "\u0645\u0648\u0628\u0627\u064A\u0644",
            3 => "\u064A\u062F\u0648\u064A",
            _ => "-"
        };
    }


    public static string ProcessingStatusText(string? status)
    {
        return status switch
        {
            "Present" => "\u062D\u0627\u0636\u0631",
            "Late" => "\u0645\u062A\u0623\u062E\u0631",
            "Absent" => "\u063A\u0627\u0626\u0628",
            "Leave" => "\u0625\u062C\u0627\u0632\u0629",
            "Holiday" => "\u0639\u0637\u0644\u0629",
            "Weekly Off" => "\u0631\u0627\u062D\u0629 \u0623\u0633\u0628\u0648\u0639\u064A\u0629",
            "Work On Weekly Off" => "\u0639\u0645\u0644 \u0641\u064A \u0627\u0644\u0631\u0627\u062D\u0629 \u0627\u0644\u0623\u0633\u0628\u0648\u0639\u064A\u0629",
            _ => string.IsNullOrWhiteSpace(status) ? "-" : status
        };
    }


    public static string ImportStatusText(string? status)
    {
        return status switch
        {
            "Ready" => "\u062C\u0627\u0647\u0632",
            "Warning" => "\u062A\u062D\u0630\u064A\u0631",
            "Error" => "\u062E\u0637\u0623",
            "Existing" => "\u0645\u0648\u062C\u0648\u062F \u0645\u0633\u0628\u0642\u0627\u064B",
            _ => string.IsNullOrWhiteSpace(status) ? "-" : status
        };
    }


    public static string ProcessingAutoStatusText(DateTime? checkIn, DateTime? checkOut, string? calculatedStatus)
    {
        if (calculatedStatus == "Weekly Off")
        {
            return "\u0631\u0627\u062D\u0629 \u0623\u0633\u0628\u0648\u0639\u064A\u0629";
        }

        if (calculatedStatus == "Holiday")
        {
            return "\u0639\u0637\u0644\u0629";
        }

        if (calculatedStatus == "Leave")
        {
            return "\u0625\u062C\u0627\u0632\u0629";
        }

        if (!checkIn.HasValue && !checkOut.HasValue)
        {
            return "\u063A\u064A\u0627\u0628";
        }

        if ((checkIn.HasValue && !checkOut.HasValue) || (!checkIn.HasValue && checkOut.HasValue))
        {
            return "\u0628\u0635\u0645\u0629 \u0645\u0641\u0642\u0648\u062F\u0629";
        }

        return ProcessingStatusText(calculatedStatus);
    }

    public static int ProcessingStatusValueForEdit(DateTime? checkIn, DateTime? checkOut, string? calculatedStatus)
    {
        if (calculatedStatus == "Weekly Off" || calculatedStatus == "Holiday")
        {
            return 5;
        }

        if (calculatedStatus == "Leave")
        {
            return 4;
        }

        if (!checkIn.HasValue && !checkOut.HasValue)
        {
            return 3;
        }

        if (calculatedStatus == "Late")
        {
            return 2;
        }

        return 1;
    }
    public class CorrectionInput
    {
        public int Id { get; set; }

        public string? EmployeeNo { get; set; }

        public string? Date { get; set; }

        public string? CheckIn { get; set; }

        public string? CheckOut { get; set; }

        public int Status { get; set; } = 1;

        public string? Notes { get; set; }
    }

    public class CorrectionRow
    {
        public int Id { get; set; }

        public string? EmployeeNo { get; set; }

        public string? Date { get; set; }


        public string EmployeeName { get; set; } = string.Empty;


        public string CheckIn { get; set; } = string.Empty;

        public string CheckOut { get; set; } = string.Empty;

        public int StatusValue { get; set; }

        public int SourceValue { get; set; }

        public string Notes { get; set; } = string.Empty;
    }
}



