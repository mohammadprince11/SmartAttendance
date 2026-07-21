using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// باني حقول الكيانات (الداينمك مرحلة 2 — نمط كيان «الحقول الإضافية»):
/// الأدمن يضيف حقولاً مخصصة لأي كيان فرعي بملف الموظف وتظهر بسلايدات الملف 360°.
/// </summary>
public class EntityFieldsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public EntityFieldsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string Entity { get; set; } = "Dependent";

    public Dictionary<string, List<EntityCustomFields.FieldDefinition>> AllDefinitions { get; set; } = new();
    public List<EntityCustomFields.FieldDefinition> Fields { get; set; } = new();
    public EntityCustomFields.EntityDef? SelectedEntity { get; set; }

    public async Task OnGetAsync()
    {
        SelectedEntity = EntityCustomFields.Entities.FirstOrDefault(e => e.Key.Equals(Entity, StringComparison.OrdinalIgnoreCase))
                         ?? EntityCustomFields.Entities[0];
        Entity = SelectedEntity.Key;

        AllDefinitions = await EntityCustomFields.DefinitionsByEntityAsync(_dbContext, activeOnly: false);
        Fields = AllDefinitions.TryGetValue(Entity, out var list) ? list : new();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id, string fieldLabel, string fieldType, string? fieldOptions, bool isRequired, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(fieldLabel))
        {
            TempData["SuccessMessage"] = "تسمية الحقل مطلوبة.";
            return RedirectToPage(new { Entity });
        }

        await EntityCustomFields.SaveDefinitionAsync(_dbContext, new EntityCustomFields.FieldDefinition
        {
            Id = id,
            EntityKey = Entity,
            FieldLabel = fieldLabel.Trim(),
            FieldType = fieldType,
            FieldOptions = fieldOptions ?? string.Empty,
            IsRequired = isRequired,
            IsActive = isActive
        });

        TempData["SuccessMessage"] = id > 0 ? "تم تحديث الحقل." : "تمت إضافة الحقل.";
        return RedirectToPage(new { Entity });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await EntityCustomFields.DeleteDefinitionAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم حذف الحقل وقيمه المخزّنة.";
        return RedirectToPage(new { Entity });
    }

    public async Task<IActionResult> OnPostReorderAsync(string order)
    {
        var ids = (order ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .Where(value => value > 0)
            .ToList();

        await EntityCustomFields.ReorderAsync(_dbContext, Entity, ids);
        return new JsonResult(new { ok = true });
    }
}
