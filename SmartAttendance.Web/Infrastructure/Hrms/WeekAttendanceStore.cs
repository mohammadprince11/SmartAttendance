using System.Globalization;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// الحضور الأسبوعي (نمط كيان — قسم 36.د بدراسة الحضور): نظير الحضور الشهري بتجميع
/// أسبوعي بأرقام أسابيع ISO (الإثنين→الأحد). صف لكل موظف×أسبوع يجمع يومياته المحللة
/// بدورة حالة UnderReview←Approved←Locked. المفتاح (EmployeeId, IsoYear, WeekNumber).
/// «بناء الأسبوع» يجدد أرقام «تحت المراجعة» فقط ويترك المعتمد/المقفل. self-healing.
/// </summary>
public static class WeekAttendanceStore
{
    public sealed class WeekRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public int IsoYear { get; set; }
        public int WeekNumber { get; set; }
        public DateOnly WeekStart { get; set; }
        public DateOnly WeekEnd { get; set; }
        public int WorkDays { get; set; }
        public int PresentDays { get; set; }
        public int AbsentDays { get; set; }
        public int IncompleteDays { get; set; }
        public int UnpaidLeaveDays { get; set; }
        public decimal LateHours { get; set; }
        public decimal EarlyLeaveHours { get; set; }
        public decimal WorkedHours { get; set; }
        public string Status { get; set; } = "UnderReview";
        public DateTime? ApprovedAt { get; set; }
        public DateTime? LockedAt { get; set; }

        public string RangeText => $"{WeekStart:yyyy-MM-dd} → {WeekEnd:yyyy-MM-dd}";
    }

    public static string StatusLabel(string status) => status switch
    {
        "UnderReview" => "تحت المراجعة",
        "Approved" => "معتمد",
        "Locked" => "مقفل",
        _ => status
    };

    /// <summary>حدود أسبوع ISO (الإثنين→الأحد) لسنة ISO ورقم أسبوع.</summary>
    public static (DateOnly Start, DateOnly End) WeekRange(int isoYear, int weekNumber)
    {
        var maxWeek = ISOWeek.GetWeeksInYear(isoYear);
        weekNumber = Math.Clamp(weekNumber, 1, maxWeek);
        var monday = ISOWeek.ToDateTime(isoYear, weekNumber, DayOfWeek.Monday);
        var start = DateOnly.FromDateTime(monday);
        return (start, start.AddDays(6));
    }

    /// <summary>سنة/أسبوع ISO الحاليان (للافتراض).</summary>
    public static (int IsoYear, int Week) Current()
    {
        var today = DateTime.Today;
        return (ISOWeek.GetYear(today), ISOWeek.GetWeekOfYear(today));
    }

    public static int WeeksInYear(int isoYear) => ISOWeek.GetWeeksInYear(isoYear);

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeWeekAttendance', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeWeekAttendance
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        IsoYear int NOT NULL,
        WeekNumber int NOT NULL,
        WeekStart date NOT NULL,
        WeekEnd date NOT NULL,
        WorkDays int NOT NULL DEFAULT(0),
        PresentDays int NOT NULL DEFAULT(0),
        AbsentDays int NOT NULL DEFAULT(0),
        IncompleteDays int NOT NULL DEFAULT(0),
        UnpaidLeaveDays int NOT NULL DEFAULT(0),
        LateHours decimal(7,2) NOT NULL DEFAULT(0),
        EarlyLeaveHours decimal(7,2) NOT NULL DEFAULT(0),
        WorkedHours decimal(7,2) NOT NULL DEFAULT(0),
        Status nvarchar(20) NOT NULL DEFAULT(N'UnderReview'),
        BuiltAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        ApprovedAt datetime2 NULL,
        LockedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_EmployeeWeekAttendance_Employee_Week
        ON EmployeeWeekAttendance (EmployeeId, IsoYear, WeekNumber);
END;
""");
    }

    /// <summary>«بناء الأسبوع»: تجميع اليوميات المحللة لمدى الأسبوع لكل موظف — MERGE
    /// يجدد «تحت المراجعة» ويترك المعتمد/المقفل بلا مساس.</summary>
    public static async Task<int> BuildWeekAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int isoYear, int weekNumber)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return 0;
        await EnsureAsync(dbContext);
        await DayAttendanceStore.EnsureAsync(dbContext);

        var (start, end) = WeekRange(isoYear, weekNumber);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            $"""
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
    INNER JOIN Employees e ON e.Id = d.EmployeeId
    WHERE d.WorkDate >= @From AND d.WorkDate <= @To AND d.IsAnalyzed = 1
      AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
    GROUP BY d.EmployeeId
)
MERGE EmployeeWeekAttendance AS target
USING Aggregated AS source
    ON target.EmployeeId = source.EmployeeId AND target.IsoYear = @Year AND target.WeekNumber = @Week
WHEN MATCHED AND target.Status = N'UnderReview' THEN
    UPDATE SET WorkDays = source.WorkDays, PresentDays = source.PresentDays,
               AbsentDays = source.AbsentDays, IncompleteDays = source.IncompleteDays,
               UnpaidLeaveDays = source.UnpaidLeaveDays,
               LateHours = source.LateHours, EarlyLeaveHours = source.EarlyLeaveHours,
               WorkedHours = source.WorkedHours, BuiltAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (EmployeeId, IsoYear, WeekNumber, WeekStart, WeekEnd, WorkDays, PresentDays, AbsentDays,
            IncompleteDays, UnpaidLeaveDays, LateHours, EarlyLeaveHours, WorkedHours, Status)
    VALUES (source.EmployeeId, @Year, @Week, @From, @To, source.WorkDays, source.PresentDays,
            source.AbsentDays, source.IncompleteDays, source.UnpaidLeaveDays, source.LateHours,
            source.EarlyLeaveHours, source.WorkedHours, N'UnderReview');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", start.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", end.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Year", isoYear);
                HrmsDatabase.AddParameter(command, "@Week", weekNumber);
            });

        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            $"""
SELECT COUNT(1)
FROM EmployeeWeekAttendance w
INNER JOIN Employees e ON e.Id = w.EmployeeId
WHERE w.IsoYear = @Year AND w.WeekNumber = @Week
  AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Year", isoYear);
                HrmsDatabase.AddParameter(command, "@Week", weekNumber);
            });
    }

    public static async Task<List<WeekRow>> ListAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int isoYear, int weekNumber)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<WeekRow>();
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT w.*, e.EmployeeNo, e.FullName
FROM EmployeeWeekAttendance w
INNER JOIN Employees e ON e.Id = w.EmployeeId
WHERE w.IsoYear = @Year AND w.WeekNumber = @Week
  AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
ORDER BY e.EmployeeNo;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Year", isoYear);
                HrmsDatabase.AddParameter(command, "@Week", weekNumber);
            },
            reader => new WeekRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                IsoYear = HrmsDatabase.GetInt(reader, "IsoYear"),
                WeekNumber = HrmsDatabase.GetInt(reader, "WeekNumber"),
                WeekStart = HrmsDatabase.GetDateOnly(reader, "WeekStart") ?? default,
                WeekEnd = HrmsDatabase.GetDateOnly(reader, "WeekEnd") ?? default,
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

    public static async Task<(int Approved, int Blocked)> ApproveWithGateAsync(
        ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureAsync(dbContext);
        if (ids.Count == 0 || scope.IsDeniedAll) return (0, 0);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var eligible = new List<int>();
        var scopeFilter = scope.IsUnrestricted ? "1 = 1" : scope.ToSqlPredicate("e.CompanyId");
        foreach (var chunk in ids.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            var rows = await HrmsDatabase.QueryAsync(
                dbContext,
                $"""
SELECT w.Id,
       DATEDIFF(day, w.WeekStart, CASE WHEN w.WeekEnd < @Today THEN w.WeekEnd ELSE @Today END) + 1 AS ExpectedDays,
       (SELECT COUNT(1) FROM DayAttendances d
        WHERE d.EmployeeId = w.EmployeeId AND d.IsAnalyzed = 1
          AND d.WorkDate >= w.WeekStart AND d.WorkDate <= w.WeekEnd) AS AnalyzedDays
FROM EmployeeWeekAttendance w
INNER JOIN Employees e ON e.Id = w.EmployeeId
WHERE w.Id IN ({inList}) AND w.Status = N'UnderReview' AND {scopeFilter};
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Today", today.ToDateTime(TimeOnly.MinValue));
                    for (var i = 0; i < chunk.Length; i++)
                        HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                },
                reader => new
                {
                    Id = HrmsDatabase.GetInt(reader, "Id"),
                    Expected = HrmsDatabase.GetInt(reader, "ExpectedDays"),
                    Analyzed = HrmsDatabase.GetInt(reader, "AnalyzedDays")
                });

            eligible.AddRange(rows.Where(row => row.Expected > 0 && row.Analyzed >= row.Expected).Select(row => row.Id));
        }

        var approved = await Transition(
            dbContext, scope, eligible, from: "UnderReview", to: "Approved", "ApprovedAt = SYSUTCDATETIME()");
        return (approved, ids.Count - eligible.Count);
    }

    public static Task<int> ReopenAsync(ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids) =>
        Transition(dbContext, scope, ids, from: "Approved", to: "UnderReview", "ApprovedAt = NULL");

    public static Task<int> LockAsync(ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids) =>
        Transition(dbContext, scope, ids, from: "Approved", to: "Locked", "LockedAt = SYSUTCDATETIME()");

    /// <summary>فتحٌ استثنائي ومدقّق: مقفل ← معتمد. لا يقبل سبباً فارغاً.</summary>
    public static Task<int> UnlockAsync(
        ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids,
        string? userName, string? ipAddress, string reason) =>
        UnlockLockedAsync(dbContext, scope, ids, userName, ipAddress, reason);

    private static async Task<int> UnlockLockedAsync(
        ApplicationDbContext dbContext, CompanyScope scope, IReadOnlyCollection<int> ids,
        string? userName, string? ipAddress, string reason)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (ids.Count == 0 || scope.IsDeniedAll || string.IsNullOrWhiteSpace(reason)) return 0;
        await EnsureAsync(dbContext);
        var scopeFilter = scope.IsUnrestricted ? "1 = 1" : scope.ToSqlPredicate("e.CompanyId");
        var total = 0;
        foreach (var chunk in ids.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            total += await HrmsDatabase.ScalarAsync<int>(dbContext, $"""
DECLARE @Changed TABLE (Id int NOT NULL);
UPDATE w SET w.Status = N'Approved', w.LockedAt = NULL
OUTPUT inserted.Id INTO @Changed(Id)
FROM EmployeeWeekAttendance w
INNER JOIN Employees e ON e.Id = w.EmployeeId
WHERE w.Id IN ({inList}) AND w.Status = N'Locked' AND {scopeFilter};
IF OBJECT_ID('AuditLogs','U') IS NOT NULL
    INSERT INTO AuditLogs (EntityName, EntityId, Action, OldValues, NewValues, UserName, IpAddress, CreatedAt)
    SELECT N'EmployeeWeekAttendance', CONVERT(nvarchar(80), Id), N'Unlock',
           N'Status=Locked', CONCAT(N'Status=Approved; Reason=', @Reason), @User, @Ip, SYSUTCDATETIME()
    FROM @Changed;
SELECT COUNT(1) FROM @Changed;
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@Reason", reason.Trim());
                HrmsDatabase.AddParameter(command, "@User", (object?)userName ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Ip", (object?)ipAddress ?? DBNull.Value);
                for (var i = 0; i < chunk.Length; i++)
                    HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
            });
        }
        return total;
    }

    private static async Task<int> Transition(ApplicationDbContext dbContext,
        CompanyScope scope, IReadOnlyCollection<int> ids, string from, string to, string extraSet)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureAsync(dbContext);
        if (ids.Count == 0 || scope.IsDeniedAll) return 0;

        // 🛡️ عزل الشركات (Issue 5): نفس حارس الاعتماد الشهري — الانتقال يمسّ فقط
        // صفوف موظفي نطاق المستخدم عبر join على Employees، فلا يُنقَل صفُّ شركةٍ أخرى
        // بمعرّفٍ مزوّر من الـPOST.
        var scopeFilter = scope.IsUnrestricted ? "1 = 1" : scope.ToSqlPredicate("e.CompanyId");

        var total = 0;
        foreach (var chunk in ids.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            total += await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                $"""
UPDATE w SET w.Status = @To, {extraSet}
FROM EmployeeWeekAttendance w
INNER JOIN Employees e ON e.Id = w.EmployeeId
WHERE w.Id IN ({inList}) AND w.Status = @FromStatus AND {scopeFilter};
SELECT @@ROWCOUNT;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@To", to);
                    HrmsDatabase.AddParameter(command, "@FromStatus", from);
                    for (var i = 0; i < chunk.Length; i++)
                        HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                });
        }
        return total;
    }
}
