using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// الحضور الشهري (نمط كيان — قسمي 9 و13 بدراسة الحضور): صف لكل موظف×شهر يجمع
/// يومياته المحللة (أيام عمل/حضور/غياب، ساعات تأخير/خروج مبكر/عمل) بدورة حالة:
/// UnderReview ← Approved ← Locked. القفل هو بوابة الرواتب — الشهر المقفل لا
/// تتغير أرقامه بإعادة البناء ولا يُرجع للمراجعة. «بناء الشهر» يجدد أرقام
/// الأشهر تحت المراجعة فقط ويترك المعتمد/المقفل كما هو.
/// </summary>
public static class MonthAttendanceStore
{
    public sealed class MonthRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }
        public int WorkDays { get; set; }
        public int PresentDays { get; set; }
        public int AbsentDays { get; set; }
        public int IncompleteDays { get; set; }
        public int UnpaidLeaveDays { get; set; }
        public decimal LateHours { get; set; }
        public decimal EarlyLeaveHours { get; set; }
        public decimal WorkedHours { get; set; }
        public string Status { get; set; } = "UnderReview";   // UnderReview | Approved | Locked
        public DateTime? ApprovedAt { get; set; }
        public DateTime? LockedAt { get; set; }
    }

    public static string StatusLabel(string status) => status switch
    {
        "UnderReview" => "تحت المراجعة",
        "Approved" => "معتمد",
        "Locked" => "مقفل للرواتب",
        _ => status
    };

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeMonthAttendance', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeMonthAttendance
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        [Year] int NOT NULL,
        [Month] int NOT NULL,
        WorkDays int NOT NULL DEFAULT(0),
        PresentDays int NOT NULL DEFAULT(0),
        AbsentDays int NOT NULL DEFAULT(0),
        IncompleteDays int NOT NULL DEFAULT(0),
        LateHours decimal(7,2) NOT NULL DEFAULT(0),
        EarlyLeaveHours decimal(7,2) NOT NULL DEFAULT(0),
        WorkedHours decimal(7,2) NOT NULL DEFAULT(0),
        Status nvarchar(20) NOT NULL DEFAULT(N'UnderReview'),
        BuiltAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        ApprovedAt datetime2 NULL,
        LockedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_EmployeeMonthAttendance_Employee_Period
        ON EmployeeMonthAttendance (EmployeeId, [Year], [Month]);
END;

-- أيام الإجازة غير المدفوعة (ربط الإجازات بالمسير): يخصمها المحرك يوماً×الأجر اليومي.
IF COL_LENGTH('EmployeeMonthAttendance', 'UnpaidLeaveDays') IS NULL
    ALTER TABLE EmployeeMonthAttendance ADD UnpaidLeaveDays int NOT NULL CONSTRAINT DF_EMA_UnpaidLeaveDays DEFAULT(0);
""");
    }

    /// <summary>
    /// «بناء الشهر»: تجميع اليوميات المحللة لكل موظف — SQL واحد بـMERGE-نمط:
    /// إدراج الجديد، تحديث أرقام «تحت المراجعة»، وترك المعتمد/المقفل بلا مساس.
    /// </summary>
    /// <remarks>
    /// <b>عزل الشركات إلزامي</b>: <paramref name="scope"/> يقيّد مصدر التجميع بموظفي
    /// نطاق المستدعي <em>قبل</em> الـMERGE، فلا يُدرَج ولا يُحدَّث صفٌّ لموظف خارج
    /// النطاق. المسير يمرّر نطاق شركة المسير، والشاشة تمرّر نطاق المستخدم الفعّال.
    /// المخاطرة قبل هذا: كان يجمّع ويُدمج يوميات كل الشركات (والمسير يستدعيه تلقائياً).
    /// </remarks>
    /// <returns>عدد صفوف الموظفين ضمن النطاق بعد البناء.</returns>
    /// <summary>مفتاح «يوم بداية دورة الرواتب» بـ<c>NexoraHrSettings</c> (1 = تقويمي).</summary>
    public const string CycleStartDayKey = "Attendance.PayCycleStartDay";

    /// <summary>يوم بداية الدورة (1..28). قيمة تالفة/خارج المدى ⟹ 1 (تقويمي، سلوك قديم).</summary>
    public static async Task<int> CycleStartDayAsync(ApplicationDbContext dbContext)
    {
        var raw = await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.GetAsync(
            dbContext, CycleStartDayKey, "1");
        return int.TryParse(raw, out var d) && d is >= 1 and <= 28 ? d : 1;
    }

    /// <summary>
    /// نافذة الدورة المنتهية بالشهر (year, month). startDay=1 ⟹ الشهر التقويمي.
    /// startDay=21 ⟹ [21 من الشهر السابق .. 20 من هذا الشهر]. دالة نقيّة قابلة للاختبار.
    /// </summary>
    public static (DateOnly From, DateOnly To) CycleWindow(int year, int month, int startDay)
    {
        if (startDay <= 1)
        {
            var first = new DateOnly(year, month, 1);
            return (first, first.AddMonths(1).AddDays(-1));
        }
        var clamped = Math.Min(startDay, DateTime.DaysInMonth(year, month));
        var start = new DateOnly(year, month, clamped);
        return (start.AddMonths(-1), start.AddDays(-1));
    }

    public static async Task<int> BuildMonthAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int year, int month)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return 0;
        await EnsureAsync(dbContext);
        await DayAttendanceStore.EnsureAsync(dbContext);
        await ShiftTypeStore.EnsureAsync(dbContext); // عمود TotalDurationMode مطلوب بالتجميع

        // نافذة التجميع تتبع «دورة الرواتب»: يوم بدايةٍ قابل للضبط. الافتراض 1 ⟹
        // الشهر التقويمي (1→آخر الشهر) = السلوك القديم حرفياً. قيمةٌ >1 (مثل 21) ⟹
        // الدورة المنتهية بهذا الشهر: [يوم البداية من الشهر السابق .. يوم البداية−1 منه]
        // — فلا تُبتلع أيام تخصّ دورة الشهر التالي (بلاغ محمد: غياب 21/7→31/7 بتموز).
        var (from, to) = CycleWindow(year, month, await CycleStartDayAsync(dbContext));

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            $"""
WITH Aggregated AS
(
    -- «أيام العمل» تشمل العمل من المنزل ورحلة العمل: هي أيام دوامٍ اختلف مكانها لا
    -- حكمها، ويوميّاتها تُشتقّ بقواعد الدوام كاملةً (DayAttendanceStore.IsWorkingKind).
    -- استثناؤها كان سيُنقص مقام كل نسبة شهرية بينما بسطها (الحضور) يعدّها — فتظهر
    -- نسبةُ حضورٍ فوق 100% لموظفٍ عمل أسبوعاً من بيته.
    SELECT d.EmployeeId,
           SUM(CASE WHEN d.DayKind IN (N'Work', N'Remote', N'BusinessTrip') THEN 1 ELSE 0 END) AS WorkDays,
           SUM(CASE WHEN d.Status IN (N'Present', N'Late') THEN 1 ELSE 0 END) AS PresentDays,
           SUM(CASE WHEN d.Status = N'Absent' THEN 1 ELSE 0 END) AS AbsentDays,
           SUM(CASE WHEN d.Status = N'Incomplete' THEN 1 ELSE 0 END) AS IncompleteDays,
           SUM(CASE WHEN d.Status = N'LeaveUnpaid' THEN 1 ELSE 0 END) AS UnpaidLeaveDays,
           SUM(d.LateHours) AS LateHours,
           SUM(d.EarlyLeaveHours) AS EarlyLeaveHours,
           -- TotalDurationMode للمناوبة: WorkOnly = ساعات أيام العمل فقط تدخل إجمالي
           -- الشهر؛ IncludeOff/Both = تُضمّ أيضاً ساعات العطل/الراحة. صف اليوم يحتفظ
           -- بساعاته الفعلية دوماً (مادة الأوفرتايم) — الوضع يحكم التجميع الشهري فقط.
           SUM(CASE WHEN d.DayKind IN (N'Work', N'Remote', N'BusinessTrip')
                      OR ISNULL(st.TotalDurationMode, N'WorkOnly') <> N'WorkOnly'
                    THEN d.WorkedHours ELSE 0 END) AS WorkedHours
    FROM DayAttendances d
    INNER JOIN Employees e ON e.Id = d.EmployeeId
    LEFT JOIN ShiftTypes st ON st.Id = d.ShiftTypeId
    WHERE d.WorkDate >= @From AND d.WorkDate <= @To AND d.IsAnalyzed = 1
      AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
    GROUP BY d.EmployeeId
)
MERGE EmployeeMonthAttendance AS target
USING Aggregated AS source
    ON target.EmployeeId = source.EmployeeId AND target.[Year] = @Year AND target.[Month] = @Month
WHEN MATCHED AND target.Status = N'UnderReview' THEN
    UPDATE SET WorkDays = source.WorkDays, PresentDays = source.PresentDays,
               AbsentDays = source.AbsentDays, IncompleteDays = source.IncompleteDays,
               UnpaidLeaveDays = source.UnpaidLeaveDays,
               LateHours = source.LateHours, EarlyLeaveHours = source.EarlyLeaveHours,
               WorkedHours = source.WorkedHours, BuiltAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (EmployeeId, [Year], [Month], WorkDays, PresentDays, AbsentDays, IncompleteDays,
            UnpaidLeaveDays, LateHours, EarlyLeaveHours, WorkedHours, Status)
    VALUES (source.EmployeeId, @Year, @Month, source.WorkDays, source.PresentDays,
            source.AbsentDays, source.IncompleteDays, source.UnpaidLeaveDays, source.LateHours,
            source.EarlyLeaveHours, source.WorkedHours, N'UnderReview');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Year", year);
                HrmsDatabase.AddParameter(command, "@Month", month);
            });

        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            $"""
SELECT COUNT(1)
FROM EmployeeMonthAttendance m
INNER JOIN Employees e ON e.Id = m.EmployeeId
WHERE m.[Year] = @Year AND m.[Month] = @Month
  AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Year", year);
                HrmsDatabase.AddParameter(command, "@Month", month);
            });
    }

    public static async Task<List<MonthRow>> ListAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int year, int month)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<MonthRow>();
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT m.*, e.EmployeeNo, e.FullName
FROM EmployeeMonthAttendance m
INNER JOIN Employees e ON e.Id = m.EmployeeId
WHERE m.[Year] = @Year AND m.[Month] = @Month
  AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
ORDER BY e.EmployeeNo;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Year", year);
                HrmsDatabase.AddParameter(command, "@Month", month);
            },
            reader => new MonthRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Year = HrmsDatabase.GetInt(reader, "Year"),
                Month = HrmsDatabase.GetInt(reader, "Month"),
                WorkDays = HrmsDatabase.GetInt(reader, "WorkDays"),
                PresentDays = HrmsDatabase.GetInt(reader, "PresentDays"),
                AbsentDays = HrmsDatabase.GetInt(reader, "AbsentDays"),
                IncompleteDays = HrmsDatabase.GetInt(reader, "IncompleteDays"),
                UnpaidLeaveDays = HrmsDatabase.GetInt(reader, "UnpaidLeaveDays"),
                LateHours = reader["LateHours"] is decimal late ? late : 0,
                EarlyLeaveHours = reader["EarlyLeaveHours"] is decimal early ? early : 0,
                WorkedHours = reader["WorkedHours"] is decimal worked ? worked : 0,
                Status = HrmsDatabase.GetString(reader, "Status") is { Length: > 0 } s ? s : "UnderReview",
                ApprovedAt = HrmsDatabase.GetDateTime(reader, "ApprovedAt"),
                LockedAt = HrmsDatabase.GetDateTime(reader, "LockedAt")
            });
    }

    /// <summary>
    /// اعتماد: تحت المراجعة ← معتمد — <b>ويُمنع الاعتماد بوجود أيام غير محلّلة</b>.
    /// الشهر المعتمد يُقفل ثم يقرأه المسير (AbsentDays ⟹ اقتطاع)، فاعتماد شهر
    /// ناقص التحليل يعني راتباً محسوباً على أيام لم يشتقها المحرك.
    /// </summary>
    /// <returns>عدد المعتمَد فعلاً، وعدد المحجوب لنقص التحليل.</returns>
    public static async Task<(int Approved, int Blocked)> ApproveWithGateAsync(
        ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureAsync(dbContext);
        if (ids.Count == 0 || scope.IsDeniedAll) return (0, 0);

        // النطاق أوّلاً: المعرّفات خارج شركات المستخدم تُصفّى قبل الأهلية والكتابة معاً.
        var eligible = await EligibleForApprovalAsync(dbContext, scope, ids);
        var approved = await Transition(dbContext, scope, eligible, from: "UnderReview", to: "Approved",
            "ApprovedAt = SYSUTCDATETIME()");

        return (approved, ids.Count - eligible.Count);
    }

    /// <summary>
    /// يصفّي المعرّفات إلى ما اكتمل تحليله: عدد اليوميات المحلّلة للموظف بالشهر
    /// يساوي أيام الشهر المنقضية (الشهر الجاري يُقاس حتى اليوم، لا حتى آخره).
    /// </summary>
    private static async Task<List<int>> EligibleForApprovalAsync(
        ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids)
    {
        await DayAttendanceStore.EnsureAsync(dbContext);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var eligible = new List<int>();
        // 🛡️ عزل الشركات (Issue 4): صفٌّ لموظف خارج نطاق المستخدم لا يُعدّ مؤهّلاً
        // أصلاً، فلا يُعتمَد بمعرّفٍ مزوَّر من شركةٍ أخرى.
        var scopeFilter = scope.IsUnrestricted ? "1 = 1" : scope.ToSqlPredicate("e.CompanyId");

        foreach (var chunk in ids.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            var rows = await HrmsDatabase.QueryAsync(
                dbContext,
                $"""
SELECT m.Id,
       DATEDIFF(day, DATEFROMPARTS(m.[Year], m.[Month], 1),
                CASE WHEN EOMONTH(DATEFROMPARTS(m.[Year], m.[Month], 1)) < @Today
                     THEN EOMONTH(DATEFROMPARTS(m.[Year], m.[Month], 1)) ELSE @Today END) + 1 AS ExpectedDays,
       (SELECT COUNT(1) FROM DayAttendances d
        WHERE d.EmployeeId = m.EmployeeId AND d.IsAnalyzed = 1
          AND d.WorkDate >= DATEFROMPARTS(m.[Year], m.[Month], 1)
          AND d.WorkDate <= EOMONTH(DATEFROMPARTS(m.[Year], m.[Month], 1))) AS AnalyzedDays
FROM EmployeeMonthAttendance m
INNER JOIN Employees e ON e.Id = m.EmployeeId
WHERE m.Id IN ({inList}) AND {scopeFilter};
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Today", today.ToDateTime(TimeOnly.MinValue));
                    var index = 0;
                    foreach (var id in chunk) HrmsDatabase.AddParameter(command, $"@P{index++}", id);
                },
                reader => new
                {
                    Id = HrmsDatabase.GetInt(reader, "Id"),
                    Expected = HrmsDatabase.GetInt(reader, "ExpectedDays"),
                    Analyzed = HrmsDatabase.GetInt(reader, "AnalyzedDays")
                });

            eligible.AddRange(rows
                .Where(r => r.Expected > 0 && r.Analyzed >= r.Expected)
                .Select(r => r.Id));
        }

        return eligible;
    }

    /// <summary>إرجاع للمراجعة: معتمد ← تحت المراجعة (المقفل لا يُرجع).</summary>
    public static Task<int> ReopenAsync(ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids) =>
        Transition(dbContext, scope, ids, from: "Approved", to: "UnderReview",
            "ApprovedAt = NULL");

    /// <summary>قفل للرواتب: معتمد ← مقفل (نهائي).</summary>
    public static Task<int> LockAsync(ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids) =>
        Transition(dbContext, scope, ids, from: "Approved", to: "Locked",
            "LockedAt = SYSUTCDATETIME()");

    private static async Task<int> Transition(ApplicationDbContext dbContext,
        CompanyScope scope, IReadOnlyCollection<int> ids, string from, string to, string extraSet)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureAsync(dbContext);
        if (ids.Count == 0 || scope.IsDeniedAll) return 0;

        // 🛡️ عزل الشركات (Issue 4): الانتقال يمسّ فقط صفوف موظفي نطاق المستخدم — join
        // على Employees وحصرٌ بشركاته، فلا يُعتمَد/يُرجَع/يُقفَل صفُّ شركةٍ أخرى بمعرّف مزوَّر.
        var scopeFilter = scope.IsUnrestricted ? "1 = 1" : scope.ToSqlPredicate("e.CompanyId");

        var total = 0;
        foreach (var chunk in ids.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            total += await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                $"""
UPDATE m SET m.Status = @To, {extraSet}
FROM EmployeeMonthAttendance m
INNER JOIN Employees e ON e.Id = m.EmployeeId
WHERE m.Id IN ({inList}) AND m.Status = @FromStatus AND {scopeFilter};
SELECT @@ROWCOUNT;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@To", to);
                    HrmsDatabase.AddParameter(command, "@FromStatus", from);
                    for (var i = 0; i < chunk.Length; i++)
                    {
                        HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                    }
                });
        }
        return total;
    }
}
