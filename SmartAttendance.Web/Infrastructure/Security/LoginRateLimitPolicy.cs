namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// حدّ معدّل محاولات الدخول (P1-7 بتدقيق الجاهزية).
///
/// <para><b>الفجوة</b>: الحماية الوحيدة كانت قفل الحساب (5 محاولات) — وهو <b>لكل
/// حساب</b>. رشُّ كلمة مرور واحدة على ألف حساب لا يلمس أي عدّاد قفل، فيمرّ بلا
/// مقاومة. وبالعكس: القفل نفسه سلاح تعطيل — معرفة اسم المستخدم تكفي لإقفال المدير
/// متى شئت. الحدّ <b>بعنوان الطالب</b> يغلق الاتجاهين: يخنق الرشّ، ويُبطئ من يحاول
/// إقفال حسابات غيره.</para>
///
/// <para>القرارات نقيّة هنا كي تُختبَر بلا خادم — بناء المحدِّد نفسه بـ
/// <c>Program.cs</c>.</para>
/// </summary>
public static class LoginRateLimitPolicy
{
    public const string PolicyName = "login";

    /// <summary>محاولات مسموحة بالنافذة لكل عنوان.</summary>
    public const int PermitLimit = 10;

    /// <summary>طول النافذة بالدقائق.</summary>
    public const int WindowMinutes = 5;

    /// <summary>
    /// هل يخضع هذا المسار للحدّ؟ مسارات الدخول وحدها — والحدُّ العام على كل النظام
    /// يخنق الاستعمال المشروع (شاشة الحضور تُحدَّث كثيراً) بلا مكسب أمنيّ.
    /// </summary>
    public static bool AppliesTo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var normalized = path.ToLowerInvariant();

        return normalized == "/account/login" ||
               normalized == "/api/auth/login" ||
               normalized == "/api/v1/auth/login" ||
               // WebAuthn: التحقّق يقبل تأكيداً بلا كوكي (دخول بالبصمة) فهو مسار دخول.
               normalized == "/api/webauthn/login/options" ||
               normalized == "/api/v1/webauthn/login/options" ||
               normalized == "/api/webauthn/login/verify" ||
               normalized == "/api/v1/webauthn/login/verify";
    }

    /// <summary>
    /// يستهلك الدلو طلبُ المصادقة الفعلي فقط. عرض نموذج الدخول بـ GET ليس محاولة
    /// كلمة مرور، واحتسابه يسمح لمهاجم بحجب صفحة الدخول عن عنوان مشترك بمجرد
    /// تحديثها دون إرسال أي اعتماد.
    /// </summary>
    public static bool AppliesToAttempt(string? method, string? path) =>
        string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) &&
        AppliesTo(path);

    /// <summary>
    /// مفتاح التقسيم: عنوان الطالب، أو <c>unknown</c> حين يتعذّر.
    ///
    /// <para><b>مغلق الفشل:</b> غياب العنوان يجمع كل المجهولين بدلوٍ واحد بدل
    /// إعفائهم — الإعفاء يجعل تزوير غياب العنوان طريقاً للالتفاف.</para>
    ///
    /// <para>⚠️ العنوان خلف وسيط عكسيّ يجب أن يكون <b>مُطبَّعاً</b> من
    /// <c>UseForwardedHeaders</c> بقائمة وسطاء موثوقة — وهو مضبوط بهذا النظام. بلا
    /// ذلك يصير كل الطلبات من عنوان الوسيط فيصير الحدّ عامّاً لا لكل طالب.</para>
    /// </summary>
    public static string PartitionKey(string? remoteIpAddress) =>
        string.IsNullOrWhiteSpace(remoteIpAddress) ? "unknown" : remoteIpAddress;
}
