using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>كتالوج عناصر الراتب (/Payroll/SalaryItems) — نمط كيان «عناصر الراتب».</summary>
public class SalaryItemsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public SalaryItemsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<SalaryItemStore.SalaryItem> Items { get; set; } = new();

    public async Task OnGetAsync()
    {
        Items = await SalaryItemStore.ListAsync(_db);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var form = Request.Form;
        var item = new SalaryItemStore.SalaryItem
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            Name = form["Name"].ToString().Trim(),
            NameEn = string.IsNullOrWhiteSpace(form["NameEn"]) ? null : form["NameEn"].ToString().Trim(),
            ItemType = form["ItemType"].ToString() is { Length: > 0 } t ? t : "Income",
            ValueKind = form["ValueKind"].ToString() is { Length: > 0 } v ? v : "Fixed",
            DefaultValue = decimal.TryParse(form["DefaultValue"], out var dv) ? dv : 0,
            Taxable = form["Taxable"] == "true",
            InGross = form["InGross"] == "true",
            Prorated = form["Prorated"] == "true",
            IsActive = form["IsActive"] == "true",
            SortOrder = int.TryParse(form["SortOrder"], out var so) ? so : 0
        };

        if (string.IsNullOrWhiteSpace(item.Name))
        {
            TempData["PayrollMessage"] = "اسم العنصر مطلوب.";
            return RedirectToPage();
        }

        await SalaryItemStore.SaveAsync(_db, item);
        TempData["PayrollMessage"] = item.Id > 0 ? "تم تحديث العنصر." : "تمت إضافة العنصر.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await SalaryItemStore.DeleteAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف العنصر (عناصر النظام محميّة).";
        return RedirectToPage();
    }
}
