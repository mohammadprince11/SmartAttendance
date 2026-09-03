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
        var cookieName =
            CookieRequestCultureProvider
                .DefaultCookieName;

        string? requestedCode = null;

        if (context.Request.Cookies.TryGetValue(
                cookieName,
                out var cookieValue))
        {
            requestedCode =
                CookieRequestCultureProvider
                    .ParseCookieValue(cookieValue)?
                    .Cultures
                    .FirstOrDefault()
                    .Value;
        }

        /*
         * المصدر الوحيد للغات واجهة النظام:
         * GetLanguagesAsync = اللغات الظاهرة فقط.
         *
         * اللغة المخفية تبقى في القاموس الإداري
         * لكنها لا يمكن أن تعمل كلغة واجهة.
         */
        var visibleLanguages =
            await dictionary.GetLanguagesAsync(
                context.RequestAborted);

        var language =
            visibleLanguages.FirstOrDefault(item =>
                string.Equals(
                    item.Code,
                    requestedCode,
                    StringComparison.OrdinalIgnoreCase))
            ??
            visibleLanguages.FirstOrDefault(item =>
                item.IsDefault)
            ??
            visibleLanguages.FirstOrDefault();

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

            /*
             * إذا Cookie يشير إلى لغة أصبحت مخفية،
             * نبدله فوراً باللغة البديلة الظاهرة.
             */
            if (!string.Equals(
                    requestedCode,
                    language.Code,
                    StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Cookies.Append(
                    cookieName,
                    CookieRequestCultureProvider
                        .MakeCookieValue(
                            new RequestCulture(
                                language.Code)),
                    BuildCultureCookieOptions(
                        context.Request.IsHttps));
            }
        }

        await _next(context);
    }

    private static CookieOptions
        BuildCultureCookieOptions(bool secure)
    {
        return new CookieOptions
        {
            Expires =
                DateTimeOffset.UtcNow
                    .AddYears(1),

            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = secure,
            Path = "/"
        };
    }
}