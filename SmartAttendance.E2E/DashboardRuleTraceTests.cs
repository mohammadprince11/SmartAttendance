using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SmartAttendance.E2E;

[TestFixture]
[NonParallelizable]
public class DashboardRuleTraceTests : PageTest
{
    [Test]
    public async Task TraceRulesAndRawDonut()
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
        await Page.WaitForTimeoutAsync(300);
        var rules = await Page.EvaluateAsync<string>("""
          () => {
            const el = document.querySelector('.zxd-card-body');
            const hits = [];
            if (!el) return JSON.stringify(hits);
            for (const ss of [...document.styleSheets]) {
              let rs; try { rs = ss.cssRules; } catch { continue; }
              for (const r of [...rs]) {
                if (!r.selectorText || !el.matches(r.selectorText)) continue;
                const s = r.style;
                if (s.border || s.background || s.backgroundColor || s.padding || s.overflow)
                  hits.push({href:ss.href, selector:r.selectorText, css:s.cssText});
              }
            }
            return JSON.stringify(hits);
          }
        """);
        var resp = await Page.Context.APIRequest.GetAsync(baseUrl + "/");
        var html = await resp.TextAsync();
        var i = html.IndexOf("class=\"zxd-donut\"");
        var raw = i >= 0 ? html.Substring(Math.Max(0, i - 180), Math.Min(500, html.Length - Math.Max(0, i - 180))) : "NO_DONUT";
        TestContext.WriteLine(rules);
        TestContext.WriteLine(raw);
    }
}
