using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

public class LookupsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public LookupsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<HrLookups.LookupCategory> Categories => HrLookups.Categories;

    public List<HrLookups.LookupItem> Items { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Items = await HrLookups.LoadAsync(_dbContext);
    }

    public async Task<IActionResult> OnPostAddAsync(string category, string arabicName, string? englishName, int sortOrder)
    {
        await HrLookups.EnsureSchemaAsync(_dbContext);

        if (!HrLookups.Categories.Any(c => c.Key == category) || string.IsNullOrWhiteSpace(arabicName))
        {
            Message = "الفئة والاسم العربي مطلوبان.";
            return RedirectToPage(null, null, null, category);
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO HrLookups (Category, ArabicName, EnglishName, SortOrder)
VALUES (@Category, @ArabicName, @EnglishName, @SortOrder);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Category", category);
                HrmsDatabase.AddParameter(command, "@ArabicName", arabicName.Trim());
                HrmsDatabase.AddParameter(command, "@EnglishName",
                    string.IsNullOrWhiteSpace(englishName) ? DBNull.Value : englishName.Trim());
                HrmsDatabase.AddParameter(command, "@SortOrder", Math.Max(0, sortOrder));
            });

        Message = "تمت الإضافة.";
        return RedirectToPage(null, null, null, category);
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, string category)
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "UPDATE HrLookups SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        Message = "تم تحديث الحالة.";
        return RedirectToPage(null, null, null, category);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, string category)
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "DELETE FROM HrLookups WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        Message = "تم الحذف.";
        return RedirectToPage(null, null, null, category);
    }
}
