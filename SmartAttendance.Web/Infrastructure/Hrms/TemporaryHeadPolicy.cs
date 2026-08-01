namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تخصيص رئيس وحدة مؤقت (نظير `/Employees/TemporaryHeadofUnitAllocation` بكيان) —
/// منطق نقيّ.
///
/// **المشكلة التي يحلّها:** إجازة مديرٍ تجمّد كل ما يُوجَّه إليه. فيُسنَد رئيسٌ
/// مؤقت لوحدةٍ لمدى تواريخ، ويصير هو **المدير الفعّال** لموظفي تلك الوحدة خلاله.
///
/// ⚠️ **الإسناد لا يغيّر <c>DirectManagerId</c> بجدول الموظفين إطلاقاً.** الأصل
/// يبقى مصدر الحقيقة والنيابة طبقةٌ فوقه تنتهي بانتهاء تاريخها؛ ولو دهسنا الأصل
/// لَما عاد المدير الحقيقي بعد رجوعه من إجازته إلا بتدخّل يدوي — وهو خطأ لا يُكتشف
/// إلا بعد أن تضيع موافقات.
/// </summary>
public static class TemporaryHeadPolicy
{
    /// <summary>إسناد واحد: وحدة + رئيسها المؤقت + مدى السريان.</summary>
    public sealed record Allocation(
        int Id,
        int DepartmentId,
        int HeadEmployeeId,
        DateOnly FromDate,
        DateOnly ToDate,
        bool IsActive);

    /// <summary>سارٍ الآن: نشط والتاريخ داخل المدى (الطرفان محسوبان).</summary>
    public static bool IsEffective(Allocation allocation, DateOnly asOf) =>
        allocation.IsActive && allocation.FromDate <= asOf && allocation.ToDate >= asOf;

    /// <summary>
    /// المدير الفعّال لموظف بتاريخ معيّن.
    ///
    /// القواعد بترتيبها:
    /// 1. لا إسناد سارٍ لوحدته ⟹ مديره الأصلي.
    /// 2. أحدث إسناد سارٍ يفوز (الأكبر <c>Id</c>) — إسنادان متداخلان يعنيان
    ///    تصحيحاً لاحقاً، والأحدث هو المقصود.
    /// 3. **الرئيس المؤقت لا ينوب عن نفسه**: لو كان الموظف هو الرئيس المؤقت
    ///    نفسه، يبقى مديره الأصلي — وإلا صار موظفٌ مديرَ نفسه فتُعتمد طلباته ذاتياً.
    /// </summary>
    public static int? EffectiveManagerId(
        int employeeId,
        int departmentId,
        int? originalManagerId,
        IEnumerable<Allocation> allocations,
        DateOnly asOf)
    {
        var winner = allocations
            .Where(allocation => allocation.DepartmentId == departmentId)
            .Where(allocation => IsEffective(allocation, asOf))
            .OrderByDescending(allocation => allocation.Id)
            .FirstOrDefault();

        if (winner is null || winner.HeadEmployeeId == employeeId)
        {
            return originalManagerId;
        }

        return winner.HeadEmployeeId;
    }

    /// <summary>
    /// هل المدى صالح؟ نهايةٌ قبل بدايتها إسنادٌ لا يسري أبداً ويُحفظ بصمت — وهو
    /// عطل صامت بامتياز، فيُرفض عند الحفظ.
    /// </summary>
    public static bool IsValidRange(DateOnly fromDate, DateOnly toDate) => toDate >= fromDate;

    /// <summary>
    /// إسنادات تتداخل بنفس الوحدة ومدىً متقاطع — تُعرض تحذيراً لا تُمنع، لأن
    /// التصحيح بإسناد أحدث ممارسةٌ مشروعة (والقاعدة 2 تحسمه).
    /// </summary>
    public static List<Allocation> Overlapping(
        Allocation candidate,
        IEnumerable<Allocation> existing) =>
        existing
            .Where(allocation => allocation.Id != candidate.Id)
            .Where(allocation => allocation.DepartmentId == candidate.DepartmentId)
            .Where(allocation => allocation.IsActive)
            .Where(allocation => allocation.FromDate <= candidate.ToDate
                                 && allocation.ToDate >= candidate.FromDate)
            .ToList();

    public static string StatusLabel(Allocation allocation, DateOnly asOf)
    {
        if (!allocation.IsActive) return "معطّل";
        if (allocation.ToDate < asOf) return "منتهٍ";
        if (allocation.FromDate > asOf) return "مجدول";
        return "سارٍ الآن";
    }
}
