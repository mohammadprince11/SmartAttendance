using SmartAttendance.Infrastructure.Persistence;

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
    /// <returns>عدد صفوف الموظفين بعد البناء.</returns>
    public static async Task<int> BuildMonthAsync(ApplicationDbContext dbContext, int year, int month)
    {
        await EnsureAsync(dbContext);
        await DayAttendanceStore.EnsureAsync(dbContext);

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
WITH Aggregated AS
(
    SELECT d.EmployeeId,
           SUM(CASE WHEN d.DayKind = N'Work' THEN 1 ELSE 0 END) AS WorkDays,
           SUM(CASE WHEN d.Status IN (N'Present', N'Late') THEN 1 ELSE 0 END) AS PresentDays,
           SUM(CASE WHEN d.Status = N'Absent' THEN 1 ELSE 0 END) AS AbsentDays,
           SUM(CASE WHEN d.Status = N'Incomplete' THEN 1 ELSE 0 END) AS IncompleteDays,
           SUM(CASE WHEN d.Status = N'LeaveUnpaid' THEN 1 ELSE 0 END) AS UnpaidLeaveDays,
           SUM(d.LateHours) AS LateHours,
           SUM(d.EarlyLeaveHours) AS EarlyLeaveHours,
           SUM(d.WorkedHours) AS WorkedHours
    FROM DayAttendances d
    WHERE d.WorkDate >= @From AND d.WorkDate <= @To AND d.IsAnalyzed = 1
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
            "SELECT COUNT(1) FROM EmployeeMonthAttendance WHERE [Year] = @Year AND [Month] = @Month;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Year", year);
                HrmsDatabase.AddParameter(command, "@Month", month);
            });
    }

    public static async Task<List<MonthRow>> ListAsync(ApplicationDbContext dbContext, int year, int month)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT m.*, e.EmployeeNo, e.FullName
FROM EmployeeMonthAttendance m
INNER JOIN Employees e ON e.Id = m.EmployeeId
WHERE m.[Year] = @Year AND m.[Month] = @Month
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
        ApplicationDbContext dbContext, IReadOnlyCollection<int> ids)
    {
        await EnsureAsync(dbContext);
        if (ids.Count == 0) return (0, 0);

        var eligible = await EligibleForApprovalAsync(dbContext, ids);
        var approved = await Transition(dbContext, eligible, from: "UnderReview", to: "Approved",
            "ApprovedAt = SYSUTCDATETIME()");

        return (approved, ids.Count - eligible.Count);
    }

    /// <summary>
    /// يصفّي المعرّفات إلى ما اكتمل تحليله: عدد اليوميات المحلّلة للموظف بالشهر
    /// يساوي أيام الشهر المنقضية (الشهر الجاري يُقاس حتى اليوم، لا حتى آخره).
    /// </summary>
    private static async Task<List<int>> EligibleForApprovalAsync(
        ApplicationDbContext dbContext, IReadOnlyCollection<int> ids)
    {
        await DayAttendanceStore.EnsureAsync(dbContext);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var eligible = new List<int>();

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
WHERE m.Id IN ({inList});
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
    public static Task<int> ReopenAsync(ApplicationDbContext dbContext, IReadOnlyCollection<int> ids) =>
        Transition(dbContext, ids, from: "Approved", to: "UnderReview",
            "ApprovedAt = NULL");

    /// <summary>قفل للرواتب: معتمد ← مقفل (نهائي).</summary>
    public static Task<int> LockAsync(ApplicationDbContext dbContext, IReadOnlyCollection<int> ids) =>
        Transition(dbContext, ids, from: "Approved", to: "Locked",
            "LockedAt = SYSUTCDATETIME()");

    private static async Task<int> Transition(ApplicationDbContext dbContext,
        IReadOnlyCollection<int> ids, string from, string to, string extraSet)
    {
        await EnsureAsync(dbContext);
        if (ids.Count == 0) return 0;

        var total = 0;
        foreach (var chunk in ids.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            total += await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                $"""
UPDATE EmployeeMonthAttendance SET Status = @To, {extraSet}
WHERE Id IN ({inList}) AND Status = @FromStatus;
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
