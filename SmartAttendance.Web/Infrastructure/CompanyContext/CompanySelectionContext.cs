using Microsoft.AspNetCore.Http;

namespace SmartAttendance.Web.Infrastructure.CompanyContext;

public static class CompanySelectionContext
{
    public const string CookieName = "NEXORA.CompanyId";

    public static int? Resolve(
        HttpContext httpContext,
        int? requestedCompanyId,
        IReadOnlyCollection<int> allowedCompanyIds)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(allowedCompanyIds);

        if (allowedCompanyIds.Count == 0)
        {
            return null;
        }

        int? selectedCompanyId = null;

        if (requestedCompanyId.HasValue &&
            allowedCompanyIds.Contains(requestedCompanyId.Value))
        {
            selectedCompanyId = requestedCompanyId.Value;
        }
        else
        {
            var cookieCompanyId = Read(httpContext.Request);

            if (cookieCompanyId.HasValue &&
                allowedCompanyIds.Contains(cookieCompanyId.Value))
            {
                selectedCompanyId = cookieCompanyId.Value;
            }
        }

        selectedCompanyId ??= allowedCompanyIds.First();
        Persist(httpContext, selectedCompanyId.Value);

        return selectedCompanyId;
    }

    public static void Persist(
        HttpContext httpContext,
        int companyId)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (companyId <= 0)
        {
            return;
        }

        var value = companyId.ToString(
            System.Globalization.CultureInfo.InvariantCulture);

        if (httpContext.Request.Cookies.TryGetValue(
                CookieName,
                out var currentValue) &&
            string.Equals(
                currentValue,
                value,
                StringComparison.Ordinal))
        {
            return;
        }

        httpContext.Response.Cookies.Append(
            CookieName,
            value,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = httpContext.Request.IsHttps,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddYears(1)
            });
    }

    private static int? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(
                CookieName,
                out var rawValue))
        {
            return null;
        }

        return int.TryParse(
            rawValue,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var companyId) &&
            companyId > 0
                ? companyId
                : null;
    }
}
