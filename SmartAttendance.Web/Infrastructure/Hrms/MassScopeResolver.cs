using Microsoft.AspNetCore.Http;
using SmartAttendance.Infrastructure.Persistence;

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
    public static async Task<(List<string> Departments, List<string> Branches, List<string> JobTitles)> OrgListsAsync(ApplicationDbContext db)
    {
        var attrs = await LoadAsync(db);
        return (
            attrs.Select(x => x.Dept).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList(),
            attrs.Select(x => x.Branch).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList(),
            attrs.Select(x => x.Position).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList());
    }

    /// <summary>يحلّ الموظفين المستهدفين حسب ScopeMode. Error != null ⇒ توقف بعرض الرسالة.</summary>
    public static async Task<(List<int> Ids, int Skipped, string Label, string? Error)> ResolveAsync(
        ApplicationDbContext db, IFormCollection f, IFormFile? file)
    {
        var emps = await LoadAsync(db);
        var byCode = new Dictionary<string, int>();
        foreach (var e in emps) { var k = e.No.Trim().ToLowerInvariant(); if (k.Length > 0) byCode[k] = e.Id; }

        var mode = f["ScopeMode"].ToString();
        if (string.IsNullOrWhiteSpace(mode)) mode = "Manual";
        var ids = new List<int>();
        int skipped = 0;
        string label;

        if (mode == "Paste" || mode == "File")
        {
            IEnumerable<string> codes;
            if (mode == "File")
            {
                if (file == null || file.Length == 0) return (ids, 0, "ملف إكسل", "اختر ملف إكسل أو CSV.");
                List<string[]> rows;
                try { await using var s = file.OpenReadStream(); rows = SpreadsheetReader.Read(s, file.FileName); }
                catch (Exception ex) { return (ids, 0, "ملف إكسل", "تعذّر قراءة الملف: " + ex.Message); }
                codes = rows.Where(r => r.Length > 0).Select(r => r[0]);
                label = "ملف إكسل";
            }
            else
            {
                codes = f["MassCodes"].ToString().Split(new[] { '\n', '\r', ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                label = "لصق أكواد";
            }
            foreach (var raw in codes)
            {
                var k = raw.Trim().ToLowerInvariant();
                if (k.Length == 0) continue;
                if (byCode.TryGetValue(k, out var id)) ids.Add(id); else skipped++;
            }
        }
        else if (mode == "Criteria")
        {
            var dept = f["MassDept"].ToString().Trim();
            var branch = f["MassBranch"].ToString().Trim();
            var job = f["MassJobTitle"].ToString().Trim();
            if (dept.Length == 0 && branch.Length == 0 && job.Length == 0)
                return (ids, 0, "حسب معايير", "حدد معياراً واحداً على الأقل (قسم/فرع/مسمى وظيفي).");
            ids = emps.Where(e =>
                (dept.Length == 0 || e.Dept == dept) &&
                (branch.Length == 0 || e.Branch == branch) &&
                (job.Length == 0 || e.Position == job)).Select(e => e.Id).ToList();
            label = "حسب معايير";
        }
        else
        {
            ids = f["MassEmployeeIds"].Where(v => int.TryParse(v, out _)).Select(int.Parse).ToList();
            label = "اختيار يدوي";
        }

        return (ids.Distinct().ToList(), skipped, label, null);
    }

    private static Task<List<EmpRow>> LoadAsync(ApplicationDbContext db) =>
        HrmsDatabase.QueryAsync(db,
            "SELECT e.Id, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(d.Name, N'') AS Dept, ISNULL(b.Name, N'') AS Branch, ISNULL(e.Position, N'') AS Position FROM Employees e LEFT JOIN Departments d ON d.Id = e.DepartmentId LEFT JOIN Branches b ON b.Id = e.BranchId WHERE ISNULL(e.IsDeleted,0)=0 AND ISNULL(e.IsActive,1)=1;",
            command => { },
            reader => new EmpRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Dept = HrmsDatabase.GetString(reader, "Dept"),
                Branch = HrmsDatabase.GetString(reader, "Branch"),
                Position = HrmsDatabase.GetString(reader, "Position")
            });
}
