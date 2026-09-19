using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SmartAttendance.E2E;

[TestFixture]
[NonParallelizable]
public class DashboardDiagnosticsTests : PageTest
{
    [Test]
    public async Task InspectDashboardStyles()
    {
        var baseUrl = Environment.GetEnvironmentVariable("ZYNORA_E2E_BASE_URL")!.TrimEnd('/');
        var user = Environment.GetEnvironmentVariable("ZYNORA_E2E_USERNAME")!;
        var pass = Environment.GetEnvironmentVariable("ZYNORA_E2E_PASSWORD")!;
        await Page.SetViewportSizeAsync(1440, 1200);
        await Page.GotoAsync($"{baseUrl}/Account/Login");
        await Page.FillAsync("input[name='Username']", user);
        await Page.FillAsync("input[name='Password']", pass);
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("^(?!.*/Account/Login).*$"));
        await Page.GotoAsync(baseUrl + "/");
        await Page.WaitForTimeoutAsync(500);
        var details = await Page.EvaluateAsync<string>("""
          () => {
            const d = document.querySelector('.zxd-donut');
            const b = document.querySelector('.zxd-card-body');
            const c = document.querySelector('.zxd-grid .zxd-card');
            const out = {};
            for (const [k,e] of Object.entries({donut:d, body:b, card:c})) {
              if (!e) { out[k] = null; continue; }
              const s = getComputedStyle(e), r = e.getBoundingClientRect();
              out[k] = {display:s.display,width:r.width,height:r.height,border:s.border,
                overflow:s.overflow,background:s.background,backgroundImage:s.backgroundImage};
            }
            return JSON.stringify(out);
          }
        """);
        TestContext.WriteLine(details);
    }
}
