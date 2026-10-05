using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Platform;

/// <summary>
/// Resolves licensed product modules from server-owned routes. The tenant id is
/// always taken from the authenticated principal by the caller, never from input.
/// </summary>
public static class TenantModuleAccess
{
    private static readonly (string Prefix, string Module)[] RouteModules =
    [
        ("/payroll", "Payroll"),
        ("/payrollprovisions", "Payroll"),

        ("/attendancedashboard", "Attendance"),
        ("/attendanceoperations", "Attendance"),
        ("/attendancerecommendations", "Attendance"),
        ("/attendancerecords", "Attendance"),
        ("/attendancesettings", "Attendance"),
        ("/attendanceviewer", "Attendance"),
        ("/dayattendance", "Attendance"),
        ("/monthattendance", "Attendance"),
        ("/weekattendance", "Attendance"),
        ("/devices", "Attendance"),
        ("/holidays", "Attendance"),
        ("/missingpunchrequests", "Attendance"),
        ("/roster", "Attendance"),
        ("/shiftassignments", "Attendance"),
        ("/shiftoverrides", "Attendance"),
        ("/shiftrules", "Attendance"),
        ("/shifttypes", "Attendance"),

        ("/employeeportal", "SelfService"),
        ("/selfservices", "SelfService"),
        ("/leaverequests", "SelfService"),
        ("/leavebalances", "SelfService"),
        ("/workfromhome", "SelfService"),
        ("/myprofile", "SelfService"),

        ("/performance", "Performance"),
        ("/hrsettings/peopleai", "PeopleAI"),
        ("/employees/smartonboarding", "PeopleAI"),
        ("/employees/smartonboardingreview", "PeopleAI")
    ];

    public static string? ResolveModule(string path)
    {
        var normalized = string.IsNullOrWhiteSpace(path)
            ? "/"
            : path.Trim().ToLowerInvariant();

        foreach (var (prefix, module) in RouteModules)
        {
            if (normalized == prefix || normalized.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                return module;
            }
        }

        return null;
    }

    public static async Task<bool> IsEnabledAsync(
        ApplicationDbContext db,
        int tenantId,
        string module)
    {
        if (tenantId <= 0 || !PlatformPortalStore.ModuleCatalog.ContainsKey(module))
        {
            return false;
        }

        var enabled = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
SELECT COUNT(*)
FROM dbo.TenantLicenses license
CROSS APPLY STRING_SPLIT(license.EnabledModulesCsv, N',') enabledModule
WHERE license.TenantId = @TenantId
  AND LTRIM(RTRIM(enabledModule.value)) = @Module;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
                HrmsDatabase.AddParameter(command, "@Module", module);
            });

        return enabled > 0;
    }
}
