namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// نطاق تشغيل المسير (نمط كيان «النطاق» بنموذج احتساب الرواتب) — منطق نقيّ بلا
/// قاعدة بيانات: تطبيع الوضع، وتفكيك الأكواد الملصوقة من إكسل، ومطابقتها بأرقام
/// الموظفين، وقرار «هل يدخل هذا الموظف بالتشغيل؟».
///
/// ⚠️ القاعدة المحورية: **نطاق فارغ = كل الموظفين النشطين — بالكود لا بالقاعدة.**
/// أي «افتراض مخزَّن» بصفوف القاعدة حالةٌ مخفية قابلة للتلف بلا أثر ظاهر (وقع هذا
/// فعلاً بأوعية الاحتساب — راجع <see cref="SalaryBaseStore"/>). فصفوف
/// <c>PayrollRunScopeMembers</c> لا تُكتب إلا لتشغيل حُدّد نطاقه صراحةً.
///
/// وسبب فصل الأكواد المفقودة عن المطابِقة: المستخدم يلصق عموداً من إكسل، وكودٌ
/// واحد مخطئ يعني موظفاً يغيب عن راتب الشهر **بصمت**. المفقود يُعرض قبل التشغيل.
/// </summary>
public static class PayrollRunScope
{
    public const string ModeAll = "All";
    public const string ModeManual = "Manual";
    public const string ModePaste = "Paste";
    public const string ModeFile = "File";
    public const string ModeCriteria = "Criteria";

    /// <summary>الفواصل التي قد يأتي بها عمود إكسل ملصوق (سطر/تاب/فاصلة/مسافة).</summary>
    private static readonly char[] CodeSeparators = { '\n', '\r', ',', ';', '\t', ' ' };

    /// <summary>وضع غير معروف (أو فارغ) يعود لـ«الكل» — أوسع نطاق، فلا يغيب أحد بصمت.</summary>
    public static string NormalizeMode(string? mode) => mode switch
    {
        ModeManual => ModeManual,
        ModePaste => ModePaste,
        ModeFile => ModeFile,
        ModeCriteria => ModeCriteria,
        _ => ModeAll
    };

    public static string ModeLabel(string? mode) => NormalizeMode(mode) switch
    {
        ModeManual => "اختيار يدوي",
        ModePaste => "لصق أكواد",
        ModeFile => "ملف إكسل",
        ModeCriteria => "حسب معايير",
        _ => "كل الموظفين"
    };

    /// <summary>وصف النطاق كما يُعرض بقائمة الدفعات: الوضع + عدد المستهدفين.</summary>
    public static string Describe(string? mode, int memberCount) =>
        NormalizeMode(mode) == ModeAll || memberCount == 0
            ? "كل الموظفين"
            : $"{ModeLabel(mode)} — {memberCount} موظفاً";

    /// <summary>مفتاح المطابقة: الرمز مُشذَّب وبحالة موحّدة (أكواد إكسل تأتي بفراغات).</summary>
    public static string CodeKey(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// يفكّك نصاً ملصوقاً إلى أكواد فريدة بترتيب اللصق. التكرار يُطوى لأن نفس الموظف
    /// مرّتين نطاقٌ واحد، والمطابقة لاحقاً تتجاهل الحالة والفراغات.
    /// </summary>
    public static List<string> ParseCodes(string? raw) =>
        (raw ?? string.Empty)
            .Split(CodeSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>خريطة رمز الموظف ← معرّفه، جاهزة للمطابقة (آخر تكرار يفوز).</summary>
    public static Dictionary<string, int> BuildCodeMap(IEnumerable<(string Code, int Id)> employees)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (code, id) in employees)
        {
            var key = CodeKey(code);
            if (key.Length > 0) map[key] = id;
        }
        return map;
    }

    /// <summary>
    /// يطابق أكواداً مفكَّكة بخريطة الأرقام. المفقود يُعاد بنصّه الأصلي ليُعرض
    /// للمستخدم كما كتبه — عددٌ مجرّد لا يكفي لتصحيح كود مخطئ.
    /// </summary>
    public static (List<int> Ids, List<string> Missing) MatchCodes(
        IEnumerable<string> codes, IReadOnlyDictionary<string, int> byCode)
    {
        var ids = new List<int>();
        var missing = new List<string>();

        foreach (var code in codes)
        {
            var key = CodeKey(code);
            if (key.Length == 0) continue;
            if (byCode.TryGetValue(key, out var id))
            {
                if (!ids.Contains(id)) ids.Add(id);
            }
            else
            {
                missing.Add(code.Trim());
            }
        }

        return (ids, missing);
    }

    /// <summary>نطاق فارغ ⟹ الكل. أي منطق آخر يجعل تشغيلاً بلا صفوف يحسب صفراً.</summary>
    public static bool Includes(IReadOnlyCollection<int> scope, int employeeId) =>
        scope.Count == 0 || scope.Contains(employeeId);

    /// <summary>
    /// معرّفات النطاق التي لا مقابل لها بقائمة الموظفين القابلين للاحتساب (غير نشط
    /// أو محذوف). تُعرض بعد التشغيل حتى لا يغيب موظفٌ اختاره المستخدم بلا تفسير.
    /// </summary>
    public static List<int> OutsideCandidates(IReadOnlyCollection<int> scope, IReadOnlyCollection<int> candidateIds)
    {
        if (scope.Count == 0) return new List<int>();
        var candidates = candidateIds as HashSet<int> ?? new HashSet<int>(candidateIds);
        return scope.Where(id => !candidates.Contains(id)).ToList();
    }
}
