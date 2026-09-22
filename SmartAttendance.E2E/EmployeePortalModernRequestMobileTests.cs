using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SmartAttendance.E2E;

[TestFixture]
[NonParallelizable]
public sealed class EmployeePortalModernRequestMobileTests : PageTest
{
    private string BaseUrl => Environment.GetEnvironmentVariable("ZYNORA_E2E_BASE_URL")!.TrimEnd('/');
    private string Username => Environment.GetEnvironmentVariable("ZYNORA_E2E_USERNAME")!;
    private string Password => Environment.GetEnvironmentVariable("ZYNORA_E2E_PASSWORD")!;

    [Test]
    public async Task ModernRequestSheet_Fits_360_390_430()
    {
        await LoginAsync();

        foreach (var width in new[] { 360, 390, 430 })
        {
            await Page.SetViewportSizeAsync(width, 900);
            await Page.GotoAsync($"{BaseUrl}/EmployeePortal?tab=requests&open=leave",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Page.Locator("#nxex-leave-modal.open").WaitForAsync(new() { Timeout = 10000 });
            var departures = Page.Locator("[data-leave-tab]").Filter(new() { HasText = "المغادرات" });
            await departures.ClickAsync();
            var form = Page.Locator("[data-leave-panel]:not([hidden])");
            var typeTrigger = form.Locator("[data-csel-trigger]");
            await typeTrigger.WaitForAsync();
            await typeTrigger.ClickAsync();
            await form.Locator("[data-csel-list]:not([hidden])").WaitForAsync();
            await AssertFitsAsync("[data-leave-panel]:not([hidden]) [data-csel-list]:not([hidden])", width);
            await typeTrigger.ClickAsync();

            var today = DateTime.Today.ToString("yyyy-MM-dd");
            await form.Locator("[data-dp-from]").EvaluateAsync("(el,v)=>{el.value=v;el.dispatchEvent(new Event('change',{bubbles:true}));}", today);
            await form.Locator("[data-dp-to]").EvaluateAsync("(el,v)=>{el.value=v;el.dispatchEvent(new Event('change',{bubbles:true}));}", today);
            await form.Locator("[data-punch-block]:not([hidden])").WaitForAsync(new() { Timeout = 10000 });

            await AssertFitsAsync("#nxex-leave-modal", width);
            await AssertFitsAsync("[data-leave-panel]:not([hidden]) [data-csel-trigger]", width);
            await AssertFitsAsync("[data-leave-panel]:not([hidden]) [data-punch-block]", width);

            await form.Locator("[data-open-datepicker]").ClickAsync();
            await Page.Locator("#nxex-dp-sheet.open").WaitForAsync();
            await AssertFitsAsync("#nxex-dp-sheet", width);
            await Page.Locator("#nxex-dp-sheet [data-dp-cancel]").ClickAsync();

            await form.Locator("[data-open-timepicker]").First.ClickAsync();
            await Page.Locator("#nxex-tp-sheet.open").WaitForAsync();
            await AssertFitsAsync("#nxex-tp-sheet", width);
            await Page.Locator("#nxex-tp-sheet [data-tp-cancel]").ClickAsync();

            var submit = form.Locator(".nxex-leave-submit");
            await submit.ScrollIntoViewIfNeededAsync();
            await AssertFitsAsync("[data-leave-panel]:not([hidden]) .nxex-leave-submit", width);

            var overflow = await Page.Locator("#nxex-leave-modal .nxex-modal-body")
                .EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
            Assert.That(overflow, Is.False, $"Horizontal overflow at {width}px");
        }
    }

    [Test]
    public async Task FreshLogin_IgnoresStaleHiddenTimestamp_FromPreviousSession()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.EvaluateAsync("localStorage.setItem('zyHiddenAt', String(Date.now() - 3600000));");
        await Page.FillAsync("input[name='Username']", Username);
        await Page.FillAsync("input[name='Password']", Password);
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(".*/EmployeePortal.*"),
            new() { Timeout = 15000, WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.WaitForTimeoutAsync(1000);

        Assert.That(Page.Url, Does.Contain("/EmployeePortal"));
        Assert.That(await Page.EvaluateAsync<string?>("localStorage.getItem('zyHiddenAt')"), Is.Null);
    }

    private async Task LoginAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.FillAsync("input[name='Username']", Username);
        await Page.FillAsync("input[name='Password']", Password);
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync(
            new System.Text.RegularExpressions.Regex("^(?!.*/Account/Login).*$"),
            new() { Timeout = 15000, WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    private async Task AssertFitsAsync(string selector, int viewportWidth)
    {
        var box = await Page.Locator(selector).BoundingBoxAsync();
        Assert.That(box, Is.Not.Null, selector);
        Assert.That(box!.X, Is.GreaterThanOrEqualTo(-1), selector);
        Assert.That(box.X + box.Width, Is.LessThanOrEqualTo(viewportWidth + 1), selector);
    }
}
