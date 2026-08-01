using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Pages.Shared;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// تخصيص رئيس وحدة مؤقت — إجازة مديرٍ كانت تجمّد كل ما يُوجَّه إليه.
/// المنطق كلّه بـ<see cref="TemporaryHeadPolicy"/>، وهذه الصفحة إدخالٌ وعرض فقط.
/// </summary>
public class TemporaryHeadsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public TemporaryHeadsModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "edit")] public int? EditingId { get; set; }

    [BindProperty] public int AllocationId { get; set; }
    [BindProperty] public int DepartmentId { get; set; }
    [BindProperty] public int HeadEmployeeId { get; set; }
    [BindProperty] public DateOnly FromDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public DateOnly ToDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(7);
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public string? Note { get; set; }

    public List<TemporaryHeadStore.Row> Allocations { get; private set; } = new();
    public List<(int Id, string Name)> Departments { get; private set; } = new();

    /// <summary>رمز الرئيس المؤقت المحسوم واسمه — لتعبئة المنتقي ابتداءً.</summary>
    public string? HeadEmployeeCode { get; private set; }

    public string? HeadEmployeeName { get; private set; }
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);

    public int ActiveNow => Allocations.Count(row => TemporaryHeadPolicy.IsEffective(row.ToAllocation(), Today));

    public async Task OnGetAsync()
    {
        await LoadAsync();

        if (EditingId is > 0 && Allocations.FirstOrDefault(row => row.Id == EditingId.Value) is { } row)
        {
            AllocationId = row.Id;
            DepartmentId = row.DepartmentId;
            HeadEmployeeId = row.HeadEmployeeId;
            FromDate = row.FromDate;
            ToDate = row.ToDate;
            IsActive = row.IsActive;
            Note = row.Note;
        }

        await LoadPickerIdentityAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (DepartmentId <= 0 || HeadEmployeeId <= 0)
        {
            TempData["ErrorMessage"] = "الوحدة والرئيس المؤقت إلزاميان.";
            return RedirectToPage();
        }

        // مدىً مقلوب يُحفظ بصمت ولا يسري أبداً — عطل صامت، فيُرفض هنا.
        if (!TemporaryHeadPolicy.IsValidRange(FromDate, ToDate))
        {
            TempData["ErrorMessage"] = "تاريخ النهاية قبل البداية — الإسناد لن يسري أبداً.";
            return RedirectToPage();
        }

        // المعرّف المُعاد لازم لفحص التداخل: بإسنادٍ جديد يبقى AllocationId صفراً،
        // فيرى الصفُّ المحفوظ نفسَه «إسناداً آخر» ويحذّر من تداخله مع ذاته دائماً.
        var savedId = await TemporaryHeadStore.SaveAsync(
            _db, AllocationId, DepartmentId, HeadEmployeeId, FromDate, ToDate, IsActive, Note, User.Identity?.Name);

        var overlaps = TemporaryHeadPolicy.Overlapping(
            new TemporaryHeadPolicy.Allocation(savedId, DepartmentId, HeadEmployeeId, FromDate, ToDate, IsActive),
            (await TemporaryHeadStore.LoadAsync(_db)).Select(row => row.ToAllocation()));

        TempData["SuccessMessage"] = overlaps.Count > 0
            ? $"تم الحفظ — تنبيه: يتداخل مع {overlaps.Count} إسناداً لنفس الوحدة، والأحدث هو الذي يسري."
            : "تم حفظ الإسناد.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await TemporaryHeadStore.DeleteAsync(_db, id);
        TempData["SuccessMessage"] = "تم حذف الإسناد.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Allocations = await TemporaryHeadStore.LoadAsync(_db);

        Departments = (await HrmsDatabase.QueryAsync(
            _db,
            "SELECT Id, ISNULL(Name, N'') AS Name FROM Departments WHERE ISNULL(IsDeleted, 0) = 0 ORDER BY Name;",
            command => { },
            reader => (HrmsDatabase.GetInt(reader, "Id"), HrmsDatabase.GetString(reader, "Name")))).ToList();

    }

    private async Task LoadPickerIdentityAsync()
    {
        var identity = await EmployeePickerLookup.LoadAsync(_db, HeadEmployeeId);
        HeadEmployeeCode = identity?.Code;
        HeadEmployeeName = identity?.Name;
    }
}
