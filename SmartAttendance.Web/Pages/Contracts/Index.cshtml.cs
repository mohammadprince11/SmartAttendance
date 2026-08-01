using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Contracts;

/// <summary>
/// سجلّ عقود الموظفين (نظير «عقود الموظفين» بكيان) — الشاشة التي كانت غائبةً تماماً.
///
/// كان العقد عندنا **ثلاثة حقول بملف الموظف تُدهَس عند التغيير**، فسؤال «كم عقداً
/// جُدِّد له؟» و«متى تغيّر نوع عقده؟» بلا جواب. صار سجلّاً: التجديد يضيف صفّاً
/// ويُبقي السابق.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "employee")] public int? EmployeeFilter { get; set; }
    /// <summary>يُظهر المنتهية أيضاً. مخفيّة افتراضياً لأن السجلّ يتضخّم بها بسرعة.</summary>
    [BindProperty(SupportsGet = true, Name = "expired")] public bool ShowExpired { get; set; }

    [BindProperty] public int ContractId { get; set; }
    [BindProperty] public int EmployeeId { get; set; }
    [BindProperty] public string? ContractNo { get; set; }
    [BindProperty] public string ContractType { get; set; } = string.Empty;
    [BindProperty] public DateOnly FromDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public DateOnly? ToDate { get; set; }
    [BindProperty] public bool UseDefaultDuration { get; set; } = true;
    [BindProperty] public bool MakeCurrent { get; set; } = true;
    [BindProperty] public string? Note { get; set; }

    public List<ContractRegisterStore.ContractRow> Contracts { get; private set; } = new();
    public Dictionary<string, int?> ContractTypes { get; private set; } = new();
    public List<(int Id, string Label)> Employees { get; private set; } = new();
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>عقود تنتهي خلال 60 يوماً — الرقم الذي يفتح الشاشة عادةً.</summary>
    public int ExpiringSoon => Contracts.Count(row =>
        row.IsCurrent && row.Remaining(Today) is > 0 and <= 60);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (EmployeeId <= 0 || string.IsNullOrWhiteSpace(ContractType))
        {
            TempData["ErrorMessage"] = "الموظف ونوع العقد إلزاميان.";
            return RedirectToPage();
        }

        var types = await ContractRegisterStore.LoadContractTypesAsync(_db);
        var end = ResolveEnd(types);

        if (ContractId > 0)
        {
            await ContractRegisterStore.UpdateContractAsync(
                _db, ContractId, ContractNo, ContractType.Trim(), FromDate, end, Note, User.Identity?.Name);
            TempData["SuccessMessage"] = "تم تحديث العقد.";
        }
        else
        {
            await ContractRegisterStore.AddContractAsync(
                _db, EmployeeId, ContractNo, ContractType.Trim(), FromDate, end,
                MakeCurrent, null, null, null, Note, User.Identity?.Name);
            TempData["SuccessMessage"] = "تم إضافة العقد للسجلّ.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await ContractRegisterStore.DeleteContractAsync(_db, id);
        TempData["SuccessMessage"] = "تم حذف العقد من السجلّ (حذف منطقي).";
        return RedirectToPage();
    }

    /// <summary>
    /// تاريخ الانتهاء: إمّا مشتقّ من المدة الافتراضية لنوع العقد (نمط كيان) أو
    /// محدَّد يدوياً. نوعٌ بلا مدة افتراضية ⟹ عقد غير محدود، لا تاريخ مخترَع.
    /// </summary>
    private DateOnly? ResolveEnd(Dictionary<string, int?> types)
    {
        if (!UseDefaultDuration)
        {
            return ToDate;
        }

        var months = types.TryGetValue(ContractType.Trim(), out var value) ? value : null;
        return ContractPolicy.DeriveEndDate(FromDate, months);
    }

    private async Task LoadAsync()
    {
        await ContractRegisterStore.EnsureAsync(_db);

        Contracts = await ContractRegisterStore.LoadContractsAsync(_db, EmployeeFilter, Search);
        if (!ShowExpired)
        {
            Contracts = Contracts
                .Where(row => row.ToDate is null || row.ToDate >= Today)
                .ToList();
        }

        ContractTypes = await ContractRegisterStore.LoadContractTypesAsync(_db);

        Employees = (await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT Id, ISNULL(FullName, N'') AS FullName, ISNULL(EmployeeNo, N'') AS EmployeeNo
FROM Employees
WHERE IsActive = 1 AND ISNULL(IsDeleted, 0) = 0
ORDER BY FullName;
""",
            command => { },
            reader => (
                HrmsDatabase.GetInt(reader, "Id"),
                $"{HrmsDatabase.GetString(reader, "FullName")} ({HrmsDatabase.GetString(reader, "EmployeeNo")})")))
            .ToList();
    }
}
