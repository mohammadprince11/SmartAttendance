using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// سجل اليومية DayAttendance (نمط كيان — قسمي 9 و14 بدراسة الحضور): صف لكل
/// موظف×يوم بالحقول المشتقة (ساعات التأخير، الخروج المبكر، ساعات العمل، الحالة،
/// علم «تم التحليل»). «تحديث الحضور» يبني الشهر من بصمات AttendanceRecords الخام
/// (أول بصمة دخول وآخر بصمة خروج — نمط كيان) مقابل تعريف المناوبة يوماً-بيوم من
/// ShiftTypes. تعيين المناوبة لكل موظف يأتي مع الروستر (مرحلة 5) — حالياً تُختار
/// مناوبة التحليل من الشاشة وتُطبق على الجميع.
/// </summary>
public static class DayAttendanceStore
{
    public sealed class DayRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public DateOnly WorkDate { get; set; }
        public int? ShiftTypeId { get; set; }
        public string? ShiftName { get; set; }
        public string? ShiftColor { get; set; }
        public string DayKind { get; set; } = "Work";     // Work | Weekend | Rest
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public decimal LateHours { get; set; }
        public decimal EarlyLeaveHours { get; set; }
        public decimal WorkedHours { get; set; }
        public string Status { get; set; } = "Absent";    // Present | Late | Incomplete | Absent | Weekend | Rest
        public bool IsAnalyzed { get; set; }

        /// <summary>
        /// طرأ على هذا اليوم تغييرٌ **بعد** آخر تحليل (اعتماد نسيان بصمة · إجازة ·
        /// مغادرة · تعديل بصمة · إعادة استيراد) فصارت اليومية بائتة.
        /// </summary>
        public bool IsStale { get; set; }

        /// <summary>محلَّلة وحديثة — العلامة التي تظهر بعمود «تم التحليل».</summary>
        public bool IsUpToDate => IsAnalyzed && !IsStale;
    }

    /// <summary>
    /// مفتاح فلتر «لم تُحلَّل». ليس حالةً بل خاصية حداثة، فيُميَّز عن قيم
    /// <see cref="DayRow.Status"/> (Present · Late · Absent · Incomplete …) ولا يتصادم معها.
    /// </summary>
    public const string StaleFilterKey = "Stale";

    /// <summary>
    /// فلتر أزرار العدّادات بشاشة الحضور اليومي. فارغ = الكل.
    ///
    /// <para>هنا لا بالصفحة عمداً: تستهلكه الشاشة لبناء العرض، **ويستهلكه زرّ إشعار
    /// الموظفين** لتحديد من يُشعَر. لو تفرّع المنطقان لصار الزرّ يُرسل لمجموعةٍ غير
    /// التي يراها المستخدم أمامه — وهو عطلٌ صامت أسوأ من غياب الميزة. دالّة نقيّة
    /// كي تُختبَر بلا قاعدة بيانات.</para>
    /// </summary>
    public static List<DayRow> FilterByStatus(IEnumerable<DayRow> rows, string? statusFilter) =>
        statusFilter switch
        {
            null or "" => rows.ToList(),
            StaleFilterKey => rows.Where(row => row.IsStale).ToList(),
            _ => rows.Where(row => row.Status == statusFilter).ToList()
        };

    /// <summary>0=السبت .. 6=الجمعة (ترتيب ShiftTypeStore) من DayOfWeek.</summary>
    public static int ToDayIndex(DateOnly date) => ((int)date.DayOfWeek + 1) % 7;

    public static string StatusLabel(string status) => status switch
    {
        "Present" => "حاضر",
        "Late" => "متأخر",
        "Incomplete" => "بصمة ناقصة",
        "Absent" => "غائب",
        "Weekend" => "عطلة أسبوعية",
        "Rest" => "يوم راحة",
        "Holiday" => "عطلة رسمية",
        "Leave" => "إجازة",
        "LeaveUnpaid" => "إجازة بدون راتب",
        _ => status
    };

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('DayAttendances', 'U') IS NULL
BEGIN
    CREATE TABLE DayAttendances
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        WorkDate date NOT NULL,
        ShiftTypeId int NULL,
        DayKind nvarchar(20) NOT NULL DEFAULT(N'Work'),
        CheckIn datetime2 NULL,
        CheckOut datetime2 NULL,
        LateHours decimal(5,2) NOT NULL DEFAULT(0),
        EarlyLeaveHours decimal(5,2) NOT NULL DEFAULT(0),
        WorkedHours decimal(5,2) NOT NULL DEFAULT(0),
        Status nvarchar(20) NOT NULL DEFAULT(N'Absent'),
        IsAnalyzed bit NOT NULL DEFAULT(0),
        AnalyzedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_DayAttendances_Employee_Date ON DayAttendances (EmployeeId, WorkDate);
END;
""");
    }

    /// <summary>
    /// «تحديث الحضور»: يعيد بناء يوميات الشهر لكل الموظفين النشطين. كل موظف يُحلل
    /// بمناوبته المعيّنة (EmployeeShiftTypes — مرحلة 5)، ومن بلا تعيين يسقط
    /// للمناوبة الافتراضية الممررة (مفهوم كيان). يحذف يوميات الشهر ثم يولّدها من
    /// البصمات الخام — آمن للإعادة (إعادة تحليل).
    /// </summary>
    /// <returns>عدد الصفوف المولّدة.</returns>
    /// <summary>
    /// يفحص ما يمنع التحليل **قبل** تشغيله، ويعيد سبباً مقروءاً أو <c>null</c>.
    ///
    /// سببه عطلٌ حقيقي: <see cref="AnalyzeMonthAsync"/> يخرج بـ<c>return 0</c>
    /// صامتاً حين لا توجد مناوبة، فتظهر كل شاشات الحضور فارغةً وكأن لا بيانات —
    /// والحقيقة أن المحرّك لم يعمل أصلاً. الصفر ليس نتيجةً هنا بل امتناع.
    /// </summary>
    public static async Task<string?> FindAnalyzeBlockerAsync(
        ApplicationDbContext dbContext, int year, int month, int shiftTypeId)
    {
        var shiftTypes = await ShiftTypeStore.ListAsync(dbContext);

        if (shiftTypes.Count == 0)
        {
            return "لا توجد أنواع مناوبات بالنظام — أنشئ مناوبة من «أنواع المناوبات» " +
                   "وأسنِد إليها الموظفين، ثم أعِد تحديث الحضور.";
        }

        if (shiftTypeId <= 0)
        {
            return "اختر مناوبة التحليل أولاً.";
        }

        if (shiftTypes.All(shift => shift.Id != shiftTypeId))
        {
            return "المناوبة المختارة لم تعد موجودة — حدّث الصفحة واختر مناوبة قائمة.";
        }

        if (new DateOnly(year, month, 1) > DateOnly.FromDateTime(DateTime.Today))
        {
            return "الشهر المطلوب لم يبدأ بعد — لا يوجد ما يُحلَّل.";
        }

        // التحليل يشمل الفعّالين فقط؛ ذكرُ ذلك يمنع حيرةً حين تكون القاعدة مملوءة.
        if (!await dbContext.Employees.AsNoTracking().AnyAsync(employee => employee.IsActive))
        {
            return "لا يوجد موظفون فعّالون — التحليل يشمل الفعّالين فقط.";
        }

        return null;
    }

    public static async Task<int> AnalyzeMonthAsync(
        ApplicationDbContext dbContext, int year, int month, int defaultShiftTypeId)
    {
        await EnsureAsync(dbContext);

        var shifts = (await ShiftTypeStore.ListAsync(dbContext))
            .ToDictionary(s => s.Id, s => (Shift: s, Days: s.Days.ToDictionary(d => d.DayIndex)));
        if (!shifts.ContainsKey(defaultShiftTypeId)) return 0;
        var assignments = await EmployeeShiftTypeStore.MapAsync(dbContext);

        var monthStart = new DateOnly(year, month, 1);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        if (monthEnd > today) monthEnd = today;
        if (monthEnd < monthStart) return 0;

        var employees = await dbContext.Employees.AsNoTracking()
            .Where(e => e.IsActive)
            .Select(e => new
            {
                e.Id, e.DepartmentId, e.BranchId, e.PositionId,
                e.ContractType, e.Nationality, e.MaritalStatus
            })
            .ToListAsync();

        // معايير الاستحقاق: المناوبات ذات القواعد تُطبَّق تلقائياً على الموظف المطابِق
        // (ما لم يكن له تعيين يدوي صريح). ترتيب ثابت بالمعرّف لحسم التطابق المتعدد.
        var eligibilityShifts = shifts.Values
            .Select(v => v.Shift)
            .Where(s => s.Eligibility.Count > 0)
            .OrderBy(s => s.Id)
            .ToList();

        // التعديلات المؤقتة المتقاطعة مع الشهر — أعلى أولوية بالإسناد (موظف×يوم ← مناوبة)
        var overrideMap = await ShiftOverrideStore.MapAsync(dbContext, monthStart, monthEnd);

        // الروستر الشهري — أولوية تحت التجاوز وفوق التعيين الدائم (مناوبة أو عطلة/راحة)
        var rosterMap = await RosterStore.MapAsync(dbContext, year, month);

        // أزواج البصمات الحضورية فقط: الأزواج غير-الحضورية (استراحة/صلاة/مهمة عمل)
        // مستثناة حتى لا تصبح استراحةٌ «آخر خروج» فتُحتسب خروجاً مبكراً كاذباً.
        // NULL = حضور، فالبيانات السابقة لتصنيف الأزواج تبقى على معناها.
        // قراءة خام لا EF: العمود مُضاف بمسار الترقية الذاتي لا بهجرة.
        var attendanceSemanticId = await PunchSemanticStore.AttendanceSemanticIdAsync(dbContext);

        // هامش يوم قبل/بعد الشهر: نافذة الالتقاط الزمنية (TimeLimit بمرساة اليوم
        // السابق/التالي) قد تلتقط بصمة سجّلها الجهاز بتاريخ مجاور لحدود الشهر.
        var punches = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT EmployeeId, AttendanceDate, CheckIn, CheckOut
FROM AttendanceRecords
WHERE AttendanceDate >= @From AND AttendanceDate <= @To
  AND ISNULL(IsDeleted, 0) = 0
  AND ISNULL(PunchSemanticId, @Attendance) = @Attendance;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", monthStart.AddDays(-1).ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", monthEnd.AddDays(1).ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Attendance", attendanceSemanticId);
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                AttendanceDate = HrmsDatabase.GetDateOnly(reader, "AttendanceDate") ?? default,
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn") ?? default,
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut")
            });

        // أول بصمة دخول وآخر بصمة خروج لكل موظف×يوم (نمط الالتقاط بكيان)
        var byDay = punches
            .GroupBy(p => (p.EmployeeId, p.AttendanceDate))
            .ToDictionary(
                g => g.Key,
                g => (In: g.Min(p => p.CheckIn), Out: g.Max(p => p.CheckOut)));

        // كل أوقات بصم الموظف مرتّبة (بلا تقيّد بتاريخ الجهاز) — تغذية نافذة الالتقاط
        // الزمنية للمناوبات ذات TimeLimit، حيث قد ينتمي خروجُ ما-بعد-منتصف-الليل ليوم أمس.
        var timesByEmployee = new Dictionary<int, List<DateTime>>();
        foreach (var p in punches)
        {
            if (!timesByEmployee.TryGetValue(p.EmployeeId, out var list))
                timesByEmployee[p.EmployeeId] = list = new List<DateTime>();
            if (p.CheckIn != default) list.Add(p.CheckIn);
            if (p.CheckOut is { } co && co != p.CheckIn) list.Add(co);
        }
        foreach (var list in timesByEmployee.Values) list.Sort();

        // العطل الرسمية والإجازات المعتمدة — لمنع احتساب يوم عطلة/إجازة كـ«غياب»
        // (سدّ فجوة المحرك الجديد مقابل القديم؛ حرج لصحة خصومات الرواتب لاحقاً).
        var holidayRows = await dbContext.Holidays.AsNoTracking()
            .Select(h => new { h.HolidayDate, h.IsRecurring }).ToListAsync();
        var holidayDates = new HashSet<DateOnly>();
        foreach (var h in holidayRows)
        {
            var concrete = h.IsRecurring ? new DateOnly(year, h.HolidayDate.Month, h.HolidayDate.Day) : h.HolidayDate;
            if (concrete >= monthStart && concrete <= monthEnd) holidayDates.Add(concrete);
        }

        // نوع الإجازة يحدّد أثرها المالي: المدفوعة ⟶ «Leave» (لا تُخصم)، وغير المدفوعة
        // ⟶ «LeaveUnpaid» (يخصمها المسير يوماً×الأجر اليومي). المصدر IraqiLeavePolicy.
        var leaveRows = await dbContext.LeaveRequests.AsNoTracking()
            .Where(l => l.Status == LeaveStatus.Approved && l.FromDate <= monthEnd && l.ToDate >= monthStart)
            .Select(l => new { l.EmployeeId, l.FromDate, l.ToDate, l.LeaveType })
            .ToListAsync();
        var leavesByEmployee = leaveRows
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(l => (l.FromDate, l.ToDate,
                    Paid: SmartAttendance.Domain.Leave.IraqiLeavePolicy.IsPaid(l.LeaveType))).ToList());

        // المغادرات المعتمدة (ExitPermission بنافذة زمنية) — تغذّي قسيمة التأخير حين
        // تفعّل المناوبة «استثناء المغادرات خارج بداية المناوبة من التأخير».
        var permissionsByDay = await ApprovedPermissionsAsync(dbContext, monthStart, monthEnd);

        // دقائق الأزواج غير-الحضورية (استراحة/صلاة/مهمة…) لكل موظف×يوم — تُجرَّد من
        // مدة الحضور حين تفعّل المناوبة StripSemantics (وإلا لا تُقرأ أصلاً).
        var semanticMinutesByDay = (await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT EmployeeId, AttendanceDate, SUM(DATEDIFF(minute, CheckIn, CheckOut)) AS StripMinutes
FROM AttendanceRecords
WHERE AttendanceDate >= @From AND AttendanceDate <= @To
  AND ISNULL(IsDeleted, 0) = 0
  AND PunchSemanticId IS NOT NULL AND PunchSemanticId <> @Attendance
  AND CheckOut IS NOT NULL AND CheckOut > CheckIn
GROUP BY EmployeeId, AttendanceDate;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", monthStart.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", monthEnd.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Attendance", attendanceSemanticId);
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate") ?? default,
                Minutes = HrmsDatabase.GetInt(reader, "StripMinutes")
            }))
            .ToDictionary(r => (r.EmployeeId, r.Date), r => r.Minutes);

        // ترانزاكشن واحدة للحذف وكل دفعات الإدخال — فلَش لوغ واحد بدل واحد لكل أمر
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM DayAttendances WHERE WorkDate >= @From AND WorkDate <= @To;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", monthStart.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", monthEnd.ToDateTime(TimeOnly.MinValue));
            });

        // SqlBulkCopy: إدخال جماعي واحد لكل يوميات الشهر (آلاف الصفوف بأجزاء من الثانية)
        var table = new DataTable();
        table.Columns.Add("EmployeeId", typeof(int));
        table.Columns.Add("WorkDate", typeof(DateTime));
        table.Columns.Add("ShiftTypeId", typeof(int));
        table.Columns.Add("DayKind", typeof(string));
        table.Columns.Add("CheckIn", typeof(DateTime));
        table.Columns.Add("CheckOut", typeof(DateTime));
        table.Columns.Add("LateHours", typeof(decimal));
        table.Columns.Add("EarlyLeaveHours", typeof(decimal));
        table.Columns.Add("WorkedHours", typeof(decimal));
        table.Columns.Add("Status", typeof(string));
        table.Columns.Add("IsAnalyzed", typeof(bool));
        table.Columns.Add("AnalyzedAt", typeof(DateTime));

        var analyzedAt = DateTime.UtcNow;
        foreach (var emp in employees)
        {
            var employeeId = emp.Id;

            // المناوبة الأساس للموظف: (1) تعيين يدوي صريح، (2) مناوبة تطابق معايير
            // استحقاقها الموظف، (3) الافتراضية. التعيين لمناوبة محذوفة ← الافتراضية.
            int baseShiftId;
            if (assignments.TryGetValue(employeeId, out var assigned) && shifts.ContainsKey(assigned))
            {
                baseShiftId = assigned;
            }
            else
            {
                var attrs = new Dictionary<string, string?>
                {
                    ["Department"] = emp.DepartmentId.ToString(),
                    ["Branch"] = emp.BranchId.ToString(),
                    ["Position"] = emp.PositionId?.ToString(),
                    ["ContractType"] = emp.ContractType,
                    ["Nationality"] = emp.Nationality,
                    ["MaritalStatus"] = emp.MaritalStatus,
                    ["Employee"] = employeeId.ToString()
                };
                var eligible = eligibilityShifts
                    .FirstOrDefault(s => ShiftTypeStore.EmployeeMatchesEligibility(s, attrs));
                baseShiftId = eligible?.Id ?? defaultShiftTypeId;
            }

            for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
            {
                // أولوية الإسناد اليومي: تجاوز مؤقت ← روستر ← الأساس. الروستر قد يفرض
                // نوع اليوم (عطلة/راحة) بدل تعيين مناوبة.
                var shiftId = baseShiftId;
                string? forcedDayKind = null;
                if (overrideMap.TryGetValue((employeeId, date), out var ov) && shifts.ContainsKey(ov))
                {
                    shiftId = ov;
                }
                else if (rosterMap.TryGetValue((employeeId, date), out var rc))
                {
                    if (rc.ForcedDayKind != null) forcedDayKind = rc.ForcedDayKind;
                    else if (rc.ShiftId is int rs && shifts.ContainsKey(rs)) shiftId = rs;
                }
                var (shift, shiftDays) = shifts[shiftId];

                var day = shiftDays.TryGetValue(ToDayIndex(date), out var d) ? d : null;
                var dayKind = forcedDayKind ?? (day?.DayKind ?? "Work");
                byDay.TryGetValue((employeeId, date), out var punch);
                DateTime? checkIn = punch.In == default ? null : punch.In;
                DateTime? checkOut = punch.Out;

                // نافذة الالتقاط الزمنية (TimeLimitFrom/To + MidShiftTime): تتجاوز تاريخ
                // الجهاز فتضمّ خروج ما-بعد-منتصف-الليل ليومه الصحيح. بلا نافذة = السلوك القائم.
                if (HasCaptureWindow(shift)
                    && TrySelectWindowPunches(shift, day, date,
                        timesByEmployee.TryGetValue(employeeId, out var empTimes)
                            ? empTimes : (IReadOnlyList<DateTime>)Array.Empty<DateTime>(),
                        out var captured))
                {
                    (checkIn, checkOut) = captured;
                }

                // قسيمة التأخير من المغادرات المعتمدة — محروسة بإعداد المناوبة
                var lateCredit = TimeSpan.Zero;
                if (shift.ExcludePermsOutsideStartFromLate && checkIn.HasValue
                    && permissionsByDay.TryGetValue((employeeId, date), out var dayPerms)
                    && TimeSpan.TryParse(day?.StartTime, out var creditShiftStart))
                {
                    TimeSpan.TryParse(day?.EndTime, out var creditShiftEnd);
                    lateCredit = PermissionLatenessCredit(
                        dayPerms, TimeOnly.FromTimeSpan(creditShiftStart),
                        TimeOnly.FromDateTime(checkIn.Value),
                        shift.ConsiderPermissionsOutsideShift,
                        TimeOnly.FromTimeSpan(creditShiftEnd));
                }

                var row = Derive(shift, day, dayKind, checkIn, checkOut, lateCredit);

                // تجريد فترات الدلالات من مدة الحضور — محروس بإعداد المناوبة
                var workedHours = row.WorkedHours;
                if (shift.StripSemantics && workedHours > 0
                    && semanticMinutesByDay.TryGetValue((employeeId, date), out var stripMinutes))
                {
                    workedHours = StripSemanticHours(workedHours, stripMinutes);
                }

                // يوم بلا حضور فعلي (غياب) يُعاد تصنيفه إن كان عطلة رسمية أو ضمن إجازة
                // معتمدة — فلا يُخصم بالرواتب. الأيام المشتغَل بها تبقى كما هي.
                var status = row.Status;
                if (status == "Absent")
                {
                    if (holidayDates.Contains(date))
                        status = "Holiday";
                    else if (leavesByEmployee.TryGetValue(employeeId, out var intervals))
                    {
                        var hit = intervals
                            .Where(iv => date >= iv.FromDate && date <= iv.ToDate)
                            .Select(iv => (bool?)iv.Paid)
                            .FirstOrDefault();
                        if (hit.HasValue) status = hit.Value ? "Leave" : "LeaveUnpaid";
                    }
                }

                table.Rows.Add(employeeId, date.ToDateTime(TimeOnly.MinValue), shiftId, dayKind,
                    (object?)row.CheckIn ?? DBNull.Value, (object?)row.CheckOut ?? DBNull.Value,
                    row.LateHours, row.EarlyLeaveHours, workedHours, status, true, analyzedAt);
            }
        }

        var connection = (SqlConnection)dbContext.Database.GetDbConnection();
        var sqlTransaction = (SqlTransaction)transaction.GetDbTransaction();
        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, sqlTransaction))
        {
            bulk.DestinationTableName = "DayAttendances";
            bulk.BulkCopyTimeout = 120;
            foreach (DataColumn column in table.Columns)
            {
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }
            await bulk.WriteToServerAsync(table);
        }

        await transaction.CommitAsync();
        return table.Rows.Count;
    }

    /// <summary>
    /// تعديل يدوي لبصمتي يوم (نمط كيان — تعديل من خلية المستعرض): يحدّث الدخول/الخروج
    /// ويعيد اشتقاق التأخير/الخروج المبكر/الساعات/الحالة بمناوبة اليوم نفسها. يتطلب
    /// وجود يومية محللة مسبقاً (لمعرفة المناوبة ونوع اليوم).
    /// </summary>
    public static async Task<bool> UpdateDayAsync(
        ApplicationDbContext dbContext, int employeeId, DateOnly date, DateTime? checkIn, DateTime? checkOut)
    {
        await EnsureAsync(dbContext);

        var existing = (await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT TOP 1 ShiftTypeId, DayKind FROM DayAttendances WHERE EmployeeId=@Emp AND WorkDate=@Date;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", date.ToDateTime(TimeOnly.MinValue));
            },
            reader => new
            {
                ShiftTypeId = HrmsDatabase.GetNullableInt(reader, "ShiftTypeId"),
                DayKind = HrmsDatabase.GetString(reader, "DayKind") is { Length: > 0 } k ? k : "Work"
            })).FirstOrDefault();
        if (existing == null) return false;

        var shifts = (await ShiftTypeStore.ListAsync(dbContext)).ToDictionary(s => s.Id);
        ShiftTypeStore.ShiftType? shift = existing.ShiftTypeId is int sid && shifts.TryGetValue(sid, out var s) ? s : null;
        var shiftDay = shift?.Days.FirstOrDefault(d => d.DayIndex == ToDayIndex(date));

        var row = shift != null
            ? Derive(shift, shiftDay, existing.DayKind, checkIn, checkOut)
            : (checkIn, checkOut, 0m, 0m,
                checkIn.HasValue && checkOut.HasValue ? Math.Max(0, Math.Round((decimal)(checkOut.Value - checkIn.Value).TotalHours, 2)) : 0m,
                !checkIn.HasValue ? "Absent" : !checkOut.HasValue ? "Incomplete" : "Present");

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE DayAttendances
SET CheckIn=@In, CheckOut=@Out, LateHours=@Late, EarlyLeaveHours=@Early,
    WorkedHours=@Worked, Status=@Status, IsAnalyzed=1, AnalyzedAt=SYSUTCDATETIME()
WHERE EmployeeId=@Emp AND WorkDate=@Date;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", date.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@In", (object?)row.Item1 ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Out", (object?)row.Item2 ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Late", row.Item3);
                HrmsDatabase.AddParameter(command, "@Early", row.Item4);
                HrmsDatabase.AddParameter(command, "@Worked", row.Item5);
                HrmsDatabase.AddParameter(command, "@Status", row.Item6);
            });
        return true;
    }

    /// <summary>
    /// تطبيق فترة السماح على فارق زمني (تأخير أو خروج مبكر) وإرجاعه بالساعات.
    /// داخل السماحية (أو عندها بالضبط) ⇒ صفر. بعدها ⇒ حسب سياسة المناوبة:
    /// Subtract = الفارق ناقص السماحية · Full = الفارق كاملاً.
    /// </summary>
    public static decimal ApplyGrace(TimeSpan span, int graceMinutes, string? policy)
    {
        if (span <= TimeSpan.Zero) return 0;
        var grace = TimeSpan.FromMinutes(Math.Max(0, graceMinutes));
        if (span <= grace) return 0;
        var effective = policy == "Full" ? span : span - grace;
        return Math.Round((decimal)effective.TotalHours, 2);
    }

    /// <summary>
    /// المغادرات المعتمدة (SelfServiceRequests نوع ExitPermission حالة Approved) بنافذة
    /// زمنية كاملة، مفهرسة موظف×يوم. جدول الطلبات يُنشأ بمسار EnsureCreated العام لا هنا،
    /// فغيابه (قاعدة بكر) يعيد قاموساً فارغاً بدل خطأ.
    /// </summary>
    public static async Task<Dictionary<(int EmployeeId, DateOnly Date), List<(TimeOnly Start, TimeOnly End)>>>
        ApprovedPermissionsAsync(ApplicationDbContext dbContext, DateOnly from, DateOnly to)
    {
        var result = new Dictionary<(int, DateOnly), List<(TimeOnly, TimeOnly)>>();

        var tableExists = await HrmsDatabase.ScalarAsync<int>(
            dbContext, "SELECT CASE WHEN OBJECT_ID('SelfServiceRequests','U') IS NULL THEN 0 ELSE 1 END;");
        if (tableExists == 0) return result;

        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT EmployeeId, COALESCE(FromDate, RequestDate) AS OnDate, StartTime, EndTime
FROM SelfServiceRequests
WHERE RequestType = N'ExitPermission' AND Status = N'Approved'
  AND StartTime IS NOT NULL AND EndTime IS NOT NULL
  AND COALESCE(FromDate, RequestDate) >= @From AND COALESCE(FromDate, RequestDate) <= @To;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                OnDate = HrmsDatabase.GetDateOnly(reader, "OnDate"),
                Start = reader["StartTime"] is TimeSpan s ? (TimeOnly?)TimeOnly.FromTimeSpan(s) : null,
                End = reader["EndTime"] is TimeSpan e ? (TimeOnly?)TimeOnly.FromTimeSpan(e) : null
            });

        foreach (var row in rows)
        {
            if (row.OnDate is not { } onDate || row.Start is not { } start || row.End is not { } end) continue;
            var key = (row.EmployeeId, onDate);
            if (!result.TryGetValue(key, out var list)) result[key] = list = new List<(TimeOnly, TimeOnly)>();
            list.Add((start, end));
        }
        return result;
    }

    /// <summary>
    /// قسيمة المغادرات المعتمدة من التأخير (إعداد <c>ExcludePermsOutsideStartFromLate</c>):
    /// مجموع تداخل نوافذ المغادرة المعتمدة مع فترة التأخير [بداية الدوام، الدخول الفعلي].
    /// حين <paramref name="considerOutsideShift"/>=false تُقصّ نافذة المغادرة على ساعات
    /// الدوام [البداية، النهاية] أولاً (إعداد <c>ConsiderPermissionsOutsideShift</c>).
    /// تُطرح القسيمة من مدة التأخير قبل تطبيق السماحية. نقية قابلة للاختبار.
    /// </summary>
    public static TimeSpan PermissionLatenessCredit(
        IEnumerable<(TimeOnly Start, TimeOnly End)> permissions,
        TimeOnly shiftStart, TimeOnly checkIn, bool considerOutsideShift, TimeOnly shiftEnd)
    {
        if (checkIn <= shiftStart) return TimeSpan.Zero;

        var credit = TimeSpan.Zero;
        foreach (var (permStart, permEnd) in permissions)
        {
            var start = permStart;
            var end = permEnd;
            if (end <= start) continue;

            if (!considerOutsideShift)
            {
                if (start < shiftStart) start = shiftStart;
                if (shiftEnd > shiftStart && end > shiftEnd) end = shiftEnd;
                if (end <= start) continue;
            }

            var overlapStart = start > shiftStart ? start : shiftStart;
            var overlapEnd = end < checkIn ? end : checkIn;
            if (overlapEnd > overlapStart) credit += overlapEnd - overlapStart;
        }
        return credit;
    }

    /// <summary>
    /// تجريد دقائق الدلالات (استراحة/صلاة…) من ساعات الحضور (إعداد <c>StripSemantics</c>).
    /// لا تنزل تحت الصفر، وبدقّة منزلتين كبقية ساعات الاشتقاق. نقية قابلة للاختبار.
    /// </summary>
    public static decimal StripSemanticHours(decimal workedHours, int semanticMinutes) =>
        semanticMinutes <= 0 ? workedHours
            : Math.Max(0m, workedHours - Math.Round(semanticMinutes / 60m, 2));

    /// <summary>هل تُعرِّف المناوبة نافذة التقاط زمنية للبصمات الصالحة؟ (تبويب «القواعد»)</summary>
    public static bool HasCaptureWindow(ShiftTypeStore.ShiftType shift) =>
        TimeSpan.TryParse(shift.TimeLimitFrom, out _) || TimeSpan.TryParse(shift.TimeLimitTo, out _);

    /// <summary>
    /// اختيار بصمتي اليوم من نافذة الالتقاط الزمنية للمناوبة بدل تاريخ الجهاز:
    /// النافذة = [TimeLimitFrom بمرساة نفس اليوم/السابق ، TimeLimitTo بمرساة نفس اليوم/التالي]،
    /// والطرف غير المحدَّد يسقط لحدّ اليوم التقويمي. مع MidShiftTime تُقسَم بصمات النافذة:
    /// الدخول = أبكر بصمة ≤ المنتصف، والخروج = أحدث بصمة بعده (منتصفٌ أبكر من بداية
    /// الدوام يُرسى باليوم التالي — حالة المناوبة الليلية). بلا منتصف: أبكر/أحدث بصمة.
    /// يعيد false (فيبقى سلوك تاريخ الجهاز) إن كانت النافذة غير صالحة (النهاية ≤ البداية).
    /// نقي قابل للاختبار؛ يفترض orderedTimes مرتّبة تصاعدياً.
    /// </summary>
    public static bool TrySelectWindowPunches(
        ShiftTypeStore.ShiftType shift, ShiftTypeStore.ShiftDay? day, DateOnly date,
        IReadOnlyList<DateTime> orderedTimes, out (DateTime? CheckIn, DateTime? CheckOut) selected)
    {
        selected = (null, null);
        var dayStart = date.ToDateTime(TimeOnly.MinValue);

        var windowStart = TimeSpan.TryParse(shift.TimeLimitFrom, out var fromTime)
            ? dayStart.AddDays(shift.TimeLimitFromDayBefore ? -1 : 0) + fromTime
            : dayStart;
        var windowEnd = TimeSpan.TryParse(shift.TimeLimitTo, out var toTime)
            ? dayStart.AddDays(shift.TimeLimitToDayAfter ? 1 : 0) + toTime
            : dayStart.AddDays(1).AddTicks(-1);
        if (windowEnd <= windowStart) return false;

        List<DateTime>? candidates = null;
        foreach (var time in orderedTimes)
        {
            if (time < windowStart) continue;
            if (time > windowEnd) break;
            (candidates ??= new List<DateTime>()).Add(time);
        }
        if (candidates == null) return true; // نافذة صالحة بلا بصمات ⇒ غياب

        if (TimeSpan.TryParse(shift.MidShiftTime, out var mid))
        {
            var midAt = dayStart + mid;
            if (TimeSpan.TryParse(day?.StartTime, out var shiftStart) && mid < shiftStart)
                midAt = midAt.AddDays(1);

            DateTime? checkIn = null, checkOut = null;
            foreach (var time in candidates)
            {
                if (time <= midAt) { checkIn ??= time; }
                else { checkOut = time; }
            }
            selected = (checkIn, checkOut);
            return true;
        }

        selected = (candidates[0], candidates.Count > 1 ? candidates[^1] : null);
        return true;
    }

    /// <summary>اشتقاق حقول اليوم: التأخير/الخروج المبكر/الساعات/الحالة. (عامة لتغطية الاختبارات)</summary>
    public static (DateTime? CheckIn, DateTime? CheckOut, decimal LateHours,
        decimal EarlyLeaveHours, decimal WorkedHours, string Status) Derive(
        ShiftTypeStore.ShiftType shift, ShiftTypeStore.ShiftDay? day,
        string dayKind, DateTime? checkIn, DateTime? checkOut, TimeSpan lateCredit = default)
    {
        if (dayKind != "Work")
        {
            // بصمة بيوم عطلة/راحة تُسجل كساعات عمل بلا تأخير (مادة أوفرتايم لاحقاً)
            var offWorked = checkIn.HasValue && checkOut.HasValue
                ? Math.Round((decimal)(checkOut.Value - checkIn.Value).TotalHours, 2) : 0;
            return (checkIn, checkOut, 0, 0, Math.Max(0, offWorked),
                dayKind == "Weekend" ? "Weekend" : "Rest");
        }

        // تعبئة البصمة المفقودة (إعداد كان صمّاءً حتى الآن): يُكمل الطرف المفقود بوقت
        // المناوبة إن وُجد الطرف الآخر فقط. كلا الطرفين مفقود = غياب حقيقي فلا يُلفَّق
        // يوم كامل من لا بيانات. محروس بالرايتين (الافتراضي false ⟹ لا تغيير للسلوك القائم).
        if (!checkIn.HasValue && checkOut.HasValue && shift.FillMissingCheckIn)
        {
            checkIn = FillFromShift(checkOut.Value, day?.StartTime, day?.EndTime, fillingStart: true);
        }
        else if (checkIn.HasValue && !checkOut.HasValue && shift.FillMissingCheckOut)
        {
            checkOut = FillFromShift(checkIn.Value, day?.StartTime, day?.EndTime, fillingStart: false);
        }

        if (!checkIn.HasValue)
        {
            return (null, null, 0, 0, 0, "Absent");
        }

        // بصمة ناقصة ⟹ **كل المقادير صفر**.
        //
        // طرفٌ واحد لا يكفي لاشتقاق أي كمية، والأسوأ أن البصمة الوحيدة قد تكون
        // بصمة **انصراف** سُجّلت أوّلاً — فاحتساب التأخير منها يعطي «تأخير 9.55
        // ساعة» لموظفٍ بصم 17:33. رقمٌ كاذب يدخل المسير خصماً.
        // القرار: لا تأخير ولا خروج مبكر ولا ساعات عمل بلا طرفَين.
        if (!checkOut.HasValue)
        {
            return (checkIn, null, 0, 0, 0, "Incomplete");
        }

        var worked = Math.Max(0, Math.Round((decimal)(checkOut.Value - checkIn.Value).TotalHours, 2));

        if (shift.IsFlexible)
        {
            // مرنة: لا تأخير بالساعة؛ النقص عن الساعات المطلوبة = خروج مبكر.
            // الطرفان مضمونان هنا — البصمة الناقصة خرجت أعلاه بأصفارها.
            var shortfall = Math.Max(0, Math.Round(shift.FlexDailyHours - worked, 2));
            return (checkIn, checkOut, 0, shortfall, worked, "Present");
        }

        decimal late = 0, early = 0;
        if (TimeSpan.TryParse(day?.StartTime, out var shiftStart))
        {
            // قسيمة المغادرة المعتمدة (إن وُجدت) تُطرح من مدة التأخير قبل السماحية
            late = ApplyGrace(checkIn.Value.TimeOfDay - shiftStart - lateCredit,
                shift.LatenessGraceMinutes, shift.GraceExceededPolicy);
        }
        if (checkOut.Value.Date == checkIn.Value.Date
            && TimeSpan.TryParse(day?.StartTime, out var dayStart)
            && TimeSpan.TryParse(day?.EndTime, out var shiftEnd)
            && shiftEnd > dayStart) // العابرة لمنتصف الليل (نهاية < بداية) خروجها باليوم التالي — لا اشتقاق مبكر هنا
        {
            early = ApplyGrace(shiftEnd - checkOut.Value.TimeOfDay,
                shift.EarlyLeaveGraceMinutes, shift.GraceExceededPolicy);
        }

        var status = late > 0 ? "Late" : "Present";
        return (checkIn, checkOut, late, early, worked, status);
    }

    /// <summary>
    /// يبني وقت التعبئة من مواقيت المناوبة على تاريخ البصمة المرساة. يعيد null إن كان
    /// الوقت الهدف غير قابل للتحليل (⟹ يبقى الطرف مفقوداً فيسقط لغياب/غير مكتمل — آمن).
    /// يراعي المناوبة العابرة لمنتصف الليل (نهاية ≤ بداية): البداية باليوم السابق للخروج،
    /// والنهاية باليوم التالي للدخول.
    /// </summary>
    private static DateTime? FillFromShift(DateTime anchor, string? startStr, string? endStr, bool fillingStart)
    {
        var target = fillingStart ? startStr : endStr;
        if (!TimeSpan.TryParse(target, out var time))
        {
            return null;
        }

        var result = anchor.Date + time;
        var overnight = TimeSpan.TryParse(startStr, out var st)
            && TimeSpan.TryParse(endStr, out var en) && en <= st;
        if (overnight)
        {
            if (fillingStart && result > anchor) result = result.AddDays(-1);
            if (!fillingStart && result < anchor) result = result.AddDays(1);
        }
        return result;
    }

    public static Task<List<DayRow>> ListAsync(
        ApplicationDbContext dbContext, int year, int month, string? search)
    {
        var from = new DateOnly(year, month, 1);
        return ListRangeAsync(dbContext, from, from.AddMonths(1).AddDays(-1), search);
    }

    /// <summary>يوميات مدى تواريخ حر — تغذّي شاشة «إدارة الحضور» من المحرك الرسمي.</summary>
    public static async Task<List<DayRow>> ListRangeAsync(
        ApplicationDbContext dbContext, DateOnly from, DateOnly to, string? search)
    {
        await EnsureAsync(dbContext);

        // SQL يترجم الاستعلام كاملاً، فذكرُ جدولٍ غير موجود يفشل حتى داخل شرطٍ
        // معطّل. لذلك يُدرَج جزء الخدمة الذاتية **نصّياً** بعد التأكد من وجوده.
        var hasSelfService = await HrmsDatabase.ScalarAsync<int>(
            dbContext, "SELECT CASE WHEN OBJECT_ID('SelfServiceRequests','U') IS NULL THEN 0 ELSE 1 END;");

        var selfServiceClause = hasSelfService == 1
            ? """
                WHEN EXISTS (
                    SELECT 1 FROM SelfServiceRequests r
                    WHERE r.EmployeeId = d.EmployeeId
                      AND COALESCE(r.FromDate, r.RequestDate) = d.WorkDate
                      AND COALESCE(r.UpdatedAt, r.CreatedAt) > d.AnalyzedAt) THEN 1
            """
            : string.Empty;

        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT d.*, e.EmployeeNo, e.FullName, s.Name AS ShiftName, s.ColorHex AS ShiftColor,
    -- بائتة = طرأ تغيير على اليوم بعد لحظة تحليله. ثلاثة مصادر:
    --   1) البصمات (يشمل اعتماد «نسيان بصمة» لأنه يُنشئ بصمة فعلية، وإلغاءه
    --      لأنه يحذفها منطقياً ويحدّث UpdatedAt) وأي تعديل يدوي أو إعادة استيراد.
    --   2) الإجازات المعتمدة المتقاطعة مع اليوم.
    --   3) المغادرات/الزمنية المعتمدة بذلك اليوم.
    -- AnalyzedAt NULL ⟹ لم تُحلَّل أصلاً.
    CAST(CASE
        WHEN d.AnalyzedAt IS NULL THEN 1
        WHEN EXISTS (
            SELECT 1 FROM AttendanceRecords a
            WHERE a.EmployeeId = d.EmployeeId
              AND a.AttendanceDate = d.WorkDate
              AND COALESCE(a.UpdatedAt, a.CreatedAt) > d.AnalyzedAt) THEN 1
        WHEN EXISTS (
            SELECT 1 FROM LeaveRequests l
            WHERE l.EmployeeId = d.EmployeeId
              AND ISNULL(l.IsDeleted, 0) = 0
              AND d.WorkDate BETWEEN l.FromDate AND l.ToDate
              AND COALESCE(l.UpdatedAt, l.CreatedAt) > d.AnalyzedAt) THEN 1
{selfServiceClause}
        ELSE 0
    END AS bit) AS IsStale
FROM DayAttendances d
INNER JOIN Employees e ON e.Id = d.EmployeeId
LEFT JOIN ShiftTypes s ON s.Id = d.ShiftTypeId
WHERE d.WorkDate >= @From AND d.WorkDate <= @To
ORDER BY e.EmployeeNo, d.WorkDate;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            },
            reader => new DayRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                WorkDate = HrmsDatabase.GetDateOnly(reader, "WorkDate") ?? default,
                ShiftTypeId = reader["ShiftTypeId"] is int s ? s : null,
                ShiftName = HrmsDatabase.GetString(reader, "ShiftName") is { Length: > 0 } n ? n : null,
                ShiftColor = HrmsDatabase.GetString(reader, "ShiftColor") is { Length: > 0 } c ? c : null,
                DayKind = HrmsDatabase.GetString(reader, "DayKind") is { Length: > 0 } k ? k : "Work",
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut"),
                LateHours = reader["LateHours"] is decimal late ? late : 0,
                EarlyLeaveHours = reader["EarlyLeaveHours"] is decimal early ? early : 0,
                WorkedHours = reader["WorkedHours"] is decimal worked ? worked : 0,
                Status = HrmsDatabase.GetString(reader, "Status") is { Length: > 0 } st ? st : "Absent",
                IsAnalyzed = HrmsDatabase.GetBool(reader, "IsAnalyzed"),
                IsStale = HrmsDatabase.GetBool(reader, "IsStale")
            });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            rows = rows.Where(r =>
                r.EmployeeNo.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return rows;
    }
}
