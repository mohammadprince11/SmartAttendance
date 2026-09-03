using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace SmartAttendance.Web.Infrastructure.Localization;

public sealed class DynamicDictionaryCultureMiddleware
{
    private readonly RequestDelegate _next;

    public DynamicDictionaryCultureMiddleware(
        RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ILocalizationDictionaryService dictionary)
    {
        var requestedCode =
            context.Request.Query["culture"]
                .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(requestedCode) &&
            context.Request.Cookies.TryGetValue(
                CookieRequestCultureProvider.DefaultCookieName,
                out var cookieValue))
        {
            requestedCode =
                CookieRequestCultureProvider
                    .ParseCookieValue(cookieValue)?
                    .Cultures
                    .FirstOrDefault()
                    .Value;
        }

        var visibleLanguages =
            await dictionary.GetLanguagesAsync(
                context.RequestAborted);

        var language =
            visibleLanguages.FirstOrDefault(item =>
                string.Equals(
                    item.Code,
                    requestedCode,
                    StringComparison.OrdinalIgnoreCase))
            ?? visibleLanguages.FirstOrDefault();

        if (language is not null)
        {
            var culture =
                CultureInfo.GetCultureInfo(
                    language.Code);

            CultureInfo.CurrentCulture =
                culture;

            CultureInfo.CurrentUICulture =
                culture;

            context.Response.Headers.ContentLanguage =
                language.Code;
        }

        await _next(context);
    }
}