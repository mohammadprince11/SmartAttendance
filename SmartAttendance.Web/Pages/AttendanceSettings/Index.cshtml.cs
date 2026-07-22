using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AttendanceSettings;

/// <summary>
/// إعدادات الحضور (/AttendanceSettings) — المرحلة 2 من مودل الحضور بنمط كيان:
/// قسم دلالات البصمات (تصنيف ثنائي اللغة للبصمات) وقسم مصادر بيانات الحضور
/// (إكسل/عرض قاعدة بيانات/API). راجع قسمي 3.2 و3.3 و15 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<PunchSemanticStore.PunchSemantic> Semantics { get; set; } = new();
    public List<AttendanceSourceStore.AttendanceSource> Sources { get; set; } = new();

    public async Task OnGetAsync()
    {
        Semantics = await PunchSemanticStore.ListAsync(_dbContext);
        Sources = await AttendanceSourceStore.ListAsync(_dbContext);
    }

    public async Task<IActionResult> OnPostSaveSemanticAsync()
    {
        var form = Request.Form;
        var semantic = new PunchSemanticStore.PunchSemantic
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            Name = form["Name"].ToString().Trim(),
            NameEn = string.IsNullOrWhiteSpace(form["NameEn"]) ? null : form["NameEn"].ToString().Trim(),
            IsActive = form["IsActive"] == "true",
            SortOrder = int.TryParse(form["SortOrder"], out var sort) ? sort : 0
        };

        if (string.IsNullOrWhiteSpace(semantic.Name))
        {
            TempData["SuccessMessage"] = "اسم الدلالة مطلوب.";
            return RedirectToPage();
        }

        await PunchSemanticStore.SaveAsync(_dbContext, semantic);
        TempData["SuccessMessage"] = semantic.Id > 0 ? "تم تحديث الدلالة." : "تمت إضافة الدلالة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteSemanticAsync(int id)
    {
        await PunchSemanticStore.DeleteAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم حذف الدلالة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveSourceAsync()
    {
        var form = Request.Form;
        var source = new AttendanceSourceStore.AttendanceSource
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            Name = form["Name"].ToString().Trim(),
            ReadType = form["ReadType"].ToString() is { Length: > 0 } type ? type : "Excel",
            ConfigValue = string.IsNullOrWhiteSpace(form["ConfigValue"]) ? null : form["ConfigValue"].ToString().Trim(),
            UsesSemantics = form["UsesSemantics"] == "true",
            IsActive = form["IsActive"] == "true"
        };

        if (string.IsNullOrWhiteSpace(source.Name))
        {
            TempData["SuccessMessage"] = "اسم المصدر مطلوب.";
            return RedirectToPage();
        }

        await AttendanceSourceStore.SaveAsync(_dbContext, source);
        TempData["SuccessMessage"] = source.Id > 0 ? "تم تحديث المصدر." : "تمت إضافة المصدر.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteSourceAsync(int id)
    {
        await AttendanceSourceStore.DeleteAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم حذف المصدر.";
        return RedirectToPage();
    }
}
