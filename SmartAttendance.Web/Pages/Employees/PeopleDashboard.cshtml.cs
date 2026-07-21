using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// داشبورد الأشخاص (نمط كيان قسم 18.7): مؤشرات المودل المتخصصة التي لا تظهر
/// باللوحة التنفيذية — جديد هذا الشهر، بفترة التجربة، أُنهي هذا الشهر،
/// معدل الدوران الطوعي/غير الطوعي بنطاق سنة، تكافؤ الجنسين، وشرائح مدة الخدمة.
/// </summary>
public class PeopleDashboardModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public PeopleDashboardModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public int NewThisMonth { get; set; }
    public int InProbation { get; set; }
    public int Suspended { get; set; }
    public int EndedThisMonth { get; set; }

    // معدل الدوران بنطاق سنة: استقالة = طوعي، غيره (فصل/إنهاء عقد...) = غير طوعي.
    public int VoluntaryExits { get; set; }
    public int InvoluntaryExits { get; set; }
    public double TurnoverRate { get; set; }
    public double VoluntaryRate { get; set; }

    public int MaleCount { get; set; }
    public int FemaleCount { get; set; }

    public sealed record Bucket(string Label, int Count);
    public List<Bucket> TenureBuckets { get; set; } = new();
    public List<Bucket> AgeBuckets { get; set; } = new();

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
DECLARE @MonthStart date = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
DECLARE @YearAgo date = DATEADD(year, -1, GETDATE());

SELECT
    TotalEmployees   = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0),
    ActiveEmployees  = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND IsActive = 1),
    NewThisMonth     = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND HireDate >= @MonthStart),
    InProbation      = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND IsActive = 1
                          AND DATEADD(day, 90, ISNULL(JoiningDate, HireDate)) >= GETDATE()),
    Suspended        = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND IsActive = 0),
    EndedThisMonth   = (SELECT COUNT(*) FROM EmployeeEndServices WHERE LastWorkingDate >= @MonthStart),
    VoluntaryExits   = (SELECT COUNT(*) FROM EmployeeEndServices
                        WHERE LastWorkingDate >= @YearAgo
                          AND (EndServiceType LIKE N'%استقال%' OR EndServiceTypeText LIKE N'%استقال%')),
    InvoluntaryExits = (SELECT COUNT(*) FROM EmployeeEndServices
                        WHERE LastWorkingDate >= @YearAgo
                          AND NOT (EndServiceType LIKE N'%استقال%' OR EndServiceTypeText LIKE N'%استقال%')),
    MaleCount        = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND Gender = 'Male'),
    FemaleCount      = (SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0 AND Gender = 'Female');
""",
            command => { },
            reader => new
            {
                TotalEmployees = HrmsDatabase.GetInt(reader, "TotalEmployees"),
                ActiveEmployees = HrmsDatabase.GetInt(reader, "ActiveEmployees"),
                NewThisMonth = HrmsDatabase.GetInt(reader, "NewThisMonth"),
                InProbation = HrmsDatabase.GetInt(reader, "InProbation"),
                Suspended = HrmsDatabase.GetInt(reader, "Suspended"),
                EndedThisMonth = HrmsDatabase.GetInt(reader, "EndedThisMonth"),
                VoluntaryExits = HrmsDatabase.GetInt(reader, "VoluntaryExits"),
                InvoluntaryExits = HrmsDatabase.GetInt(reader, "InvoluntaryExits"),
                MaleCount = HrmsDatabase.GetInt(reader, "MaleCount"),
                FemaleCount = HrmsDatabase.GetInt(reader, "FemaleCount")
            });

        var stats = rows.FirstOrDefault();
        if (stats == null) return;

        TotalEmployees = stats.TotalEmployees;
        ActiveEmployees = stats.ActiveEmployees;
        NewThisMonth = stats.NewThisMonth;
        InProbation = stats.InProbation;
        Suspended = stats.Suspended;
        EndedThisMonth = stats.EndedThisMonth;
        VoluntaryExits = stats.VoluntaryExits;
        InvoluntaryExits = stats.InvoluntaryExits;
        MaleCount = stats.MaleCount;
        FemaleCount = stats.FemaleCount;

        // معدل الدوران = المغادرون خلال سنة ÷ متوسط قوة العمل (تقريب: الفعالون الحاليون).
        var exits = VoluntaryExits + InvoluntaryExits;
        var basis = Math.Max(1, ActiveEmployees);
        TurnoverRate = Math.Round(exits * 100.0 / basis, 1);
        VoluntaryRate = Math.Round(VoluntaryExits * 100.0 / basis, 1);

        TenureBuckets = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Label, COUNT(*) AS Count FROM
(
    SELECT CASE
        WHEN DATEDIFF(year, HireDate, GETDATE()) < 1 THEN N'أقل من سنة'
        WHEN DATEDIFF(year, HireDate, GETDATE()) < 3 THEN N'1 – 3 سنوات'
        WHEN DATEDIFF(year, HireDate, GETDATE()) < 5 THEN N'3 – 5 سنوات'
        ELSE N'أكثر من 5 سنوات' END AS Label
    FROM Employees WHERE IsDeleted = 0 AND IsActive = 1
) t GROUP BY Label
ORDER BY MIN(CASE Label WHEN N'أقل من سنة' THEN 1 WHEN N'1 – 3 سنوات' THEN 2 WHEN N'3 – 5 سنوات' THEN 3 ELSE 4 END);
""",
            command => { },
            reader => new Bucket(HrmsDatabase.GetString(reader, "Label"), HrmsDatabase.GetInt(reader, "Count")));

        AgeBuckets = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Label, COUNT(*) AS Count FROM
(
    SELECT CASE
        WHEN BirthDate IS NULL THEN N'غير محدد'
        WHEN DATEDIFF(year, BirthDate, GETDATE()) < 25 THEN N'أقل من 25'
        WHEN DATEDIFF(year, BirthDate, GETDATE()) < 35 THEN N'25 – 35'
        WHEN DATEDIFF(year, BirthDate, GETDATE()) < 45 THEN N'35 – 45'
        ELSE N'45 فأكثر' END AS Label
    FROM Employees WHERE IsDeleted = 0 AND IsActive = 1
) t GROUP BY Label
ORDER BY MIN(CASE Label WHEN N'أقل من 25' THEN 1 WHEN N'25 – 35' THEN 2 WHEN N'35 – 45' THEN 3 WHEN N'45 فأكثر' THEN 4 ELSE 5 END);
""",
            command => { },
            reader => new Bucket(HrmsDatabase.GetString(reader, "Label"), HrmsDatabase.GetInt(reader, "Count")));
    }
}
