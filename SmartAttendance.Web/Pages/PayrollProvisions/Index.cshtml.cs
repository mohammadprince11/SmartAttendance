using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.PayrollProvisions;

/// <summary>
/// حساب الاحتياطي (/PayrollProvisions) — نمط كيان: تقرير الالتزام المتراكم بتاريخ
/// محدّد لكل موظف نشط = مخصص نهاية الخدمة + مخصص رصيد الإجازات، مع إجماليات الشركة.
/// للقراءة فقط (تحليلي/محاسبي) + تصدير CSV.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public string? AsOf { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? FDept { get; set; }
    [BindProperty(SupportsGet = true)] public string? FBranch { get; set; }

    public ProvisionCalculator.Result Data { get; set; } = new();
    public List<string> AllDepartments { get; set; } = new();
    public List<string> AllBranches { get; set; } = new();

    public DateOnly AsOfDate => DateOnly.TryParse(AsOf, out var d) ? d : DateOnly.FromDateTime(DateTime.Today);
    public int ResolvedYear => Year is > 1999 and < 2100 ? Year.Value : DateTime.Today.Year;

    public async Task OnGetAsync()
    {
        var asOf = AsOfDate;
        AsOf = asOf.ToString("yyyy-MM-dd");
        Year = ResolvedYear;

        Data = await ProvisionCalculator.ComputeAsync(_db, asOf, ResolvedYear, null, Search, FDept, FBranch);
        (AllDepartments, AllBranches, _) = await MassScopeResolver.OrgListsAsync(_db);
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var asOf = AsOfDate;
        var data = await ProvisionCalculator.ComputeAsync(_db, asOf, ResolvedYear, null, Search, FDept, FBranch);

        var sb = new StringBuilder();
        sb.AppendLine("الرقم الوظيفي,اسم الموظف,القسم,الفرع,تاريخ التعيين,الأساسي,سنوات الخدمة,مخصص نهاية الخدمة,أيام الإجازة,مخصص الإجازات,إجمالي الاحتياطي");
        foreach (var r in data.Rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.EmployeeNo), Csv(r.EmployeeName), Csv(r.Department), Csv(r.Branch),
                r.HireDate?.ToString("yyyy-MM-dd") ?? "", r.Basic.ToString("0.##"), r.Years.ToString("0.##"),
                r.EosProvision.ToString("0.##"), r.LeaveDays.ToString("0.##"),
                r.LeaveProvision.ToString("0.##"), r.Total.ToString("0.##")));
        }
        sb.AppendLine(string.Join(",", "الإجمالي", "", "", "", "", "", "",
            data.TotalEos.ToString("0.##"), "", data.TotalLeave.ToString("0.##"), data.Total.ToString("0.##")));

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"provisions-{asOf:yyyy-MM-dd}.csv");
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
