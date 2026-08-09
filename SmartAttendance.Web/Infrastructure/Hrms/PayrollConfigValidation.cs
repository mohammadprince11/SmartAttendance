namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// حرّاس صحّة تهيئة المسير — نظامٌ ديناميكيّ يحرّر فيه المستخدم كل رقم، فالمدخل
/// الفاسد يجب أن يُرفض برسالةٍ واضحة لا أن يُنتج راتباً خاطئاً بصمت.
///
/// <para>دوالٌ نقيّة تُعيد قائمة أخطاء (فارغة ⟹ صالح)، تُستدعى من معالِجات الحفظ
/// قبل الكتابة. لا تُصلح المدخل ولا تحوّله صفراً — ترفضه وتُبقي القيمة القديمة.</para>
/// </summary>
public static class PayrollConfigValidation
{
    /// <summary>ملف الضمان: النسب بين 0 و100، والسقف غير سالب.</summary>
    public static List<string> ValidateGosi(decimal employeeRate, decimal companyRate, decimal ceiling)
    {
        var errors = new List<string>();
        if (employeeRate < 0m) errors.Add("نسبة الموظف لا يمكن أن تكون سالبة.");
        if (employeeRate > 100m) errors.Add("نسبة الموظف لا تتجاوز 100%.");
        if (companyRate < 0m) errors.Add("نسبة الشركة لا يمكن أن تكون سالبة.");
        if (companyRate > 100m) errors.Add("نسبة الشركة لا تتجاوز 100%.");
        if (ceiling < 0m) errors.Add("سقف الضمان لا يمكن أن يكون سالباً.");
        return errors;
    }

    /// <summary>
    /// ملف الضريبة: الإعفاء غير سالب، وكل شريحة نسبتها 0–100 وحدّها غير سالب،
    /// ونهايتها تتجاوز بدايتها، و<b>لا تداخل</b> بين الشرائح (التداخل يضاعف احتساب
    /// الشريحة المشتركة بـ<see cref="PayrollConfigStore.ComputeTax"/>).
    /// </summary>
    public static List<string> ValidateTax(
        decimal exemption, IReadOnlyList<(decimal From, decimal? To, decimal Rate)> brackets)
    {
        var errors = new List<string>();
        if (exemption < 0m) errors.Add("مبلغ الإعفاء لا يمكن أن يكون سالباً.");

        var ordered = brackets.OrderBy(b => b.From).ToList();
        decimal? previousTo = null;
        var previousOpenEnded = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var b = ordered[i];
            var n = i + 1;

            if (b.From < 0m) errors.Add($"الشريحة {n}: حدّ البداية لا يمكن أن يكون سالباً.");
            if (b.Rate < 0m || b.Rate > 100m) errors.Add($"الشريحة {n}: النسبة يجب أن تكون بين 0 و100%.");
            if (b.To is { } to && to <= b.From)
                errors.Add($"الشريحة {n}: نهايتها ({to:0.##}) يجب أن تتجاوز بدايتها ({b.From:0.##}).");

            // تداخل: شريحةٌ مفتوحة (بلا نهاية) يجب أن تكون الأخيرة، وبداية أي شريحة
            // يجب ألا تسبق نهاية سابقتها.
            if (i > 0)
            {
                if (previousOpenEnded)
                    errors.Add($"الشريحة {n}: تأتي بعد شريحةٍ مفتوحة النهاية — المفتوحة يجب أن تكون الأخيرة.");
                else if (previousTo is { } pt && b.From < pt)
                    errors.Add($"الشريحة {n}: بدايتها ({b.From:0.##}) تسبق نهاية سابقتها ({pt:0.##}) — تداخل.");
            }

            previousTo = b.To;
            previousOpenEnded = b.To is null;
        }

        return errors;
    }

    /// <summary>الأرقام المالية على الموظف: الرواتب المُدخَلة غير سالبة.</summary>
    public static List<string> ValidateEmployeeSalaries(decimal? basic, decimal? currentTaxSalary, decimal? socialSecuritySalary)
    {
        var errors = new List<string>();
        if (basic is < 0m) errors.Add("الراتب الأساسي لا يمكن أن يكون سالباً.");
        if (currentTaxSalary is < 0m) errors.Add("راتب الضريبة لا يمكن أن يكون سالباً.");
        if (socialSecuritySalary is < 0m) errors.Add("راتب الضمان لا يمكن أن يكون سالباً.");
        return errors;
    }

    /// <summary>الساعات المعيارية لليوم موجبة (مقام الأجر الساعي).</summary>
    public static List<string> ValidateStandardDailyHours(decimal hours)
    {
        var errors = new List<string>();
        if (hours <= 0m) errors.Add("الساعات المعيارية لليوم يجب أن تكون أكبر من صفر.");
        if (hours > 24m) errors.Add("الساعات المعيارية لليوم لا تتجاوز 24.");
        return errors;
    }
}
