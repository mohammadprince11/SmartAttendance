using System;
using System.IO;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// كشف ملفات <c>/uploads</c> القديمة (People — Task 4): الجرد أثبت أنّ الخطر
/// **مُغلَقٌ أصلاً**، وهذه الاختبارات تقفل الضمانات كي لا يُعاد فتحه صامتاً.
///
/// <para><b>لماذا لا ترحيل؟</b> ثلاث طبقات تمنع خدمة ملفٍ حسّاسٍ بلا نطاق الموظف:
/// (1) التطبيق يخدم الأصول عبر <c>MapStaticAssets()</c> (net10) لا <c>UseStaticFiles</c>،
/// فمانيفست البناء وحده يُخدَم و**الملفات المرفوعة وقت التشغيل تحت <c>wwwroot/uploads</c>
/// لا تُخدَم إطلاقاً**؛ (2) الصفوف التاريخية بمسار <c>/uploads</c> تُقرأ حصراً عبر
/// <c>EmployeeFilesController.DownloadProfileFile</c> بعد <c>CanAccessEmployeeAsync</c>
/// (قواعد ∩ أدوار وصول)، ومحصورةً بجذر wwwroot عبر <c>TryResolvePhysicalPath</c>؛
/// (3) الملفات الجديدة الحسّاسة خارج wwwroot عبر <c>ProtectedFileService</c>. فالترحيل
/// الأعمى غير لازم — بل خطرٌ على قاعدة/نظام ملفات مشتركين مع الإنتاج.</para>
///
/// <para><b>الحلقة الأضعف</b>: إعادة إدخال <c>UseStaticFiles</c> يوماً ما تبدأ خدمة
/// <c>/uploads/employee-documents/*</c> لأي مستخدم باك-أوفيس (BackOfficeOnly، بلا نطاق
/// موظف) — أي إعادة فتح الثغرة بالضبط. هذا الحارس يمنعها.</para>
/// </summary>
public class LegacyUploadsStaticServingGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ═══ الحلقة الأضعف: لا خدمة أصولٍ ثابتةٍ لملفات التشغيل ═══

    [Fact]
    public void Pipeline_ServesAssetsViaMapStaticAssets_NotUseStaticFiles()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "SmartAttendance.Web", "Program.cs"));

        Assert.Contains("MapStaticAssets", program);

        // إعادة إدخالها تُعيد خدمة /uploads/employee-documents/* لأي باك-أوفيس بلا نطاق موظف.
        Assert.DoesNotContain("UseStaticFiles", program);
    }

    // ═══ الصفوف التاريخية تُخدَم بالنطاق فقط ═══

    [Fact]
    public void ProfileFileDownload_ChecksEmployeeScope_BeforeServingTheFile()
    {
        var controller = File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Controllers", "EmployeeFilesController.cs"));

        var handler = controller.IndexOf("DownloadProfileFile", StringComparison.Ordinal);
        Assert.True(handler > 0, "معالج DownloadProfileFile غير موجود.");

        var gate = controller.IndexOf("CanAccessEmployeeAsync", handler, StringComparison.Ordinal);
        var serve = controller.IndexOf("return File(", handler, StringComparison.Ordinal);

        Assert.True(gate > handler, "لا فحص نطاق بمعالج تنزيل الملف التاريخيّ.");
        Assert.True(serve > gate, "الملف يُخدَم قبل فحص النطاق — تسريب.");
    }

    [Fact]
    public void ProfileFileDownload_ResolvesLegacyPathThroughConfinedResolver()
    {
        var controller = File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Controllers", "EmployeeFilesController.cs"));

        // المسار التاريخيّ يُقرأ عبر ResolvePhysicalPath الذي يمرّ بـTryResolvePhysicalPath
        // المحصور بجذر wwwroot — لا من مسار StoredPath الخام مباشرةً.
        Assert.Contains("ResolvePhysicalPath", controller);
        Assert.Contains("TryResolvePhysicalPath", controller);
    }

    // ═══ حصر المسار التاريخيّ داخل الجذر (path traversal يفشل بأمان) ═══

    [Theory]
    [InlineData("employee-documents/id-scan.pdf", true)]        // مسارٌ تاريخيٌّ شرعيّ ⟹ داخل الجذر
    [InlineData("employee-profile-files/5/medical.pdf", true)]
    [InlineData("../appsettings.json", false)]                  // تسلّق ⟹ يفشل
    [InlineData("../../secret.txt", false)]
    [InlineData("/etc/passwd", false)]                          // مطلق ⟹ يفشل
    public void LegacyStoredPath_ResolvesOnlyInsideWebRoot(string legacyRelative, bool expectedInside)
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "zynora-webroot-guard");

        var inside = ProtectedFileStore.TryResolvePhysicalPath(webRoot, legacyRelative, out var resolved);

        Assert.Equal(expectedInside, inside);
        if (expectedInside)
        {
            Assert.StartsWith(Path.GetFullPath(webRoot), resolved, StringComparison.OrdinalIgnoreCase);
        }
    }
}
