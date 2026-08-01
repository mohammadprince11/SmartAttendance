using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.Theming;

namespace SmartAttendance.Web.Pages.DesignLab;

/// <summary>
/// مختبر المقاسات — تُجرَّب أحجام عناصر الواجهة حيّاً ثم تُعتمد للنظام كلّه.
///
/// **لماذا؟** ارتفاع عنصر التحكّم كان مثبَّتاً بالكود بثمانِ قيمٍ مختلفة عبر 71
/// ملف CSS، فما كان لأحدٍ أن «يوحّد الأزرار» إلا بتعديل عشرات الملفات يدوياً.
/// هنا مصدرُ حقيقةٍ واحد، والمعاينة داخل الصفحة نفسها بعناصر حقيقية لا برسومٍ
/// تحاكيها — فما تراه هو ما سيصير عليه النظام.
///
/// ⚠️ **الاعتماد يكتب لكل المستخدمين** لا لصاحب الجلسة، فهو مقصورٌ على من يملك
/// صلاحية إدارة الإعدادات.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPermissionAuthorizationService _permissions;

    public IndexModel(ApplicationDbContext db, IPermissionAuthorizationService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public IReadOnlyDictionary<string, double> Saved { get; private set; } =
        new Dictionary<string, double>();

    public string? StatusMessage { get; private set; }

    public IReadOnlyList<DesignTokenCatalog.Token> TokensIn(string group) =>
        DesignTokenCatalog.All.Where(token => token.Group == group).ToList();

    public double Value(DesignTokenCatalog.Token token) => DesignTokenStore.Effective(Saved, token);

    public bool IsCustomised(DesignTokenCatalog.Token token) => Saved.ContainsKey(token.Key);

    public int CustomisedCount => Saved.Count;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanManageAsync())
        {
            return Forbid();
        }

        StatusMessage = TempData["DesignLabStatus"] as string;
        Saved = await DesignTokenStore.LoadAsync(_db);
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync()
    {
        if (!await CanManageAsync())
        {
            return Forbid();
        }

        // تُقرأ من النموذج بمفاتيح الكتالوج لا بما أرسله المتصفّح: مفتاحٌ ملفَّق
        // لا يقابل توكناً يُتجاهَل، والقيم تُقصر على مداها بـ`Clamp` داخل المخزن.
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in DesignTokenCatalog.All)
        {
            var raw = Request.Form["t_" + token.Key].ToString();
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                values[token.Key] = parsed;
            }
        }

        await DesignTokenStore.SaveAsync(_db, values, User.Identity?.Name);

        TempData["DesignLabStatus"] = "اعتُمدت المقاسات وسرت على النظام كلّه.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        if (!await CanManageAsync())
        {
            return Forbid();
        }

        await DesignTokenStore.ResetAsync(_db);

        TempData["DesignLabStatus"] = "أُعيدت كل المقاسات لافتراضها.";
        return RedirectToPage();
    }

    private async Task<bool> CanManageAsync()
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        return await _permissions.HasPermissionAsync(
            systemUserId,
            PeoplePermissionCodes.ManagePermissions,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ManagePermissions),
            HttpContext.RequestAborted);
    }
}
