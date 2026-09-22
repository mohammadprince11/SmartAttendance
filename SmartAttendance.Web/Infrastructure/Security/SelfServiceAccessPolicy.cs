using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// الحارس الخادمي الموحّد لمنح الخدمة الذاتية. عدم وجود دور SelfService للمستخدم
/// يحافظ على السلوك السابق؛ إسناد أي دور يحوّل منح ذلك النوع إلى قائمة بيضاء.
/// </summary>
public static class SelfServiceAccessPolicy
{
    public static Task<bool> IsAllowedAsync(
        ApplicationDbContext dbContext, HttpContext context, string actionCode)
    {
        if (!SelfServiceCatalog.IsValid(actionCode)) return Task.FromResult(false);
        if (RoleRouteCatalog.IsAdmin(PeopleAccessContext.GetRole(context))) return Task.FromResult(true);

        var systemUserId = PeopleAccessContext.GetSystemUserId(context) ?? 0;
        return AccessRoleStore.IsGrantedOrUnrestrictedAsync(
            dbContext, systemUserId, AccessRoleStore.TypeSelfService, actionCode);
    }

    public static string? ActionForEffectCode(string? effectCode)
    {
        var value = effectCode?.Trim() ?? string.Empty;
        if (value.Length == 0) return null;

        return value switch
        {
            "LeaveAnnual" or "LeaveSick" or "LeaveUnpaid" or "LeaveOther" or
            "BusinessTrip" or "WorkFromHome" => "LeaveRequest",
            "ExitPermission" => "ExitPermission",
            "Overtime" => "OvertimeRequest",
            "ShiftChange" => "ShiftRequest",
            _ => null
        };
    }

    public static string? ActionForRequestType(string? requestType)
    {
        var value = requestType?.Trim() ?? string.Empty;
        if (value.Length == 0) return null;
        if (value.Contains("نسيان بصمة", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("punch", StringComparison.OrdinalIgnoreCase)) return "PunchCorrection";
        if (value.Contains("مغادرة", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("خروج", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("exit", StringComparison.OrdinalIgnoreCase)) return "ExitPermission";
        if (value.Contains("أوفر", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("إضافي", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("overtime", StringComparison.OrdinalIgnoreCase)) return "OvertimeRequest";
        if (value.Contains("إجاز", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("leave", StringComparison.OrdinalIgnoreCase)) return "LeaveRequest";
        return null;
    }
}
