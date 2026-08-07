namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>تصنيف المسار من ناحية المصادقة، يستهلكه حارس المسارات.</summary>
public enum PathAccessClass
{
    /// <summary>يُخدَم بلا مصادقة إطلاقاً (أصول العرض وملفات الـPWA).</summary>
    Public = 0,

    /// <summary>يكفي أن يكون الطالب مصادَقاً بأي دور (صور الموظفين).</summary>
    AnyAuthenticated = 1,

    /// <summary>مصادَق + دور غير «موظف» (ملفات الباك أوفيس التاريخية).</summary>
    BackOfficeOnly = 2,

    /// <summary>لا حكم خاص — يُكمل الحارس بكتالوج الأدوار والصلاحيات.</summary>
    Guarded = 3
}

/// <summary>
/// المرحلة 6 — سياسة المسارات العامة، منتزعة من الحارس لتكون نقية وقابلة للاختبار.
///
/// الثغرة المُصلَحة: <c>/uploads/</c> كلها كانت عامة، فأي شخص يعرف/يخمّن اسم ملف
/// يفتح وثيقة تأديبية أو مستند موظف <b>بلا مصادقة</b>. الآن:
/// - العام يقتصر على شعارات الشركة وأصول الهوية والعرض.
/// - صور الموظفين تحتاج مصادقة (مضمّنة بوسوم img عبر كل النظام فتبقى بمسارها).
/// - بقية <c>/uploads/</c> التاريخية للباك أوفيس فقط.
/// - الملفات الحسّاسة الجديدة لا تُخزَّن تحت wwwroot أصلاً بل تمرّ بنقطة التنزيل
///   المصادَقة التي تفحص الصلاحية لكل موظف على حدة.
/// </summary>
public static class PublicPathPolicy
{
    public static PathAccessClass Classify(string? rawPath)
    {
        var path = (rawPath ?? "/").ToLowerInvariant();

        if (path == "/account/login" ||
            path == "/account/logout" ||
            path == "/accessdenied")
        {
            return PathAccessClass.Public;
        }

        // ملفات الـPWA بالجذر يجب أن تُخدَم بلا مصادقة (المتصفح يجلبها قبل/بعد الدخول).
        // ملاحظة: .NET يبصم اسم الملف (manifest.<hash>.webmanifest) فنطابق اللاحقة.
        if (path.EndsWith(".webmanifest") ||
            path == "/sw.js" ||
            path == "/offline.html" ||
            path == "/app.apk")
        {
            return PathAccessClass.Public;
        }

        // واجهة الموبايل (REST) تصادَق بتوكن Bearer داخل الكنترولرات لا بكوكيز هذا
        // الحارس، فنتركها تمرّ ويتولّى [Authorize(ApiToken)] الحماية.
        if (path.StartsWith("/api/"))
        {
            return PathAccessClass.Public;
        }

        // مسبارا المنصّة: تستدعيهما البنية التحتية قبل وجود أي جلسة، فحجبهما بالحارس
        // يجعل النسخة تبدو «غير جاهزة» دائماً فلا تصلها حركة أبداً.
        // لا يكشفان شيئاً: `live` بلا أي فحص، و`ready` حالة نجاح/فشل بلا تفاصيل.
        if (path == "/health/live" || path == "/health/ready")
        {
            return PathAccessClass.Public;
        }

        // نقاط Web-Push (كنترولر بمصادقة كوكي [Authorize]) خارج كتالوج الأدوار.
        if (path.StartsWith("/push/"))
        {
            return PathAccessClass.Public;
        }

        // التحقق من أصالة وثيقة صادرة: **عامّة عمداً** — يفحصها طرفٌ ثالث (بنك ·
        // سفارة) لا حساب له بالنظام، وهو جوهر الميزة.
        //
        // ⚠️ الصفحة لا تكشف شيئاً بلا الرمز الصحيح **وعامل التحقق** (رقم وطني أو
        // PIN)، ولا تعرض الوثيقة نفسها — تجيب «صحيحة/غير صحيحة» ببيانات دنيا فقط.
        if (path == "/verify" || path.StartsWith("/verify/"))
        {
            return PathAccessClass.Public;
        }

        // نقطة تنزيل الملفات المحمية: كنترولر بـ[Authorize] يفحص صلاحية الموظف
        // المستهدف بنفسه — لو حجبها كتالوج الأدوار لَمنع HR من فتح مستنداته.
        if (path.StartsWith("/files/"))
        {
            return PathAccessClass.Public;
        }

        if (path.StartsWith("/uploads/"))
        {
            // شعار الشركة يظهر بصفحة الدخول قبل المصادقة.
            if (path.StartsWith("/uploads/company-logos/"))
            {
                return PathAccessClass.Public;
            }

            if (path.StartsWith("/uploads/employee-photos/"))
            {
                return PathAccessClass.AnyAuthenticated;
            }

            return PathAccessClass.BackOfficeOnly;
        }

        if (path.StartsWith("/css/") ||
            path.StartsWith("/js/") ||
            path.StartsWith("/lib/") ||
            path.StartsWith("/images/") ||
            path.StartsWith("/brand/") ||
            path.StartsWith("/favicon"))
        {
            return PathAccessClass.Public;
        }

        return PathAccessClass.Guarded;
    }

    /// <summary>مسارات لا يُجرى عليها فحص حالة الحساب بقاعدة البيانات (أصول ثابتة).</summary>
    public static bool IsStaticAsset(string? rawPath)
    {
        var path = (rawPath ?? "/").ToLowerInvariant();

        return path.StartsWith("/css/") ||
               path.StartsWith("/js/") ||
               path.StartsWith("/lib/") ||
               path.StartsWith("/images/") ||
               path.StartsWith("/brand/") ||
               path.StartsWith("/favicon") ||
               path.EndsWith(".webmanifest") ||
               path.EndsWith(".css") ||
               path.EndsWith(".js") ||
               path.EndsWith(".map");
    }
}
