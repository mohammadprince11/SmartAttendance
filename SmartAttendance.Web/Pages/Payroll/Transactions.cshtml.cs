using Microsoft.AspNetCore.Http;
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

    // بحث متقدم
    [BindProperty(SupportsGet = true)]
    public int? Emp { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PayType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Src { get; set; }

    [BindProperty(SupportsGet = true)]
    public decimal? MinAmount { get; set; }

    [BindProperty(SupportsGet = true)]
    public decimal? MaxAmount { get; set; }

    // فلاتر إضافية بنمط كيان
    [BindProperty(SupportsGet = true)]
    public string? Dept { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Branch { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? JobTitle { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? DateFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? DateTo { get; set; }

    public List<string> Sources { get; set; } = new();
    public List<string> Departments { get; set; } = new();
    public List<string> Branches { get; set; } = new();
    public List<string> JobTitles { get; set; } = new();

    public bool HasAdvanced => Emp is > 0 || !string.IsNullOrWhiteSpace(PayType) || !string.IsNullOrWhiteSpace(Src)
        || MinAmount.HasValue || MaxAmount.HasValue || !string.IsNullOrWhiteSpace(Dept) || !string.IsNullOrWhiteSpace(Branch)
        || !string.IsNullOrWhiteSpace(JobTitle) || DateFrom.HasValue || DateTo.HasValue;

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

        // قوائم الفلاتر (من حركات الفترة قبل تطبيق الفلاتر المتقدمة)
        Sources = Items.Select(x => x.Source).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
        Departments = Items.Select(x => x.Department).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
        Branches = Items.Select(x => x.Branch).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
        JobTitles = Items.Select(x => x.Position).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();

        if (Emp is > 0) Items = Items.Where(x => x.EmployeeId == Emp).ToList();
        if (!string.IsNullOrWhiteSpace(PayType)) Items = Items.Where(x => x.PaymentType == PayType).ToList();
        if (!string.IsNullOrWhiteSpace(Src)) Items = Items.Where(x => x.Source == Src).ToList();
        if (!string.IsNullOrWhiteSpace(Dept)) Items = Items.Where(x => x.Department == Dept).ToList();
        if (!string.IsNullOrWhiteSpace(Branch)) Items = Items.Where(x => x.Branch == Branch).ToList();
        if (!string.IsNullOrWhiteSpace(JobTitle)) Items = Items.Where(x => x.Position == JobTitle).ToList();
        if (DateFrom.HasValue) Items = Items.Where(x => x.TransactionDate.HasValue && x.TransactionDate.Value >= DateFrom.Value).ToList();
        if (DateTo.HasValue) Items = Items.Where(x => x.TransactionDate.HasValue && x.TransactionDate.Value <= DateTo.Value).ToList();
        if (MinAmount.HasValue) Items = Items.Where(x => x.Amount >= MinAmount.Value).ToList();
        if (MaxAmount.HasValue) Items = Items.Where(x => x.Amount <= MaxAmount.Value).ToList();

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

    /// <summary>دخل جماعي: بند + مبلغ موحّد لعدة موظفين محددين.</summary>
    public async Task<IActionResult> OnPostMassEntryAsync()
    {
        var f = Request.Form;
        var type = f["Type"].ToString() == PayrollTransactionStore.Deduction ? PayrollTransactionStore.Deduction : PayrollTransactionStore.Income;
        var y = int.TryParse(f["MassYear"], out var yy) ? yy : Year;
        var m = int.TryParse(f["MassMonth"], out var mm) ? mm : Month;
        var back = new { Type = type, Year = y, Month = m };

        var empIds = f["MassEmployeeIds"].Where(v => int.TryParse(v, out _)).Select(int.Parse).Distinct().ToList();
        var itemName = f["MassItemName"].ToString().Trim();
        var amount = decimal.TryParse(f["MassAmount"], out var a) ? a : 0;

        if (empIds.Count == 0) { TempData["PayrollMessage"] = "اختر موظفاً واحداً على الأقل."; TempData["PayrollOk"] = false; return RedirectToPage(back); }
        if (string.IsNullOrWhiteSpace(itemName)) { TempData["PayrollMessage"] = "اختر البند."; TempData["PayrollOk"] = false; return RedirectToPage(back); }
        if (amount <= 0) { TempData["PayrollMessage"] = "المبلغ يجب أن يكون أكبر من صفر."; TempData["PayrollOk"] = false; return RedirectToPage(back); }

        var template = new PayrollTransactionStore.Transaction
        {
            Year = y,
            Month = m,
            TxType = type,
            SalaryItemId = int.TryParse(f["MassSalaryItemId"], out var si) && si > 0 ? si : null,
            ItemName = itemName,
            Amount = amount,
            Taxable = f["MassTaxable"] == "true",
            PaymentType = f["MassPaymentType"].ToString() == "OutSalary" ? "OutSalary" : "InSalary",
            TransactionDate = DateOnly.FromDateTime(DateTime.Today),
            Note = string.IsNullOrWhiteSpace(f["MassNote"]) ? null : f["MassNote"].ToString().Trim(),
            Status = "Approved",
            Source = "دخل جماعي"
        };

        var n = await PayrollTransactionStore.SaveManyAsync(_db, empIds, template, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = $"تمت إضافة {n} حركة عبر الدخل الجماعي.";
        return RedirectToPage(back);
    }

    /// <summary>استيراد حركات من إكسل/CSV (الأعمدة: رمز الموظف | البند | المبلغ | نوع الدفعة؟ | ملاحظة؟).</summary>
    public async Task<IActionResult> OnPostImportAsync(IFormFile? importFile)
    {
        var f = Request.Form;
        var type = f["Type"].ToString() == PayrollTransactionStore.Deduction ? PayrollTransactionStore.Deduction : PayrollTransactionStore.Income;
        var y = int.TryParse(f["ImportYear"], out var yy) ? yy : Year;
        var m = int.TryParse(f["ImportMonth"], out var mm) ? mm : Month;
        var back = new { Type = type, Year = y, Month = m };

        if (importFile == null || importFile.Length == 0)
        { TempData["PayrollMessage"] = "اختر ملف إكسل أو CSV."; TempData["PayrollOk"] = false; return RedirectToPage(back); }

        List<string[]> rows;
        try { await using var s = importFile.OpenReadStream(); rows = SpreadsheetReader.Read(s, importFile.FileName); }
        catch (Exception ex) { TempData["PayrollMessage"] = "تعذّر قراءة الملف: " + ex.Message; TempData["PayrollOk"] = false; return RedirectToPage(back); }

        var emps = await HrmsDatabase.QueryAsync(_db,
            "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1;",
            command => { },
            reader => new { Id = HrmsDatabase.GetInt(reader, "Id"), No = HrmsDatabase.GetString(reader, "EmployeeNo") });
        var byCode = new Dictionary<string, int>();
        foreach (var e in emps) { var k = e.No.Trim().ToLowerInvariant(); if (k.Length > 0) byCode[k] = e.Id; }

        int imported = 0, skipped = 0;
        var user = User?.Identity?.Name ?? "system";
        for (var i = 1; i < rows.Count; i++) // الصف 0 ترويسة
        {
            var row = rows[i];
            if (row.Length < 3) { skipped++; continue; }
            var code = row[0].Trim().ToLowerInvariant();
            var itemName = row[1].Trim();
            if (code.Length == 0 || itemName.Length == 0 || !byCode.TryGetValue(code, out var empId)) { skipped++; continue; }
            if (!decimal.TryParse(row[2], out var amount) || amount <= 0) { skipped++; continue; }

            var paymentType = row.Length > 3 && (row[3].Contains("خارج") || row[3].Trim().Equals("OutSalary", StringComparison.OrdinalIgnoreCase)) ? "OutSalary" : "InSalary";
            var note = row.Length > 4 && !string.IsNullOrWhiteSpace(row[4]) ? row[4].Trim() : null;

            await PayrollTransactionStore.SaveAsync(_db, new PayrollTransactionStore.Transaction
            {
                EmployeeId = empId, Year = y, Month = m, TxType = type,
                ItemName = itemName, Amount = amount, Taxable = true,
                PaymentType = paymentType, TransactionDate = DateOnly.FromDateTime(DateTime.Today),
                Note = note, Status = "Approved", Source = "استيراد إكسل"
            }, user);
            imported++;
        }

        TempData["PayrollMessage"] = $"استُوردت {imported} حركة" + (skipped > 0 ? $"، وتُخطّي {skipped} صفاً (بيانات غير صالحة)." : ".");
        TempData["PayrollOk"] = imported > 0;
        return RedirectToPage(back);
    }

    public async Task<IActionResult> OnPostLockSelectedAsync()
    {
        var ids = Request.Form["SelectedIds"].Where(v => int.TryParse(v, out _)).Select(int.Parse).ToList();
        if (ids.Count > 0) { await PayrollTransactionStore.SetLockedAsync(_db, ids, true); TempData["PayrollMessage"] = $"أُقفلت {ids.Count} حركة."; }
        else TempData["PayrollMessage"] = "حدد حركات أولاً.";
        return RedirectToPage(new { Type, Year, Month, Lock = "Locked" });
    }

    public async Task<IActionResult> OnPostUnlockSelectedAsync()
    {
        var ids = Request.Form["SelectedIds"].Where(v => int.TryParse(v, out _)).Select(int.Parse).ToList();
        if (ids.Count > 0) { await PayrollTransactionStore.SetLockedAsync(_db, ids, false); TempData["PayrollMessage"] = $"فُتح قفل {ids.Count} حركة."; }
        else TempData["PayrollMessage"] = "حدد حركات أولاً.";
        return RedirectToPage(new { Type, Year, Month, Lock = "Open" });
    }
}
