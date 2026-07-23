using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تعديل مناوبات مؤقت (نمط كيان — الصفحة الثانية بقسم «حضور الموظفين»): إسناد
/// مناوبة بديلة لموظف/موظفين لفترة محددة (من–إلى) دون تغيير تعيينه الدائم. المحلل
/// يعطي التجاوز الأولوية على التعيين اليدوي والاستحقاق والافتراضية لأيام الفترة.
/// </summary>
public static class ShiftOverrideStore
{
    public sealed class OverrideRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string? OriginalShiftName { get; set; }     // المناوبة الأصلية (التعيين اليدوي)
        public string? OriginalShiftColor { get; set; }
        public int NewShiftTypeId { get; set; }
        public string NewShiftName { get; set; } = string.Empty;
        public string? NewShiftColor { get; set; }
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        public string Source { get; set; } = "يدوي";
        public DateTime CreatedAt { get; set; }
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('ShiftOverrides', 'U') IS NULL
BEGIN
    CREATE TABLE ShiftOverrides
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        ShiftTypeId int NOT NULL,
        FromDate date NOT NULL,
        ToDate date NOT NULL,
        Source nvarchar(30) NOT NULL DEFAULT(N'يدوي'),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_ShiftOverrides_Employee_Range ON ShiftOverrides (EmployeeId, FromDate, ToDate);
END;
""");
    }

    public static async Task<List<OverrideRow>> ListAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT o.Id, o.EmployeeId, e.EmployeeNo, e.FullName,
       os.Name AS OriginalShiftName, os.ColorHex AS OriginalShiftColor,
       o.ShiftTypeId, ns.Name AS NewShiftName, ns.ColorHex AS NewShiftColor,
       o.FromDate, o.ToDate, o.Source, o.CreatedAt
FROM ShiftOverrides o
INNER JOIN Employees e ON e.Id = o.EmployeeId
LEFT JOIN EmployeeShiftTypes est ON est.EmployeeId = o.EmployeeId
LEFT JOIN ShiftTypes os ON os.Id = est.ShiftTypeId
LEFT JOIN ShiftTypes ns ON ns.Id = o.ShiftTypeId
ORDER BY o.FromDate DESC, e.EmployeeNo;
""",
            command => { },
            reader => new OverrideRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                OriginalShiftName = HrmsDatabase.GetString(reader, "OriginalShiftName") is { Length: > 0 } os ? os : null,
                OriginalShiftColor = HrmsDatabase.GetString(reader, "OriginalShiftColor") is { Length: > 0 } oc ? oc : null,
                NewShiftTypeId = HrmsDatabase.GetInt(reader, "ShiftTypeId"),
                NewShiftName = HrmsDatabase.GetString(reader, "NewShiftName"),
                NewShiftColor = HrmsDatabase.GetString(reader, "NewShiftColor") is { Length: > 0 } nc ? nc : null,
                FromDate = HrmsDatabase.GetDateOnly(reader, "FromDate") ?? default,
                ToDate = HrmsDatabase.GetDateOnly(reader, "ToDate") ?? default,
                Source = HrmsDatabase.GetString(reader, "Source") is { Length: > 0 } s ? s : "يدوي",
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default
            });
    }

    /// <summary>إنشاء تجاوز مؤقت لموظفين لفترة محددة.</summary>
    public static async Task<int> CreateAsync(ApplicationDbContext dbContext,
        IReadOnlyCollection<int> employeeIds, int shiftTypeId, DateOnly from, DateOnly to)
    {
        await EnsureAsync(dbContext);
        if (employeeIds.Count == 0 || shiftTypeId <= 0) return 0;
        if (to < from) (from, to) = (to, from);

        foreach (var employeeId in employeeIds)
        {
            var id = employeeId;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO ShiftOverrides (EmployeeId, ShiftTypeId, FromDate, ToDate, Source)
VALUES (@Emp, @Shift, @From, @To, N'يدوي');
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Emp", id);
                    HrmsDatabase.AddParameter(command, "@Shift", shiftTypeId);
                    HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                    HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
                });
        }
        return employeeIds.Count;
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, IReadOnlyCollection<int> ids)
    {
        await EnsureAsync(dbContext);
        if (ids.Count == 0) return;
        foreach (var chunk in ids.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                $"DELETE FROM ShiftOverrides WHERE Id IN ({inList});",
                command =>
                {
                    for (var i = 0; i < chunk.Length; i++)
                        HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                });
        }
    }

    /// <summary>
    /// خريطة التجاوزات المتقاطعة مع شهر [monthStart..monthEnd]: (موظف×يوم) ← مناوبة.
    /// عند تداخل تجاوزين لنفس اليوم يفوز الأحدث إنشاءً. يستخدمها المحلل بأعلى أولوية.
    /// </summary>
    public static async Task<Dictionary<(int EmployeeId, DateOnly Date), int>> MapAsync(
        ApplicationDbContext dbContext, DateOnly monthStart, DateOnly monthEnd)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT EmployeeId, ShiftTypeId, FromDate, ToDate
FROM ShiftOverrides
WHERE FromDate <= @To AND ToDate >= @From
ORDER BY CreatedAt;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", monthStart.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", monthEnd.ToDateTime(TimeOnly.MinValue));
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                ShiftTypeId = HrmsDatabase.GetInt(reader, "ShiftTypeId"),
                FromDate = HrmsDatabase.GetDateOnly(reader, "FromDate") ?? default,
                ToDate = HrmsDatabase.GetDateOnly(reader, "ToDate") ?? default
            });

        var map = new Dictionary<(int, DateOnly), int>();
        foreach (var o in rows)  // مرتبة بالإنشاء ⇒ الأحدث يكتب فوق الأقدم
        {
            var start = o.FromDate < monthStart ? monthStart : o.FromDate;
            var end = o.ToDate > monthEnd ? monthEnd : o.ToDate;
            for (var d = start; d <= end; d = d.AddDays(1))
                map[(o.EmployeeId, d)] = o.ShiftTypeId;
        }
        return map;
    }
}
