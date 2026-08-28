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
[NonParallelizable]
public class SmokeTests : PageTest
{
    private static readonly (string Path, string Evidence)[] ReleaseSurfaces =
    {
        ("/Employees", "main, table, [role='main']"),
        ("/AttendanceViewer", "main, table, [role='main']"),
        ("/LeaveRequests", "main, table, form, [role='main']"),
        ("/Approvals", "main, table, [role='main']"),
        ("/Payroll/Runs", "main, table, [role='main']"),
        ("/Payroll/BankTemplates", "main, table, form, [role='main']"),
        ("/Payroll/PayslipInquiry", "main, table, form, [role='main']")
    };
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
        await Page.WaitForURLAsync(
            new Regex("^(?!.*/Account/Login).*$"),
            new() { Timeout = 15000, WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.That(Page.Url, Does.Not.Contain("/Account/Login"));
    }

    [Test]
    public async Task ReleaseJourney_CriticalHrSurfaces_AreReachableAndRtl()
    {
        var baseUrl = RequireBaseUrl();
        await LoginAsync(baseUrl);

        foreach (var surface in ReleaseSurfaces)
        {
            var response = await Page.GotoAsync(baseUrl + surface.Path,
                new() { WaitUntil = WaitUntilState.DOMContentLoaded });

            Assert.That(response, Is.Not.Null, $"No response for {surface.Path}");
            Assert.That(response!.Status, Is.LessThan(400), $"HTTP {response.Status} for {surface.Path}");
            Assert.That(Page.Url, Does.Not.Contain("/Account/Login"), $"Unauthorized redirect for {surface.Path}");
            await Expect(Page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
            await Expect(Page.Locator(surface.Evidence).First).ToBeVisibleAsync();

            var overflow = await Page.EvaluateAsync<bool>(
                "document.documentElement.scrollWidth > document.documentElement.clientWidth + 2");
            Assert.That(overflow, Is.False, $"Horizontal overflow on {surface.Path}");

            var accessibilityIssues = await Page.EvaluateAsync<string[]>("""
                () => {
                  const issues = [];
                  const visible = element => !!(element.offsetWidth || element.offsetHeight || element.getClientRects().length);
                  const describe = element => {
                    const id = element.id ? '#' + element.id : '';
                    const classes = typeof element.className === 'string' && element.className.trim()
                      ? '.' + element.className.trim().split(/\s+/).join('.') : '';
                    const name = element.getAttribute('name') ? '[name=' + element.getAttribute('name') + ']' : '';
                    return element.tagName.toLowerCase() + id + classes + name;
                  };
                  const ids = [...document.querySelectorAll('[id]')]
                    .map(x => x.id).filter(Boolean);
                  const duplicates = [...new Set(ids.filter((id, i) => ids.indexOf(id) !== i))];
                  if (duplicates.length) issues.push('duplicate ids: ' + duplicates.join(','));
                  if ([...document.images].some(img => !img.hasAttribute('alt')))
                    issues.push('image without alt');
                  [...document.querySelectorAll('button')].filter(button => visible(button) &&
                        !((button.innerText || button.getAttribute('aria-label') || button.title || '').trim()))
                    .forEach(button => issues.push('unnamed button: ' + describe(button)));
                  [...document.querySelectorAll('input:not([type=hidden]),select,textarea')].filter(control => visible(control) && (() => {
                        const labelled = control.labels && control.labels.length > 0;
                        return !(labelled || control.getAttribute('aria-label') ||
                          control.getAttribute('aria-labelledby') || control.getAttribute('placeholder') || control.title);
                      })()).forEach(control => issues.push('unnamed form control: ' + describe(control)));
                  return issues;
                }
                """);
            Assert.That(accessibilityIssues, Is.Empty,
                $"Accessibility smoke failed on {surface.Path}: {string.Join("; ", accessibilityIssues)}");
        }
    }

    [Test]
    public async Task ThemeCss_IsExternalPrivateAndValidCss()
    {
        var baseUrl = RequireBaseUrl();
        await LoginAsync(baseUrl);

        var response = await Page.Context.APIRequest.GetAsync(baseUrl + "/theme/current.css");
        Assert.That(response.Status, Is.EqualTo(200));
        Assert.That(response.Headers["content-type"], Does.StartWith("text/css"));
        Assert.That(response.Headers["cache-control"], Does.Contain("no-store"));
        Assert.That(await response.TextAsync(), Does.Contain(":root"));
    }

    private async Task LoginAsync(string baseUrl)
    {
        var (user, pass) = RequireCredentials();
        await Page.GotoAsync($"{baseUrl}/Account/Login");
        if (!Page.Url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase)) return;
        await Page.FillAsync("input[name='Username']", user);
        await Page.FillAsync("input[name='Password']", pass);
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync(
            new Regex("^(?!.*/Account/Login).*$"),
            new() { Timeout = 15000, WaitUntil = WaitUntilState.DOMContentLoaded });
    }
}
