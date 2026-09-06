using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SmartAttendance.Web.Infrastructure.Localization;

/// <summary>
/// Keeps standalone Razor shells aligned with the active UI culture even when
/// a legacy layout still carries static lang/dir attributes.
/// </summary>
[HtmlTargetElement("html")]
public sealed class LocalizationHtmlTagHelper : TagHelper
{
    private readonly ILocalizationDictionaryService _dictionary;

    public LocalizationHtmlTagHelper(
        ILocalizationDictionaryService dictionary)
    {
        _dictionary = dictionary;
    }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override async Task ProcessAsync(
        TagHelperContext context,
        TagHelperOutput output)
    {
        var culture = CultureInfo.CurrentUICulture;

        var language = await _dictionary.FindLanguageAsync(
            culture.Name,
            ViewContext.HttpContext.RequestAborted);

        var direction = language?.Direction;

        if (!string.Equals(
                direction,
                "rtl",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                direction,
                "ltr",
                StringComparison.OrdinalIgnoreCase))
        {
            direction = culture.TextInfo.IsRightToLeft
                ? "rtl"
                : "ltr";
        }

        output.Attributes.SetAttribute(
            "lang",
            culture.Name);

        output.Attributes.SetAttribute(
            "dir",
            direction!.ToLowerInvariant());
    }
}

/// <summary>
/// The primary application layout already loads the runtime localization
/// bridge. Legacy standalone shells do not, so append it only for those
/// surfaces.
/// </summary>
[HtmlTargetElement("body")]
public sealed class LocalizationRuntimeScriptTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(
        TagHelperContext context,
        TagHelperOutput output)
    {
        var path =
            ViewContext.HttpContext.Request.Path.Value
            ?? string.Empty;

        var needsBridge =
            path.StartsWith(
                "/EmployeePortal",
                StringComparison.OrdinalIgnoreCase)
            ||
            string.Equals(
                path,
                "/Verify",
                StringComparison.OrdinalIgnoreCase);

        if (!needsBridge)
        {
            return;
        }

        output.PostContent.AppendHtml(
            "<script src=\"/js/zynora-runtime-localization.js?v=20260906-p2\" defer></script>");
    }
}