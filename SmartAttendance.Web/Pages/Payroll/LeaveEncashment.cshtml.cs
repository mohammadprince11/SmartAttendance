using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// بدل الإجازة (/Payroll/LeaveEncashment) — صرف أيام رصيد إجازة الموظف نقداً.
/// حركة TxType=LeaveEncashment بعدد أيام موجب؛ القيمة تُحتسب بالمسير = الأيام ×
/// الأجر اليومي (الأساسي ÷ 30) كاستحقاق خاضع للضريبة. تعرض الشاشة رصيد الموظف
/// المتاح (سنوي/مرضي) إرشاداً. تشارك تبويبَي القفل ودورة الحركات نفسها.
/// </summary>
public class LeaveEncashmentModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public LeaveEncashmentModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; } = DateTime.Today.Year;

    [BindProperty(SupportsGet = true)]
    public int Month { get; set; } = DateTime.Today.Month;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>التبويب: Open (غير مقفلة، قابلة للتعديل) | Locked (مقفلة، قراءة فقط).</summary>
    [BindProperty(SupportsGet = true)]
    public string Lock { get; set; } = "Open";

    public bool IsReadOnly => Lock == "Locked";

    public List<PayrollTransactionStore.Transaction> Items { get; set; } = new();
    public List<EmployeeOption> Employees { get; set; } = new();
    public List<SalaryItemStore.SalaryItem> Catalog { get; set; } = new();

    public int TotalCount { get; set; }
    public decimal TotalDays { get; set; }
    public int EmployeesCovered { get; set; }

    public List<string> AllDepartments { get; set; } = new();
    public List<string> AllBranches { get; set; } = new();
    public List<string> AllJobTitles { get; set; } = new();

    public sealed class EmployeeOption
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public async Task OnGetAsync()
    {
        if (Month is < 1 or > 12) Month = DateTime.Today.Month;
        if (Lock != "Locked") Lock = "Open";

        Items = await PayrollTransactionStore.ListAsync(
            _db, Year, Month, PayrollTransactionStore.LeaveEncashment, Search, locked: Lock == "Locked");

        var all = await SalaryItemStore.ListAsync(_db);
        Catalog = all.Where(x => x.IsActive && x.ItemType == "LeaveEncashment").ToList();

        Employees = await HrmsDatabase.QueryAsync(_db,
            "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1 ORDER BY FullName;",
            command => { },
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Name = HrmsDatabase.GetString(reader, "FullName")
            });

        (AllDepartments, AllBranches, AllJobTitles) = await MassScopeResolver.OrgListsAsync(_db);

        TotalCount = Items.Count;
        TotalDays = Items.Sum(x => x.Days ?? 0);
        EmployeesCovered = Items.Select(x => x.EmployeeId).Distinct().Count();
    }

    /// <summary>رصيد الموظف المتاح (JSON) — يستدعيه منتقي الموظف لعرض الأيام القابلة للصرف.</summary>
    public async Task<IActionResult> OnGetBalanceAsync(int employeeId, int year)
    {
        if (employeeId <= 0) return new JsonResult(new { ok = false });
        if (year < 2000 || year > 2100) year = Year;

        var balances = await LeaveBalanceCalculator.ForEmployeeAsync(_db, employeeId, year);
        return new JsonResult(new
        {
            ok = true,
            year,
            types = balances.Select(b => new
            {
                type = b.Type.ToString(),
                label = LeaveTypeText(b.Type),
                remaining = b.Remaining,
                entitled = b.Entitled + b.CarriedOver,
                used = b.Used
            })
        });
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var f = Request.Form;
        DateOnly? D(string key) => DateOnly.TryParse(f[key], out var d) ? d : null;

        var days = decimal.TryParse(f["Days"], out var dd) ? Math.Abs(dd) : 0;

        var tx = new PayrollTransactionStore.Transaction
        {
            Id = int.TryParse(f["Id"], out var id) ? id : 0,
            EmployeeId = int.TryParse(f["EmployeeId"], out var emp) ? emp : 0,
            Year = int.TryParse(f["FYear"], out var y) ? y : Year,
            Month = int.TryParse(f["FMonth"], out var m) ? m : Month,
            SalaryItemId = int.TryParse(f["SalaryItemId"], out var si) && si > 0 ? si : null,
            ItemName = f["ItemName"].ToString().Trim() is { Length: > 0 } n ? n : "بدل إجازة",
            TxType = PayrollTransactionStore.LeaveEncashment,
            Taxable = true,
            PaymentType = "InSalary",
            Days = days,
            Amount = 0,
            TransactionDate = D("TransactionDate"),
            Note = string.IsNullOrWhiteSpace(f["Note"]) ? null : f["Note"].ToString().Trim(),
            Status = f["Status"].ToString() is { Length: > 0 } st ? st : "Approved"
        };

        var back = new { Year = tx.Year, Month = tx.Month };

        if (tx.EmployeeId <= 0) { TempData["PayrollMessage"] = "اختر الموظف."; TempData["PayrollOk"] = false; return RedirectToPage(back); }
        if (days <= 0) { TempData["PayrollMessage"] = "عدد أيام الصرف يجب أن يكون أكبر من صفر."; TempData["PayrollOk"] = false; return RedirectToPage(back); }

        if (tx.Id > 0 && await PayrollTransactionStore.IsLockedAsync(_db, tx.Id))
        {
            TempData["PayrollMessage"] = "الحركة مقفلة (دخلت مسيراً مقفلاً) — لا يمكن تعديلها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Year = tx.Year, Month = tx.Month, Lock = "Locked" });
        }

        await PayrollTransactionStore.SaveAsync(_db, tx, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = tx.Id > 0 ? "تم تحديث بدل الإجازة." : "تمت إضافة بدل الإجازة.";
        return RedirectToPage(back);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (await PayrollTransactionStore.IsLockedAsync(_db, id))
        {
            TempData["PayrollMessage"] = "الحركة مقفلة — لا يمكن حذفها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Year, Month, Lock = "Locked" });
        }
        await PayrollTransactionStore.DeleteAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف الحركة.";
        return RedirectToPage(new { Year, Month });
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
        return RedirectToPage(new { Year, Month });
    }

    /// <summary>إدخال جماعي (نمط كيان «النطاق»): نفس عدد أيام الصرف لمجموعة موظفين تُحدَّد بأربع طرق.</summary>
    public async Task<IActionResult> OnPostMassEntryAsync(IFormFile? massFile)
    {
        var f = Request.Form;
        var y = int.TryParse(f["MassYear"], out var yy) ? yy : Year;
        var m = int.TryParse(f["MassMonth"], out var mm) ? mm : Month;
        var back = new { Year = y, Month = m };

        var itemName = f["MassItemName"].ToString().Trim() is { Length: > 0 } n ? n : "بدل إجازة";
        var days = decimal.TryParse(f["MassDays"], out var dd) ? Math.Abs(dd) : 0;
        if (days <= 0) { TempData["PayrollMessage"] = "عدد أيام الصرف يجب أن يكون أكبر من صفر."; TempData["PayrollOk"] = false; return RedirectToPage(back); }

        var (empIds, skipped, scopeLabel, err) = await MassScopeResolver.ResolveAsync(_db, f, massFile);
        if (err != null) { TempData["PayrollMessage"] = err; TempData["PayrollOk"] = false; return RedirectToPage(back); }
        if (empIds.Count == 0) { TempData["PayrollMessage"] = "لم يُحدَّد أي موظف مطابق."; TempData["PayrollOk"] = false; return RedirectToPage(back); }

        var template = new PayrollTransactionStore.Transaction
        {
            Year = y,
            Month = m,
            TxType = PayrollTransactionStore.LeaveEncashment,
            SalaryItemId = int.TryParse(f["MassSalaryItemId"], out var si) && si > 0 ? si : null,
            ItemName = itemName,
            Days = days,
            Amount = 0,
            Taxable = true,
            PaymentType = "InSalary",
            TransactionDate = DateOnly.FromDateTime(DateTime.Today),
            Note = string.IsNullOrWhiteSpace(f["MassNote"]) ? null : f["MassNote"].ToString().Trim(),
            Status = "Approved",
            Source = "إدخال جماعي"
        };

        var count = await PayrollTransactionStore.SaveManyAsync(_db, empIds, template, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = $"تمت إضافة {count} بدل إجازة (النطاق: {scopeLabel})"
            + (skipped > 0 ? $"، وتُخطّي {skipped} كوداً غير مطابق." : ".");
        TempData["PayrollOk"] = true;
        return RedirectToPage(back);
    }

    /// <summary>استيراد بدل إجازة من إكسل/CSV (الأعمدة: رمز الموظف | عدد الأيام | البند؟).</summary>
    public async Task<IActionResult> OnPostImportAsync(IFormFile? importFile)
    {
        var f = Request.Form;
        var y = int.TryParse(f["ImportYear"], out var yy) ? yy : Year;
        var m = int.TryParse(f["ImportMonth"], out var mm) ? mm : Month;
        var back = new { Year = y, Month = m };

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
            if (row.Length < 2) { skipped++; continue; }
            var code = row[0].Trim().ToLowerInvariant();
            if (code.Length == 0 || !byCode.TryGetValue(code, out var empId)) { skipped++; continue; }
            if (!decimal.TryParse(row[1], out var days) || days <= 0) { skipped++; continue; }

            var itemName = row.Length > 2 && !string.IsNullOrWhiteSpace(row[2]) ? row[2].Trim() : "بدل إجازة";

            await PayrollTransactionStore.SaveAsync(_db, new PayrollTransactionStore.Transaction
            {
                EmployeeId = empId, Year = y, Month = m, TxType = PayrollTransactionStore.LeaveEncashment,
                ItemName = itemName, Days = Math.Abs(days), Amount = 0, Taxable = true,
                PaymentType = "InSalary", TransactionDate = DateOnly.FromDateTime(DateTime.Today),
                Status = "Approved", Source = "استيراد إكسل"
            }, user);
            imported++;
        }

        TempData["PayrollMessage"] = $"استُوردت {imported} حركة بدل إجازة" + (skipped > 0 ? $"، وتُخطّي {skipped} صفاً (بيانات غير صالحة)." : ".");
        TempData["PayrollOk"] = imported > 0;
        return RedirectToPage(back);
    }

    public async Task<IActionResult> OnPostLockSelectedAsync()
    {
        var ids = Request.Form["SelectedIds"].Where(v => int.TryParse(v, out _)).Select(int.Parse).ToList();
        if (ids.Count > 0) { await PayrollTransactionStore.SetLockedAsync(_db, ids, true); TempData["PayrollMessage"] = $"أُقفلت {ids.Count} حركة."; }
        else TempData["PayrollMessage"] = "حدد حركات أولاً.";
        return RedirectToPage(new { Year, Month, Lock = "Locked" });
    }

    public async Task<IActionResult> OnPostUnlockSelectedAsync()
    {
        var ids = Request.Form["SelectedIds"].Where(v => int.TryParse(v, out _)).Select(int.Parse).ToList();
        if (ids.Count > 0) { await PayrollTransactionStore.SetLockedAsync(_db, ids, false); TempData["PayrollMessage"] = $"فُتح قفل {ids.Count} حركة."; }
        else TempData["PayrollMessage"] = "حدد حركات أولاً.";
        return RedirectToPage(new { Year, Month, Lock = "Open" });
    }

    public static string LeaveTypeText(LeaveType type) => type switch
    {
        LeaveType.Annual => "سنوية",
        LeaveType.Sick => "مرضية",
        LeaveType.Unpaid => "بدون راتب",
        LeaveType.Emergency => "طارئة",
        LeaveType.Official => "رسمية",
        _ => type.ToString()
    };
}
