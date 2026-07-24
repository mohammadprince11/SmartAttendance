using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

public class RequestTypesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    public RequestTypesModel(ApplicationDbContext db) => _db = db;

    public List<RequestTypeStore.Category> Categories { get; private set; } = new();
    public List<RequestTypeStore.ReqType> Types { get; private set; } = new();
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await RequestTypeStore.EnsureAsync(_db);
        Categories = await RequestTypeStore.ListCategoriesAsync(_db);
        Types = await RequestTypeStore.ListTypesAsync(_db);
    }

    public async Task<IActionResult> OnPostSaveCategoryAsync(int id, string name, string? nameEn, int order, bool active)
    {
        await RequestTypeStore.EnsureAsync(_db);
        if (string.IsNullOrWhiteSpace(name)) { StatusMessage = "اسم التبويب مطلوب."; return RedirectToPage(); }
        await RequestTypeStore.SaveCategoryAsync(_db, new RequestTypeStore.Category
        { Id = id, Name = name.Trim(), NameEn = nameEn?.Trim(), DisplayOrder = order, IsActive = active });
        StatusMessage = "تم حفظ التبويب.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id)
    {
        await RequestTypeStore.DeleteCategoryAsync(_db, id);
        StatusMessage = "تم حذف التبويب وأنواعه.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveTypeAsync(
        int id, int categoryId, string name, string? nameEn,
        int? allowedDays, string repeat, int serviceMonths, string gender,
        string paidMode, bool deductFromSalary, bool countsInService, bool hasBalance,
        int? maxPerRequest, bool attachmentRequired, string? attachmentLabel,
        bool needsTime, bool active, int order)
    {
        await RequestTypeStore.EnsureAsync(_db);
        if (categoryId <= 0 || string.IsNullOrWhiteSpace(name))
        { StatusMessage = "التبويب والاسم مطلوبان."; return RedirectToPage(); }

        await RequestTypeStore.SaveTypeAsync(_db, new RequestTypeStore.ReqType
        {
            Id = id,
            CategoryId = categoryId,
            Name = name.Trim(),
            NameEn = nameEn?.Trim(),
            AllowedDays = allowedDays,
            Repeat = string.IsNullOrWhiteSpace(repeat) ? "yearly" : repeat,
            ServiceMonths = serviceMonths,
            Gender = string.IsNullOrWhiteSpace(gender) ? "all" : gender,
            PaidMode = string.IsNullOrWhiteSpace(paidMode) ? "full" : paidMode,
            DeductFromSalary = deductFromSalary,
            CountsInService = countsInService,
            HasBalance = hasBalance,
            MaxPerRequest = maxPerRequest,
            AttachmentRequired = attachmentRequired,
            AttachmentLabel = attachmentLabel?.Trim(),
            NeedsTime = needsTime,
            IsActive = active,
            DisplayOrder = order
        });
        StatusMessage = "تم حفظ نوع الطلب.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTypeAsync(int id)
    {
        await RequestTypeStore.DeleteTypeAsync(_db, id);
        StatusMessage = "تم حذف النوع.";
        return RedirectToPage();
    }
}
