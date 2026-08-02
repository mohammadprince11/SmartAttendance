using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// تهيئة المخالفات (نظير `/Employees/ViolationConfiguration` بكيان).
///
/// المنطق كلّه بـ<see cref="ViolationConfigPolicy"/>؛ هذه الصفحة تهيئة وعرضُ أثر.
/// و«رقم الفقرة من لائحة الجزاءات» يُحرَّر هنا لأنواع المخالفات القائمة.
/// </summary>
public class ViolationConfigurationModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ViolationConfigurationModel(ApplicationDbContext db) => _db = db;

    [BindProperty] public int ObjectionDays { get; set; } = ViolationConfigPolicy.DefaultObjectionDays;
    [BindProperty] public bool RequireEmployeeReply { get; set; }
    [BindProperty] public int ReplyDays { get; set; } = ViolationConfigPolicy.DefaultReplyDays;
    [BindProperty] public bool RequireCommitteeApproval { get; set; }
    [BindProperty] public int DropMonths { get; set; }
    [BindProperty] public string RefPrefix { get; set; } = "VC";

    public List<(int Id, string Name, string? Clause)> ViolationTypes { get; private set; } = new();

    // ── تصفّح: خمسة وعشرون بالصفحة ───────────────────────────────────────────
    //
    // بلائحةٍ من خمسين نوعاً فأكثر كان الجدول يمتدّ حتى يضيع فيه ما تبحث عنه،
    // ولكل صفٍّ نموذجُ حفظٍ مستقلّ — أي مئةُ حقلٍ بصفحةٍ واحدة.

    public const int PageSize = 25;

    /// <summary>رقم الصفحة من الرابط — يبدأ من واحد.</summary>
    [BindProperty(SupportsGet = true)]
    public int PageNo { get; set; } = 1;

    /// <summary>عدد الأنواع كلّها قبل التقطيع.</summary>
    public int TotalTypes { get; private set; }

    public int PageCount => TotalTypes == 0 ? 1 : (int)Math.Ceiling(TotalTypes / (double)PageSize);

    /// <summary>ترتيب أول صفٍّ بالصفحة — يُعرض كي يعرف المستخدم أين هو من القائمة.</summary>
    public int FirstIndex => TotalTypes == 0 ? 0 : ((CurrentPage - 1) * PageSize) + 1;

    public int LastIndex => Math.Min(CurrentPage * PageSize, TotalTypes);

    /// <summary>
    /// الصفحة بعد الحصر بالمدى الصالح.
    ///
    /// ⚠️ رقمٌ خارج المدى يُقصَر ولا يُفرغ الجدول: رابطٌ قديم لصفحةٍ سابعة بعد
    /// حذف مخالفات كان سيُظهر شاشةً فارغة توحي بأن اللائحة ضاعت.
    /// </summary>
    public int CurrentPage => Math.Clamp(PageNo <= 0 ? 1 : PageNo, 1, PageCount);

    /// <summary>معاينة الأثر: كم حالة ستخرج من عدّ التدرّج بهذه المدّة؟</summary>
    public int TotalCases { get; private set; }
    public int DroppedByCurrentSetting { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await SetAsync(ViolationConfigPolicy.KeyObjectionDays, Math.Max(0, ObjectionDays).ToString());
        await SetAsync(ViolationConfigPolicy.KeyRequireReply, RequireEmployeeReply.ToString());
        await SetAsync(ViolationConfigPolicy.KeyReplyDays, Math.Max(0, ReplyDays).ToString());
        await SetAsync(ViolationConfigPolicy.KeyRequireCommittee, RequireCommitteeApproval.ToString());
        await SetAsync(ViolationConfigPolicy.KeyDropMonths, Math.Max(0, DropMonths).ToString());
        await SetAsync(ViolationConfigPolicy.KeyRefPrefix, string.IsNullOrWhiteSpace(RefPrefix) ? "VC" : RefPrefix.Trim());

        TempData["SuccessMessage"] = DropMonths > 0
            ? $"تم الحفظ. العقوبات ستسقط من عدّ التدرّج بعد {DropMonths} شهراً من تاريخ القرار."
            : "تم الحفظ. مدّة الإسقاط صفر ⟹ العقوبات لا تسقط أبداً (سلوك النظام الحالي).";

        return RedirectToPage();
    }

    /// <summary>رقم الفقرة لكل نوع مخالفة — سند العقوبة النظامي.</summary>
    public async Task<IActionResult> OnPostClauseAsync(int id, string? clause)
    {
        await HrmsDatabase.ExecuteAsync(
            _db,
            "UPDATE DisciplinaryViolationTypes SET RegulationClause = @Clause WHERE Id = @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Clause", string.IsNullOrWhiteSpace(clause) ? null : clause.Trim());
            });

        TempData["SuccessMessage"] = "تم حفظ رقم الفقرة.";

        // العودة لنفس الصفحة: من حفظ صفّاً بالصفحة الثانية لا يُقذف للأولى ليبحث
        // عن موضعه من جديد.
        return RedirectToPage(new { pageNo = PageNo <= 1 ? (int?)null : PageNo });
    }

    private async Task SetAsync(string key, string value) =>
        await HrmsDatabase.ExecuteAsync(
            _db,
            """
IF EXISTS (SELECT 1 FROM DisciplinarySettings WHERE [Key] = @Key)
    UPDATE DisciplinarySettings SET [Value] = @Value, UpdatedAt = SYSUTCDATETIME() WHERE [Key] = @Key;
ELSE
    INSERT INTO DisciplinarySettings([Key], [Value], UpdatedAt) VALUES(@Key, @Value, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Key", key);
                HrmsDatabase.AddParameter(command, "@Value", value);
            });

    private async Task<string> GetAsync(string key, string fallback) =>
        await HrmsDatabase.ScalarAsync<string>(
            _db,
            "SELECT TOP 1 [Value] FROM DisciplinarySettings WHERE [Key] = @Key;",
            command => HrmsDatabase.AddParameter(command, "@Key", key)) ?? fallback;

    private async Task LoadAsync()
    {
        await DisciplinarySchema.EnsureAsync(_db);

        ObjectionDays = int.TryParse(await GetAsync(ViolationConfigPolicy.KeyObjectionDays, "0"), out var od) ? od : 0;
        RequireEmployeeReply = bool.TryParse(await GetAsync(ViolationConfigPolicy.KeyRequireReply, "False"), out var rr) && rr;
        ReplyDays = int.TryParse(await GetAsync(ViolationConfigPolicy.KeyReplyDays, "0"), out var rd) ? rd : 0;
        RequireCommitteeApproval = bool.TryParse(await GetAsync(ViolationConfigPolicy.KeyRequireCommittee, "False"), out var rc) && rc;
        DropMonths = int.TryParse(await GetAsync(ViolationConfigPolicy.KeyDropMonths, "0"), out var dm) ? dm : 0;
        RefPrefix = await GetAsync(ViolationConfigPolicy.KeyRefPrefix, "VC");

        TotalTypes = await HrmsDatabase.ScalarAsync<int>(
            _db, "SELECT COUNT(1) FROM DisciplinaryViolationTypes;", command => { });

        // التقطيع بالقاعدة لا بالذاكرة: قراءة الخمسين كلّها لعرض خمسٍ وعشرين
        // تنقل ضعف ما يُعرض، وتكبر مع كل مخالفة تُضاف.
        ViolationTypes = (await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT Id, ISNULL(Name, N'') AS Name, RegulationClause
FROM DisciplinaryViolationTypes
ORDER BY Name
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Skip", (CurrentPage - 1) * PageSize);
                HrmsDatabase.AddParameter(command, "@Take", PageSize);
            },
            reader => (
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Name"),
                (string?)HrmsDatabase.GetString(reader, "RegulationClause")))).ToList();

        // معاينة الأثر قبل الحفظ: التهيئة بلا رقمٍ يُظهر أثرها تخمينٌ لا قرار.
        var decisions = await HrmsDatabase.QueryAsync(
            _db,
            "SELECT DecidedOn FROM EmployeeViolationCases WHERE ISNULL(IsDeleted, 0) = 0;",
            command => { },
            reader => HrmsDatabase.GetDateOnly(reader, "DecidedOn"));

        TotalCases = decisions.Count;
        var today = DateOnly.FromDateTime(DateTime.Today);
        DroppedByCurrentSetting = DropMonths <= 0
            ? 0
            : decisions.Count(date => ViolationConfigPolicy.IsDropped(date, DropMonths, today));
    }
}
