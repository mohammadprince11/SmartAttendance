namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// حسم **ملف الضريبة/الضمان لكل موظف** — منطق نقيّ بلا قاعدة بيانات.
///
/// كان النظام أحادي الملف: <c>ActiveTaxProfileAsync</c> يعيد ملفاً نشطاً واحداً
/// للنظام كلّه، فيُطبَّق على الجميع بلا تفريق — ولا يمكن أن يختلف اقتطاع المواطن
/// عن الوافد إلا بفرعٍ بالكود. وبكيان الاختلاف **بيانات على الموظف**: حقلا «ملف
/// الضريبة» و«الضمان الاجتماعي» بنموذج المعلومات المالية، وخيارات الضمان الحيّة
/// هناك (سعودي · غير سعودي · سامي · ضمان) ليست إلا ملفاتٍ بأسماء.
///
/// ⭐ **ثلاث درجات، الأخصّ يفوز:**
/// <list type="number">
/// <item><b>إسناد صريح على الموظف</b> — قرار إنسان مكتوب، يعلو على كل اشتقاق.</item>
/// <item><b>ملف بشروط تنطبق</b> — «هذا الملف للمواطنين» بمحرّك الشروط العام،
///       فيُختار تلقائياً بلا إسناد يدوي لـ1356 موظفاً.</item>
/// <item><b>الملف النشط</b> — سلوك اليوم حرفياً.</item>
/// </list>
///
/// ⚠️ **الدرجة الثالثة هي شرطي على نفسي**: موظفٌ بلا إسناد وبلا ملفٍ مشروطٍ ينطبق
/// عليه يأخذ **نفس ملف اليوم بالضبط**، فالقسيمة لا تتغيّر بفلس. الميزة تُفعَّل
/// بالبيانات (إسناد أو شرط) لا بمجرّد وجود الكود — وهذا ما يجعلها آمنة على قاعدة
/// إنتاجٍ حيّة تشترك مع التطوير.
///
/// وملفٌّ **بلا شروط لا يُلتقط بالدرجة الثانية أبداً** (<c>matchWhenEmpty: false</c>)
/// — وإلا صار أوّل ملفٍ بالقائمة سياسةَ الشركة كلّها بصمت، وهو نفس فخّ نُسَخ
/// السياسة بـ<see cref="HrPolicyResolver"/>.
/// </summary>
public static class PayrollProfileResolver
{
    /// <summary>ملفٌّ مرشَّح — القدر المشترك بين ملف الضريبة وملف الضمان.</summary>
    public sealed record Candidate(
        int Id,
        string Name,
        int SortOrder,
        bool IsActive,
        HrConditions.ConditionSet? Conditions);

    /// <summary>أي درجة حسمت الاختيار — تُعرض بالواجهة فيُفهم «لماذا هذا الملف؟».</summary>
    public enum Origin
    {
        /// <summary>لا ملف أصلاً (قائمة فارغة).</summary>
        None,
        Assigned,
        Condition,
        Active
    }

    public sealed record Resolution(int? ProfileId, string Name, Origin Origin)
    {
        public string SourceLabel => Origin switch
        {
            Origin.Assigned => "إسناد على الموظف",
            Origin.Condition => "شرط الملف",
            Origin.Active => "الملف النشط",
            _ => "بلا ملف"
        };
    }

    public static readonly Resolution NoProfile = new(null, string.Empty, Origin.None);

    /// <summary>
    /// يحسم الملف لموظف واحد. <paramref name="profiles"/> بترتيب القراءة نفسه
    /// (الاسم) لأن الدرجة الثالثة تعتمده حرفياً كسلوك اليوم.
    /// </summary>
    public static Resolution Resolve(
        int? assignedProfileId,
        IReadOnlyList<Candidate> profiles,
        IReadOnlyDictionary<string, HrConditions.Fact> facts)
    {
        if (profiles.Count == 0)
        {
            return NoProfile;
        }

        // (1) الإسناد الصريح — حتى لو كان الملف غير نشط: «نشط» تعني الافتراض
        // للجميع، لا صلاحية الملف. إسنادٌ يُتجاهَل بصمت أسوأ من ملفٍ غير نشط.
        if (assignedProfileId is > 0)
        {
            var assigned = profiles.FirstOrDefault(profile => profile.Id == assignedProfileId);
            if (assigned is not null)
            {
                return new Resolution(assigned.Id, assigned.Name, Origin.Assigned);
            }

            // ملفٌ أُسند ثم حُذف ⟹ نُكمل للدرجات الأدنى بدل إسقاط القسيمة.
        }

        // (2) أول ملف مشروط تنطبق شروطه — الترتيب أسبقية ظاهرة يحرّرها المستخدم.
        foreach (var candidate in profiles
                     .Where(profile => profile.IsActive)
                     .Where(profile => profile.Conditions is { IsEmpty: false })
                     .OrderBy(profile => profile.SortOrder)
                     .ThenBy(profile => profile.Id))
        {
            if (HrConditions.Matches(candidate.Conditions, facts, matchWhenEmpty: false))
            {
                return new Resolution(candidate.Id, candidate.Name, Origin.Condition);
            }
        }

        // (3) سلوك اليوم حرفياً: أول نشط، وإلا أول ملف على الإطلاق.
        var active = profiles.FirstOrDefault(profile => profile.IsActive) ?? profiles[0];
        return new Resolution(active.Id, active.Name, Origin.Active);
    }
}
