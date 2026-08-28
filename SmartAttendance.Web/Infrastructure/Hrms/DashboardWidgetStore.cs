using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// ويدجتات لوحة التحكم القابلة للبناء (نمط لوحة أشخاص كيان + «إضافة ويدجت» بـZenHR):
/// المستخدم يختار «شنو يظهر» (مقياس من كتالوج) و«شلون يظهر» (رقم/أشرطة/دونات/
/// أعمدة/جدول) ويرتب ويخفي. التعريفات بجدول DashboardWidgets والتنفيذ هنا —
/// كل مقياس دالة تعيد صفوف (اسم/عدد) أو رقماً واحداً، مقيدة بشركة محددة.
/// </summary>
public static class DashboardWidgetStore
{
    public sealed class Widget
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string Title { get; set; } = string.Empty;   // فارغ = تسمية المقياس
        public string Metric { get; set; } = string.Empty;
        public string ChartKind { get; set; } = "Number";   // Number | HBars | Donut | Columns | Table
        public int SortOrder { get; set; }
        public bool IsVisible { get; set; } = true;

        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? MetricLabel(Metric) : Title;
    }

    /// <summary>نتيجة تنفيذ ويدجت: رقم واحد (للعدادات) أو صفوف توزيع.</summary>
    public sealed class WidgetData
    {
        public int Single { get; set; }
        public List<(string Name, int Total)> Rows { get; set; } = new();
    }

    /// <summary>كتالوج المقاييس: (المفتاح ← التسمية، هل هو عدّاد رقم واحد).</summary>
    public sealed record MetricDefinition(
        string Key, string Label, bool IsCounter, string Unit,
        string Source, string DrillPath);

    public static readonly MetricDefinition[] Metrics =
    {
        new("ActiveEmployees", "الموظفون النشطون", true, "موظف", "Employees.IsActive", "/Employees?activeOnly=true"),
        new("InactiveEmployees", "موقوفون / غير فعالين", true, "موظف", "Employees.IsActive", "/Employees?activeOnly=false"),
        new("NewHiresMonth", "موظف جديد هذا الشهر", true, "موظف", "Employees.HireDate", "/PeopleReports?ReportId=1"),
        new("TodayPresent", "حاضرون اليوم", true, "موظف", "DayAttendances.Status=Present", "/DayAttendance?status=Present"),
        new("TodayLate", "متأخرون اليوم", true, "موظف", "DayAttendances.Status=Late", "/DayAttendance?status=Late"),
        new("TodayAbsent", "غائبون اليوم", true, "موظف", "DayAttendances.Status=Absent", "/DayAttendance?status=Absent"),
        new("PendingRequests", "طلبات معلقة", true, "طلب", "SelfServiceRequests.Status=Pending", "/Requests?status=Pending"),
        new("ExpiringContracts60", "عقود تنتهي خلال 60 يوماً", true, "عقد", "Employees.ContractEndDate", "/Employees/Contracts"),
        new("ByBranch", "الموظفون حسب الفرع", false, "موظف", "Employees.BranchId", "/Dashboard/EmployeesDistribution?groupBy=branch"),
        new("ByDepartment", "الموظفون حسب القسم", false, "موظف", "Employees.DepartmentId", "/Dashboard/EmployeesDistribution?groupBy=department"),
        new("ByGender", "حسب الجنس", false, "موظف", "Employees.Gender", "/Dashboard/EmployeesDistribution?groupBy=gender"),
        new("ByNationality", "حسب الجنسية", false, "موظف", "Employees.Nationality", "/Dashboard/EmployeesDistribution?groupBy=nationality"),
        new("ByMaritalStatus", "الحالة الزوجية", false, "موظف", "Employees.MaritalStatus", "/Dashboard/EmployeesDistribution?groupBy=marital"),
        new("ByContractType", "حسب نوع العقد", false, "موظف", "Employees.ContractType", "/Dashboard/EmployeesDistribution?groupBy=contract"),
        new("ByAge", "إحصائيات الأعمار", false, "موظف", "Employees.BirthDate", "/Dashboard/EmployeesDistribution?groupBy=age"),
        new("ByServiceYears", "إحصائيات مدة الخدمة", false, "موظف", "Employees.HireDate", "/Dashboard/EmployeesDistribution?groupBy=service"),
        new("TodayStatus", "حالة الحضور اليوم", false, "موظف", "DayAttendances.Status", "/DayAttendance"),
        new("RequestsByStatus", "الطلبات حسب الحالة", false, "طلب", "SelfServiceRequests.Status", "/Requests")
    };

    public static string MetricLabel(string key) =>
        Metrics.FirstOrDefault(m => m.Key == key)?.Label ?? key;

    public static bool IsCounterMetric(string key) =>
        Metrics.FirstOrDefault(m => m.Key == key)?.IsCounter == true;

    public static readonly (string Key, string Label)[] ChartKinds =
    {
        ("Number", "رقم"),
        ("HBars", "أشرطة أفقية"),
        ("Columns", "أعمدة"),
        ("Donut", "دونات"),
        ("Table", "جدول")
    };

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('DashboardWidgets', 'U') IS NULL
BEGIN
    CREATE TABLE DashboardWidgets
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NULL,
        Title nvarchar(150) NOT NULL DEFAULT(N''),
        Metric nvarchar(40) NOT NULL,
        ChartKind nvarchar(20) NOT NULL DEFAULT(N'Number'),
        SortOrder int NOT NULL DEFAULT(0),
        IsVisible bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );

    -- البذور الافتراضية: تحاكي لوحة أشخاص كيان (عدادات ثم رسوم بشبكة بطاقات)
    INSERT INTO DashboardWidgets (Metric, ChartKind, SortOrder) VALUES
        (N'ActiveEmployees', N'Number', 1),
        (N'NewHiresMonth', N'Number', 2),
        (N'TodayPresent', N'Number', 3),
        (N'TodayLate', N'Number', 4),
        (N'TodayAbsent', N'Number', 5),
        (N'PendingRequests', N'Number', 6),
        (N'ByBranch', N'HBars', 10),
        (N'ByDepartment', N'HBars', 11),
        (N'TodayStatus', N'Donut', 12),
        (N'ByMaritalStatus', N'Donut', 13),
        (N'ByNationality', N'Columns', 14),
        (N'ByAge', N'Columns', 15),
        (N'ByServiceYears', N'Columns', 16),
        (N'ByGender', N'Donut', 17),
        (N'ByContractType', N'Donut', 18);
END;
""");
    }

    public static async Task<List<Widget>> ListAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int companyId)
    {
        if (!scope.Allows(companyId)) throw new UnauthorizedAccessException();
        await EnsureAsync(dbContext);
        await EnsureCompanyRowsAsync(dbContext, companyId);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM DashboardWidgets WHERE CompanyId=@CompanyId ORDER BY SortOrder, Id;",
            command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            reader => new Widget
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Metric = HrmsDatabase.GetString(reader, "Metric"),
                ChartKind = HrmsDatabase.GetString(reader, "ChartKind") is { Length: > 0 } k ? k : "Number",
                SortOrder = HrmsDatabase.GetInt(reader, "SortOrder"),
                IsVisible = HrmsDatabase.GetBool(reader, "IsVisible")
            });
    }

    public static async Task AddAsync(ApplicationDbContext dbContext, CompanyScope scope, Widget widget)
    {
        if (!scope.Allows(widget.CompanyId)) throw new UnauthorizedAccessException();
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
INSERT INTO DashboardWidgets (CompanyId,Title,Metric,ChartKind,SortOrder,IsVisible)
SELECT @CompanyId,@Title,@Metric,@Kind,ISNULL(MAX(SortOrder),0)+1,1 FROM DashboardWidgets WHERE CompanyId=@CompanyId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Title", widget.Title);
                HrmsDatabase.AddParameter(command, "@CompanyId", widget.CompanyId);
                HrmsDatabase.AddParameter(command, "@Metric", widget.Metric);
                HrmsDatabase.AddParameter(command, "@Kind", widget.ChartKind);
            });
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, CompanyScope scope, int companyId, int id)
    {
        if (!scope.Allows(companyId)) throw new UnauthorizedAccessException();
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM DashboardWidgets WHERE Id=@Id AND CompanyId=@CompanyId;",
            command => { HrmsDatabase.AddParameter(command,"@Id",id); HrmsDatabase.AddParameter(command,"@CompanyId",companyId); });
    }

    /// <summary>حفظ التخطيط: الترتيب حسب تسلسل المعرفات الممررة + حالة الإظهار.</summary>
    public static async Task SaveLayoutAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int companyId,
        IReadOnlyList<int> orderedIds, IReadOnlySet<int> visibleIds)
    {
        if (!scope.Allows(companyId)) throw new UnauthorizedAccessException();
        await EnsureAsync(dbContext);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            var id = orderedIds[index];
            var order = index + 1;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "UPDATE DashboardWidgets SET SortOrder=@Order,IsVisible=@Visible WHERE Id=@Id AND CompanyId=@CompanyId;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", id);
                    HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                    HrmsDatabase.AddParameter(command, "@Order", order);
                    HrmsDatabase.AddParameter(command, "@Visible", visibleIds.Contains(id) ? 1 : 0);
                });
        }
    }

    private static Task EnsureCompanyRowsAsync(ApplicationDbContext dbContext, int companyId) =>
        HrmsDatabase.ExecuteAsync(dbContext, """
IF NOT EXISTS(SELECT 1 FROM DashboardWidgets WITH (UPDLOCK,HOLDLOCK) WHERE CompanyId=@CompanyId)
BEGIN
 INSERT INTO DashboardWidgets(CompanyId,Title,Metric,ChartKind,SortOrder,IsVisible,CreatedAt)
 SELECT @CompanyId,Title,Metric,ChartKind,SortOrder,IsVisible,SYSUTCDATETIME()
 FROM DashboardWidgets WHERE CompanyId IS NULL;
END;
""", command => HrmsDatabase.AddParameter(command,"@CompanyId",companyId));

    /// <summary>تنفيذ مقياس ويدجت مقيداً بشركة (فروعها) — يعيد رقماً أو صفوف توزيع.</summary>
    public static async Task<WidgetData> ExecuteAsync(
        ApplicationDbContext dbContext, string metric, int companyId)
    {
        var data = new WidgetData();
        var today = DateOnly.FromDateTime(DateTime.Today);

        // استعلام الموظفين الأساس مقيداً بالشركة
        IQueryable<SmartAttendance.Domain.Entities.Employee> Employees() =>
            dbContext.Employees.AsNoTracking()
                .Where(e => !e.IsDeleted && e.Branch != null && e.Branch.CompanyId == companyId);

        switch (metric)
        {
            case "ActiveEmployees":
                data.Single = await Employees().CountAsync(e => e.IsActive);
                break;

            case "InactiveEmployees":
                data.Single = await Employees().CountAsync(e => !e.IsActive);
                break;

            case "NewHiresMonth":
            {
                var monthStart = new DateOnly(today.Year, today.Month, 1);
                data.Single = await Employees().CountAsync(e => e.HireDate >= monthStart);
                break;
            }

            case "TodayPresent":
            case "TodayLate":
            case "TodayAbsent":
            {
                await DayAttendanceStore.EnsureAsync(dbContext);
                var status = metric switch
                {
                    "TodayPresent" => "Present",
                    "TodayLate" => "Late",
                    _ => "Absent"
                };
                data.Single = await HrmsDatabase.ScalarAsync<int>(
                    dbContext,
                    """
SELECT COUNT(*)
FROM DayAttendances d
INNER JOIN Employees e ON d.EmployeeId = e.Id
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE b.CompanyId = @Company AND d.WorkDate = @Today AND d.IsAnalyzed = 1 AND d.Status = @Status;
""",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Company", companyId);
                        HrmsDatabase.AddParameter(command, "@Today", today.ToDateTime(TimeOnly.MinValue));
                        HrmsDatabase.AddParameter(command, "@Status", status);
                    });
                break;
            }

            case "PendingRequests":
                data.Single = await HrmsDatabase.ScalarAsync<int>(
                    dbContext,
                    """
SELECT COUNT(*)
FROM SelfServiceRequests r
INNER JOIN Employees e ON r.EmployeeId = e.Id
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0 AND b.CompanyId = @Company AND r.Status = 'Pending';
""",
                    command => HrmsDatabase.AddParameter(command, "@Company", companyId));
                break;

            case "ExpiringContracts60":
                data.Single = await Employees().CountAsync(e => e.IsActive
                    && e.ContractEndDate != null
                    && e.ContractEndDate >= today
                    && e.ContractEndDate <= today.AddDays(60));
                break;

            case "ByBranch":
                data.Rows = ToRows(await Employees()
                    .GroupBy(e => e.Branch!.Name)
                    .Select(g => new { Name = g.Key, Total = g.Count() })
                    .OrderByDescending(g => g.Total)
                    .ToListAsync(), r => r.Name, r => r.Total);
                break;

            case "ByDepartment":
                data.Rows = ToRows(await Employees()
                    .GroupBy(e => e.Department != null ? e.Department.Name : "")
                    .Select(g => new { Name = g.Key, Total = g.Count() })
                    .OrderByDescending(g => g.Total)
                    .ToListAsync(), r => r.Name, r => r.Total);
                break;

            case "ByGender":
                data.Rows = ToRows(await Employees()
                    .GroupBy(e => e.Gender ?? "")
                    .Select(g => new { Name = g.Key, Total = g.Count() })
                    .OrderByDescending(g => g.Total)
                    .ToListAsync(), r => r.Name, r => r.Total);
                break;

            case "ByNationality":
                data.Rows = ToRows(await Employees()
                    .GroupBy(e => e.Nationality ?? "")
                    .Select(g => new { Name = g.Key, Total = g.Count() })
                    .OrderByDescending(g => g.Total)
                    .ToListAsync(), r => r.Name, r => r.Total);
                break;

            case "ByMaritalStatus":
                data.Rows = ToRows(await Employees()
                    .GroupBy(e => e.MaritalStatus ?? "")
                    .Select(g => new { Name = g.Key, Total = g.Count() })
                    .OrderByDescending(g => g.Total)
                    .ToListAsync(), r => r.Name, r => r.Total);
                break;

            case "ByContractType":
                data.Rows = ToRows(await Employees()
                    .GroupBy(e => e.ContractType ?? "")
                    .Select(g => new { Name = g.Key, Total = g.Count() })
                    .OrderByDescending(g => g.Total)
                    .ToListAsync(), r => r.Name, r => r.Total);
                break;

            case "ByAge":
            {
                // فئات كيان: 18-22 .. 58+
                var birthDates = await Employees()
                    .Where(e => e.IsActive && e.BirthDate != null)
                    .Select(e => e.BirthDate!.Value)
                    .ToListAsync();
                var buckets = new[] { "18-22", "23-27", "28-32", "33-37", "38-42", "43-47", "48-52", "53-57", "58+" };
                var counts = new int[buckets.Length];
                foreach (var birthDate in birthDates)
                {
                    var age = today.Year - birthDate.Year - (today.DayOfYear < birthDate.DayOfYear ? 1 : 0);
                    var bucket = age < 18 ? 0 : Math.Min((age - 18) / 5, buckets.Length - 1);
                    counts[bucket]++;
                }
                data.Rows = buckets.Select((label, i) => (label, counts[i])).ToList();
                break;
            }

            case "ByServiceYears":
            {
                // مدة الخدمة بالسنين: 0..9 ثم 10+ (نمط كيان)
                var hireDates = await Employees()
                    .Where(e => e.IsActive)
                    .Select(e => e.HireDate)
                    .ToListAsync();
                var counts = new int[11];
                foreach (var hireDate in hireDates)
                {
                    var years = Math.Max(0, today.Year - hireDate.Year - (today.DayOfYear < hireDate.DayOfYear ? 1 : 0));
                    counts[Math.Min(years, 10)]++;
                }
                data.Rows = Enumerable.Range(0, 11)
                    .Select(i => (i == 10 ? "10+" : i.ToString(), counts[i]))
                    .ToList();
                break;
            }

            case "TodayStatus":
            {
                await DayAttendanceStore.EnsureAsync(dbContext);
                var rows = await HrmsDatabase.QueryAsync(
                    dbContext,
                    """
SELECT d.Status AS Name, COUNT(*) AS Total
FROM DayAttendances d
INNER JOIN Employees e ON d.EmployeeId = e.Id
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE b.CompanyId = @Company AND d.WorkDate = @Today AND d.IsAnalyzed = 1
GROUP BY d.Status ORDER BY Total DESC;
""",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Company", companyId);
                        HrmsDatabase.AddParameter(command, "@Today", today.ToDateTime(TimeOnly.MinValue));
                    },
                    reader => new
                    {
                        Name = DayAttendanceStore.StatusLabel(HrmsDatabase.GetString(reader, "Name")),
                        Total = HrmsDatabase.GetInt(reader, "Total")
                    });
                data.Rows = rows.Select(r => (r.Name, r.Total)).ToList();
                break;
            }

            case "RequestsByStatus":
            {
                var rows = await HrmsDatabase.QueryAsync(
                    dbContext,
                    """
SELECT ISNULL(r.Status, '') AS Name, COUNT(*) AS Total
FROM SelfServiceRequests r
INNER JOIN Employees e ON r.EmployeeId = e.Id
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0 AND b.CompanyId = @Company
GROUP BY ISNULL(r.Status, '') ORDER BY Total DESC;
""",
                    command => HrmsDatabase.AddParameter(command, "@Company", companyId),
                    reader => new
                    {
                        Name = HrmsDatabase.GetString(reader, "Name"),
                        Total = HrmsDatabase.GetInt(reader, "Total")
                    });
                data.Rows = rows.Select(r => (r.Name switch
                {
                    "Pending" => "معلق",
                    "Approved" => "مقبول",
                    "Rejected" => "مرفوض",
                    _ => Normalize(r.Name)
                }, r.Total)).ToList();
                break;
            }
        }

        return data;
    }

    private static List<(string Name, int Total)> ToRows<T>(
        List<T> rows, Func<T, string?> name, Func<T, int> total) =>
        rows.Select(r => (Normalize(name(r)), total(r)))
            .GroupBy(r => r.Item1)
            .Select(g => (g.Key, g.Sum(x => x.Item2)))
            .OrderByDescending(g => g.Item2)
            .ToList();

    private static string Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Equals("Not Set", StringComparison.OrdinalIgnoreCase)
            ? "غير محدد"
            : name.Trim();
}
