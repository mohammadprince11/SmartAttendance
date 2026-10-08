namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>Presentation grouping only; does not change template resolution or grant access.</summary>
public static class ApprovalTemplateNavigation
{
    private static readonly HashSet<string> SupervisorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Violation", "Transfer", "Onboarding", "Offboarding", "SalaryIncrease"
    };

    public static string NormalizeAudience(string? audience)
        => string.Equals(audience, "All", StringComparison.OrdinalIgnoreCase) ? "All" :
            string.Equals(audience, "Supervisor", StringComparison.OrdinalIgnoreCase) ? "Supervisor" : "SelfService";

    public static string AudienceFor(string type) => SupervisorTypes.Contains(type) ? "Supervisor" : "SelfService";

    public static bool VisibleIn(string type, string audience) => NormalizeAudience(audience) == "All" || AudienceFor(type) == NormalizeAudience(audience);
}
