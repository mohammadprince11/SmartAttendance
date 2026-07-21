using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// استوديو الحقول (نمط كيان + طلب المستخدم «الأدمن مبرمج»): لكل حقل بشاشة الموظف —
/// إظهار/إخفاء، إلزامي، تسمية مخصصة، وترتيب بالسحب. الحقول الجوهرية مقفلة ظاهرة إلزامية.
/// </summary>
public class FieldControlModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public FieldControlModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Dictionary<string, EmployeeFieldControl.FieldSetting> Settings { get; set; } = new();

    /// <summary>الأدوار المخوّلة برؤية الحقول الحساسة (الرواتب) — نمط أدوار كيان.</summary>
    public HashSet<string> SensitiveSalaryRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static readonly string[] AllRoles =
        { "Admin", "HR Manager", "HR Officer", "Branch Manager", "Finance Viewer" };

    public async Task OnGetAsync()
    {
        Settings = await EmployeeFieldControl.GetSettingsAsync(_dbContext);
        var csv = await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.GetAsync(
            _dbContext, "Sensitive.SalaryRoles", "Admin,HR Manager");
        SensitiveSalaryRoles = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var form = Request.Form;
        var keys = form["FieldKey"];
        var settings = new List<EmployeeFieldControl.FieldSetting>();

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i] ?? string.Empty;
            settings.Add(new EmployeeFieldControl.FieldSetting
            {
                Key = key,
                // كل صف يرسل قيمه بأسماء مفتاحية حتى لا تختل المصفوفات مع الترتيب بالسحب
                IsVisible = form[$"vis_{key}"] == "true",
                IsRequired = form[$"req_{key}"] == "true",
                CustomLabel = form[$"label_{key}"],
                DisplayOrder = i + 1
            });
        }

        await EmployeeFieldControl.SaveSettingsAsync(_dbContext, settings);

        // الأدوار الحساسة: Admin دائماً ضمنها حتى لا يقفل أحد نفسه بالخطأ.
        var sensitiveRoles = form["sensitiveRoles"].Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (!sensitiveRoles.Contains("Admin")) sensitiveRoles.Insert(0, "Admin");
        await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.SetAsync(
            _dbContext, "Sensitive.SalaryRoles", string.Join(",", sensitiveRoles));

        TempData["SuccessMessage"] = "تم حفظ إعدادات الحقول — الإخفاء والتسميات والترتيب تنطبق على شاشتي إنشاء وتعديل الموظف.";
        return RedirectToPage();
    }
}
