using Microsoft.AspNetCore.Http;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// محدّد نطاق الإدخال الجماعي (نمط كيان «النطاق») — يحلّ مجموعة الموظفين المستهدفين
/// من نموذج الطلب بأربع طرق: Manual (اختيار يدوي) | Paste (لصق أكواد) |
/// File (رفع إكسل/CSV) | Criteria (قسم/فرع/مسمى وظيفي). مشترك بين شاشات الحركات.
/// </summary>
public static class MassScopeResolver
{
    private sealed class EmpRow
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Dept { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
    }

    /// <summary>القوائم الكاملة للمؤسسة لمعايير النطاق (كل الموظفين النشطين).</summary>
    public static async Task<(List<string> Departments, List<string> Branches, List<string> JobTitles)> OrgListsAsync(
        ApplicationDbContext db,
        int? companyId = null,
        CompanyScope? authorizationScope = null)
    {
        var attrs = await LoadAsync(db, companyId, authorizationScope);
        return (
            attrs.Select(x => x.Dept).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList(),
            attrs.Select(x => x.Branch).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList(),
            attrs.Select(x => x.Position).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList());
    }

    /// <summary>يحلّ الموظفين المستهدفين حسب ScopeMode. Error != null ⇒ توقف بعرض الرسالة.</summary>
    public static async Task<(List<int> Ids, int Skipped, string Label, string? Error)> ResolveAsync(
        ApplicationDbContext db, IFormCollection f, IFormFile? file, int? companyId = null,
        CompanyScope? authorizationScope = null)
    {
        var r = await ResolveDetailedAsync(db, f, file, companyId, authorizationScope);
        return (r.Ids, r.Skipped, r.Label, r.Error);
    }

    /// <summary>
    /// نفس التحليل مع **نصوص الأكواد المفقودة** لا عددها فقط: كودٌ مخطئ واحد يعني
    /// موظفاً يغيب بصمت، وعرض «12 تُخطّي» بلا معرفة أيّها لا يمكّن من التصحيح.
    /// </summary>
    public static async Task<(List<int> Ids, int Skipped, List<string> Missing, string Label, string? Error)>
        ResolveDetailedAsync(
            ApplicationDbContext db,
            IFormCollection f,
            IFormFile? file,
            int? companyId = null,
            CompanyScope? authorizationScope = null)
    {
        var emps = await LoadAsync(db, companyId, authorizationScope);
        var byCode = PayrollRunScope.BuildCodeMap(emps.Select(e => (e.No, e.Id)));

        var mode = f["ScopeMode"].ToString();
        if (string.IsNullOrWhiteSpace(mode)) mode = "Manual";
        var ids = new List<int>();
        var missing = new List<string>();
        int skipped = 0;
        string label;

        if (mode == "Paste" || mode == "File")
        {
            IEnumerable<string> codes;
            if (mode == "File")
            {
                if (file == null || file.Length == 0) return (ids, 0, missing, "ملف إكسل", "اختر ملف إكسل أو CSV.");
                List<string[]> rows;
                try { await using var s = file.OpenReadStream(); rows = SpreadsheetReader.Read(s, file.FileName); }
                catch (Exception ex) { return (ids, 0, missing, "ملف إكسل", "تعذّر قراءة الملف: " + ex.Message); }
                codes = rows.Where(r => r.Length > 0).Select(r => r[0]);
                label = "ملف إكسل";
            }
            else
            {
                codes = PayrollRunScope.ParseCodes(f["MassCodes"].ToString());
                label = "لصق أكواد";
            }
            (ids, missing) = PayrollRunScope.MatchCodes(codes, byCode);
            skipped = missing.Count;
        }
        else if (mode == "Criteria")
        {
            var dept = f["MassDept"].ToString().Trim();
            var branch = f["MassBranch"].ToString().Trim();
            var job = f["MassJobTitle"].ToString().Trim();
            if (dept.Length == 0 && branch.Length == 0 && job.Length == 0)
                return (ids, 0, missing, "حسب معايير", "حدد معياراً واحداً على الأقل (قسم/فرع/مسمى وظيفي).");
            ids = emps.Where(e =>
                (dept.Length == 0 || e.Dept == dept) &&
                (branch.Length == 0 || e.Branch == branch) &&
                (job.Length == 0 || e.Position == job)).Select(e => e.Id).ToList();
            label = "حسب معايير";
        }
        else
        {
            ids = f["MassEmployeeIds"]
                .Select(v => int.TryParse(v, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
            label = "اختيار يدوي";
        }

        return (ids.Distinct().ToList(), skipped, missing, label, null);
    }

    private static Task<List<EmpRow>> LoadAsync(
        ApplicationDbContext db, int? companyId, CompanyScope? authorizationScope)
    {
        var scopeFilter = authorizationScope is null
            ? "1=1"
            : EmployeeCompanyGuard.ListFilter(authorizationScope, "e.CompanyId");
        return
        HrmsDatabase.QueryAsync(db,
            $"SELECT e.Id, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(d.Name, N'') AS Dept, ISNULL(b.Name, N'') AS Branch, ISNULL(e.Position, N'') AS Position FROM Employees e LEFT JOIN Departments d ON d.Id = e.DepartmentId LEFT JOIN Branches b ON b.Id = e.BranchId WHERE ISNULL(e.IsDeleted,0)=0 AND ISNULL(e.IsActive,1)=1 AND (@Company IS NULL OR e.CompanyId=@Company) AND {scopeFilter};",
            command => HrmsDatabase.AddParameter(
                command, "@Company", (object?)companyId ?? DBNull.Value),
            reader => new EmpRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Dept = HrmsDatabase.GetString(reader, "Dept"),
                Branch = HrmsDatabase.GetString(reader, "Branch"),
                Position = HrmsDatabase.GetString(reader, "Position")
            });
    }
}
