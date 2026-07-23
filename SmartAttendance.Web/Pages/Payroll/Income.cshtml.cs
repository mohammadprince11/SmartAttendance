using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// حركات الدخل (/Payroll/Income) — نمط كيان «حركات الدخل»: إدخالات دخل لكل موظف×فترة
/// (مكافأة/حافز/بدل لمرة) تُغذّي احتساب المسير. تُدار بالفترة (سنة/شهر).
/// </summary>
public class IncomeModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IncomeModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; } = DateTime.Today.Year;

    [BindProperty(SupportsGet = true)]
    public int Month { get; set; } = DateTime.Today.Month;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<PayrollTransactionStore.Transaction> Items { get; set; } = new();
    public List<EmployeeOption> Employees { get; set; } = new();
    public List<SalaryItemStore.SalaryItem> IncomeItems { get; set; } = new();

    public int TotalCount { get; set; }
    public decimal TotalAmount { get; set; }
    public int EmployeesCovered { get; set; }

    public sealed class EmployeeOption
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SearchText => $"{No} - {Name}";
    }

    public async Task OnGetAsync()
    {
        if (Month is < 1 or > 12) Month = DateTime.Today.Month;

        Items = await PayrollTransactionStore.ListAsync(_db, Year, Month, PayrollTransactionStore.Income, Search);
        IncomeItems = await SalaryItemStore.ActiveIncomeItemsAsync(_db);
        Employees = await HrmsDatabase.QueryAsync(_db,
            "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1 ORDER BY FullName;",
            command => { },
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Name = HrmsDatabase.GetString(reader, "FullName")
            });

        TotalCount = Items.Count;
        TotalAmount = Items.Sum(x => x.Amount);
        EmployeesCovered = Items.Select(x => x.EmployeeId).Distinct().Count();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var form = Request.Form;
        var tx = new PayrollTransactionStore.Transaction
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            EmployeeId = int.TryParse(form["EmployeeId"], out var emp) ? emp : 0,
            Year = int.TryParse(form["FYear"], out var y) ? y : Year,
            Month = int.TryParse(form["FMonth"], out var m) ? m : Month,
            SalaryItemId = int.TryParse(form["SalaryItemId"], out var si) && si > 0 ? si : null,
            ItemName = form["ItemName"].ToString().Trim(),
            Amount = decimal.TryParse(form["Amount"], out var a) ? a : 0,
            TxType = PayrollTransactionStore.Income,
            Taxable = form["Taxable"] == "true",
            Note = string.IsNullOrWhiteSpace(form["Note"]) ? null : form["Note"].ToString().Trim()
        };

        if (tx.EmployeeId <= 0)
        {
            TempData["PayrollMessage"] = "اختر الموظف.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Year, Month });
        }
        if (string.IsNullOrWhiteSpace(tx.ItemName))
        {
            TempData["PayrollMessage"] = "اسم بند الدخل مطلوب.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Year, Month });
        }
        if (tx.Amount <= 0)
        {
            TempData["PayrollMessage"] = "المبلغ يجب أن يكون أكبر من صفر.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Year, Month });
        }

        await PayrollTransactionStore.SaveAsync(_db, tx, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = tx.Id > 0 ? "تم تحديث حركة الدخل." : "تمت إضافة حركة الدخل.";
        return RedirectToPage(new { Year = tx.Year, Month = tx.Month });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await PayrollTransactionStore.DeleteAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف حركة الدخل.";
        return RedirectToPage(new { Year, Month });
    }
}
