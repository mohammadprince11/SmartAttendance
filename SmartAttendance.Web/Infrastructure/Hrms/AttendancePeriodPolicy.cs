using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// يحوّل «شهراً» مختاراً إلى **فترة غلق الحضور** الفعلية حسب سياسة الشركة
/// (مثال: 21 → 20 يعني 21 من الشهر السابق حتى 20 من الحالي).
///
/// دالة نقية بلا قاعدة بيانات — لأن الحدّ الخطأ هنا يزيح يوماً كاملاً من شهرٍ
/// إلى شهر، فيُحتسب حضوره أو غيابه بمسير الشهر الخطأ.
///
/// **تسمية الفترة**: تحمل اسم الشهر الذي فيه **أكثر أيام** من الفترة (قرار محمد
/// 2026-08-04). فـ21/06 → 20/07 تُسمّى «تموز» لأن فيه 20 يوماً مقابل 10 بحزيران.
/// </summary>
public static class AttendancePeriodPolicy
{
    /// <summary>
    /// يقرأ **سياسة غلق الحضور** النشطة من القاعدة ويحسم الفترة للشهر المُسمّى.
    /// بلا سياسة نشطة ⟹ الشهر التقويمي كاملاً (سلوك ما قبل السياسات).
    ///
    /// <para>كان هذا المنطق مكرّراً داخل صفحة الحضور اليومي وحدها، فكانت الشاشات
    /// الأخرى تعرض الشهر التقويمي بينما المسير يقرأ فترة الغلق — أي رقمان مختلفان
    /// لنفس الشهر. مركزيّته هنا تجعل كل الشاشات تتبع نفس الحدّ.</para>
    /// </summary>
    /// <returns>الفترة، واسم السياسة (فارغ إن لم توجد سياسة نشطة).</returns>
    public static async Task<(Period Period, string? PolicyName)> ResolveFromPolicyAsync(
        ApplicationDbContext dbContext, int labelYear, int labelMonth,
        // نوع السياسة: Attendance (الافتراض · 21→20) · WorkingDays (1→30) · Hiring …
        // فكل نوعٍ فترته الخاصة — «ماكو شي ثابت، كلها سياسة».
        SmartAttendance.Domain.Enums.PayrollCutoffType policyType =
            SmartAttendance.Domain.Enums.PayrollCutoffType.Attendance)
    {
        var policy = await (
            from p in dbContext.PayrollCutoffPolicies.AsNoTracking()
            join t in dbContext.PayrollCutoffPolicyTypes.AsNoTracking()
                on p.Id equals t.PayrollCutoffPolicyId
            where p.IsActive && !p.IsDeleted && !t.IsDeleted
                  && t.PolicyType == policyType
            orderby p.Id
            select new { p.Name, p.FromDay, p.ToDay }).FirstOrDefaultAsync();

        if (policy is null)
        {
            return (Resolve(labelYear, labelMonth, 1, DateTime.DaysInMonth(labelYear, labelMonth)), null);
        }

        return (Resolve(labelYear, labelMonth, policy.FromDay, policy.ToDay), policy.Name);
    }

    /// <summary>فترة محسومة: مداها، والشهر الذي تُسمّى به.</summary>
    public readonly record struct Period(
        DateOnly From,
        DateOnly To,
        int LabelYear,
        int LabelMonth)
    {
        public int DayCount => To.DayNumber - From.DayNumber + 1;

        /// <summary>
        /// أيام الفترة بالترتيب — أعمدة أي شبكة تقويمية تتبع الغلق.
        /// تُعدَّد كتواريخ كاملة لا كأرقام أيام: الترقيم يصلح للفترة القياسية
        /// بالمصادفة وحدها، ويلتبس بأي فترة تتجاوز شهراً.
        /// </summary>
        public IEnumerable<DateOnly> EachDay()
        {
            for (var day = From; day <= To; day = day.AddDays(1))
            {
                yield return day;
            }
        }

        /// <summary>الأشهر التقويمية التي تلمسها الفترة — التحليل يُبنى شهراً شهراً.</summary>
        public IEnumerable<(int Year, int Month)> CoveredMonths()
        {
            var cursor = new DateOnly(From.Year, From.Month, 1);
            var last = new DateOnly(To.Year, To.Month, 1);

            while (cursor <= last)
            {
                yield return (cursor.Year, cursor.Month);
                cursor = cursor.AddMonths(1);
            }
        }
    }

    /// <summary>
    /// يحسب الفترة المسمّاة <paramref name="labelYear"/>/<paramref name="labelMonth"/>.
    ///
    /// <paramref name="fromDay"/> ≤ <paramref name="toDay"/> ⟹ الفترة داخل الشهر نفسه.
    /// وإلا فهي عابرة لحدّ الشهر، ونختار أيّ الطرفين يجعل شهر التسمية هو الأكثر أياماً.
    /// </summary>
    public static Period Resolve(int labelYear, int labelMonth, int fromDay, int toDay)
    {
        var label = new DateOnly(labelYear, labelMonth, 1);

        // بلا سياسة صالحة نرجع للشهر التقويمي كاملاً — سلوك النظام قبل السياسات.
        if (fromDay < 1 || fromDay > 31 || toDay < 1 || toDay > 31)
        {
            return FullMonth(label);
        }

        if (fromDay <= toDay)
        {
            return new Period(
                ClampDay(label, fromDay),
                ClampDay(label, toDay),
                labelYear,
                labelMonth);
        }

        // عابرة: إمّا (الشهر السابق ← شهر التسمية) أو (شهر التسمية ← التالي).
        // نأخذ الترتيب الذي يجعل شهر التسمية صاحب الأيام الأكثر.
        var previous = label.AddMonths(-1);
        var next = label.AddMonths(1);

        var daysIfLabelEnds = Math.Min(toDay, DaysIn(label));
        var daysIfLabelStarts = DaysIn(label) - Math.Min(fromDay, DaysIn(label)) + 1;

        return daysIfLabelEnds >= daysIfLabelStarts
            ? new Period(ClampDay(previous, fromDay), ClampDay(label, toDay), labelYear, labelMonth)
            : new Period(ClampDay(label, fromDay), ClampDay(next, toDay), labelYear, labelMonth);
    }

    private static Period FullMonth(DateOnly label) =>
        new(label, new DateOnly(label.Year, label.Month, DaysIn(label)), label.Year, label.Month);

    /// <summary>شباط لا يملك يوم 30: اليوم المطلوب يُقصَر على آخر أيام الشهر.</summary>
    private static DateOnly ClampDay(DateOnly month, int day) =>
        new(month.Year, month.Month, Math.Min(day, DaysIn(month)));

    private static int DaysIn(DateOnly month) =>
        DateTime.DaysInMonth(month.Year, month.Month);
}
