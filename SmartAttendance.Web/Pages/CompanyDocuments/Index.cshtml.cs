using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.CompanyDocuments;

/// <summary>
/// وثائق الشركة وفئاتها — وثائق **مشتركة** (لوائح · نماذج · تعاميم) تمييزاً عن
/// وثائق الموظف. الجمهور بمحرّك الشروط العام.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(ApplicationDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    [BindProperty(SupportsGet = true, Name = "edit")] public int? EditingId { get; set; }
    [BindProperty(SupportsGet = true, Name = "cat")] public int? CategoryFilter { get; set; }

    [BindProperty] public int DocumentId { get; set; }
    [BindProperty] public int? CategoryId { get; set; }
    [BindProperty] public string Title { get; set; } = string.Empty;
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public DateOnly? ExpiryDate { get; set; }
    [BindProperty] public bool VisibleToEmployees { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public string Conditions { get; set; } = string.Empty;
    [BindProperty] public IFormFile? Upload { get; set; }

    [BindProperty] public int CategoryEditId { get; set; }
    [BindProperty] public string CategoryName { get; set; } = string.Empty;
    [BindProperty] public string? CategoryDescription { get; set; }
    [BindProperty] public int CategorySortOrder { get; set; }

    public List<CompanyDocumentStore.Category> Categories { get; private set; } = new();
    public List<CompanyDocumentStore.Document> Documents { get; private set; } = new();
    public string CriteriaJson { get; private set; } = "[]";
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);

    public int ExpiredCount => Documents.Count(document => document.IsExpired(Today));

    public async Task OnGetAsync()
    {
        await LoadAsync();

        if (EditingId is > 0 && await CompanyDocumentStore.FindDocumentAsync(_db, EditingId.Value) is { } document)
        {
            DocumentId = document.Id;
            CategoryId = document.CategoryId;
            Title = document.Title;
            Description = document.Description;
            ExpiryDate = document.ExpiryDate;
            VisibleToEmployees = document.VisibleToEmployees;
            IsActive = document.IsActive;
            Conditions = document.ConditionsJson;
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            TempData["ErrorMessage"] = "عنوان الوثيقة إلزامي.";
            return RedirectToPage();
        }

        string? fileName = null;
        string? storageKey = null;

        if (Upload is { Length: > 0 })
        {
            var extension = Path.GetExtension(Upload.FileName);

            if (!ProtectedFileStore.IsAllowedExtension(extension))
            {
                TempData["ErrorMessage"] = "نوع الملف غير مسموح.";
                return RedirectToPage(new { edit = DocumentId });
            }

            if (!ProtectedFileStore.IsAllowedSize(Upload.Length))
            {
                TempData["ErrorMessage"] = "حجم الملف يتجاوز الحدّ المسموح (10 ميغابايت).";
                return RedirectToPage(new { edit = DocumentId });
            }

            // معرّف الموظف صفر: وثيقة شركة لا تخصّ موظفاً، فتُعزل بمجلّدها الخاص
            // داخل نفس الجذر المحميّ خارج wwwroot.
            storageKey = ProtectedFileStore.BuildStorageKey(0, "company-doc", extension);
            var root = ProtectedFileStore.ResolveRoot(_environment.ContentRootPath);

            await using var stream = Upload.OpenReadStream();
            if (!await ProtectedFileStore.SaveAsync(root, storageKey, stream))
            {
                TempData["ErrorMessage"] = "تعذّر حفظ الملف.";
                return RedirectToPage(new { edit = DocumentId });
            }

            fileName = Path.GetFileName(Upload.FileName);
        }

        await CompanyDocumentStore.SaveDocumentAsync(
            _db, DocumentId, CategoryId, Title.Trim(), Description, ExpiryDate,
            VisibleToEmployees, IsActive, HrConditions.Deserialize(Conditions),
            fileName, storageKey, User.Identity?.Name);

        TempData["SuccessMessage"] = DocumentId > 0 ? "تم تحديث الوثيقة." : "تم إضافة الوثيقة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await CompanyDocumentStore.DeleteDocumentAsync(_db, id);
        TempData["SuccessMessage"] = "تم حذف الوثيقة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(CategoryName))
        {
            TempData["ErrorMessage"] = "اسم الفئة إلزامي.";
            return RedirectToPage();
        }

        await CompanyDocumentStore.SaveCategoryAsync(
            _db, CategoryEditId, CategoryName.Trim(), CategoryDescription, CategorySortOrder, true);

        TempData["SuccessMessage"] = "تم حفظ الفئة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id)
    {
        await CompanyDocumentStore.DeleteCategoryAsync(_db, id);
        TempData["SuccessMessage"] = "حُذفت الفئة — ووثائقها بقيت بلا فئة، لم تُحذف.";
        return RedirectToPage();
    }

    /// <summary>تنزيل من الجذر المحميّ خارج wwwroot — لا رابط مباشر لأي ملف.</summary>
    public async Task<IActionResult> OnGetDownloadAsync(int id)
    {
        var document = await CompanyDocumentStore.FindDocumentAsync(_db, id);
        if (document is null || !document.HasFile)
        {
            return NotFound();
        }

        var root = ProtectedFileStore.ResolveRoot(_environment.ContentRootPath);
        if (!ProtectedFileStore.TryResolvePhysicalPath(root, document.StorageKey, out var physicalPath)
            || !System.IO.File.Exists(physicalPath))
        {
            return NotFound();
        }

        var extension = Path.GetExtension(document.FileName ?? physicalPath);
        return PhysicalFile(
            physicalPath,
            ProtectedFileStore.ContentTypeFor(extension),
            document.FileName ?? "document" + extension);
    }

    private async Task LoadAsync()
    {
        Categories = await CompanyDocumentStore.LoadCategoriesAsync(_db);
        Documents = await CompanyDocumentStore.LoadDocumentsAsync(_db, CategoryFilter);
        CriteriaJson = await HrConditionOptions.BuildCatalogJsonAsync(_db);
    }
}
