using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// الحركات (/Payroll/Transactions) — مطابقة كيان «حركات الدخل/الإقتطاع». تعرض النوع
/// (دخل/اقتطاع) عبر Type، بقائمة (رقم مرجعي/موظف/بند/مصدر/تاريخ/مبلغ/حالة) وفلاتر،
/// ونموذج «حركة جديدة» كامل (نوع الدفعة، أثر رجعي، جدولة/أقساط، مركز تكلفة، مرفق).
/// </summary>
public class TransactionsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public TransactionsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = PayrollTransactionStore.Income;   // Income | Deduction

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; } = DateTime.Today.Year;

    [BindProperty(SupportsGet = true)]
    public int Month { get; set; } = DateTime.Today.Month;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Item { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    /// <summary>التبويب: Open (غير مقفلة، قابلة للتعديل) | Locked (مقفلة، قراءة فقط).</summary>
    [BindProperty(SupportsGet = true)]
    public string Lock { get; set; } = "Open";

    public bool IsReadOnly => Lock == "Locked";

    public List<PayrollTransactionStore.Transaction> Items { get; set; } = new();
    public List<EmployeeOption> Employees { get; set; } = new();
    public List<SalaryItemStore.SalaryItem> Catalog { get; set; } = new();

    public int TotalCount { get; set; }
    public decimal TotalAmount { get; set; }
    public int EmployeesCovered { get; set; }

    public bool IsDeduction => Type == PayrollTransactionStore.Deduction;
    public string TypeTitle => IsDeduction ? "حركات الاقتطاع" : "حركات الدخل";
    public string ItemLabel => IsDeduction ? "بند الاقتطاع" : "بند الدخل";

    public sealed class EmployeeOption
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SearchText => $"{No} - {Name}";
    }

    public async Task OnGetAsync()
    {
        if (Type != PayrollTransactionStore.Deduction) Type = PayrollTransactionStore.Income;
        if (Month is < 1 or > 12) Month = DateTime.Today.Month;

        if (Lock != "Locked") Lock = "Open";
        // القفل لكل حركة: التبويب يفلتر بحالة قفل الحركة نفسها
        Items = await PayrollTransactionStore.ListAsync(_db, Year, Month, Type, Search, Item, Status, locked: Lock == "Locked");

        var all = await SalaryItemStore.ListAsync(_db);
        Catalog = IsDeduction
            ? all.Where(x => x.IsActive && x.ItemType == "Deduction").ToList()
            : all.Where(x => x.IsActive && (x.ItemType == "Income" || x.ItemType == "Overtime")).ToList();

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
        var f = Request.Form;
        var type = f["Type"].ToString() == PayrollTransactionStore.Deduction ? PayrollTransactionStore.Deduction : PayrollTransactionStore.Income;

        DateOnly? D(string key) => DateOnly.TryParse(f[key], out var d) ? d : null;

        var tx = new PayrollTransactionStore.Transaction
        {
            Id = int.TryParse(f["Id"], out var id) ? id : 0,
            EmployeeId = int.TryParse(f["EmployeeId"], out var emp) ? emp : 0,
            Year = int.TryParse(f["FYear"], out var y) ? y : Year,
            Month = int.TryParse(f["FMonth"], out var m) ? m : Month,
            SalaryItemId = int.TryParse(f["SalaryItemId"], out var si) && si > 0 ? si : null,
            ItemName = f["ItemName"].ToString().Trim(),
            Amount = decimal.TryParse(f["Amount"], out var a) ? a : 0,
            TxType = type,
            Taxable = f["Taxable"] == "true",
            PaymentType = f["PaymentType"].ToString() == "OutSalary" ? "OutSalary" : "InSalary",
            TransactionDate = D("TransactionDate"),
            EffectiveDate = D("EffectiveDate"),
            IsUnlimited = f["IsUnlimited"] == "true",
            ValidFrom = D("ValidFrom"),
            ValidTo = D("ValidTo"),
            IsRetroactive = f["IsRetroactive"] == "true",
            RetroactiveDate = D("RetroactiveDate"),
            UpdateSocialSecurity = f["UpdateSocialSecurity"] == "true",
            SocialSecurityToDate = D("SocialSecurityToDate"),
            SalaryFromDate = D("SalaryFromDate"),
            SalaryToDate = D("SalaryToDate"),
            IsScheduled = f["IsScheduled"] == "true",
            InstallmentMode = string.IsNullOrWhiteSpace(f["InstallmentMode"]) ? null : f["InstallmentMode"].ToString(),
            InstallmentCount = int.TryParse(f["InstallmentCount"], out var ic) ? ic : null,
            InstallmentAmount = decimal.TryParse(f["InstallmentAmount"], out var ia) ? ia : null,
            InstallmentMonths = int.TryParse(f["InstallmentMonths"], out var imo) ? imo : null,
            FirstInstallmentDate = D("FirstInstallmentDate"),
            ChangeCostCenter = f["ChangeCostCenter"] == "true",
            CostCenter = string.IsNullOrWhiteSpace(f["CostCenter"]) ? null : f["CostCenter"].ToString().Trim(),
            Note = string.IsNullOrWhiteSpace(f["Note"]) ? null : f["Note"].ToString().Trim(),
            Status = f["Status"].ToString() is { Length: > 0 } st ? st : "Approved"
        };

        var back = new { Type = type, Year = tx.Year, Month = tx.Month };

        if (tx.EmployeeId <= 0) { TempData["PayrollMessage"] = "اختر الموظف."; TempData["PayrollOk"] = false; return RedirectToPage(back); }
        if (string.IsNullOrWhiteSpace(tx.ItemName)) { TempData["PayrollMessage"] = "اختر البند."; TempData["PayrollOk"] = false; return RedirectToPage(back); }
        if (tx.Amount <= 0) { TempData["PayrollMessage"] = "المبلغ يجب أن يكون أكبر من صفر."; TempData["PayrollOk"] = false; return RedirectToPage(back); }

        if (tx.Id > 0 && await PayrollTransactionStore.IsLockedAsync(_db, tx.Id))
        {
            TempData["PayrollMessage"] = "الحركة مقفلة (دخلت مسيراً مقفلاً) — لا يمكن تعديلها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Type = type, Year = tx.Year, Month = tx.Month, Lock = "Locked" });
        }

        await PayrollTransactionStore.SaveAsync(_db, tx, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = tx.Id > 0 ? "تم تحديث الحركة." : "تمت إضافة الحركة.";
        return RedirectToPage(back);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (await PayrollTransactionStore.IsLockedAsync(_db, id))
        {
            TempData["PayrollMessage"] = "الحركة مقفلة — لا يمكن حذفها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Type, Year, Month, Lock = "Locked" });
        }
        await PayrollTransactionStore.DeleteAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف الحركة.";
        return RedirectToPage(new { Type, Year, Month });
    }

    public async Task<IActionResult> OnPostDeleteManyAsync()
    {
        var ids = Request.Form["SelectedIds"].Where(v => int.TryParse(v, out _)).Select(int.Parse).ToList();
        if (ids.Count > 0)
        {
            await PayrollTransactionStore.DeleteManyAsync(_db, ids);
            TempData["PayrollMessage"] = $"تم حذف {ids.Count} حركة.";
        }
        else TempData["PayrollMessage"] = "حدد حركات أولاً.";
        return RedirectToPage(new { Type, Year, Month });
    }
}
