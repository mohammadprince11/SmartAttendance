namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// وعاء الحضور: أي مكوّنات الاستحقاق يمسّها معامل الحضور، وأيّها يبقى كاملاً.
///
/// <para><b>العطل الذي يصحّحه:</b> كان المحرك ينسّب <c>الأساسي</c> وحده بالمعامل
/// (<c>Basic × factor</c>) ويضيف كل العلاوات <b>كاملةً</b> مهما كان الغياب. فلمنظّمةٍ
/// وعاؤها الحضوريّ = الأساسي + العلاوات المستحقّة، كان الغياب يخصم من الأساسي فقط.</para>
///
/// <para><b>النموذج:</b> كل مكوّن استحقاق إمّا <b>حسّاسٌ للحضور</b> (يُضرب بالمعامل)
/// أو <b>ثابتٌ</b> (كامل مهما كان الحضور). الحساسية سياسةٌ على عنصر الراتب
/// (<see cref="SalaryItemStore.SalaryItem.Prorated"/>) ترثها علاوة الموظف بالاسم؛
/// والأساسي حسّاسٌ دائماً كسلوك المحرك القائم.</para>
///
/// <para>⚠️ <b>توافق خلفيّ:</b> الافتراض <c>Prorated = false</c> لكل علاوة يعيد أرقام
/// المحرك السابق حرفياً — الأساسي وحده يُنسَّب والعلاوات كاملة. تُثبَّت بالاختبارات.
/// كلٌّ منسَّبٍ يُقرَّب لخانتين على حدة كما كان الأساسي، فلا ينحرف مجموع الحالة القديمة.</para>
/// </summary>
public static class AttendanceSalaryBase
{
    /// <summary>
    /// مكوّن استحقاق داخل الحساب: مبلغه، وهل هو حسّاسٌ لمعامل الحضور.
    /// </summary>
    public sealed record EarningComponent(decimal Amount, bool AttendanceSensitive);

    /// <summary>
    /// وعاء الحضور <b>قبل</b> المعامل — مجموع المكوّنات الحسّاسة كما هي. هذا هو الرقم
    /// الذي يُعرض بأثر الاحتساب («وعاء الحضور = 2,000,000») قبل ضربه بالمعامل.
    /// </summary>
    public static decimal Base(IEnumerable<EarningComponent> components)
    {
        if (components is null) return 0m;
        decimal total = 0m;
        foreach (var c in components)
            if (c.AttendanceSensitive) total += c.Amount;
        return decimal.Round(total, 2);
    }

    /// <summary>
    /// المبلغ المنسَّب لمكوّنٍ واحد: يُضرب بالمعامل إن كان حسّاساً ويُقرَّب لخانتين،
    /// وإلا يبقى كاملاً. القرار المفرد كي تُعرض العلاوة المنسَّبة ببندها الصحيح.
    /// </summary>
    public static decimal AdjustComponent(EarningComponent component, decimal factor) =>
        component.AttendanceSensitive
            ? decimal.Round(component.Amount * factor, 2)
            : component.Amount;

    /// <summary>
    /// إجمالي الاستحقاق بعد أثر الحضور: مجموع المكوّنات، كلٌّ منسَّبٌ أو ثابتٌ حسب
    /// حساسيّته. كلٌّ يُقرَّب على حدة (كسلوك تقريب الأساسي القائم) فلا ينحرف المجموع.
    /// </summary>
    public static decimal AttendanceAdjusted(IEnumerable<EarningComponent> components, decimal factor)
    {
        if (components is null) return 0m;
        decimal total = 0m;
        foreach (var c in components)
            total += AdjustComponent(c, factor);
        return total;
    }
}
