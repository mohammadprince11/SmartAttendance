namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// وعاءٌ نظاميّ (ضريبة/ضمان) قد يكون مصدره رقماً <b>يُدخله المستخدم بالملف المالي</b>
/// بدل التركيب من مكوّنات القسيمة.
///
/// <para><b>العطلان اللذان يصحّحهما:</b> (1) <c>SocialSecuritySalary</c> على الملف
/// المالي كان يُدخَل ويُحفَظ لكن المسير لا يقرؤه أبداً — كان وعاء الضمان الإجماليَّ
/// كاملاً دوماً. (2) لم يكن لراتب الضريبة الراهن حقلٌ أصلاً؛ <c>PreviousTaxSalary</c>
/// رصيدٌ افتتاحيّ تاريخيّ لا وعاءٌ راهن.</para>
///
/// <para><b>النمط:</b> <see cref="ModeSalaryComponents"/> (تركيبٌ من المكوّنات — السلوك
/// القائم) أو <see cref="ModeEmployeeDefined"/> (الرقم المُدخَل). النمط الفارغ يُقرأ
/// «مكوّنات» فيبقى وعاء اليوم حرفياً — توافقٌ خلفيّ يُبقي الاختبارات والقسائم القائمة.</para>
///
/// <para>⚠️ <b>بلا رجوعٍ صامت (Phase 26):</b> إن اختير «مُعرَّف بالموظف» ولا رقم له،
/// لا نتظاهر بأن التركيب هو الرقم المقصود — نعيده مع <see cref="Source.EmployeeMissing"/>
/// كي ينبّه المسير (تحذير/استبعاد) بدل احتساب وعاءٍ لم يقصده المستخدم.</para>
/// </summary>
public static class EmployeeDefinedSalaryBase
{
    /// <summary>الوعاء يُركَّب من مكوّنات القسيمة (SalaryBaseComposer) — السلوك القائم.</summary>
    public const string ModeSalaryComponents = "SalaryComponents";

    /// <summary>الوعاء هو الرقم المُدخَل بالملف المالي (راتب الضمان/الضريبة).</summary>
    public const string ModeEmployeeDefined = "EmployeeDefined";

    public enum Source
    {
        /// <summary>مُركَّب من المكوّنات.</summary>
        Composed,
        /// <summary>رقم الموظف المُدخَل.</summary>
        Employee,
        /// <summary>اختير «مُعرَّف بالموظف» لكن لا رقم — رجوعٌ معلَّم لا صامت.</summary>
        EmployeeMissing
    }

    public sealed record Result(decimal Base, Source Source);

    public static string NormalizeMode(string? mode) =>
        string.Equals(mode?.Trim(), ModeEmployeeDefined, StringComparison.OrdinalIgnoreCase)
            ? ModeEmployeeDefined
            : ModeSalaryComponents;

    /// <summary>
    /// يحسم الوعاء الفعّال. <paramref name="employeeValue"/> = راتب الضمان أو الضريبة
    /// المُدخَل (قد يكون <c>null</c>)، و<paramref name="composed"/> = الوعاء المُركَّب.
    /// </summary>
    public static Result Resolve(string? mode, decimal? employeeValue, decimal composed)
    {
        if (NormalizeMode(mode) != ModeEmployeeDefined)
            return new Result(composed, Source.Composed);

        if (employeeValue is > 0m)
            return new Result(employeeValue.Value, Source.Employee);

        // «مُعرَّف بالموظف» بلا رقم: نرجع للمركَّب لكن نعلّمه ناقصاً — لا رجوع صامت.
        return new Result(composed, Source.EmployeeMissing);
    }
}
