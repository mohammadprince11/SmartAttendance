using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// المسير (/Payroll/Runs) — قلب مودل الرواتب: قائمة الدفعات الشهرية بدورة حياة
/// (مسودة ← محتسب ← مقفل ← معتمد ← أُرسلت القسائم)، إنشاء دفعة، والاحتساب والانتقالات.
/// </summary>
public class RunsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public RunsModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    public sealed class EmployeeOption
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public sealed class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public List<PayrollRunStore.PayrollRun> Runs { get; set; } = new();
    public List<int> AvailableYears { get; set; } = new();
    public List<CompanyOption> Companies { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    /// <summary>الموظفون النشطون لمنتقي النطاق (اختيار يدوي) — نفس قائمة الاحتساب.</summary>
    public List<EmployeeOption> Employees { get; set; } = new();
    public List<string> AllDepartments { get; set; } = new();
    public List<string> AllBranches { get; set; } = new();
    public List<string> AllJobTitles { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    public int NewYear { get; set; } = DateTime.Today.Year;
    public int NewMonth { get; set; } = DateTime.Today.Month;

    // مؤشرات
    public int TotalRuns { get; set; }
    public int PaidRuns { get; set; }
    public PayrollRunStore.PayrollRun? LatestRun { get; set; }
    public decimal YearNet { get; set; }
    public decimal YearGross { get; set; }
    public int LatestEmployees { get; set; }

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var all = await PayrollRunStore.ListRunsAsync(_db, scope);

        var companyQuery = _db.Companies.AsNoTracking()
            .Where(company => company.IsActive && !company.IsDeleted);
        if (!scope.IsUnrestricted)
        {
            var allowed = scope.AllowedCompanyIds.ToArray();
            companyQuery = companyQuery.Where(company => allowed.Contains(company.Id));
        }
        Companies = await companyQuery
            .OrderBy(company => company.Name)
            .Select(company => new CompanyOption { Id = company.Id, Name = company.Name })
            .ToListAsync(HttpContext.RequestAborted);
        if (CompanyId.HasValue && !Companies.Any(company => company.Id == CompanyId.Value))
            CompanyId = null;
        if (!CompanyId.HasValue && Companies.Count == 1)
            CompanyId = Companies[0].Id;
        AvailableYears = all.Select(r => r.Year).Distinct().OrderByDescending(y => y).ToList();
        if (AvailableYears.Count == 0) AvailableYears.Add(DateTime.Today.Year);

        TotalRuns = all.Count;
        PaidRuns = all.Count(r => r.Status is "Issued" or "PayslipSent");
        LatestRun = all.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenByDescending(r => r.Id).FirstOrDefault();
        LatestEmployees = LatestRun?.EmployeeCount ?? 0;

        // السنة: أول تحميل (null) ⟹ السنة الحالية افتراضاً؛ 0 ⟹ الكل؛ غير ذلك ⟹ سنة محدَّدة.
        var currentYear = DateTime.Today.Year;
        var showAllYears = Year == 0;
        var filterYear = (Year is null or 0) ? currentYear : Year.Value;
        // فلتر الشركة (متعدّد الشركات): 0/غير محدَّد ⟹ كل الشركات المسموحة بنطاق المستخدم.
        Func<PayrollRunStore.PayrollRun, bool> byCompany =
            CompanyId is > 0 ? r => r.CompanyId == CompanyId.Value : _ => true;
        YearNet = all.Where(r => r.Year == filterYear && byCompany(r)).Sum(r => r.TotalNet);
        YearGross = all.Where(r => r.Year == filterYear && byCompany(r)).Sum(r => r.TotalGross);

        Runs = (showAllYears ? all.Where(byCompany) : all.Where(r => r.Year == filterYear && byCompany(r))).ToList();

        if (CompanyId is > 0)
        {
            Employees = await HrmsDatabase.QueryAsync(_db,
                "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1 AND CompanyId=@Company ORDER BY EmployeeNo;",
                command => HrmsDatabase.AddParameter(command, "@Company", CompanyId.Value),
                reader => new EmployeeOption
                {
                    Id = HrmsDatabase.GetInt(reader, "Id"),
                    No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                    Name = HrmsDatabase.GetString(reader, "FullName")
                });

            (AllDepartments, AllBranches, AllJobTitles) =
                await MassScopeResolver.OrgListsAsync(_db, CompanyId.Value);
        }
    }

    /// <summary>
    /// موظفو/أقسام/فروع/مسميات شركةٍ بعينها (JSON) — لتحميل بيانات النطاق عند اختيار
    /// الشركة بنافذة الإنشاء <b>دون إعادة تحميل الصفحة</b>. نفس استعلامات <see cref="OnGetAsync"/>.
    /// </summary>
    public async Task<IActionResult> OnGetCompanyScopeAsync(int companyId)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (companyId <= 0 || !scope.Allows(companyId)) return Forbid();

        var employees = await HrmsDatabase.QueryAsync(_db,
            "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1 AND CompanyId=@Company ORDER BY EmployeeNo;",
            command => HrmsDatabase.AddParameter(command, "@Company", companyId),
            reader => new
            {
                id = HrmsDatabase.GetInt(reader, "Id"),
                no = HrmsDatabase.GetString(reader, "EmployeeNo"),
                name = HrmsDatabase.GetString(reader, "FullName")
            });

        var (departments, branches, jobTitles) = await MassScopeResolver.OrgListsAsync(_db, companyId);
        return new JsonResult(new { employees, departments, branches, jobTitles });
    }

    public async Task<IActionResult> OnPostCreateAsync(
        int companyId, int year, int month, IFormFile? massFile)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (companyId <= 0 || !scope.Allows(companyId)) return Forbid();

        var (scopeMode, ids, error) = await ResolveScopeAsync(massFile, companyId);
        if (error != null)
        {
            TempData["PayrollMessage"] = error;
            TempData["PayrollOk"] = false;
            return RedirectToPage();
        }

        // قاعدة (١) منع الازدواج: دفعة غير مقفلة بنفس (الشركة+الفترة) ونفس الأشخاص ⟹ هذه
        // إعادة احتساب لا دفعة جديدة. نمنع الإنشاء المكرّر ونوجّه لإعادة احتساب القائمة.
        var duplicateBatch = await PayrollRunStore.FindDuplicateUnlockedBatchAsync(
            _db, companyId, year, month, ids);
        if (duplicateBatch != null)
        {
            TempData["PayrollMessage"] =
                $"توجد دفعة غير مقفلة بنفس الفترة والأشخاص (الدفعة {duplicateBatch}) — أعد احتسابها بدلاً من إنشاء دفعة مكرّرة.";
            TempData["PayrollOk"] = false;
            return RedirectToPage();
        }

        // شركة الدفعة من نطاق مُنشئها حين يكون قاطعاً (شركة واحدة مسموحة). المقيَّد
        // بشركةٍ لا يستطيع إنشاء دفعة لغيرها، وغير المقيَّد تبقى دفعته بلا نسبة حتى
        // الاحتساب فتُشتقّ من أعضاء نطاقها.
        var (ok, message, newId) = await PayrollRunStore.CreateRunAsync(
            _db, scope, companyId, year, month, scopeMode, ids, HttpContext.RequestAborted);
        if (ok && newId > 0)
        {
            // احتساب فوري عند الإنشاء حتى تظهر بيانات الدفعة مباشرةً بدل أصفار — عبر الحارس
            // الذي يمنع ازدواج صرف موظف بأكثر من دفعة/فترة. فشل الاحتساب لا يُسقط الإنشاء.
            var (calcOk, calcMsg) = await PayrollRunStore.CalculateWithGuardAsync(
                _db, newId, User?.Identity?.Name ?? "system");
            // أُزيل التوجيه «شغّل الاحتساب» عند نجاح الاحتساب التلقائي حتى لا تتناقض الرسالة.
            message = calcOk
                ? $"{message.Replace("شغّل «الاحتساب».", "").TrimEnd()} — {calcMsg}"
                : $"{message} (تعذّر الاحتساب التلقائي: {calcMsg})";
            TempData["PayrollOk"] = calcOk;
        }
        else
        {
            TempData["PayrollOk"] = ok;
        }
        TempData["PayrollMessage"] = message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetScopeAsync(int id, IFormFile? massFile) =>
        await ActAsync(id, () => SetScopeAsync(id, massFile));

    private async Task<(bool Ok, string Message)> SetScopeAsync(int id, IFormFile? massFile)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var run = await PayrollRunStore.GetRunAsync(_db, id);
        if (run?.CompanyId is not { } companyId || !scope.Allows(companyId))
            return (false, "الدُفعة غير موجودة أو خارج نطاق صلاحيتك.");

        var (scopeMode, ids, error) = await ResolveScopeAsync(massFile, companyId);
        if (error != null)
        {
            TempData["PayrollMessage"] = error;
            TempData["PayrollOk"] = false;
            return (false, error);
        }

        return await PayrollRunStore.SetScopeAsync(
            _db, scope, id, scopeMode, ids, HttpContext.RequestAborted);
    }

    /// <summary>
    /// معاينة الأكواد الملصوقة قبل التشغيل (JSON): الموجود مقابل المفقود.
    /// الغرض ألا يُكتشف كودٌ مخطئ بعد الاحتساب — حينها يكون موظفٌ غاب عن راتبه.
    /// </summary>
    public async Task<IActionResult> OnPostPreviewCodesAsync(string? codes, int companyId)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (companyId <= 0 || !scope.Allows(companyId)) return Forbid();
        var (matched, missing) = await PayrollRunScopeStore.PreviewCodesAsync(
            _db, codes, companyId);
        return new JsonResult(new
        {
            matched = matched.Select(m => new { id = m.Id, no = m.No, name = m.Name }),
            missing
        });
    }

    /// <summary>
    /// يحلّ نطاق النموذج. الوضع «الكل» يعيد قائمة فارغة عمداً — لا صفوف نطاق
    /// بالقاعدة لتشغيل يشمل الجميع (الافتراض بالكود لا بالبيانات).
    /// </summary>
    private async Task<(string Mode, List<int> Ids, string? Error)> ResolveScopeAsync(
        IFormFile? massFile,
        int companyId)
    {
        var mode = PayrollRunScope.NormalizeMode(Request.Form["ScopeMode"].ToString());
        if (mode == PayrollRunScope.ModeAll) return (mode, new List<int>(), null);

        var (ids, _, missing, label, error) = await MassScopeResolver.ResolveDetailedAsync(
            _db, Request.Form, massFile, companyId);
        if (error != null) return (mode, ids, error);
        if (ids.Count == 0)
            return (mode, ids, missing.Count > 0
                ? $"لا كود مطابقاً من {missing.Count} — لم يُطبَّق النطاق ({label}). أول المفقود: {string.Join("، ", missing.Take(8))}"
                : $"لم تحدَّد أي موظفين بالنطاق ({label}) — اختر «كل الموظفين» إن كنت تقصد الجميع.");

        if (missing.Count > 0)
            TempData["PayrollMissingCodes"] =
                $"⚠ {missing.Count} كوداً غير مطابق تُخطّي: {string.Join("، ", missing.Take(20))}{(missing.Count > 20 ? " …" : "")}";

        return (mode, ids, null);
    }

    public async Task<IActionResult> OnPostCalculateAsync(int id) =>
        await ActAsync(id, () => PayrollRunStore.CalculateWithGuardAsync(_db, id, User?.Identity?.Name ?? "system"));

    public async Task<IActionResult> OnPostLockAsync(int id) => await ActAsync(id, () => PayrollRunStore.LockAsync(_db, id));
    public async Task<IActionResult> OnPostIssueAsync(int id) => await ActAsync(id, () => PayrollRunStore.IssueAsync(_db, id));
    public async Task<IActionResult> OnPostSendAsync(int id) => await ActAsync(id, () => PayrollRunStore.SendPayslipsAsync(_db, id));
    public async Task<IActionResult> OnPostReopenAsync(int id) => await ActAsync(id, () => PayrollRunStore.ReopenAsync(_db, id));
    public async Task<IActionResult> OnPostUnlockAsync(int id) => await ActAsync(id, () => PayrollRunStore.UnlockAsync(_db, id));
    public async Task<IActionResult> OnPostDeleteAsync(int id) => await ActAsync(id, () => PayrollRunStore.DeleteRunAsync(_db, id));

    /// <summary>
    /// كل عملية على دفعة تمرّ من هنا، و<b>الفحص يسبق تشغيل العملية</b>.
    ///
    /// <para>كان التوقيع يستقبل <c>Task</c> — أي مهمّةً <b>بدأت فعلاً</b> عند
    /// استدعاء الدالة، فلا موضع لحارسٍ قبلها. صار يستقبل مصنعاً (<c>Func</c>)
    /// فلا تنطلق العملية إلا بعد اجتياز الفحص. التغيير بنيويّ لا تجميليّ: يجعل
    /// «نسيان الحارس بنقطة جديدة» خطأ تصريف لا ثغرةً صامتة.</para>
    /// </summary>
    private async Task<IActionResult> ActAsync(int id, Func<Task<(bool Ok, string Message)>> action)
    {
        if (!await PayrollRunStore.CanAccessRunAsync(
                _db, id, await _companyScope.GetAsync(HttpContext.RequestAborted)))
        {
            TempData["PayrollMessage"] = "الدفعة غير موجودة أو خارج نطاق صلاحيتك.";
            TempData["PayrollOk"] = false;
            return RedirectToPage();
        }

        var (ok, message) = await action();
        TempData["PayrollMessage"] = message;
        TempData["PayrollOk"] = ok;
        return RedirectToPage();
    }
}
