namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// **قواعد اللائحة الانضباطية** — موادّها العامّة كمنطقٍ نقيّ لا كنصٍّ بوثيقة.
///
/// كانت اللائحة تدخل النظام جدولَ مخالفاتٍ وحده، وموادّها الإحدى عشرة تبقى ورقاً:
/// «لا يجوز اتهام الموظف بمخالفة مضى عليها ثلاثون يوماً» قاعدةٌ تمنع تسجيلاً، لا
/// جملةٌ تُقرأ. وكل مادّة هنا إمّا **تمنع** إجراءً أو **تُنبّه** إليه — وما لا
/// يفعل أيّاً منهما يبقى نصّاً معروضاً ولا يُدّعى أنه منفَّذ.
///
/// منطق نقيّ: التواريخ والأعداد تُمرَّر، ولا قاعدة بيانات هنا.
/// </summary>
public static class DisciplinaryPolicyRules
{
    // ── مادة 6 · تقادم الاتهام ───────────────────────────────────────────────

    /// <summary>الافتراض بلائحة الشركة: ثلاثون يوماً من علم الشركة بالمخالفة.</summary>
    public const int DefaultStaleDays = 30;

    /// <summary>
    /// هل سقط حقّ الاتهام بالتقادم؟ (مادة 6: «لا يجوز اتهام الموظف بمخالفة مضى
    /// على علم الشركة بها أكثر من ثلاثين يوماً، باستثناء ما يشكّل جريمة جنائية».)
    ///
    /// <paramref name="isCriminal"/> يرفع التقادم — المخالفات الجنائية مستثناة
    /// بنصّ المادة. و<paramref name="limitDays"/> صفراً يعني **بلا تقادم**، لا
    /// «كل شيء متقادم»: الخلط بينهما كان سيمنع كل تسجيل.
    /// </summary>
    public static bool IsStale(DateOnly eventDate, DateOnly today, int limitDays, bool isCriminal = false)
    {
        if (limitDays <= 0 || isCriminal) return false;

        return today.DayNumber - eventDate.DayNumber > limitDays;
    }

    // ── مادة 5 · نافذة احتساب التكرار ────────────────────────────────────────

    /// <summary>
    /// كم مخالفةً سابقة تُحتسب تكراراً؟ (مادة 5: التشديد إن تكرّرت **قبل مضيّ
    /// سنة**، وبعدها تُعدّ المخالفة الجديدة **أولى**.)
    ///
    /// يُعيد رقم التكرار الحالي — واحدٌ يعني «أولى» لا «لا سابقة له».
    /// </summary>
    public static int OccurrenceNumber(
        IEnumerable<DateOnly> previousDates,
        DateOnly eventDate,
        int windowMonths = 12)
    {
        if (windowMonths <= 0) return 1;

        var since = eventDate.AddMonths(-windowMonths);

        // السابقة **قبل** الحدث وحدها تُحتسب: مخالفةٌ لاحقة لا تجعل ما قبلها تكراراً.
        var counted = previousDates.Count(d => d > since && d <= eventDate);

        return counted + 1;
    }

    // ── «هام» · قواعد التصعيد التعاقدي ───────────────────────────────────────

    /// <summary>الفئات التي تُحتسب لقاعدتَي التصعيد — «ب» و«ج» بنصّ اللائحة.</summary>
    public static bool CountsForEscalation(string? categoryName) =>
        !string.IsNullOrWhiteSpace(categoryName)
        && (categoryName.StartsWith("ب", StringComparison.Ordinal)
            || categoryName.StartsWith("ج", StringComparison.Ordinal));

    /// <summary>
    /// «في حال حصول الموظف على خمس جزاءات أو أكثر من فئة (ب) أو (ج) خلال فترة
    /// العقد، لن يتم تجديد العقد.»
    ///
    /// **تنبيهٌ لا منع**: القرار قرار الإدارة، والنظام يقوله لا يفرضه.
    /// </summary>
    public static bool ReachedNonRenewal(int countInContract, int threshold = 5) =>
        threshold > 0 && countInContract >= threshold;

    /// <summary>
    /// «إذا تم توقيع إنذار بالفصل على الموظف، فإن ارتكابه أي مخالفة من فئة (ب)
    /// أو (ج) سيؤدي إلى إنهاء التعاقد مباشرة.»
    ///
    /// شرطان: إنذارٌ **ما زال سارياً** (لم يسقط)، ومخالفةٌ من الفئتين.
    /// </summary>
    public static bool TriggersImmediateTermination(bool hasActiveFinalWarning, string? categoryName) =>
        hasActiveFinalWarning && CountsForEscalation(categoryName);

    // ⚠️ **حُذفت `BlocksBenefits`**: دالّةٌ بلا مستهلك — مودل الترقيات لم يُبنَ
    // بعد. ومادة «هام/3» معلَنة **نصّاً مرجعياً** بتبويب القواعد، فلا شيء يَعِد
    // بإنفاذها. دالّةٌ صمّاء تنتظر مستهلكاً هي دَينٌ لا رصيد.

    // ── مادة 6 · التحقيق قبل الجزاء ──────────────────────────────────────────

    /// <summary>
    /// «لا يجوز فرض أي جزاء على الموظف إلا بعد إشعاره بالمخالفة وإجراء التحقيق
    /// معه تحريرياً، ويجوز الاكتفاء بالتحقيق الشفهي في المخالفات البسيطة.»
    ///
    /// فالتحقيق يُشترط للجزاءات **الجسيمة** وحدها حين تُفعَّل القاعدة: اشتراطه
    /// على «لفت نظر» كان سيوقف الشاشة عند كل تنبيه.
    /// </summary>
    public static bool RequiresInvestigation(string? actionType, bool enforce)
    {
        if (!enforce) return false;

        return actionType switch
        {
            PenaltyAction.SalaryDeduction => true,
            PenaltyAction.FinalWarning => true,
            PenaltyAction.Termination => true,
            PenaltyAction.RaiseDenial => true,
            _ => false
        };
    }

    // ── مادة 8 · مهلة التظلّم ────────────────────────────────────────────────
    //
    // ⚠️ **لا تُنفَّذ هنا.** مهلة الاعتراض مبنيّةٌ أصلاً بـ<see cref="ViolationConfigPolicy"/>
    // (`CanObject`) ويستهلكها تبويب الإجراءات التأديبية. كتبتُ لها نسخةً ثانية
    // بهذا الملف ثم حذفتُها: **مصدران لقاعدةٍ واحدة** أسوأ من غياب أحدهما —
    // يتغيّر أحدهما ويبقى الآخر، وهو نمط العطل الذي عالجناه بحقل «رقم الفقرة».
    //
    // وأمّا **مهلة البتّ بالتظلّم (سبعة أيام)** فغير منفَّذة بأي موضع، ومعلَنة
    // كذلك بتبويب «قواعد اللائحة» — لا يُدّعى إنفاذها.
}
