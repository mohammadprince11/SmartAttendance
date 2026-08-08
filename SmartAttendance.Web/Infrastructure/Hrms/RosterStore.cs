using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// جدولة مناوبات العمل / الروستر (نمط كيان — الصفحة الثالثة بقسم «حضور الموظفين»):
/// شبكة موظف×يوم، كل خلية إما مناوبة محددة أو «يوم عطلة» أو «يوم راحة». الأيام غير
/// المجدولة تسقط للمناوبة الافتراضية (fallback نمط كيان). المحلل يقدّم الروستر على
/// التعيين الدائم (وتحته التجاوز المؤقت). الشهر يُنشر (Publish) لتثبيت الجدول.
/// </summary>
public static class RosterStore
{
    /// <summary>خيارات خلية الروستر: مناوبة، أو يوم عطلة/راحة (تجاوز نوع اليوم).</summary>
    public const string CellShift = "Shift";
    public const string CellWeekend = "Weekend";
    public const string CellRest = "Rest";

    public sealed class Cell
    {
        public int EmployeeId { get; set; }
        public DateOnly WorkDate { get; set; }
        public string CellType { get; set; } = CellShift;
        public int? ShiftTypeId { get; set; }
    }

    // ===== الجدولة السريعة (خلاصة دراسة Deputy/When I Work/7shifts):
    // «لا تبنِ الجدول من الصفر كل مرة» — إما تكرار أسبوع مرسوم أو نسخ فترة سابقة. =====

    /// <summary>
    /// مصدر خلية اليوم عند «تعبئة الشهر من الأسبوع الأول»: اليوم المقابل بأول 7 أيام
    /// من الشهر (نفس الإزاحة % 7) — يحافظ على نمط أيام الأسبوع تماماً. نقية.
    /// </summary>
    public static DateOnly FirstWeekSource(DateOnly monthStart, DateOnly day) =>
        monthStart.AddDays((day.DayNumber - monthStart.DayNumber) % 7);

    /// <summary>
    /// مصدر خلية اليوم عند «نسخ الشهر الماضي»: قبل 28 يوماً بالضبط (4 أسابيع) —
    /// يضمن نفس يوم الأسبوع (جمعة⟵جمعة) فلا تنكسر العطل. نقية.
    /// </summary>
    public static DateOnly PreviousMonthAlignedSource(DateOnly day) => day.AddDays(-28);

    /// <summary>نسخ خلايا كل الموظفين من يوم مصدر إلى يوم هدف (upsert).</summary>
    private static Task<int> CopyDayAsync(ApplicationDbContext dbContext, DateOnly source, DateOnly target) =>
        HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
MERGE RosterCells AS t
USING (SELECT EmployeeId, CellType, ShiftTypeId FROM RosterCells WHERE WorkDate = @Src) AS s
ON t.EmployeeId = s.EmployeeId AND t.WorkDate = @Dst
WHEN MATCHED THEN
    UPDATE SET CellType = s.CellType, ShiftTypeId = s.ShiftTypeId, UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (EmployeeId, WorkDate, CellType, ShiftTypeId)
    VALUES (s.EmployeeId, @Dst, s.CellType, s.ShiftTypeId);
SELECT @@ROWCOUNT;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Src", source);
                HrmsDatabase.AddParameter(command, "@Dst", target);
            });

    /// <summary>
    /// «ارسم أسبوعاً واحداً والباقي علينا»: يكرر أول 7 أيام من الشهر على بقية أيامه
    /// لكل الموظفين الذين لهم خلايا بالأسبوع الأول. يرجع عدد الخلايا المكتوبة.
    /// </summary>
    public static async Task<int> FillMonthFromFirstWeekAsync(ApplicationDbContext dbContext, int year, int month)
    {
        await EnsureAsync(dbContext);
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var total = 0;
        for (var day = start.AddDays(7); day <= end; day = day.AddDays(1))
            total += await CopyDayAsync(dbContext, FirstWeekSource(start, day), day);
        return total;
    }

    /// <summary>
    /// «نسخ الشهر الماضي» بمحاذاة أيام الأسبوع (كل يوم يأخذ خلية اليوم قبل 4 أسابيع)
    /// لكل الموظفين. يرجع عدد الخلايا المكتوبة.
    /// </summary>
    public static async Task<int> CopyFromPreviousMonthAsync(ApplicationDbContext dbContext, int year, int month)
    {
        await EnsureAsync(dbContext);
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var total = 0;
        for (var day = start; day <= end; day = day.AddDays(1))
            total += await CopyDayAsync(dbContext, PreviousMonthAlignedSource(day), day);
        return total;
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('RosterCells', 'U') IS NULL
BEGIN
    CREATE TABLE RosterCells
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        WorkDate date NOT NULL,
        CellType nvarchar(10) NOT NULL DEFAULT(N'Shift'),
        ShiftTypeId int NULL,
        UpdatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_RosterCells_Emp_Date ON RosterCells (EmployeeId, WorkDate);
END;

IF OBJECT_ID('RosterMonths', 'U') IS NULL
BEGIN
    CREATE TABLE RosterMonths
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Year] int NOT NULL,
        [Month] int NOT NULL,
        PublishedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_RosterMonths_Year_Month ON RosterMonths ([Year], [Month]);
END;
""");
    }

    /// <summary>خلايا الشهر لموظفين محددين (فارغ = الكل): مفتاح (موظف×يوم).</summary>
    public static async Task<Dictionary<(int EmployeeId, DateOnly Date), Cell>> GetCellsAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, int year, int month)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new Dictionary<(int, DateOnly), Cell>();

        await EnsureAsync(dbContext);
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var cells = await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT c.EmployeeId, c.WorkDate, c.CellType, c.ShiftTypeId FROM RosterCells c INNER JOIN Employees e ON e.Id = c.EmployeeId WHERE c.WorkDate >= @From AND c.WorkDate <= @To AND {Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            },
            reader => new Cell
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                WorkDate = HrmsDatabase.GetDateOnly(reader, "WorkDate") ?? default,
                CellType = HrmsDatabase.GetString(reader, "CellType") is { Length: > 0 } t ? t : CellShift,
                ShiftTypeId = HrmsDatabase.GetNullableInt(reader, "ShiftTypeId")
            });

        return cells.ToDictionary(c => (c.EmployeeId, c.WorkDate), c => c);
    }

    /// <summary>حفظ خلايا (upsert). قيمة فارغة (CellType فارغ) ⇒ حذف الخلية (رجوع للافتراضية).</summary>
    public static async Task SaveCellsAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, IEnumerable<Cell> cells)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return;

        await EnsureAsync(dbContext);
        foreach (var cell in cells)
        {
            // كل خلية تُملَك بموظفها — معرّفٌ خارج النطاق يُتخطّى بصمت (سردُ
            // الشاشة معزول أصلاً فلا يصل هنا إلا بتلاعب بالطلب).
            if (!await Security.EmployeeCompanyGuard.CanAccessEmployeeAsync(dbContext, cell.EmployeeId, scope))
                continue;

            var c = cell;
            if (string.IsNullOrWhiteSpace(c.CellType) || (c.CellType == CellShift && c.ShiftTypeId is null or 0))
            {
                await HrmsDatabase.ExecuteAsync(
                    dbContext,
                    "DELETE FROM RosterCells WHERE EmployeeId = @Emp AND WorkDate = @Date;",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Emp", c.EmployeeId);
                        HrmsDatabase.AddParameter(command, "@Date", c.WorkDate.ToDateTime(TimeOnly.MinValue));
                    });
                continue;
            }

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
MERGE RosterCells AS t
USING (SELECT @Emp AS EmployeeId, @Date AS WorkDate) AS s
    ON t.EmployeeId = s.EmployeeId AND t.WorkDate = s.WorkDate
WHEN MATCHED THEN UPDATE SET CellType = @Type, ShiftTypeId = @Shift, UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (EmployeeId, WorkDate, CellType, ShiftTypeId)
    VALUES (@Emp, @Date, @Type, @Shift);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Emp", c.EmployeeId);
                    HrmsDatabase.AddParameter(command, "@Date", c.WorkDate.ToDateTime(TimeOnly.MinValue));
                    HrmsDatabase.AddParameter(command, "@Type", c.CellType);
                    HrmsDatabase.AddParameter(command, "@Shift",
                        c.CellType == CellShift ? (object?)c.ShiftTypeId ?? DBNull.Value : DBNull.Value);
                });
        }
    }

    public static async Task PublishAsync(ApplicationDbContext dbContext, int year, int month)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
MERGE RosterMonths AS t
USING (SELECT @Y AS [Year], @M AS [Month]) AS s
    ON t.[Year] = s.[Year] AND t.[Month] = s.[Month]
WHEN MATCHED THEN UPDATE SET PublishedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT ([Year], [Month], PublishedAt) VALUES (@Y, @M, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
            });
    }

    public static async Task<DateTime?> PublishedAtAsync(ApplicationDbContext dbContext, int year, int month)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT PublishedAt FROM RosterMonths WHERE [Year] = @Y AND [Month] = @M;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
            },
            reader => HrmsDatabase.GetDateTime(reader, "PublishedAt"));
        return rows.FirstOrDefault();
    }

    /// <summary>
    /// خريطة الروستر المتقاطعة مع الشهر: (موظف×يوم) ← (مناوبة؟، تجاوز نوع يوم؟).
    /// يستخدمها المحلل بأولوية تحت التجاوز المؤقت وفوق التعيين الدائم.
    /// </summary>
    public static async Task<Dictionary<(int EmployeeId, DateOnly Date), (int? ShiftId, string? ForcedDayKind)>> MapAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, int year, int month)
    {
        var cells = await GetCellsAsync(dbContext, scope, year, month);
        var map = new Dictionary<(int, DateOnly), (int?, string?)>();
        foreach (var ((emp, date), c) in cells)
        {
            map[(emp, date)] = c.CellType switch
            {
                CellWeekend => (null, "Weekend"),
                CellRest => (null, "Rest"),
                _ => (c.ShiftTypeId, null)
            };
        }
        return map;
    }
}
