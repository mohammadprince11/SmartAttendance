using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Contracts;

/// <summary>
/// تحديثات العقود (نظير «تحديثات العقود» بكيان) — حركات تجديد/تمديد/تعديل بتاريخ
/// سريان، وتبويبا «حركات غير مقفلة / مقفلة» كنمط تحديثات الموظف.
///
/// ⭐ **التمييز بين التمديد والتجديد هو جوهر الشاشة** لا تفصيلاً شكلياً:
/// التمديد يُبقي العقد واحداً (خدمة متصلة، رقم واحد)، والتجديد يُنشئ عقداً جديداً
/// يبدأ بعد انتهاء السابق. والفرق يمسّ مكافأة نهاية الخدمة وفترة التجربة.
/// </summary>
public class MovementsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    private readonly SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider _companyScope;

    public MovementsModel(ApplicationDbContext db, SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true, Name = "locked")] public bool ShowLocked { get; set; }
    [BindProperty(SupportsGet = true, Name = "contract")] public int? PreselectContractId { get; set; }

    [BindProperty] public int EmployeeId { get; set; }
    [BindProperty] public int? ContractId { get; set; }
    [BindProperty] public string MovementKind { get; set; } = ContractPolicy.MovementRenew;
    [BindProperty] public string ContractType { get; set; } = string.Empty;
    [BindProperty] public bool UseDefaultDuration { get; set; } = true;
    [BindProperty] public DateOnly? ToDate { get; set; }
    [BindProperty] public DateOnly EffectiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public string? Notes { get; set; }

    public List<ContractRegisterStore.MovementRow> Movements { get; private set; } = new();
    public List<ContractRegisterStore.ContractRow> CurrentContracts { get; private set; } = new();
    public Dictionary<string, int?> ContractTypes { get; private set; } = new();
    public ContractRegisterStore.ContractRow? Preselected { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();

        if (PreselectContractId is { } id)
        {
            Preselected = CurrentContracts.FirstOrDefault(row => row.Id == id)
                          ?? await ContractRegisterStore.FindContractAsync(_db, await _companyScope.GetAsync(), id);

            if (Preselected is not null)
            {
                EmployeeId = Preselected.EmployeeId;
                ContractId = Preselected.Id;
                ContractType = Preselected.ContractType;
            }
        }
    }

    public async Task<IActionResult> OnPostApplyAsync()
    {
        // التمديد والتعديل يتطلّبان عقداً قائماً؛ بلا عقد لا شيء يُمدَّد.
        if (MovementKind != ContractPolicy.MovementRenew && ContractId is null)
        {
            TempData["ErrorMessage"] = "التمديد والتعديل يتطلّبان اختيار عقد قائم.";
            return RedirectToPage();
        }

        var types = await ContractRegisterStore.LoadContractTypesAsync(_db);
        var current = ContractId is null ? null : await ContractRegisterStore.FindContractAsync(_db, await _companyScope.GetAsync(), ContractId.Value);

        // الموظف **يُشتقّ من العقد** لا يُؤخذ من منسدلة مستقلة: اختيار عقدٍ ثم نسيان
        // اختيار صاحبه كان يفشل الحفظ برسالة مربكة، وأسوأ منه أن اختياراً مخالفاً
        // كان سيُسند حركة عقدِ فلانٍ لغيره.
        if (current is not null)
        {
            EmployeeId = current.EmployeeId;
        }

        if (EmployeeId <= 0 || string.IsNullOrWhiteSpace(ContractType))
        {
            TempData["ErrorMessage"] = "الموظف ونوع العقد إلزاميان.";
            return RedirectToPage(new { contract = ContractId });
        }

        var start = current is null
            ? EffectiveDate
            : ContractPolicy.NextStartDate(MovementKind, current.FromDate, current.ToDate);

        var end = UseDefaultDuration
            ? ContractPolicy.DeriveEndDate(start, types.TryGetValue(ContractType.Trim(), out var months) ? months : null)
            : ToDate;

        await ContractRegisterStore.ApplyMovementAsync(
            _db, await _companyScope.GetAsync(), EmployeeId, ContractId, MovementKind, ContractType.Trim(),
            end, EffectiveDate, Notes, User.Identity?.Name);

        TempData["SuccessMessage"] = MovementKind switch
        {
            ContractPolicy.MovementExtend => "تم تمديد العقد الحالي — الخدمة تبقى متصلة ورقم العقد واحد.",
            ContractPolicy.MovementRenew => "تم تجديد العقد — أُنشئ عقد جديد وبقي السابق بالسجلّ.",
            _ => "تم تعديل بنود العقد."
        };

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLockAsync(int id)
    {
        await ContractRegisterStore.LockMovementAsync(_db, id, User.Identity?.Name);
        TempData["SuccessMessage"] = "تم قفل الحركة — لم تعد قابلة للتعديل أو الحذف.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await ContractRegisterStore.DeleteMovementAsync(_db, id);
        TempData["SuccessMessage"] = "تم حذف الحركة.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        await ContractRegisterStore.EnsureAsync(_db);

        Movements = await ContractRegisterStore.LoadMovementsAsync(_db, await _companyScope.GetAsync(), ShowLocked);
        ContractTypes = await ContractRegisterStore.LoadContractTypesAsync(_db);

        // العقود الحالية فقط: التجديد والتمديد يقعان على ما هو سارٍ، لا على أرشيف.
        CurrentContracts = (await ContractRegisterStore.LoadContractsAsync(_db, await _companyScope.GetAsync()))
            .Where(row => row.IsCurrent)
            .ToList();
    }
}
