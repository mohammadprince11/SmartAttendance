using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SmartAttendance.E2E;

[TestFixture]
[NonParallelizable]
public class EmployeeEditGeometryTests : PageTest
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("ZYNORA_E2E_BASE_URL")?.TrimEnd('/')
        ?? throw new InvalidOperationException("ZYNORA_E2E_BASE_URL is required.");

    private static string Username =>
        Environment.GetEnvironmentVariable("ZYNORA_E2E_USERNAME")
        ?? throw new InvalidOperationException("ZYNORA_E2E_USERNAME is required.");

    private static string Password =>
        Environment.GetEnvironmentVariable("ZYNORA_E2E_PASSWORD")
        ?? throw new InvalidOperationException("ZYNORA_E2E_PASSWORD is required.");

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        ViewportSize = new() { Width = 1534, Height = 900 }
    };

    [Test]
    public async Task MainGridControls_HaveIdenticalComputedGeometry()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.FillAsync("input[name='Username']", Username);
        await Page.FillAsync("input[name='Password']", Password);
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync(
            new System.Text.RegularExpressions.Regex("^(?!.*/Account/Login).*$"),
            new() { Timeout = 15000, WaitUntil = WaitUntilState.DOMContentLoaded });

        var response = await Page.GotoAsync(
            $"{BaseUrl}/Employees/Edit?id=4",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.LessThan(400));
        Assert.That(Page.Url, Does.Not.Contain("/Account/Login"));

        var geometry = await Page.EvaluateAsync<ControlGeometry[]>("""
            () => {
                const visible = el => {
                    const r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0 &&
                           getComputedStyle(el).visibility !== 'hidden';
                };

                return [...document.querySelectorAll(
                    '#employee-edit-form .nxr-edit-final-grid > .nxr-edit-field')]
                    .map(field => {
                        const candidates = [
                            ...field.querySelectorAll(
                              ':scope > input:not(.nxcal-native):not([type="hidden"]):not([type="file"]):not([type="checkbox"]):not([type="radio"]), ' +
                              ':scope > .nxcs-select .nxcs-trigger, ' +
                              ':scope > .nxcal .nxcal__button, ' +
                              ':scope > .nxr-searchable-wrapper .nxr-search-input, ' +
                              ':scope > select:not(.nxcs-native)')
                        ];
                        const control = candidates.find(visible);
                        if (!control) return null;

                        const rect = control.getBoundingClientRect();
                        const style = getComputedStyle(control);
                        const parent = control.parentElement;
                        const parentRect = parent?.getBoundingClientRect();
                        const parentStyle = parent ? getComputedStyle(parent) : null;
                        const fieldRect = field.getBoundingClientRect();
                        return {
                            name: (field.querySelector('label')?.textContent || control.getAttribute('name') || '').trim(),
                            width: rect.width,
                            height: rect.height,
                            borderWidth: style.borderTopWidth,
                            borderRadius: style.borderTopLeftRadius,
                            fieldWidth: fieldRect.width,
                            parentWidth: parentRect?.width ?? 0,
                            parentClass: parent?.className ?? '',
                            parentPosition: parentStyle?.position ?? '',
                            controlPosition: style.position
                        };
                    })
                    .filter(Boolean);
            }
            """);

        Assert.That(geometry.Length, Is.GreaterThan(10));

        var minWidth = geometry.Min(x => x.Width);
        var maxWidth = geometry.Max(x => x.Width);
        var minHeight = geometry.Min(x => x.Height);
        var maxHeight = geometry.Max(x => x.Height);

        Assert.That(
            maxWidth - minWidth,
            Is.LessThanOrEqualTo(1.0),
            "Width mismatch: " + string.Join("; ", geometry.Select(x =>
                $"{x.Name}=control:{x.Width:0.##}/parent:{x.ParentWidth:0.##}/field:{x.FieldWidth:0.##} px parent={x.ParentClass} ppos={x.ParentPosition} cpos={x.ControlPosition}")));

        Assert.That(
            maxHeight - minHeight,
            Is.LessThanOrEqualTo(0.5),
            "Height mismatch: " + string.Join("; ", geometry.Select(x => $"{x.Name}={x.Height:0.##}px")));

        Assert.That(minHeight, Is.EqualTo(42).Within(0.5));
        Assert.That(geometry.Select(x => x.BorderWidth).Distinct(), Is.EquivalentTo(new[] { "1px" }));
        Assert.That(geometry.Select(x => x.BorderRadius).Distinct(), Is.EquivalentTo(new[] { "10px" }));
    }

    private sealed class ControlGeometry
    {
        public string Name { get; set; } = string.Empty;
        public double Width { get; set; }
        public double Height { get; set; }
        public string BorderWidth { get; set; } = string.Empty;
        public string BorderRadius { get; set; } = string.Empty;
        public double FieldWidth { get; set; }
        public double ParentWidth { get; set; }
        public string ParentClass { get; set; } = string.Empty;
        public string ParentPosition { get; set; } = string.Empty;
        public string ControlPosition { get; set; } = string.Empty;
    }
}
