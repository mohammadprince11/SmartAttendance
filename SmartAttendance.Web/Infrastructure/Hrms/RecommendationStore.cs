using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// الإجراءات المقترحة (نمط كيان — قسمي 10 و13 بدراسة الحضور): مخرجات محرك القواعد.
/// «تحليل واسترجاع الاقتراحات» يقيّم القواعد النشطة على يوميات الشهر فيولّد اقتراحاً
/// لكل (موظف×يوم×قاعدة) — فريد، فلا تتكرر الاقتراحات بإعادة التحليل ولا يُعاد
/// توليد المتجاهل. القواعد التلقائية تُعتمد فوراً. اعتماد اقتراح «مخالفة» ينشئ
/// EmployeeViolationCase مرتبطة بمصدر «محرك الحضور».
/// </summary>
public static class RecommendationStore
{
    public sealed class Recommendation
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public DateOnly WorkDate { get; set; }
        public int RuleId { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string ActionType { get; set; } = "Violation";
        public string ActionText { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";   // Pending | Approved | Ignored | Auto
        public int? ViolationCaseId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public static string StatusLabel(string status) => status switch
    {
        "Pending" => "معلق",
        "Approved" => "معتمد",
        "Ignored" => "متجاهل",
        "Auto" => "تلقائي",
        _ => status
    };

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('AttendanceRecommendations', 'U') IS NULL
BEGIN
    CREATE TABLE AttendanceRecommendations
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        WorkDate date NOT NULL,
        RuleId int NOT NULL,
        RuleName nvarchar(200) NOT NULL DEFAULT(N''),
        Summary nvarchar(500) NOT NULL DEFAULT(N''),
        ActionType nvarchar(20) NOT NULL DEFAULT(N'Violation'),
        ActionText nvarchar(300) NOT NULL DEFAULT(N''),
        Status nvarchar(20) NOT NULL DEFAULT(N'Pending'),
        ViolationCaseId int NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        DecidedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_AttendanceRecommendations_Day_Rule
        ON AttendanceRecommendations (EmployeeId, WorkDate, RuleId);
END;
""");
    }

    /// <summary>
    /// «تحليل واسترجاع الاقتراحات»: تقييم القواعد النشطة على يوميات الشهر.
    /// </summary>
    /// <returns>(مقترحة جديدة، منها تلقائية معتمدة)</returns>
    public static async Task<(int Created, int Auto)> AnalyzeMonthAsync(
        ApplicationDbContext dbContext, int year, int month)
    {
        await EnsureAsync(dbContext);

        var rules = (await ShiftRuleStore.ListAsync(dbContext)).Where(r => r.IsActive).ToList();
        if (rules.Count == 0) return (0, 0);

        var days = await DayAttendanceStore.ListAsync(dbContext, year, month, null);
        var shifts = (await ShiftTypeStore.ListAsync(dbContext))
            .ToDictionary(s => s.Id, s => s.Days.ToDictionary(d => d.DayIndex));

        // المفاتيح الموجودة مسبقاً (بأي حالة) — لمنع التكرار وإبقاء قرارات الفرز
        var existing = new HashSet<(int, DateOnly, int)>(
            (await ListAsync(dbContext, year, month, null))
                .Select(r => (r.EmployeeId, r.WorkDate, r.RuleId)));

        // المعلقة تُجمع لإدخال جماعي واحد (SqlBulkCopy)؛ التلقائية تُنفذ صفاً-بصف
        // لأن كلاً منها ينشئ قضية مخالفة مرتبطة (عادةً قليلة).
        var table = new DataTable();
        table.Columns.Add("EmployeeId", typeof(int));
        table.Columns.Add("WorkDate", typeof(DateTime));
        table.Columns.Add("RuleId", typeof(int));
        table.Columns.Add("RuleName", typeof(string));
        table.Columns.Add("Summary", typeof(string));
        table.Columns.Add("ActionType", typeof(string));
        table.Columns.Add("ActionText", typeof(string));
        table.Columns.Add("Status", typeof(string));

        int created = 0, auto = 0;
        foreach (var day in days)
        {
            ShiftTypeStore.ShiftDay? shiftDay = null;
            if (day.ShiftTypeId.HasValue && shifts.TryGetValue(day.ShiftTypeId.Value, out var byIndex))
                byIndex.TryGetValue(DayAttendanceStore.ToDayIndex(day.WorkDate), out shiftDay);

            foreach (var rule in rules)
            {
                if (!ShiftRuleStore.AppliesTo(rule, day)) continue;
                if (existing.Contains((day.EmployeeId, day.WorkDate, rule.Id))) continue;

                var summary = ShiftRuleStore.Evaluate(rule, day, shiftDay);
                if (summary == null) continue;

                if (rule.IsAutomatic)
                {
                    int? violationId = rule.ActionType == "Violation"
                        ? await CreateViolationAsync(dbContext, day.EmployeeId, day.WorkDate,
                            rule.ActionText, $"{rule.Name}: {summary}")
                        : null;

                    await HrmsDatabase.ExecuteAsync(
                        dbContext,
                        """
INSERT INTO AttendanceRecommendations
    (EmployeeId, WorkDate, RuleId, RuleName, Summary, ActionType, ActionText, Status, ViolationCaseId, DecidedAt)
VALUES
    (@Employee, @Date, @Rule, @RuleName, @Summary, @ActionType, @ActionText, N'Auto', @Violation, SYSUTCDATETIME());
""",
                        command =>
                        {
                            HrmsDatabase.AddParameter(command, "@Employee", day.EmployeeId);
                            HrmsDatabase.AddParameter(command, "@Date", day.WorkDate.ToDateTime(TimeOnly.MinValue));
                            HrmsDatabase.AddParameter(command, "@Rule", rule.Id);
                            HrmsDatabase.AddParameter(command, "@RuleName", rule.Name);
                            HrmsDatabase.AddParameter(command, "@Summary", summary);
                            HrmsDatabase.AddParameter(command, "@ActionType", rule.ActionType);
                            HrmsDatabase.AddParameter(command, "@ActionText", rule.ActionText);
                            HrmsDatabase.AddParameter(command, "@Violation", (object?)violationId ?? DBNull.Value);
                        });
                    auto++;
                }
                else
                {
                    table.Rows.Add(day.EmployeeId, day.WorkDate.ToDateTime(TimeOnly.MinValue), rule.Id,
                        rule.Name, summary, rule.ActionType, rule.ActionText, "Pending");
                }

                existing.Add((day.EmployeeId, day.WorkDate, rule.Id));
                created++;
            }
        }

        if (table.Rows.Count > 0)
        {
            var connection = (SqlConnection)dbContext.Database.GetDbConnection();
            var wasClosed = connection.State != ConnectionState.Open;
            if (wasClosed) await connection.OpenAsync();
            try
            {
                using var bulk = new SqlBulkCopy(connection);
                bulk.DestinationTableName = "AttendanceRecommendations";
                bulk.BulkCopyTimeout = 120;
                foreach (DataColumn column in table.Columns)
                {
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                }
                await bulk.WriteToServerAsync(table);
            }
            finally
            {
                if (wasClosed) await connection.CloseAsync();
            }
        }
        return (created, auto);
    }

    public static async Task<List<Recommendation>> ListAsync(
        ApplicationDbContext dbContext, int year, int month, string? status)
    {
        await EnsureAsync(dbContext);

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT r.*, e.EmployeeNo, e.FullName
FROM AttendanceRecommendations r
INNER JOIN Employees e ON e.Id = r.EmployeeId
WHERE r.WorkDate >= @From AND r.WorkDate <= @To
ORDER BY r.WorkDate DESC, e.EmployeeNo;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
            },
            reader => new Recommendation
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                WorkDate = HrmsDatabase.GetDateOnly(reader, "WorkDate") ?? default,
                RuleId = HrmsDatabase.GetInt(reader, "RuleId"),
                RuleName = HrmsDatabase.GetString(reader, "RuleName"),
                Summary = HrmsDatabase.GetString(reader, "Summary"),
                ActionType = HrmsDatabase.GetString(reader, "ActionType"),
                ActionText = HrmsDatabase.GetString(reader, "ActionText"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                ViolationCaseId = HrmsDatabase.GetNullableInt(reader, "ViolationCaseId"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default
            });

        return string.IsNullOrWhiteSpace(status)
            ? rows
            : rows.Where(r => r.Status == status).ToList();
    }

    /// <summary>اعتماد اقتراح معلق: مخالفة ← إنشاء قضية مخالفة مرتبطة.</summary>
    public static async Task<bool> ApproveAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);

        var rec = (await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT TOP 1 r.*, e.EmployeeNo, e.FullName FROM AttendanceRecommendations r INNER JOIN Employees e ON e.Id = r.EmployeeId WHERE r.Id = @Id AND r.Status = N'Pending';",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            reader => new Recommendation
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                WorkDate = HrmsDatabase.GetDateOnly(reader, "WorkDate") ?? default,
                RuleName = HrmsDatabase.GetString(reader, "RuleName"),
                Summary = HrmsDatabase.GetString(reader, "Summary"),
                ActionType = HrmsDatabase.GetString(reader, "ActionType"),
                ActionText = HrmsDatabase.GetString(reader, "ActionText")
            })).FirstOrDefault();

        if (rec == null) return false;

        int? violationId = null;
        if (rec.ActionType == "Violation")
        {
            violationId = await CreateViolationAsync(dbContext, rec.EmployeeId, rec.WorkDate,
                rec.ActionText, $"{rec.RuleName}: {rec.Summary}");
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE AttendanceRecommendations
SET Status = N'Approved', ViolationCaseId = @Violation, DecidedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Violation", (object?)violationId ?? DBNull.Value);
            });
        return true;
    }

    public static async Task IgnoreAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE AttendanceRecommendations SET Status = N'Ignored', DecidedAt = SYSUTCDATETIME() WHERE Id = @Id AND Status = N'Pending';",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>إنشاء قضية مخالفة من الحضور — بمرجع AV مستقل ومصدر «محرك الحضور».</summary>
    private static async Task<int> CreateViolationAsync(ApplicationDbContext dbContext,
        int employeeId, DateOnly eventDate, string violationTitle, string notes)
    {
        await ViolationCaseSchema.EnsureAsync(dbContext);

        var prefix = $"AV{DateTime.Today:yy}-";
        var count = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "SELECT COUNT(1) FROM EmployeeViolationCases WHERE ReferenceNo LIKE @Prefix;",
            command => HrmsDatabase.AddParameter(command, "@Prefix", prefix + "%"));

        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
INSERT INTO EmployeeViolationCases
    (ReferenceNo, EmployeeId, ViolationCategory, ViolationTitle, EventDate,
     Source, ActionStatus, Status, ProposedAction, Notes, CreatedAt, IsDeleted)
VALUES
    (@ReferenceNo, @EmployeeId, N'الحضور والانصراف', @Title, @EventDate,
     N'محرك الحضور', N'بانتظار الإجراء', N'مسودة', @Title, @Notes, SYSUTCDATETIME(), 0);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@ReferenceNo", $"{prefix}{count + 1:0000}");
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Title", violationTitle);
                HrmsDatabase.AddParameter(command, "@EventDate", eventDate.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Notes", notes);
            });
    }
}
