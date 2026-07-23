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
    }

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

        var punches = await dbContext.AttendanceRecords.AsNoTracking()
            .Where(r => r.AttendanceDate >= monthStart && r.AttendanceDate <= monthEnd)
            .Select(r => new { r.EmployeeId, r.AttendanceDate, r.CheckIn, r.CheckOut })
            .ToListAsync();

        // أول بصمة دخول وآخر بصمة خروج لكل موظف×يوم (نمط الالتقاط بكيان)
        var byDay = punches
            .GroupBy(p => (p.EmployeeId, p.AttendanceDate))
            .ToDictionary(
                g => g.Key,
                g => (In: g.Min(p => p.CheckIn), Out: g.Max(p => p.CheckOut)));

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

        var leaveRows = await dbContext.LeaveRequests.AsNoTracking()
            .Where(l => l.Status == LeaveStatus.Approved && l.FromDate <= monthEnd && l.ToDate >= monthStart)
            .Select(l => new { l.EmployeeId, l.FromDate, l.ToDate })
            .ToListAsync();
        var leavesByEmployee = leaveRows
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(l => (l.FromDate, l.ToDate)).ToList());

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

            // ترتيب الإسناد: (1) تعيين يدوي صريح، (2) مناوبة تطابق معايير استحقاقها
            // الموظف، (3) المناوبة الافتراضية. التعيين لمناوبة محذوفة ← الافتراضية.
            int shiftId;
            if (assignments.TryGetValue(employeeId, out var assigned) && shifts.ContainsKey(assigned))
            {
                shiftId = assigned;
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
                shiftId = eligible?.Id ?? defaultShiftTypeId;
            }

            var (shift, shiftDays) = shifts[shiftId];

            for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
            {
                var day = shiftDays.TryGetValue(ToDayIndex(date), out var d) ? d : null;
                var dayKind = day?.DayKind ?? "Work";
                byDay.TryGetValue((employeeId, date), out var punch);
                var row = Derive(shift, day, dayKind, punch.In == default ? null : punch.In, punch.Out);

                // يوم بلا حضور فعلي (غياب) يُعاد تصنيفه إن كان عطلة رسمية أو ضمن إجازة
                // معتمدة — فلا يُخصم بالرواتب. الأيام المشتغَل بها تبقى كما هي.
                var status = row.Status;
                if (status == "Absent")
                {
                    if (holidayDates.Contains(date))
                        status = "Holiday";
                    else if (leavesByEmployee.TryGetValue(employeeId, out var intervals)
                             && intervals.Any(iv => date >= iv.FromDate && date <= iv.ToDate))
                        status = "Leave";
                }

                table.Rows.Add(employeeId, date.ToDateTime(TimeOnly.MinValue), shiftId, dayKind,
                    (object?)row.CheckIn ?? DBNull.Value, (object?)row.CheckOut ?? DBNull.Value,
                    row.LateHours, row.EarlyLeaveHours, row.WorkedHours, status, true, analyzedAt);
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

    /// <summary>اشتقاق حقول اليوم: التأخير/الخروج المبكر/الساعات/الحالة.</summary>
    private static (DateTime? CheckIn, DateTime? CheckOut, decimal LateHours,
        decimal EarlyLeaveHours, decimal WorkedHours, string Status) Derive(
        ShiftTypeStore.ShiftType shift, ShiftTypeStore.ShiftDay? day,
        string dayKind, DateTime? checkIn, DateTime? checkOut)
    {
        if (dayKind != "Work")
        {
            // بصمة بيوم عطلة/راحة تُسجل كساعات عمل بلا تأخير (مادة أوفرتايم لاحقاً)
            var offWorked = checkIn.HasValue && checkOut.HasValue
                ? Math.Round((decimal)(checkOut.Value - checkIn.Value).TotalHours, 2) : 0;
            return (checkIn, checkOut, 0, 0, Math.Max(0, offWorked),
                dayKind == "Weekend" ? "Weekend" : "Rest");
        }

        if (!checkIn.HasValue)
        {
            return (null, null, 0, 0, 0, "Absent");
        }

        var worked = checkOut.HasValue
            ? Math.Max(0, Math.Round((decimal)(checkOut.Value - checkIn.Value).TotalHours, 2)) : 0;

        if (shift.IsFlexible)
        {
            // مرنة: لا تأخير بالساعة؛ النقص عن الساعات المطلوبة = خروج مبكر
            var shortfall = checkOut.HasValue
                ? Math.Max(0, Math.Round(shift.FlexDailyHours - worked, 2)) : 0;
            return (checkIn, checkOut, 0, shortfall, worked,
                !checkOut.HasValue ? "Incomplete" : "Present");
        }

        decimal late = 0, early = 0;
        if (TimeSpan.TryParse(day?.StartTime, out var shiftStart))
        {
            var lateSpan = checkIn.Value.TimeOfDay - shiftStart;
            if (lateSpan > TimeSpan.Zero) late = Math.Round((decimal)lateSpan.TotalHours, 2);
        }
        if (checkOut.HasValue && checkOut.Value.Date == checkIn.Value.Date
            && TimeSpan.TryParse(day?.StartTime, out var dayStart)
            && TimeSpan.TryParse(day?.EndTime, out var shiftEnd)
            && shiftEnd > dayStart) // العابرة لمنتصف الليل (نهاية < بداية) خروجها باليوم التالي — لا اشتقاق مبكر هنا
        {
            var earlySpan = shiftEnd - checkOut.Value.TimeOfDay;
            if (earlySpan > TimeSpan.Zero) early = Math.Round((decimal)earlySpan.TotalHours, 2);
        }

        var status = !checkOut.HasValue ? "Incomplete" : late > 0 ? "Late" : "Present";
        return (checkIn, checkOut, late, early, worked, status);
    }

    public static async Task<List<DayRow>> ListAsync(
        ApplicationDbContext dbContext, int year, int month, string? search)
    {
        await EnsureAsync(dbContext);

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT d.*, e.EmployeeNo, e.FullName, s.Name AS ShiftName, s.ColorHex AS ShiftColor
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
                IsAnalyzed = HrmsDatabase.GetBool(reader, "IsAnalyzed")
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
