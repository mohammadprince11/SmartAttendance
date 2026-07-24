using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.OrgStructures;

/// <summary>
/// الهياكل الثلاث المتوازية (/OrgStructures) — نمط كيان قسم 17.ي: ثلاثة أبعاد
/// تنظيمية على نفس الموظفين (وحدات الأعمال / الهرمي / الوظيفي)، كلٌّ شجرة تجميع.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }

    public List<CompanyOption> Companies { get; set; } = new();
    public string CompanyName { get; set; } = string.Empty;

    public OrgStructuresBuilder.Node? BusinessUnits { get; set; }
    public OrgStructuresBuilder.Node? Functional { get; set; }
    public List<OrgStructuresBuilder.Node> Hierarchy { get; set; } = new();
    public int HierWithManager { get; set; }
    public int HierWithoutManager { get; set; }
    public int HierManagers { get; set; }

    public sealed class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public async Task OnGetAsync()
    {
        Companies = await _db.Companies.AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CompanyOption { Id = c.Id, Name = c.Name })
            .ToListAsync();

        CompanyId = CompanySelectionContext.Resolve(HttpContext, CompanyId, Companies.Select(x => x.Id).ToArray());
        if (CompanyId is not > 0) return;

        CompanyName = Companies.FirstOrDefault(c => c.Id == CompanyId)?.Name ?? "الشركة";

        BusinessUnits = await OrgStructuresBuilder.BusinessUnitsAsync(_db, CompanyId.Value, CompanyName);
        Functional = await OrgStructuresBuilder.FunctionalAsync(_db, CompanyId.Value, CompanyName);
        (Hierarchy, HierWithManager, HierWithoutManager, HierManagers) =
            await OrgStructuresBuilder.HierarchyAsync(_db, CompanyId.Value);
    }
}
