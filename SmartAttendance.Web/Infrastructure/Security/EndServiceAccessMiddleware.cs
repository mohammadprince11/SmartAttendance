using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Security;

public sealed class EndServiceAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        // Platform owner authorization uses a separate account/scheme. A customer
        // cookie on the same local host must not interfere with that independent policy.
        if (context.Request.Path.StartsWithSegments("/Platform", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }
        // Platform identities use a separate scheme; never infer a customer identity from them.
        var tenantId = TenantContext.GetTenantId(context.User) ?? 0;
        if (tenantId <= 0)
        {
            await next(context);
            return;
        }
        EndServiceAccessStore.Schedule? schedule;
        try { schedule = await EndServiceAccessStore.GetForRequestAsync(db, context, tenantId, context.User.Identity.Name); }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return; // No silent fallback to full access on an unavailable security store.
        }
        if (schedule is null)
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        if (!EndServiceAccessPolicy.AllowsAccess(true, schedule.AccessEndsAtUtc, DateTimeOffset.UtcNow))
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        var path = context.Request.Path.Value ?? "";
        if (EndServiceAccessPolicy.IsFarewellOnlyRoute(path, context.Request.Method))
        {
            await next(context);
            return;
        }
        if (HttpMethods.IsGet(context.Request.Method) && !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            context.Response.Redirect(EndServiceAccessStore.FarewellPath);
        else context.Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}
