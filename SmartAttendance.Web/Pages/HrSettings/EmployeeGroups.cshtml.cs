using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// مجموعات الموظفين الديناميكية (نمط كيان SavedQuery): مجموعة = اسم + شروط
/// (فرع/قسم/نوع دوام/فعال فقط) وتُحسب عضويتها حيّاً — تُستهدف لاحقاً
/// بالإعلانات والتقارير والطلبات. جدول ذاتي الإنشاء EmployeeGroups.
/// </summary>
public class EmployeeGroupsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public EmployeeGroupsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public sealed class GroupRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int? BranchId { get; set; }
        public int? DepartmentId { get; set; }
        public string? WorkType { get; set; }
        public bool ActiveOnly { get; set; }
        public int MemberCount { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
    }

    public List<GroupRow> Groups { get; set; } = new();
    public List<(int Id, string Name)> Branches { get; set; } = new();
    public List<(int Id, string Name)> Departments { get; set; } = new();
    public List<string> WorkTypes { get; set; } = new();

    private static async Task EnsureAsync(ApplicationDbContext db) =>
        await HrmsDatabase.ExecuteAsync(db, """
IF OBJECT_ID('EmployeeGroups', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeGroups
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        Note nvarchar(500) NULL,
        BranchId int NULL,
        DepartmentId int NULL,
        WorkType nvarchar(50) NULL,
        ActiveOnly bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;
""");

    public async Task OnGetAsync()
    {
        await EnsureAsync(_dbContext);

        // العضوية تُحسب حيّاً من شروط المجموعة — لا تخزين للأعضاء.
        Groups = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT g.*, ISNULL(b.Name, N'') AS BranchName, ISNULL(d.Name, N'') AS DepartmentName,
       MemberCount = (SELECT COUNT(*) FROM Employees e
                      WHERE e.IsDeleted = 0
                        AND (g.ActiveOnly = 0 OR e.IsActive = 1)
                        AND (g.BranchId IS NULL OR e.BranchId = g.BranchId)
                        AND (g.DepartmentId IS NULL OR e.DepartmentId = g.DepartmentId)
                        AND (g.WorkType IS NULL OR e.WorkType = g.WorkType))
FROM EmployeeGroups g
LEFT JOIN Branches b ON b.Id = g.BranchId
LEFT JOIN Departments d ON d.Id = g.DepartmentId
ORDER BY g.Name;
""",
            command => { },
            reader => new GroupRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                Note = HrmsDatabase.GetString(reader, "Note"),
                BranchId = HrmsDatabase.GetNullableInt(reader, "BranchId"),
                DepartmentId = HrmsDatabase.GetNullableInt(reader, "DepartmentId"),
                WorkType = HrmsDatabase.GetString(reader, "WorkType"),
                ActiveOnly = HrmsDatabase.GetBool(reader, "ActiveOnly"),
                MemberCount = HrmsDatabase.GetInt(reader, "MemberCount"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName")
            });

        Branches = (await _dbContext.Branches.AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive).OrderBy(b => b.Name)
            .Select(b => new { b.Id, b.Name }).ToListAsync())
            .Select(b => (b.Id, b.Name)).ToList();
        Departments = (await _dbContext.Departments.AsNoTracking()
            .Where(d => !d.IsDeleted && d.IsActive).OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name }).ToListAsync())
            .Select(d => (d.Id, d.Name)).ToList();
        WorkTypes = await HrLookups.ValuesAsync(_dbContext, "worktypes");
    }

    public async Task<IActionResult> OnPostAddAsync(string name, string? note, int? branchId, int? departmentId, string? workType, bool activeOnly)
    {
        await EnsureAsync(_dbContext);
        if (!string.IsNullOrWhiteSpace(name))
        {
            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                """
INSERT INTO EmployeeGroups (Name, Note, BranchId, DepartmentId, WorkType, ActiveOnly)
VALUES (@Name, @Note, @BranchId, @DepartmentId, @WorkType, @ActiveOnly);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Name", name.Trim());
                    HrmsDatabase.AddParameter(command, "@Note", (object?)note ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@BranchId", (object?)branchId ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@DepartmentId", (object?)departmentId ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@WorkType", string.IsNullOrWhiteSpace(workType) ? DBNull.Value : workType);
                    HrmsDatabase.AddParameter(command, "@ActiveOnly", activeOnly ? 1 : 0);
                });
            TempData["SuccessMessage"] = "تمت إضافة المجموعة.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await EnsureAsync(_dbContext);
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "DELETE FROM EmployeeGroups WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
        TempData["SuccessMessage"] = "تم حذف المجموعة.";
        return RedirectToPage();
    }
}
