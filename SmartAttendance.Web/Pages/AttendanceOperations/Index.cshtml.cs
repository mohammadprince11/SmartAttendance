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

    [BindProperty]
    public IFormFile? AttendanceFile { get; set; }

    [BindProperty]
    public CorrectionInput Correction { get; set; } = new();

    public List<AttendanceProcessingResultViewModel> ProcessedRecords { get; set; } = new();

    public Dictionary<string, string> AttendanceNotes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> AttendanceEditedKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int ProcessTotalResults { get; set; }

    /// <summary>
    /// بالفترة صفوف غير محلّلة تُقرأ من المحرك القديم (عرض فقط) — تنبيه الشاشة
    /// يطلب تشغيل «تحديث الحضور» ليصير العرض كله من المحرك الرسمي.
    /// </summary>
    public bool IsLegacyEngineFallback { get; set; }

    /// <summary>عدد صفوف الفترة التي لا يقابلها يومية محلّلة.</summary>
    public int UnanalyzedRows { get; set; }

    public bool ProcessIsLimited { get; set; }

    public AttendanceImportPreviewViewModel? Preview { get; set; }

    public AttendanceImportResultViewModel? ImportResult { get; set; }

    /// <summary>
    /// البصمات الأخرى (نمط كيان — قسم 29.د): أزواج بصمات بدلالة غير-حضورية
    /// (استراحة/صلاة/مهمة عمل). لا تدخل اشتقاق اليومية، وتُعرض وتُدار مستقلة.
    /// </summary>
    public List<OtherPunchRow> OtherPunches { get; set; } = new();

    public List<PunchSemanticStore.PunchSemantic> OtherSemantics { get; set; } = new();

    [BindProperty]
    public OtherPunchInput OtherPunch { get; set; } = new();

    /// <summary>
    /// أزواج بصمات اليوم قيد التحرير (نمط كيان — قسم 29.ج): اليوم قد يحمل أزواجاً
    /// متعددة، والمحرك يشتق الحالة منها. صفٌّ فارغ الوقتين = حذف.
    /// </summary>
    [BindProperty]
    public List<PunchPairInput> PunchPairs { get; set; } = new();

    /// <summary>خريطة «كود الموظف|التاريخ» ← أزواج اليوم، تغذّي مودال التحرير.</summary>
    public string AttendancePairsJson { get; set; } = "{}";

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
            await LoadProcessingAsync();
            return Page();
        }

        var extension = Path.GetExtension(AttendanceFile.FileName).ToLowerInvariant();

        if (extension is not ".xlsx" and not ".csv")
        {
            ErrorMessage = "Ù†ÙˆØ¹ Ø§Ù„Ù…Ù„Ù ØºÙŠØ± Ù…Ø¯Ø¹ÙˆÙ…. Ø§Ø³ØªØ®Ø¯Ù… xlsx Ø£Ùˆ csv ÙÙ‚Ø·.";
            NormalizeDefaults();
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
(EmployeeId, AttendanceDate, CheckIn, CheckOut, Source, Status, DeviceId, Notes, CreatedAt, IsDeleted)
VALUES
(@EmployeeId, @AttendanceDate, @CheckIn, @CheckOut, 3, @Status, NULL, @Notes, SYSUTCDATETIME(), 0);

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

    private async Task LoadCurrentTabAsync()
    {
        // بعد إزالة شاشة التصحيحات لم يبقَ إلا جدول المعالجة أياً كان التبويب
        await LoadProcessingAsync();
        await LoadAttendancePairsAsync();
        await LoadOtherPunchesAsync();
    }

    /// <summary>
    /// أزواج البصمات غير-الحضورية للفترة المعروضة + الدلالات المتاحة لإضافتها.
    /// مستثناة من اشتقاق اليومية (راجع DayAttendanceStore) فتُعرض مستقلة.
    /// </summary>
    private async Task LoadOtherPunchesAsync()
    {
        OtherSemantics = await PunchSemanticStore.OtherSemanticsAsync(_dbContext);

        var from = ProcessFromDate ?? DateOnly.FromDateTime(DateTime.Today);
        var to = ProcessToDate ?? from;
        var attendanceId = await PunchSemanticStore.AttendanceSemanticIdAsync(_dbContext);

        OtherPunches = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 200
    ar.Id, e.EmployeeNo, e.FullName, ar.AttendanceDate, ar.CheckIn, ar.CheckOut,
    ISNULL(ar.PunchSemanticId, 0) AS PunchSemanticId,
    ISNULL(ps.Name, N'-') AS SemanticName
FROM AttendanceRecords ar
INNER JOIN Employees e ON e.Id = ar.EmployeeId
LEFT JOIN PunchSemantics ps ON ps.Id = ar.PunchSemanticId
WHERE ISNULL(ar.IsDeleted, 0) = 0
  AND ar.AttendanceDate >= @From AND ar.AttendanceDate <= @To
  AND ar.PunchSemanticId IS NOT NULL
  AND ar.PunchSemanticId <> @Attendance
ORDER BY ar.AttendanceDate DESC, ar.CheckIn DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from);
                HrmsDatabase.AddParameter(command, "@To", to);
                HrmsDatabase.AddParameter(command, "@Attendance", attendanceId);
            },
            reader => new OtherPunchRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate") ?? default,
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut"),
                SemanticName = HrmsDatabase.GetString(reader, "SemanticName")
            });
    }

    /// <summary>
    /// أزواج البصمات الحضورية للفترة المعروضة كخريطة JSON — يقرأها مودال التحرير
    /// ليعرض أزواج اليوم كلها بدل زوج واحد مفترض.
    /// </summary>
    private async Task LoadAttendancePairsAsync()
    {
        if (!ProcessedRecords.Any())
        {
            AttendancePairsJson = "{}";
            return;
        }

        var from = ProcessedRecords.Min(x => x.AttendanceDate);
        var to = ProcessedRecords.Max(x => x.AttendanceDate);
        var attendanceId = await PunchSemanticStore.AttendanceSemanticIdAsync(_dbContext);

        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT ar.Id, e.EmployeeNo, ar.AttendanceDate, ar.CheckIn, ar.CheckOut
FROM AttendanceRecords ar
INNER JOIN Employees e ON e.Id = ar.EmployeeId
WHERE ISNULL(ar.IsDeleted, 0) = 0
  AND ar.AttendanceDate >= @From AND ar.AttendanceDate <= @To
  AND ISNULL(ar.PunchSemanticId, @Attendance) = @Attendance
ORDER BY ar.AttendanceDate, e.EmployeeNo, ar.CheckIn;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from);
                HrmsDatabase.AddParameter(command, "@To", to);
                HrmsDatabase.AddParameter(command, "@Attendance", attendanceId);
            },
            reader => new
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate") ?? default,
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut")
            });

        var map = rows
            .GroupBy(r => BuildAttendanceNoteKey(r.EmployeeNo, r.Date))
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new
                {
                    id = r.Id,
                    inTime = r.CheckIn?.ToString("HH:mm") ?? "",
                    outTime = r.CheckOut?.ToString("HH:mm") ?? ""
                }).ToList());

        AttendancePairsJson = System.Text.Json.JsonSerializer.Serialize(map);
    }

    /// <summary>
    /// حفظ أزواج بصمات يوم كاملة (نمط كيان — قسم 29.ج): تُحدَّث/تُضاف/تُحذف الأزواج
    /// ثم <b>يُعاد اشتقاق اليومية من المحرك</b> بدل فرض حالة يدوية. صفٌّ فارغ الوقتين
    /// = حذف، وزوج بلا معرّف = إضافة.
    /// </summary>
    public async Task<IActionResult> OnPostSavePunchesAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        var redirect = new
        {
            Tab = "process",
            ProcessFromDate,
            ProcessToDate,
            ProcessSearchTerm,
            MaxRows
        };

        if (string.IsNullOrWhiteSpace(Correction.EmployeeNo) || string.IsNullOrWhiteSpace(Correction.Date))
        {
            ErrorMessage = "رقم الموظف والتاريخ مطلوبان.";
            return RedirectToPage("./Index", redirect);
        }

        var employeeId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT TOP 1 Id FROM Employees WHERE EmployeeNo = @EmployeeNo",
            command => HrmsDatabase.AddParameter(command, "@EmployeeNo", Correction.EmployeeNo));

        if (employeeId <= 0)
        {
            ErrorMessage = "لم يتم العثور على الموظف.";
            return RedirectToPage("./Index", redirect);
        }

        var date = DateOnly.Parse(Correction.Date);
        var attendanceId = await PunchSemanticStore.AttendanceSemanticIdAsync(_dbContext);

        var pairs = (PunchPairs ?? new List<PunchPairInput>())
            .Where(p => !p.IsEmpty || p.Id > 0)
            .ToList();

        if (pairs.All(p => p.IsEmpty))
        {
            ErrorMessage = "أبقِ زوجاً واحداً على الأقل — لحذف اليوم كاملاً استخدم سجلات الحضور الخام.";
            return RedirectToPage("./Index", redirect);
        }

        var noteText = string.IsNullOrWhiteSpace(Correction.Notes)
            ? "تعديل يدوي لأزواج البصمات"
            : Correction.Notes.Trim();

        var keptIds = new List<int>();

        foreach (var pair in pairs)
        {
            // زوج فارغ الوقتين بمعرّف قائم = حذف صريح
            if (pair.IsEmpty)
            {
                await HrmsDatabase.ExecuteAsync(
                    _dbContext,
                    "DELETE FROM AttendanceRecords WHERE Id = @Id;",
                    command => HrmsDatabase.AddParameter(command, "@Id", pair.Id));
                continue;
            }

            var checkIn = BuildDateTime(date, pair.CheckIn);
            object checkOut = string.IsNullOrWhiteSpace(pair.CheckOut)
                ? DBNull.Value
                : BuildDateTime(date, pair.CheckOut);

            if (pair.Id > 0)
            {
                keptIds.Add(pair.Id);
                await HrmsDatabase.ExecuteAsync(
                    _dbContext,
                    """
UPDATE AttendanceRecords
SET CheckIn = @CheckIn, CheckOut = @CheckOut, Source = 3, Notes = @Notes
WHERE Id = @Id;
""",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Id", pair.Id);
                        HrmsDatabase.AddParameter(command, "@CheckIn", checkIn);
                        HrmsDatabase.AddParameter(command, "@CheckOut", checkOut);
                        HrmsDatabase.AddParameter(command, "@Notes", noteText);
                    });
            }
            else
            {
                var newId = await HrmsDatabase.ScalarAsync<int>(
                    _dbContext,
                    """
INSERT INTO AttendanceRecords
    (EmployeeId, AttendanceDate, CheckIn, CheckOut, Source, Status, DeviceId, Notes, CreatedAt, IsDeleted, PunchSemanticId)
VALUES
    (@EmployeeId, @Date, @CheckIn, @CheckOut, 3, 1, NULL, @Notes, SYSUTCDATETIME(), 0, NULL);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                        HrmsDatabase.AddParameter(command, "@Date", date);
                        HrmsDatabase.AddParameter(command, "@CheckIn", checkIn);
                        HrmsDatabase.AddParameter(command, "@CheckOut", checkOut);
                        HrmsDatabase.AddParameter(command, "@Notes", noteText);
                    });

                keptIds.Add(newId);
            }
        }

        // الأزواج الحضورية التي اختفت من النموذج تُحذف — التحرير يعكس اليوم كاملاً
        var keepList = keptIds.Count > 0 ? string.Join(",", keptIds) : "0";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            $"""
DELETE FROM AttendanceRecords
WHERE EmployeeId = @EmployeeId AND AttendanceDate = @Date
  AND ISNULL(PunchSemanticId, @Semantic) = @Semantic
  AND Id NOT IN ({keepList});
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", date);
                HrmsDatabase.AddParameter(command, "@Semantic", attendanceId);
            });

        // ⟵ جوهر التغيير: الحالة تُشتق من المحرك لا تُفرض يدوياً
        var derived = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT MIN(CheckIn) AS FirstIn, MAX(CheckOut) AS LastOut
FROM AttendanceRecords
WHERE EmployeeId = @EmployeeId AND AttendanceDate = @Date
  AND ISNULL(IsDeleted, 0) = 0
  AND ISNULL(PunchSemanticId, @Semantic) = @Semantic;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", date);
                HrmsDatabase.AddParameter(command, "@Semantic", attendanceId);
            },
            reader => new
            {
                FirstIn = HrmsDatabase.GetDateTime(reader, "FirstIn"),
                LastOut = HrmsDatabase.GetDateTime(reader, "LastOut")
            });

        var day = derived.FirstOrDefault();
        var reDerived = day != null &&
            await DayAttendanceStore.UpdateDayAsync(_dbContext, employeeId, date, day.FirstIn, day.LastOut);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
BEGIN
    INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
    VALUES ('AttendanceRecord', CAST(@EmployeeId AS nvarchar(80)), 'Attendance Punch Pairs Edit', @NewValues, @UserName, @IpAddress);
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                    ("EmployeeNo", Correction.EmployeeNo),
                    ("Date", Correction.Date),
                    ("Pairs", keptIds.Count.ToString()),
                    ("FirstIn", day?.FirstIn?.ToString("HH:mm") ?? "-"),
                    ("LastOut", day?.LastOut?.ToString("HH:mm") ?? "-"),
                    ("Notes", noteText)));
                HrmsDatabase.AddParameter(command, "@UserName", User.Identity?.Name ?? "HR");
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        SuccessMessage = reDerived
            ? $"حُفظت {keptIds.Count} من أزواج البصمات وأُعيد اشتقاق اليومية."
            : $"حُفظت {keptIds.Count} من أزواج البصمات — اليومية غير محللة بعد، شغّل «تحديث الحضور».";

        return RedirectToPage("./Index", redirect);
    }

    /// <summary>إضافة زوج بصمة غير-حضوري (نظير «إضافة بصمات أخرى» بكيان).</summary>
    public async Task<IActionResult> OnPostAddOtherPunchAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        var redirect = new
        {
            Tab = "process",
            ProcessFromDate,
            ProcessToDate,
            ProcessSearchTerm,
            MaxRows
        };

        if (string.IsNullOrWhiteSpace(OtherPunch.EmployeeNo) ||
            string.IsNullOrWhiteSpace(OtherPunch.Date) ||
            OtherPunch.PunchSemanticId <= 0)
        {
            ErrorMessage = "رقم الموظف والتاريخ ونوع البصمة مطلوبة.";
            return RedirectToPage("./Index", redirect);
        }

        if (string.IsNullOrWhiteSpace(OtherPunch.CheckIn))
        {
            ErrorMessage = "أدخل وقت البداية على الأقل.";
            return RedirectToPage("./Index", redirect);
        }

        var employeeId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT TOP 1 Id FROM Employees WHERE EmployeeNo = @EmployeeNo",
            command => HrmsDatabase.AddParameter(command, "@EmployeeNo", OtherPunch.EmployeeNo));

        if (employeeId <= 0)
        {
            ErrorMessage = "لم يتم العثور على الموظف.";
            return RedirectToPage("./Index", redirect);
        }

        var date = DateOnly.Parse(OtherPunch.Date);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO AttendanceRecords
    (EmployeeId, AttendanceDate, CheckIn, CheckOut, Source, Status, DeviceId, Notes, CreatedAt, IsDeleted, PunchSemanticId)
VALUES
    (@EmployeeId, @Date, @CheckIn, @CheckOut, 3, 1, NULL, @Notes, SYSUTCDATETIME(), 0, @Semantic);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", date);
                HrmsDatabase.AddParameter(command, "@CheckIn", BuildDateTime(date, OtherPunch.CheckIn));
                HrmsDatabase.AddParameter(command, "@CheckOut",
                    string.IsNullOrWhiteSpace(OtherPunch.CheckOut)
                        ? DBNull.Value
                        : BuildDateTime(date, OtherPunch.CheckOut));
                HrmsDatabase.AddParameter(command, "@Notes", (object?)OtherPunch.Notes ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Semantic", OtherPunch.PunchSemanticId);
            });

        SuccessMessage = "أُضيفت البصمة — لا تدخل اشتقاق اليومية.";
        return RedirectToPage("./Index", redirect);
    }

    public async Task<IActionResult> OnPostDeleteOtherPunchAsync(int id)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        // حذف مقيّد بالبصمات غير-الحضورية حتى لا يمسّ سجلات الحضور
        var affected = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
DELETE FROM AttendanceRecords WHERE Id = @Id AND PunchSemanticId IS NOT NULL;
SELECT @@ROWCOUNT;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        SuccessMessage = affected > 0 ? "حُذفت البصمة." : "لم تُحذف — ليست بصمة غير-حضورية.";

        return RedirectToPage("./Index", new
        {
            Tab = "process",
            ProcessFromDate,
            ProcessToDate,
            ProcessSearchTerm,
            MaxRows
        });
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

        // المحرك الرسمي (DayAttendances) هو المصدر لكل يوم محلَّل — نفس ما يغذّي
        // المخالفات والرواتب، فتُطبَّق فترة السماح وسياستها ولا تتناقض الشاشة مع
        // ما سُجِّل (قسم 35). الأيام غير المحلّلة تبقى بقراءة المحرك القديم حتى لا
        // يختفي موظف من الكشف، ومع تنبيه صريح بأنها قراءة أولية.
        var dayRows = await DayAttendanceStore.ListRangeAsync(
            _dbContext, ProcessFromDate.Value, ProcessToDate.Value, null);

        var official = dayRows.ToDictionary(
            r => BuildAttendanceNoteKey(r.EmployeeNo, r.WorkDate),
            MapDayRow,
            StringComparer.OrdinalIgnoreCase);

        var legacy = await _attendanceProcessingService.GetProcessedRecordsAsync(
            ProcessFromDate,
            ProcessToDate,
            null);

        var processedRecords = new List<AttendanceProcessingResultViewModel>();
        var legacyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var legacyCount = 0;

        foreach (var row in legacy)
        {
            var key = BuildAttendanceNoteKey(row.EmployeeNo ?? string.Empty, row.AttendanceDate);
            legacyKeys.Add(key);

            if (official.TryGetValue(key, out var derived))
            {
                processedRecords.Add(derived);
            }
            else
            {
                legacyCount++;
                processedRecords.Add(row);
            }
        }

        // يوميات محلّلة لا يقابلها صف بالمحرك القديم (موظف خارج نطاقه مثلاً)
        processedRecords.AddRange(official
            .Where(pair => !legacyKeys.Contains(pair.Key))
            .Select(pair => pair.Value));

        UnanalyzedRows = legacyCount;
        IsLegacyEngineFallback = legacyCount > 0;

        var materialized = CleanProcessFilter(processedRecords);

        ProcessTotalResults = materialized.Count;
        ProcessIsLimited = ProcessTotalResults > MaxRows;

        ProcessedRecords = materialized
            .Take(MaxRows)
            .ToList();

        await LoadAttendanceNotesForProcessedAsync();
    }


    /// <summary>
    /// يومية المحرك الرسمي ← صف الجدول. الحالات تُترجم لمفردات الشاشة القائمة:
    /// عطلة/راحة ← «راحة أسبوعية» (وبعمل فيها ← «عمل في الراحة الأسبوعية»)،
    /// و«بصمة ناقصة» تُترك للشاشة تشتقها من غياب أحد الوقتين.
    /// </summary>
    private static AttendanceProcessingResultViewModel MapDayRow(DayAttendanceStore.DayRow row)
    {
        var isOff = row.DayKind is "Weekend" or "Rest";
        var status = isOff
            ? (row.WorkedHours > 0 ? "Work On Weekly Off" : "Weekly Off")
            : row.Status == "Incomplete" ? "Present" : row.Status;

        return new AttendanceProcessingResultViewModel
        {
            EmployeeId = row.EmployeeId,
            EmployeeNo = row.EmployeeNo,
            EmployeeName = row.EmployeeName,
            AttendanceDate = row.WorkDate,
            ShiftCode = string.Empty,   // المناوبة تُعرَّف بالاسم في المحرك الرسمي
            ShiftName = row.ShiftName ?? string.Empty,
            IsWeeklyOff = isOff,
            CheckIn = row.CheckIn,
            CheckOut = row.CheckOut,
            WorkingHours = row.WorkedHours,
            LateMinutes = (int)Math.Round(row.LateHours * 60, MidpointRounding.AwayFromZero),
            EarlyLeaveMinutes = (int)Math.Round(row.EarlyLeaveHours * 60, MidpointRounding.AwayFromZero),
            MissingCheckOut = row.CheckIn.HasValue && !row.CheckOut.HasValue,
            CalculatedStatus = status
        };
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
    public class OtherPunchRow
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public string SemanticName { get; set; } = string.Empty;

        /// <summary>مدة الزوج بالساعات — «الفترة» بجدول كيان.</summary>
        public decimal? DurationHours =>
            CheckIn.HasValue && CheckOut.HasValue
                ? Math.Round((decimal)(CheckOut.Value - CheckIn.Value).TotalHours, 2)
                : null;
    }

    public class PunchPairInput
    {
        public int Id { get; set; }
        public string? CheckIn { get; set; }
        public string? CheckOut { get; set; }

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(CheckIn) && string.IsNullOrWhiteSpace(CheckOut);
    }

    public class OtherPunchInput
    {
        public string? EmployeeNo { get; set; }
        public string? Date { get; set; }
        public string? CheckIn { get; set; }
        public string? CheckOut { get; set; }
        public int PunchSemanticId { get; set; }
        public string? Notes { get; set; }
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

}



