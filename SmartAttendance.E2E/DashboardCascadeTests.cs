using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SmartAttendance.E2E;

[TestFixture]
[NonParallelizable]
public class DashboardCascadeTests : PageTest
{
    [Test]
    public async Task InspectCascade()
    {
        var baseUrl = Environment.GetEnvironmentVariable("ZYNORA_E2E_BASE_URL")!.TrimEnd('/');
        var user = Environment.GetEnvironmentVariable("ZYNORA_E2E_USERNAME")!;
        var pass = Environment.GetEnvironmentVariable("ZYNORA_E2E_PASSWORD")!;
        await Page.GotoAsync($"{baseUrl}/Account/Login");
        await Page.FillAsync("input[name='Username']", user);
        await Page.FillAsync("input[name='Password']", pass);
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("^(?!.*/Account/Login).*$"));
        await Page.GotoAsync(baseUrl + "/");
        await Page.WaitForTimeoutAsync(400);
        var details = await Page.EvaluateAsync<string>("""
          () => JSON.stringify({
            styles:[...document.styleSheets].map(s => s.href).filter(Boolean),
            donut:document.querySelector('.zxd-donut')?.outerHTML,
            body:document.querySelector('.zxd-card-body')?.outerHTML
          })
        """);
        TestContext.WriteLine(details);
    }
}
