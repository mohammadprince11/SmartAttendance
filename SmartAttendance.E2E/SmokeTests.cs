using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SmartAttendance.E2E;

/// <summary>
/// اختبارات E2E دخانية للبوابة عبر Playwright (نسخة .NET) — قراءة/دخول فقط، بلا
/// تعديل بيانات. **لا أوراق اعتماد مثبّتة بالكود إطلاقاً**: العنوان والحساب يُقرآن
/// من متغيرات البيئة، وغيابها يتخطّى الاختبار برسالة واضحة بدل استعمال حساب
/// افتراضي أو الضرب على الإنتاج بالخطأ.
///
/// المتغيرات (راجع docs/LOCAL-TEST-ENVIRONMENT.md):
///   ZYNORA_E2E_BASE_URL   عنوان بيئة اختبار مخصصة (ليس الإنتاج)
///   ZYNORA_E2E_USERNAME   حساب اختبار
///   ZYNORA_E2E_PASSWORD   كلمة مروره
///
/// شغّلها: dotnet test SmartAttendance.E2E
/// </summary>
[TestFixture]
public class SmokeTests : PageTest
{
    private static string? BaseUrl =>
        Environment.GetEnvironmentVariable("ZYNORA_E2E_BASE_URL")?.TrimEnd('/');

    private static string? TestUsername =>
        Environment.GetEnvironmentVariable("ZYNORA_E2E_USERNAME");

    private static string? TestPassword =>
        Environment.GetEnvironmentVariable("ZYNORA_E2E_PASSWORD");

    /// <summary>عنوان البيئة أو تخطٍّ معلن — لا سقوط على قيمة افتراضية.</summary>
    private static string RequireBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            Assert.Ignore(
                "تُخطّي: عرّف ZYNORA_E2E_BASE_URL لبيئة اختبار مخصصة (لا تستخدم الإنتاج). " +
                "راجع docs/LOCAL-TEST-ENVIRONMENT.md");
        }

        return BaseUrl!;
    }

    /// <summary>حساب الاختبار أو تخطٍّ معلن — ممنوع أي حساب افتراضي.</summary>
    private static (string User, string Pass) RequireCredentials()
    {
        if (string.IsNullOrWhiteSpace(TestUsername) || string.IsNullOrWhiteSpace(TestPassword))
        {
            Assert.Ignore(
                "تُخطّي الاختبارات المصادَقة: عرّف ZYNORA_E2E_USERNAME وZYNORA_E2E_PASSWORD. " +
                "راجع docs/LOCAL-TEST-ENVIRONMENT.md");
        }

        return (TestUsername!, TestPassword!);
    }

    // بيئات الاختبار المحلية قد تحمل شهادة موقّعة ذاتياً.
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task LoginPage_Loads_WithForm()
    {
        var baseUrl = RequireBaseUrl();
        await Page.GotoAsync($"{baseUrl}/Account/Login");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/Account/Login.*"));
        await Expect(Page.Locator("input[name='Username']")).ToBeVisibleAsync();
        await Expect(Page.Locator("input[name='Password']")).ToBeVisibleAsync();
        await Expect(Page.Locator("button[type='submit']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task EmployeePortal_RedirectsToLogin_WhenAnonymous()
    {
        var baseUrl = RequireBaseUrl();
        await Page.GotoAsync($"{baseUrl}/EmployeePortal");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/Account/Login.*"));
    }

    [Test]
    public async Task Employee_CanLogin_AndReachPortal()
    {
        var baseUrl = RequireBaseUrl();
        var (user, pass) = RequireCredentials();

        await Page.GotoAsync($"{baseUrl}/Account/Login");
        await Page.FillAsync("input[name='Username']", user);
        await Page.FillAsync("input[name='Password']", pass);
        await Page.ClickAsync("button[type='submit']");

        // بعد الدخول لا نبقى على صفحة تسجيل الدخول (نجح الدخول).
        await Page.WaitForURLAsync(new Regex("^(?!.*/Account/Login).*$"), new() { Timeout = 15000 });
        Assert.That(Page.Url, Does.Not.Contain("/Account/Login"));
    }
}
