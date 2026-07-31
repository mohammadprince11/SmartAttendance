using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// المرحلة 6 — إغلاق كشف <c>/uploads</c> بلا مصادقة: هذه الاختبارات تثبّت أن
/// الوثائق التأديبية ومستندات الموظفين ومرفقات الطلبات لم تعد عامة، وأن ما بقي
/// عاماً هو أصول العرض وشعار الشركة فقط.
/// </summary>
public class PublicPathPolicyTests
{
    // ===== الثغرة المُصلَحة =====

    [Theory]
    [InlineData("/uploads/disciplinary-forms/case-11.pdf")]
    [InlineData("/uploads/employee-documents/id-scan.pdf")]
    [InlineData("/uploads/employee-profile-files/5/medical.pdf")]
    [InlineData("/uploads/employee-financial/bank.pdf")]
    [InlineData("/uploads/employee-records/warning.docx")]
    [InlineData("/uploads/requests/attachment.jpg")]
    [InlineData("/uploads/loans/contract.pdf")]
    public void SensitiveUploads_AreNoLongerPublic(string path) =>
        Assert.Equal(PathAccessClass.BackOfficeOnly, PublicPathPolicy.Classify(path));

    [Fact]
    public void UploadsRoot_IsNotPublic() =>
        Assert.NotEqual(PathAccessClass.Public, PublicPathPolicy.Classify("/uploads/whatever.pdf"));

    [Fact]
    public void UppercasePath_IsNormalized() =>
        Assert.Equal(
            PathAccessClass.BackOfficeOnly,
            PublicPathPolicy.Classify("/Uploads/Disciplinary-Forms/Case.PDF"));

    // ===== ما يبقى عاماً =====

    [Theory]
    [InlineData("/uploads/company-logos/logo.png")]
    [InlineData("/css/site.css")]
    [InlineData("/js/app.js")]
    [InlineData("/lib/leaflet/leaflet.js")]
    [InlineData("/brand/theme.css")]
    [InlineData("/favicon.ico")]
    [InlineData("/manifest.abc123.webmanifest")]
    [InlineData("/sw.js")]
    [InlineData("/offline.html")]
    [InlineData("/account/login")]
    public void DisplayAssetsAndLogin_StayPublic(string path) =>
        Assert.Equal(PathAccessClass.Public, PublicPathPolicy.Classify(path));

    /// <summary>واجهة الموبايل ونقاط الدفع والملفات المحمية تُصادَق داخل الكنترولر.</summary>
    [Theory]
    [InlineData("/api/me")]
    [InlineData("/push/subscribe")]
    [InlineData("/files/employee-profile/12")]
    public void ControllerGuardedEndpoints_PassThroughMiddleware(string path) =>
        Assert.Equal(PathAccessClass.Public, PublicPathPolicy.Classify(path));

    // ===== صور الموظفين: مصادقة بلا كسر وسوم img =====

    [Fact]
    public void EmployeePhotos_RequireAuthentication() =>
        Assert.Equal(
            PathAccessClass.AnyAuthenticated,
            PublicPathPolicy.Classify("/uploads/employee-photos/5.jpg"));

    // ===== الصفحات العادية تبقى تحت الكتالوج =====

    [Theory]
    [InlineData("/employees/profile")]
    [InlineData("/payroll/loans")]
    [InlineData("/")]
    public void ApplicationPages_StayGuarded(string path) =>
        Assert.Equal(PathAccessClass.Guarded, PublicPathPolicy.Classify(path));

    // ===== استثناء فحص حالة الحساب =====

    [Theory]
    [InlineData("/css/site.css")]
    [InlineData("/js/app.js")]
    [InlineData("/lib/x.map")]
    public void StaticAssets_SkipAccountStateLookup(string path) =>
        Assert.True(PublicPathPolicy.IsStaticAsset(path));

    [Theory]
    [InlineData("/employees/profile")]
    [InlineData("/files/employee-profile/3")]
    [InlineData("/uploads/employee-documents/id.pdf")]
    public void ContentPaths_DoNotSkipAccountStateLookup(string path) =>
        Assert.False(PublicPathPolicy.IsStaticAsset(path));
}
