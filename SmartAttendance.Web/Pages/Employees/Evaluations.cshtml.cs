using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// متابعة تقييمات الموظفين (نظير `/Employees/EvaluationsScreening` بكيان):
/// إدخال يدوي لنتيجة تقييم سنوية (نسبة + تقدير + وصف) وقائمة بكل النتائج.
///
/// المصدر «مباشر» للإدخال اليدوي — حين يتكامل مودل إدارة الأداء يكتب صفوفه
/// بمصدر «إدارة الأداء» على نفس الجدول فتبقى الشاشة واحدة.
/// </summary>
public class EvaluationsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public EvaluationsModel(ApplicationDbContext dbContext, ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    public sealed record ResultRow(
        int Id, int EmployeeId, string EmployeeNo, string EmployeeName, string? Position,
        int EvalYear, DateOnly FromDate, DateOnly ToDate,
        decimal ScorePercent, string? Grade, string? GradeText, string Source);

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }

    // ── نموذج الإضافة ──
    [BindProperty] public int EmployeeId { get; set; }
    [BindProperty] public int EvalYear { get; set; } = DateTime.Today.Year;
    [BindProperty] public DateOnly? FromDate { get; set; }
    [BindProperty] public DateOnly? ToDate { get; set; }
    [BindProperty] public decimal ScorePercent { get; set; }
    [BindProperty] public string? Grade { get; set; }
    [BindProperty] public string? GradeText { get; set; }

    [TempData] public string? Message { get; set; }

    public List<ResultRow> Rows { get; private set; } = new();
    public List<(int Id, string Label)> EmployeeOptions { get; private set; } = new();
    public List<int> YearOptions { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        // حارس النطاق عند الكتابة: النتيجة تخص موظفاً — يجب أن يكون ضمن شركاتي.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!scope.IsUnrestricted)
        {
            var allowed = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                _dbContext, new[] { EmployeeId }, scope, HttpContext.RequestAborted);
            if (!allowed.Contains(EmployeeId))
                return NotFound();
        }

        // حرّاس المدخل: رفض الفاسد بدل حفظ نتيجة مضلِّلة.
        var from = FromDate ?? new DateOnly(EvalYear, 1, 1);
        var to = ToDate ?? new DateOnly(EvalYear, 12, 31);

        if (EmployeeId <= 0 || EvalYear < 2000 || EvalYear > 2100 ||
            to < from || ScorePercent is < 0 or > 100)
        {
            Message = "تحقق من المدخلات: موظف وسنة صحيحة، فترة سليمة، ونسبة بين 0 و100.";
            return RedirectToPage(new { Search, Year });
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF NOT EXISTS (SELECT 1 FROM EmployeeEvaluationResults
               WHERE EmployeeId = @EmployeeId AND EvalYear = @EvalYear
                 AND FromDate = @FromDate AND ToDate = @ToDate)
    INSERT INTO EmployeeEvaluationResults
        (EmployeeId, EvalYear, FromDate, ToDate, ScorePercent, Grade, GradeText, Source, CreatedBy)
    VALUES
        (@EmployeeId, @EvalYear, @FromDate, @ToDate, @ScorePercent, @Grade, @GradeText, N'مباشر', @CreatedBy);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId);
                HrmsDatabase.AddParameter(command, "@EvalYear", EvalYear);
                HrmsDatabase.AddParameter(command, "@FromDate", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@ToDate", to.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@ScorePercent", ScorePercent);
                HrmsDatabase.AddParameter(command, "@Grade", string.IsNullOrWhiteSpace(Grade) ? null : Grade.Trim());
                HrmsDatabase.AddParameter(command, "@GradeText", string.IsNullOrWhiteSpace(GradeText) ? null : GradeText.Trim());
                HrmsDatabase.AddParameter(command, "@CreatedBy", User.Identity?.Name ?? "System");
            });

        Message = "حُفظت نتيجة التقييم.";
        return RedirectToPage(new { Search, Year });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // حارس الملكية قبل الحذف — النتيجة تتبع موظفاً بشركة.
        var ownerEmployee = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT ISNULL((SELECT EmployeeId FROM EmployeeEvaluationResults WHERE Id = @Id), 0);",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (ownerEmployee <= 0 ||
            (!scope.IsUnrestricted &&
             !(await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                 _dbContext, new[] { ownerEmployee }, scope, HttpContext.RequestAborted)).Contains(ownerEmployee)))
        {
            return NotFound();
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "DELETE FROM EmployeeEvaluationResults WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        Message = "حُذفت النتيجة.";
        return RedirectToPage(new { Search, Year });
    }

    private async Task LoadAsync()
    {
        var like = string.IsNullOrWhiteSpace(Search) ? null : "%" + Search.Trim() + "%";
        var filter = like is null ? string.Empty : " AND (e.FullName LIKE @Q OR e.EmployeeNo LIKE @Q)";
        var yearFilter = Year is > 0 ? " AND r.EvalYear = @Year" : string.Empty;

        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT r.Id, r.EmployeeId, e.EmployeeNo, e.FullName, e.Position,
       r.EvalYear, r.FromDate, r.ToDate, r.ScorePercent, r.Grade, r.GradeText, r.Source
FROM EmployeeEvaluationResults r
JOIN Employees e ON e.Id = r.EmployeeId AND ISNULL(e.IsDeleted, 0) = 0
WHERE 1 = 1{filter}{yearFilter}
ORDER BY r.EvalYear DESC, e.EmployeeNo;
""",
            command =>
            {
                if (like is not null) HrmsDatabase.AddParameter(command, "@Q", like);
                if (Year is > 0) HrmsDatabase.AddParameter(command, "@Year", Year.Value);
            },
            reader => new ResultRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "FullName"),
                HrmsDatabase.GetString(reader, "Position"),
                HrmsDatabase.GetInt(reader, "EvalYear"),
                HrmsDatabase.GetDateOnly(reader, "FromDate") ?? default,
                HrmsDatabase.GetDateOnly(reader, "ToDate") ?? default,
                reader["ScorePercent"] is decimal score ? score : 0m,
                HrmsDatabase.GetString(reader, "Grade"),
                HrmsDatabase.GetString(reader, "GradeText"),
                HrmsDatabase.GetString(reader, "Source")));

        // حصر القائمة بموظفي شركاتي — النتائج تتبع موظفين بشركات.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!scope.IsUnrestricted)
        {
            var allowed = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                _dbContext, rows.Select(r => r.EmployeeId), scope, HttpContext.RequestAborted);
            rows = rows.Where(r => allowed.Contains(r.EmployeeId)).ToList();
        }

        Rows = rows.ToList();
        YearOptions = Rows.Select(r => r.EvalYear).Distinct().OrderByDescending(y => y).ToList();

        EmployeeOptions = (await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 300 Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName
FROM Employees
WHERE ISNULL(IsDeleted, 0) = 0 AND ISNULL(IsActive, 1) = 1
ORDER BY EmployeeNo;
""",
            command => { },
            reader => (
                HrmsDatabase.GetInt(reader, "Id"),
                $"{HrmsDatabase.GetString(reader, "EmployeeNo")} — {HrmsDatabase.GetString(reader, "FullName")}"))).ToList();
    }
}
