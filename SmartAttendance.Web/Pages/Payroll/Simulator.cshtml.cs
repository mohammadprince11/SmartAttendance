using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// نظام المحاكاة (نظير `/Payroll/KayanSimulator` بكيان): تحليل ومطابقة عناصر راتب
/// محدّدة على شريحة موظفين **قبل اتخاذ القرار** — بلا مساس بالبيانات.
///
/// ما اكتشفناه بتشغيل محاكي كيان فعلياً: هو **كاشف فجوات استحقاق بالصيغة** لا مجرد
/// what-if — يعرض «مؤهل لعنصر ليس بملفه» ويُظهر صيغة العنصر نفسها. هذا ما نبنيه:
/// لكل موظف × عنصر: مؤهل؟ (شروط العنصر على حقائقه) · مُسند بملفه؟ · القيمة/الصيغة.
///
/// **قراءة فقط**: يعيد استخدام نفس محرّك الاستحقاق (`SalaryItem.EligibleFor`) ونفس
/// مقيّم الصيغ (`SalaryFormulaEvaluator`) اللذين يستخدمهما المسير — فالنتيجة تنبؤ
/// صادق لا تقريب.
/// </summary>
public class SimulatorModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public SimulatorModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    public sealed record ResultRow(
        int EmployeeId, string EmployeeNo, string EmployeeName, decimal Basic,
        int ItemId, string ItemName, bool Eligible, bool Assigned,
        decimal? SimulatedValue, string? Formula, string? Error)
    {
        /// <summary>التصنيف بنمط رسائل كيان.</summary>
        public string Verdict =>
            Eligible && !Assigned ? "مؤهل وليس بملفه" :
            !Eligible && Assigned ? "بملفه وغير مؤهل" :
            Eligible && Assigned ? "مطابق" : "غير معني";
        public string VerdictClass =>
            Eligible && !Assigned ? "gap" : !Eligible && Assigned ? "over" : Eligible ? "ok" : "none";
    }

    [BindProperty(SupportsGet = true)] public List<int> ItemIds { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }
    [BindProperty(SupportsGet = true)] public bool GapsOnly { get; set; }

    public List<SalaryItemStore.SalaryItem> Items { get; private set; } = new();
    public List<ResultRow> Rows { get; private set; } = new();
    public bool Ran { get; private set; }

    public int EmployeesCount { get; private set; }
    public int GapCount => Rows.Count(r => r.Eligible && !r.Assigned);
    public int OverCount => Rows.Count(r => !r.Eligible && r.Assigned);
    public int MatchCount => Rows.Count(r => r.Eligible && r.Assigned);

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (scope.IsDeniedAll) return;

        Items = (await SalaryItemStore.ListAsync(_db, scope, CompanyId))
            .Where(i => i.IsActive && i.ItemType is "Income" or "Deduction")
            .ToList();

        if (ItemIds.Count == 0) return;

        var selected = Items.Where(i => ItemIds.Contains(i.Id)).ToList();
        if (selected.Count == 0) return;

        // موظفو النطاق (بالشركة إن حُدّدت) + بحث نصي — ثم مرشّح عزل الشركات.
        var rows = await HrConditionFacts.LoadAsync(
            _db, companyId: CompanyId, authorizationScope: scope);

        var names = await LoadNamesAsync(rows.Select(r => r.Id).ToList(), scope);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var q = Search.Trim();
            rows = rows.Where(r => r.EmployeeNo.Contains(q, StringComparison.OrdinalIgnoreCase)
                                || (names.TryGetValue(r.Id, out var n) && n.Contains(q, StringComparison.OrdinalIgnoreCase)))
                       .ToList();
        }

        // الإسنادات القائمة (العلاوات بملف الموظف) لتمييز «مؤهل وليس بملفه».
        var assigned = await LoadAssignmentsAsync(rows.Select(r => r.Id).ToList(), scope);

        var asOf = DateOnly.FromDateTime(DateTime.Today);
        var results = new List<ResultRow>();

        foreach (var emp in rows)
        {
            var facts = HrConditionFacts.Build(emp, asOf);
            var basic = emp.BasicSalary ?? 0m;
            var vars = PayrollFormulaVariables.Build(new PayrollFormulaVariables.Context
            {
                Basic = basic,
                ProratedBasic = basic,
                Days = 30,
                PresentDays = 30,
                DaysInPeriod = 30,
                DailyRate = basic > 0 ? Math.Round(basic / 30m, 4) : 0,
                HourlyRate = basic > 0 ? Math.Round(basic / 30m / 8m, 4) : 0
            });

            foreach (var item in selected)
            {
                var eligible = item.EligibleFor(facts) && item.WithinValidity(asOf, asOf);
                var isAssigned = assigned.TryGetValue(emp.Id, out var set) && set.Contains(item.Id);

                decimal? value = null;
                string? error = null;
                if (item.ValueKind == "Formula" && !string.IsNullOrWhiteSpace(item.Formula))
                {
                    if (SalaryFormulaEvaluator.TryEvaluate(item.Formula, vars, out var v, out var err))
                        value = Math.Round(v, 2);
                    else
                        error = err;
                }
                else if (item.ValueKind == "Fixed")
                {
                    value = item.DefaultValue;
                }

                var row = new ResultRow(emp.Id, emp.EmployeeNo, names.GetValueOrDefault(emp.Id, string.Empty), basic,
                    item.Id, item.Name, eligible, isAssigned, value, item.Formula, error);

                if (!GapsOnly || row.VerdictClass is "gap" or "over")
                    results.Add(row);
            }
        }

        EmployeesCount = rows.Count;
        Rows = results.OrderBy(r => r.VerdictClass == "gap" ? 0 : r.VerdictClass == "over" ? 1 : 2)
                      .ThenBy(r => r.EmployeeNo).ToList();
        Ran = true;
    }

    private async Task<Dictionary<int, string>> LoadNamesAsync(List<int> ids, CompanyScope scope)
    {
        if (ids.Count == 0) return new();
        var list = await HrmsDatabase.QueryAsync(
            _db,
            $"SELECT Id, ISNULL(FullName, N'') AS FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND {EmployeeCompanyGuard.ListFilter(scope, "CompanyId")};",
            command => { },
            reader => (Id: HrmsDatabase.GetInt(reader, "Id"), Name: HrmsDatabase.GetString(reader, "FullName")));
        var wanted = ids.ToHashSet();
        return list.Where(x => wanted.Contains(x.Id)).ToDictionary(x => x.Id, x => x.Name);
    }

    private async Task<Dictionary<int, HashSet<int>>> LoadAssignmentsAsync(List<int> ids, CompanyScope scope)
    {
        if (ids.Count == 0) return new();
        var pairs = await HrmsDatabase.QueryAsync(
            _db,
            $"SELECT a.EmployeeId, a.SalaryItemId FROM EmployeeAllowances a INNER JOIN Employees e ON e.Id=a.EmployeeId WHERE ISNULL(a.IsDeleted,0)=0 AND a.SalaryItemId IS NOT NULL AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")};",
            command => { },
            reader => (Emp: HrmsDatabase.GetInt(reader, "EmployeeId"), Item: HrmsDatabase.GetInt(reader, "SalaryItemId")));
        var wanted = ids.ToHashSet();
        return pairs.Where(p => wanted.Contains(p.Emp))
                    .GroupBy(p => p.Emp)
                    .ToDictionary(g => g.Key, g => g.Select(p => p.Item).ToHashSet());
    }
}
