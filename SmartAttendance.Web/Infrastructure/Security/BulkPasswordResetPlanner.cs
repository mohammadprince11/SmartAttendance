namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// يحسب أيّ حسابات الدخول المحدّدة تُطبَّق عليها إعادة التعيين الجماعيّة وأيّها
/// يُتخطّى — دالّة نقيّة قابلة للاختبار كي لا يتسرّب حارس «لا تمسّ حسابك الشخصيّ»
/// إلى معالج الصفحة. الأدمن لا يعيد تعيين كلمة مروره الشخصيّة أثناء استخدامه
/// (لو أُجبر على التغيير + طُردت جلسته لخرج من الصفحة التي ينفّذ منها).
/// </summary>
public static class BulkPasswordResetPlanner
{
    public sealed record Plan(
        IReadOnlyList<int> Apply,
        IReadOnlyList<int> SkippedSelf);

    public static Plan Build(IEnumerable<int>? selectedLoginIds, int currentLoginId)
    {
        var distinct = (selectedLoginIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var apply = distinct
            .Where(id => id != currentLoginId)
            .ToList();

        var skippedSelf = distinct
            .Where(id => id == currentLoginId)
            .ToList();

        return new Plan(apply, skippedSelf);
    }
}
