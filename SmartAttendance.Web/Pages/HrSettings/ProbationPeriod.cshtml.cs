using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// فترة التجربة — أول مستهلك لطبقة «سياسة أمّ + نُسَخ مشروطة»
/// (<see cref="HrPolicyResolver"/>)، وهي الشاشة التي أخذنا عنها النمط بكيان.
///
/// السياسة الأمّ تبقى بمفاتيح <c>Probation.*</c> كما هي — **صفر تغيير على سلوك
/// النظام القائم** ما لم يُنشئ المستخدم نسخة. والنُسَخ تحمل ما تغيّره فقط، فتعديل
/// الأمّ يسري على كل نسخة لم تخصّص ذلك الحقل.
/// </summary>
public class ProbationPeriodModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ProbationPeriodModel(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>مفاتيح الحمولة — تُشارَك بين الأمّ والنُسَخ، فاختلافها يعني نسخةً لا تُقرأ.</summary>
    public const string KeyDuration = "DurationValue";
    public const string KeyUnit = "DurationUnit";
    public const string KeyBasis = "StartBasis";
    public const string KeyExcludeHolidays = "ExcludeHolidays";
    public const string KeyAllowExtension = "AllowExtension";
    public const string KeyExtensionDays = "ExtensionDays";

    [BindProperty] public int DurationValue { get; set; } = 90;
    [BindProperty] public string DurationUnit { get; set; } = "Day";
    [BindProperty] public string StartBasis { get; set; } = "HireDate";
    [BindProperty] public bool ExcludeHolidays { get; set; }
    [BindProperty] public bool AllowExtension { get; set; }
    [BindProperty] public int ExtensionDays { get; set; }

    // ── نموذج النسخة ───────────────────────────────────────────────────────────

    [BindProperty] public int CopyId { get; set; }
    [BindProperty] public string CopyName { get; set; } = string.Empty;
    [BindProperty] public bool CopyIsActive { get; set; } = true;
    [BindProperty] public string CopyConditions { get; set; } = string.Empty;
    [BindProperty] public int? CopyDuration { get; set; }
    [BindProperty] public string? CopyUnit { get; set; }
    [BindProperty] public string? CopyBasis { get; set; }
    [BindProperty] public int? CopyExtensionDays { get; set; }

    public List<HrPolicyOverrideStore.Row> Copies { get; private set; } = new();
    public Dictionary<int, int> MatchCounts { get; private set; } = new();
    public string CriteriaJson { get; private set; } = "[]";

    /// <summary>النسخة المفتوحة للتعديل (<c>?edit=0</c> = نسخة جديدة، ولا شيء = مغلق).</summary>
    [BindProperty(SupportsGet = true, Name = "edit")]
    public int? EditingId { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();

        if (EditingId is > 0)
        {
            var row = await HrPolicyOverrideStore.FindAsync(_db, EditingId.Value);
            if (row is not null && row.PolicyKey == HrPolicyOverrideStore.PolicyProbation)
            {
                var payload = row.Payload;
                CopyId = row.Id;
                CopyName = row.Name;
                CopyIsActive = row.IsActive;
                CopyConditions = row.ConditionsJson;
                CopyDuration = payload.TryGetValue(KeyDuration, out var duration) && int.TryParse(duration, out var parsed) ? parsed : null;
                CopyUnit = payload.GetValueOrDefault(KeyUnit);
                CopyBasis = payload.GetValueOrDefault(KeyBasis);
                CopyExtensionDays = payload.TryGetValue(KeyExtensionDays, out var days) && int.TryParse(days, out var parsedDays) ? parsedDays : null;
            }
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HrSettingsStore.SetAsync(_db, "Probation.DurationValue", Math.Max(0, DurationValue).ToString());
        await HrSettingsStore.SetAsync(_db, "Probation.DurationUnit", DurationUnit);
        await HrSettingsStore.SetAsync(_db, "Probation.StartBasis", StartBasis);
        await HrSettingsStore.SetAsync(_db, "Probation.ExcludeHolidays", ExcludeHolidays.ToString());
        await HrSettingsStore.SetAsync(_db, "Probation.AllowExtension", AllowExtension.ToString());
        await HrSettingsStore.SetAsync(_db, "Probation.ExtensionDays", Math.Max(0, ExtensionDays).ToString());

        TempData["SuccessMessage"] = "تم حفظ إعدادات فترة التجربة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveCopyAsync()
    {
        var name = string.IsNullOrWhiteSpace(CopyName) ? "نسخة بلا اسم" : CopyName.Trim();
        var conditions = HrConditions.Deserialize(CopyConditions);

        // الحمولة تحمل **ما مُلئ فقط**: حقل تُرك فارغاً يعني «كما بالسياسة الأمّ»،
        // وهو الفرق بين نسخة تعدّل حقلاً وأخرى تجمّد كل الحقول على قيم اليوم.
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (CopyDuration is { } duration) payload[KeyDuration] = Math.Max(0, duration).ToString();
        if (!string.IsNullOrWhiteSpace(CopyUnit)) payload[KeyUnit] = CopyUnit!;
        if (!string.IsNullOrWhiteSpace(CopyBasis)) payload[KeyBasis] = CopyBasis!;
        if (CopyExtensionDays is { } extension) payload[KeyExtensionDays] = Math.Max(0, extension).ToString();

        if (CopyId > 0)
        {
            await HrPolicyOverrideStore.UpdateAsync(_db, CopyId, name, CopyIsActive, conditions, payload);
            TempData["SuccessMessage"] = "تم تحديث النسخة.";
        }
        else
        {
            await HrPolicyOverrideStore.CreateAsync(
                _db, HrPolicyOverrideStore.PolicyProbation, name, conditions, payload, User.Identity?.Name);
            TempData["SuccessMessage"] = conditions.IsEmpty
                ? "أُنشئت النسخة — لكنها بلا شروط فلن تنطبق على أحد."
                : "تم إنشاء النسخة.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCopyAsync(int id)
    {
        await HrPolicyOverrideStore.DeleteAsync(_db, id);
        TempData["SuccessMessage"] = "تم حذف النسخة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDuplicateCopyAsync(int id)
    {
        await HrPolicyOverrideStore.DuplicateAsync(_db, id, User.Identity?.Name);
        TempData["SuccessMessage"] = "تم استنساخ النسخة — عدّل شروطها قبل تفعيلها.";
        return RedirectToPage();
    }

    /// <summary>نقل صفّ خطوةً بالترتيب = تغيير أسبقيته. بديل السحب، ويعمل بلا JS.</summary>
    public async Task<IActionResult> OnPostMoveCopyAsync(int id, int direction)
    {
        var rows = await HrPolicyOverrideStore.LoadAsync(_db, HrPolicyOverrideStore.PolicyProbation);
        var ids = rows.Select(row => row.Id).ToList();
        var index = ids.IndexOf(id);
        var target = index + direction;

        if (index >= 0 && target >= 0 && target < ids.Count)
        {
            (ids[index], ids[target]) = (ids[target], ids[index]);
            await HrPolicyOverrideStore.ReorderAsync(_db, HrPolicyOverrideStore.PolicyProbation, ids);
        }

        return RedirectToPage();
    }

    /// <summary>ترتيب كامل بعد السحب — الواجهة ترسل المعرّفات بترتيبها الجديد.</summary>
    public async Task<IActionResult> OnPostReorderCopiesAsync(string order)
    {
        var ids = HrConditions.SplitValues(order)
            .Select(token => int.TryParse(token, out var parsed) ? parsed : 0)
            .Where(parsed => parsed > 0)
            .ToList();

        if (ids.Count > 0)
        {
            await HrPolicyOverrideStore.ReorderAsync(_db, HrPolicyOverrideStore.PolicyProbation, ids);
        }

        return RedirectToPage();
    }

    /// <summary>حمولة السياسة الأمّ — نفس المفاتيح التي تحملها النُسَخ.</summary>
    public Dictionary<string, string> ParentPayload() => new(StringComparer.OrdinalIgnoreCase)
    {
        [KeyDuration] = DurationValue.ToString(),
        [KeyUnit] = DurationUnit,
        [KeyBasis] = StartBasis,
        [KeyExcludeHolidays] = ExcludeHolidays.ToString(),
        [KeyAllowExtension] = AllowExtension.ToString(),
        [KeyExtensionDays] = ExtensionDays.ToString()
    };

    public string DescribeCopy(HrPolicyOverrideStore.Row row)
    {
        var payload = row.Payload;
        var parts = new List<string>();

        if (payload.TryGetValue(KeyDuration, out var duration))
        {
            var unit = payload.GetValueOrDefault(KeyUnit, DurationUnit) == "Month" ? "شهر" : "يوم";
            parts.Add($"المدة {duration} {unit}");
        }

        if (payload.TryGetValue(KeyBasis, out var basis))
        {
            parts.Add(basis == "JoiningDate" ? "من تاريخ الانضمام" : "من تاريخ التعيين");
        }

        if (payload.TryGetValue(KeyExtensionDays, out var extension))
        {
            parts.Add($"تمديد {extension} يوم");
        }

        return parts.Count == 0 ? "كالسياسة الأمّ" : string.Join(" · ", parts);
    }

    private async Task LoadAsync()
    {
        DurationValue = int.TryParse(await HrSettingsStore.GetAsync(_db, "Probation.DurationValue", "90"), out var days) ? days : 90;
        DurationUnit = await HrSettingsStore.GetAsync(_db, "Probation.DurationUnit", "Day");
        StartBasis = await HrSettingsStore.GetAsync(_db, "Probation.StartBasis", "HireDate");
        ExcludeHolidays = bool.TryParse(await HrSettingsStore.GetAsync(_db, "Probation.ExcludeHolidays", "False"), out var exclude) && exclude;
        AllowExtension = bool.TryParse(await HrSettingsStore.GetAsync(_db, "Probation.AllowExtension", "False"), out var allow) && allow;
        ExtensionDays = int.TryParse(await HrSettingsStore.GetAsync(_db, "Probation.ExtensionDays", "0"), out var ext) ? ext : 0;

        Copies = await HrPolicyOverrideStore.LoadAsync(_db, HrPolicyOverrideStore.PolicyProbation);
        CriteriaJson = await HrConditionOptions.BuildCatalogJsonAsync(_db);

        // العدّاد يمسح كل الموظفين؛ يُحسب فقط حين توجد نُسَخ فعلاً.
        MatchCounts = Copies.Count == 0
            ? new Dictionary<int, int>()
            : await HrPolicyOverrideStore.CountMatchesAsync(
                _db, HrPolicyOverrideStore.PolicyProbation, DateOnly.FromDateTime(DateTime.Today));
    }
}
