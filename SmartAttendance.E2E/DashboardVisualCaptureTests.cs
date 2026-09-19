using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SmartAttendance.E2E;

[TestFixture]
[NonParallelizable]
public class DashboardVisualCaptureTests : PageTest
{
    [Test]
    public async Task CaptureMainDashboard()
    {
        var baseUrl = Environment.GetEnvironmentVariable("ZYNORA_E2E_BASE_URL")!.TrimEnd('/');
        var user = Environment.GetEnvironmentVariable("ZYNORA_E2E_USERNAME")!;
        var pass = Environment.GetEnvironmentVariable("ZYNORA_E2E_PASSWORD")!;

        await Page.SetViewportSizeAsync(1440, 1200);
        await Page.GotoAsync($"{baseUrl}/Account/Login");
        await Page.FillAsync("input[name='Username']", user);
        await Page.FillAsync("input[name='Password']", pass);
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("^(?!.*/Account/Login).*$"),
            new() { Timeout = 15000, WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.GotoAsync(baseUrl + "/", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(800);
        var output = @"C:\Users\anas\SmartAttendance-PayrollFix\.design-backups\dashboard-current-20260916.png";
        await Page.ScreenshotAsync(new()
        {
            Path = output,
            FullPage = true
        });
        TestContext.WriteLine(output);
    }
}
