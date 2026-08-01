namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// كتالوج مقاسات الواجهة — **مصدر الحقيقة الوحيد** لأحجام عناصر التحكّم.
///
/// **لماذا وُجد؟** كان بالمشروع 71 ملف CSS و6735 <c>!important</c>، وارتفاع
/// عنصر التحكّم مثبَّت بالكود بثمانِ قيمٍ مختلفة (42px بـ65 موضعاً · 40px بـ52 ·
/// 44px بـ48 · 38px بـ46 …). فما كانت الأزرار «غير موحّدة» صدفةً: لم يكن ثمّة
/// مكانٌ واحد يقول كم ارتفاع الزرّ. كل صفحة تكتب رقمها وتفرضه، فتتصارع بالتخصصية.
///
/// كل توكن هنا يصير متغيّر CSS يُحقَن بالتخطيط، ويقرأه <c>zynora-tokens.css</c>.
/// إضافة توكن هنا وحدها لا تكفي — **يجب أن يستهلكه أحد**، وإلا صار حقلاً صمّاءً
/// وهو ما ننتقده على غيرنا.
/// </summary>
public static class DesignTokenCatalog
{
    /// <param name="Key">مفتاح التخزين ومتغيّر الـCSS معاً (<c>--zy-control-h</c>).</param>
    /// <param name="Label">ما يراه المستخدم بالمختبر.</param>
    /// <param name="Default">القيمة الحالية بالنظام — تغييرها يغيّر المظهر الافتراضي للجميع.</param>
    /// <param name="Min">أدنى قيمة مسموحة. ليست تجميلاً: زرٌّ بـ20px هدفُ نقرٍ يتعذّر على الإصبع.</param>
    public sealed record Token(
        string Key,
        string Label,
        string Group,
        double Default,
        double Min,
        double Max,
        double Step = 1,
        string Unit = "px",
        string? Hint = null);

    public const string GroupButtons = "الأزرار";
    public const string GroupControls = "الحقول (البوكس)";
    public const string GroupCalendar = "الكلاندر (حقل التاريخ)";
    public const string GroupDropdown = "المنسدلة";
    public const string GroupEmployeePicker = "منتقي الموظف";
    public const string GroupChecks = "الجيك بوكس والراديو";
    public const string GroupText = "العناوين والنصوص";
    public const string GroupSurfaces = "الجداول والبطاقات";
    public const string GroupChips = "الشارات";

    /// <summary>ترتيب المجموعات كما تُعرض بالمختبر.</summary>
    public static readonly IReadOnlyList<string> Groups = new[]
    {
        GroupButtons, GroupControls, GroupCalendar, GroupDropdown, GroupEmployeePicker,
        GroupChecks, GroupText, GroupSurfaces, GroupChips,
    };

    public static readonly IReadOnlyList<Token> All = new[]
    {
        // ---- الأزرار ----
        //
        // مفصولةٌ عن الحقول لأن قيمها الحقيقية بالنظام مختلفة (زرّ 40px بحشوة 18
        // وخطّ 14 · حقل 44px بحشوة 12 وخطّ 13). دمجُهما بمقياسٍ واحد كان سيجعل
        // «الافتراضي» بالمختبر يخالف ما تراه بالشاشة.
        new Token("button-h", "ارتفاع الزرّ", GroupButtons, 40, 28, 60,
            Hint: "الارتفاع الموحَّد لكل أزرار النظام — 104 عائلة زرّ كانت تُقاس كلٌّ على حدة."),
        new Token("button-pad-x", "الحشوة الأفقية", GroupButtons, 18, 6, 40),
        new Token("button-radius", "استدارة الزرّ", GroupButtons, 12, 0, 24),
        new Token("button-font", "حجم خطّه", GroupButtons, 14, 10, 20, 0.5),
        new Token("button-weight", "ثخانة خطّه", GroupButtons, 600, 400, 900, 100, ""),

        // ---- الحقول ----
        new Token("control-h", "ارتفاع الحقل", GroupControls, 44, 28, 60,
            Hint: "كان مثبَّتاً بثمانِ قيمٍ مختلفة عبر 71 ملف CSS."),
        new Token("control-pad-x", "الحشوة الأفقية داخل الحقل", GroupControls, 12, 4, 28),
        new Token("control-radius", "استدارة الحقل", GroupControls, 12, 0, 24),
        new Token("control-font", "حجم خطّ الحقل", GroupControls, 13, 10, 18, 0.5),
        new Token("control-gap", "المسافة بين الحقول", GroupControls, 14, 4, 32),

        // ---- الكلاندر · المنسدلة · المنتقي ----
        //
        // ثلاثتها كانت تتقاسم مقياساً واحداً، فلم يكن ممكناً جعل الكلاندر 44
        // والمنتقي 52. صار لكلٍّ مختبره. والافتراضات متطابقة عمداً: اختلاف
        // ارتفاعاتها هو ما سبّب الإزاحة الرأسية بصفوف الحقول.
        new Token("calendar-h", "ارتفاع حقل التاريخ", GroupCalendar, 44, 28, 60,
            Hint: "يُطابَق بارتفاع الحقل العادي افتراضياً — اختلافهما كان يزيح الصفّ."),
        new Token("calendar-radius", "استدارته", GroupCalendar, 12, 0, 24),
        new Token("calendar-font", "حجم خطّه", GroupCalendar, 13, 10, 18, 0.5),
        // اللوحة المنسدلة (شبكة الأيام) كانت وحدها بلا مقياس بينما للمنسدلة
        // `dropdown-menu-radius` — الافتراضي 22 هو رقمها الحاليّ بـv18 كي لا
        // يتغيّر مظهر أحد بمجرّد ظهور المقياس.
        new Token("calendar-menu-radius", "استدارة اللوحة المنسدلة", GroupCalendar, 22, 0, 34),

        new Token("dropdown-h", "ارتفاع المنسدلة", GroupDropdown, 44, 28, 60),
        new Token("dropdown-radius", "استدارتها", GroupDropdown, 12, 0, 24),
        new Token("dropdown-font", "حجم خطّها", GroupDropdown, 13, 10, 18, 0.5),
        new Token("dropdown-menu-radius", "استدارة القائمة المفتوحة", GroupDropdown, 14, 0, 28),

        new Token("empicker-h", "ارتفاع المنتقي", GroupEmployeePicker, 44, 32, 60),
        new Token("empicker-radius", "استدارته", GroupEmployeePicker, 12, 0, 24),
        new Token("empicker-font", "حجم خطّه", GroupEmployeePicker, 13, 10, 18, 0.5),
        new Token("empicker-code-w", "عرض خانة «رمز»", GroupEmployeePicker, 108, 60, 200),
        new Token("empicker-row-h", "ارتفاع صفّ النتيجة بنافذة البحث", GroupEmployeePicker, 40, 28, 60),
        new Token("empicker-modal-radius", "استدارة نافذة البحث", GroupEmployeePicker, 14, 0, 28),

        // ---- الجيك بوكس ----
        new Token("check-size", "حجم المربّع", GroupChecks, 18, 12, 30),
        new Token("check-radius", "استدارته", GroupChecks, 5, 0, 15),
        new Token("check-gap", "المسافة بينه وبين نصّه", GroupChecks, 10, 2, 24),
        new Token("check-font", "حجم نصّه", GroupChecks, 13, 10, 18, 0.5),

        // ---- العناوين والنصوص ----
        new Token("h1", "عنوان الصفحة (h1)", GroupText, 26, 16, 44),
        new Token("h2", "عنوان القسم (h2)", GroupText, 20, 14, 34),
        new Token("h3", "عنوان البطاقة (h3)", GroupText, 16, 12, 28),
        new Token("body-font", "النصّ العادي", GroupText, 13, 10, 20, 0.5),
        new Token("label-font", "عنوان الحقل", GroupText, 12, 9, 18, 0.5),
        new Token("hint-font", "النصّ التوضيحي الصغير", GroupText, 11, 8, 16, 0.5),
        new Token("line-height", "تباعد الأسطر", GroupText, 1.55, 1.1, 2.2, 0.05, ""),

        // ---- الجداول والبطاقات ----
        new Token("row-h", "ارتفاع صفّ الجدول", GroupSurfaces, 44, 28, 72),
        new Token("cell-pad-x", "حشوة الخلية الأفقية", GroupSurfaces, 12, 4, 28),
        new Token("card-pad", "حشوة البطاقة", GroupSurfaces, 16, 6, 36),
        new Token("card-radius", "استدارة البطاقة", GroupSurfaces, 18, 0, 34),
        new Token("card-gap", "المسافة بين البطاقات", GroupSurfaces, 14, 4, 32),

        // ---- الشارات ----
        //
        // ⚠️ **حُذف `tab-h` و`tab-font` عمداً — ولا يُعادان.** كانت التبويبات
        // العلوية للصفحات (`nxupd-top-tabs > a`) تُرسَم كأزرار تماماً بينما
        // يحكمها مقياسٌ آخر، فمن ضبط الأزرار على 34px وجد ثلاثة «أزرار» بـ51px
        // وسط صفحته ولم يعرف من أين تأتي. الشكل البصريّ الواحد لا يحتمل مصدرَي
        // حقيقة: التبويبات صارت تتبع مقاس الزرّ، فتتساوى بلا ضبطٍ منفصل.
        //
        // الشارة تبقى مستقلّة لأنها **شكلٌ مختلف فعلاً**: كبسولة صغيرة للعرض لا
        // هدفَ نقرٍ، ولها حدّ أدنى أصغر (18px) لا يصحّ لزرّ.
        //
        // الافتراضات تتبع **الغالبية المقيسة حيّاً** لا أرقاماً قديمة: بعد ضمّ
        // عائلة شارة الحالة صار 27 من 29 شارةً بالنظام على حشوة 10 وخطّ 12
        // واستدارة 6 (قِيست على ثلاث صفحات). فالافتراضي يُحاذي الكتلة كي لا
        // يتغيّر شكل 25 شارةً يوم الضمّ.
        new Token("pill-h", "ارتفاع الشارة", GroupChips, 28, 18, 44),
        new Token("pill-pad-x", "حشوتها الأفقية", GroupChips, 10, 4, 26),
        new Token("pill-radius", "استدارتها", GroupChips, 6, 0, 20),
        new Token("pill-font", "حجم خطّها", GroupChips, 12, 8, 16, 0.5),
    };

    private static readonly Dictionary<string, Token> ByKey =
        All.ToDictionary(token => token.Key, StringComparer.OrdinalIgnoreCase);

    public static Token? Find(string key) =>
        ByKey.TryGetValue(key, out var token) ? token : null;

    /// <summary>
    /// يقصر القيمة على مدى التوكن. القصْر بالخادم لا بالمتصفّح فقط: طلبٌ مُلفَّق
    /// بـ<c>control-h=9999</c> يجعل كل زرّ بالنظام شريطاً بارتفاع الشاشة، والمختبر
    /// يكتب لكل المستخدمين لا لصاحب الطلب وحده.
    /// </summary>
    public static double Clamp(Token token, double value) =>
        Math.Clamp(double.IsFinite(value) ? value : token.Default, token.Min, token.Max);

    /// <summary>الصياغة بثقافةٍ ثابتة — «12,5px» بفاصلةٍ عربية تكسر الـCSS صمتاً.</summary>
    public static string Format(Token token, double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + token.Unit;
}
