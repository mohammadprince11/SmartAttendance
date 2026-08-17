namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// سياسة «إجبار تغيير كلمة المرور»: حسابٌ موسومٌ بـ<c>MustChangePassword</c> لا يصل
/// أيّ صفحةٍ إلا صفحة التغيير أو الخروج، حتى يعيّن كلمة مرورٍ جديدة. دالّة نقيّة
/// قابلة للاختبار كي لا يتسرّب قرار «أيّ مسارٍ معفى» إلى الحارس نفسه.
///
/// صفحة التغيير مصنّفة <see cref="PathAccessClass.AnyAuthenticated"/> فيصلها أيّ
/// دورٍ (موظف/أدمن) دون المرور بكتالوج الأدوار.
/// </summary>
public static class ForcedPasswordChangePolicy
{
    /// <summary>صفحة تغيير كلمة المرور العامّة — وجهة إعادة التوجيه القسري.</summary>
    public const string PagePath = "/Account/ChangePassword";

    /// <summary>
    /// المسارات التي لا تُحوَّل قسرياً حتى لو كان الحساب موسوماً: صفحة التغيير
    /// نفسها (وإلا حلقة لانهائية) والخروج. الدخول عامٌّ أصلاً فلا يصل هنا.
    /// </summary>
    public static bool IsExempt(string? rawPath)
    {
        var path = (rawPath ?? "/").ToLowerInvariant().TrimEnd('/');

        return path == "/account/changepassword" ||
               path == "/account/logout" ||
               path == "/account/login";
    }
}
