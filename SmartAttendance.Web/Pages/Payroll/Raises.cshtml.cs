using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// زيادات الراتب (/Payroll/Raises) — مطابقة كيان «زيادة الراتب». تغيير دائم للأساسي
/// (بمبلغ أو نسبة) بأثر تاريخي؛ عند «التطبيق» يُحدَّث EmployeeFinancialInfos.BasicSalary.
/// الأساسي القديم/الجديد يُحتسبان بالسيرفر من الراتب الحالي للموظف لا من العميل.
/// </summary>
public class RaisesModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public RaisesModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Emp { get; set; }

    /// <summary>التبويب: Pending (غير مُطبَّقة) | Applied (مُطبَّقة).</summary>
    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "Pending";

    public bool IsApplied => Tab == "Applied";

    public List<SalaryRaiseStore.Raise> Items { get; set; } = new();
    public List<SalaryRaiseStore.EmployeeBasic> Employees { get; set; } = new();

    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int AppliedCount { get; set; }
    public decimal TotalIncrease { get; set; }

    public List<string> AllDepartments { get; set; } = new();
    public List<string> AllBranches { get; set; } = new();
    public List<string> AllJobTitles { get; set; } = new();

    public async Task OnGetAsync()
    {
        if (Tab != "Applied") Tab = "Pending";

        var all = await SalaryRaiseStore.ListAsync(_db, Emp, search: Search);
        PendingCount = all.Count(x => !x.IsApplied);
        AppliedCount = all.Count(x => x.IsApplied);
        TotalIncrease = all.Where(x => x.IsApplied).Sum(x => x.Increase);

        Items = all.Where(x => x.IsApplied == IsApplied).ToList();
        TotalCount = Items.Count;

        Employees = await SalaryRaiseStore.EmployeeBasicsAsync(_db);
        (AllDepartments, AllBranches, AllJobTitles) = await MassScopeResolver.OrgListsAsync(_db);
    }

    /// <summary>تطبيق جماعي (= قفل) للزيادات المحددة قيد الانتظار.</summary>
    public async Task<IActionResult> OnPostApplyManyAsync()
    {
        var ids = Request.Form["SelectedIds"].Where(v => int.TryParse(v, out _)).Select(int.Parse).ToList();
        if (ids.Count == 0) { TempData["PayrollMessage"] = "حدد زيادات أولاً."; TempData["PayrollOk"] = false; return RedirectToPage(); }
        var n = await SalaryRaiseStore.ApplyManyAsync(_db, ids, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = $"طُبّقت {n} زيادة وحُدّثت رواتبها الأساسية.";
        TempData["PayrollOk"] = n > 0;
        return RedirectToPage(new { Tab = "Applied" });
    }

    public async Task<IActionResult> OnPostDeleteManyAsync()
    {
        var ids = Request.Form["SelectedIds"].Where(v => int.TryParse(v, out _)).Select(int.Parse).ToList();
        if (ids.Count == 0) { TempData["PayrollMessage"] = "حدد زيادات أولاً."; TempData["PayrollOk"] = false; return RedirectToPage(); }
        await SalaryRaiseStore.DeleteManyAsync(_db, ids);
        TempData["PayrollMessage"] = $"تم حذف {ids.Count} زيادة.";
        return RedirectToPage();
    }

    /// <summary>إدخال جماعي (نمط كيان «النطاق»): نفس الزيادة (مبلغ/نسبة) لمجموعة موظفين — زيادة قيد الانتظار لكل موظف.</summary>
    public async Task<IActionResult> OnPostMassEntryAsync(IFormFile? massFile)
    {
        var f = Request.Form;
        DateOnly? D(string key) => DateOnly.TryParse(f[key], out var d) ? d : null;
        var type = f["RaiseType"].ToString() == SalaryRaiseStore.ByPercentage ? SalaryRaiseStore.ByPercentage : SalaryRaiseStore.ByAmount;
        var value = decimal.TryParse(f["RaiseValue"], out var v) ? v : 0;
        if (value <= 0) { TempData["PayrollMessage"] = "قيمة الزيادة يجب أن تكون أكبر من صفر."; TempData["PayrollOk"] = false; return RedirectToPage(); }

        var (empIds, skipped, scopeLabel, err) = await MassScopeResolver.ResolveAsync(_db, f, massFile);
        if (err != null) { TempData["PayrollMessage"] = err; TempData["PayrollOk"] = false; return RedirectToPage(); }
        if (empIds.Count == 0) { TempData["PayrollMessage"] = "لم يُحدَّد أي موظف مطابق."; TempData["PayrollOk"] = false; return RedirectToPage(); }

        var basics = (await SalaryRaiseStore.EmployeeBasicsAsync(_db)).ToDictionary(x => x.Id, x => x.Basic);
        var reason = string.IsNullOrWhiteSpace(f["Reason"]) ? null : f["Reason"].ToString().Trim();
        var eff = D("EffectiveDate");
        var user = User?.Identity?.Name ?? "system";
        int n = 0;
        foreach (var empId in empIds)
        {
            var oldBasic = basics.TryGetValue(empId, out var b) ? b : 0;
            var newBasic = type == SalaryRaiseStore.ByPercentage
                ? Math.Round(oldBasic * (1 + value / 100m), 2)
                : Math.Round(oldBasic + value, 2);
            await SalaryRaiseStore.SaveAsync(_db, new SalaryRaiseStore.Raise
            {
                EmployeeId = empId, OldBasic = oldBasic, RaiseType = type, RaiseValue = value, NewBasic = newBasic,
                EffectiveDate = eff, Reason = reason, Status = "Approved"
            }, user);
            n++;
        }
        TempData["PayrollMessage"] = $"أُضيفت {n} زيادة قيد الانتظار (النطاق: {scopeLabel})"
            + (skipped > 0 ? $"، وتُخطّي {skipped} كوداً غير مطابق." : ".");
        TempData["PayrollOk"] = true;
        return RedirectToPage();
    }

    /// <summary>استيراد زيادات من إكسل/CSV (الأعمدة: رمز الموظف | القيمة | النوع؟(مبلغ/نسبة) | السبب؟).</summary>
    public async Task<IActionResult> OnPostImportAsync(IFormFile? importFile)
    {
        if (importFile == null || importFile.Length == 0)
        { TempData["PayrollMessage"] = "اختر ملف إكسل أو CSV."; TempData["PayrollOk"] = false; return RedirectToPage(); }

        List<string[]> rows;
        try { await using var s = importFile.OpenReadStream(); rows = SpreadsheetReader.Read(s, importFile.FileName); }
        catch (Exception ex) { TempData["PayrollMessage"] = "تعذّر قراءة الملف: " + ex.Message; TempData["PayrollOk"] = false; return RedirectToPage(); }

        var basics = await SalaryRaiseStore.EmployeeBasicsAsync(_db);
        var byCode = new Dictionary<string, SalaryRaiseStore.EmployeeBasic>();
        foreach (var e in basics) { var k = e.No.Trim().ToLowerInvariant(); if (k.Length > 0) byCode[k] = e; }

        int imported = 0, skipped = 0;
        var user = User?.Identity?.Name ?? "system";
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Length < 2) { skipped++; continue; }
            var code = row[0].Trim().ToLowerInvariant();
            if (code.Length == 0 || !byCode.TryGetValue(code, out var emp)) { skipped++; continue; }
            if (!decimal.TryParse(row[1], out var value) || value <= 0) { skipped++; continue; }

            var type = row.Length > 2 && (row[2].Contains("نسب") || row[2].Contains("%") || row[2].Trim().Equals("Percentage", StringComparison.OrdinalIgnoreCase))
                ? SalaryRaiseStore.ByPercentage : SalaryRaiseStore.ByAmount;
            var reason = row.Length > 3 && !string.IsNullOrWhiteSpace(row[3]) ? row[3].Trim() : null;
            var newBasic = type == SalaryRaiseStore.ByPercentage
                ? Math.Round(emp.Basic * (1 + value / 100m), 2)
                : Math.Round(emp.Basic + value, 2);

            await SalaryRaiseStore.SaveAsync(_db, new SalaryRaiseStore.Raise
            {
                EmployeeId = emp.Id, OldBasic = emp.Basic, RaiseType = type, RaiseValue = value, NewBasic = newBasic,
                Reason = reason, Status = "Approved"
            }, user);
            imported++;
        }
        TempData["PayrollMessage"] = $"استُوردت {imported} زيادة قيد الانتظار" + (skipped > 0 ? $"، وتُخطّي {skipped} صفاً (بيانات غير صالحة)." : ".");
        TempData["PayrollOk"] = imported > 0;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var f = Request.Form;
        DateOnly? D(string key) => DateOnly.TryParse(f[key], out var d) ? d : null;

        var empId = int.TryParse(f["EmployeeId"], out var e) ? e : 0;
        var type = f["RaiseType"].ToString() == SalaryRaiseStore.ByPercentage ? SalaryRaiseStore.ByPercentage : SalaryRaiseStore.ByAmount;
        var value = decimal.TryParse(f["RaiseValue"], out var v) ? v : 0;

        if (empId <= 0) { TempData["PayrollMessage"] = "اختر الموظف."; TempData["PayrollOk"] = false; return RedirectToPage(); }
        if (value <= 0) { TempData["PayrollMessage"] = "قيمة الزيادة يجب أن تكون أكبر من صفر."; TempData["PayrollOk"] = false; return RedirectToPage(); }

        // الأساسي الحالي من السيرفر (لا من العميل)
        var basics = await SalaryRaiseStore.EmployeeBasicsAsync(_db);
        var oldBasic = basics.FirstOrDefault(x => x.Id == empId)?.Basic ?? 0;
        var newBasic = type == SalaryRaiseStore.ByPercentage
            ? Math.Round(oldBasic * (1 + value / 100m), 2)
            : Math.Round(oldBasic + value, 2);

        var id = int.TryParse(f["Id"], out var rid) ? rid : 0;
        if (id > 0 && await SalaryRaiseStore.IsAppliedAsync(_db, id))
        {
            TempData["PayrollMessage"] = "الزيادة مُطبَّقة — لا يمكن تعديلها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Tab = "Applied" });
        }

        var raise = new SalaryRaiseStore.Raise
        {
            Id = id,
            EmployeeId = empId,
            OldBasic = oldBasic,
            RaiseType = type,
            RaiseValue = value,
            NewBasic = newBasic,
            EffectiveDate = D("EffectiveDate"),
            Reason = string.IsNullOrWhiteSpace(f["Reason"]) ? null : f["Reason"].ToString().Trim(),
            Note = string.IsNullOrWhiteSpace(f["Note"]) ? null : f["Note"].ToString().Trim(),
            Status = f["Status"].ToString() is { Length: > 0 } st ? st : "Approved"
        };

        await SalaryRaiseStore.SaveAsync(_db, raise, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = id > 0 ? "تم تحديث الزيادة." : $"تمت إضافة الزيادة (الأساسي {oldBasic:#,0.##} ← {newBasic:#,0.##}).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApplyAsync(int id)
    {
        var ok = await SalaryRaiseStore.ApplyAsync(_db, id, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = ok ? "طُبّقت الزيادة وحُدّث الراتب الأساسي." : "تعذّر تطبيق الزيادة (ربما مُطبَّقة سابقاً).";
        TempData["PayrollOk"] = ok;
        return RedirectToPage(new { Tab = ok ? "Applied" : "Pending" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (await SalaryRaiseStore.IsAppliedAsync(_db, id))
        {
            TempData["PayrollMessage"] = "الزيادة مُطبَّقة — لا يمكن حذفها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Tab = "Applied" });
        }
        await SalaryRaiseStore.DeleteAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف الزيادة.";
        return RedirectToPage();
    }
}
