using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SmartAttendance.E2E;

/// <summary>
/// اختبارات E2E دخانية للبوابة عبر Playwright (نسخة .NET). تعمل ضد الخادم الدائم
/// المحلي (HTTPS بشهادة داخلية — لذا نتجاهل خطأ الشهادة). قراءة/دخول فقط — بلا تعديل بيانات.
/// شغّلها: dotnet test SmartAttendance.E2E
/// </summary>
[TestFixture]
public class SmokeTests : PageTest
{
    private const string BaseUrl = "https://localhost:5443";

    // الشهادة موقّعة ذاتياً على الشبكة المحلية — نسمح بها للاختبار.
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task LoginPage_Loads_WithForm()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/Account/Login.*"));
        await Expect(Page.Locator("input[name='Username']")).ToBeVisibleAsync();
        await Expect(Page.Locator("input[name='Password']")).ToBeVisibleAsync();
        await Expect(Page.Locator("button[type='submit']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task EmployeePortal_RedirectsToLogin_WhenAnonymous()
    {
        await Page.GotoAsync($"{BaseUrl}/EmployeePortal");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/Account/Login.*"));
    }

    [Test]
    public async Task Employee_CanLogin_AndReachPortal()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.FillAsync("input[name='Username']", "employee");
        await Page.FillAsync("input[name='Password']", "Emp@12345");
        await Page.ClickAsync("button[type='submit']");

        // بعد الدخول لا نبقى على صفحة تسجيل الدخول (نجح الدخول).
        await Page.WaitForURLAsync(new Regex("^(?!.*/Account/Login).*$"), new() { Timeout = 15000 });
        Assert.That(Page.Url, Does.Not.Contain("/Account/Login"));
    }
}
