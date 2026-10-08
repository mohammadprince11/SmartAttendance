namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>Routing inputs must already be restricted to the subject's company.</summary>
public static class NotificationRoutingPolicy
{
    public const string Supervisors = "المشرفين";
    public const string ManagerAndSupervisors = "المدير المباشر والمشرفون";
    public const string Employee = "الموظف";
    public const string Self = "الموظف نفسه";
    public const string Manager = "المدير المباشر";
    public const string SelfAndManager = "الموظف نفسه و مديره المباشر";
    public const string SelfAndSupervisors = "الموظف نفسه والمشرفون";
    public const string Everyone = "الموظف نفسه و مديره المباشر والمشرفون";
    public const string Group = "مجموعة الموظفين";
    public const string Specific = "موظفين محددين";
    public const string AllEmployees = "جميع الموظفين";
    public const string Voters = "المصوتين";
    public const string VotersAndSupervisors = "المصوتين والمشرفين";
    public static IReadOnlyList<string> Audiences { get; } =
        [Self, Manager, SelfAndManager, Group, Supervisors, Everyone, ManagerAndSupervisors, SelfAndSupervisors];

    public static bool IncludesSelf(string audience) => audience is Employee or Self or SelfAndManager or SelfAndSupervisors or Everyone;
    public static bool IncludesManager(string audience) => audience is Manager or SelfAndManager or ManagerAndSupervisors or Everyone;
    public static bool IncludesSupervisors(string audience) => audience is Supervisors or ManagerAndSupervisors or SelfAndSupervisors or Everyone or VotersAndSupervisors;

    public sealed record Supervisor(int? EmployeeId, string Username, bool BackOfficeAllowed = true);
    public sealed record Plan(IReadOnlyCollection<int> EmployeeIds, IReadOnlyCollection<string> BackOfficeUsers);

    public static bool ValidAudience(string? audience) =>
        audience is Employee or Specific or AllEmployees or Voters or VotersAndSupervisors || Audiences.Contains(audience);

    public static bool ValidEmployeeItems(string? items) =>
        items is "كل الموظفين" or "كل العناصر المختارة";

    public static Plan Resolve(string audience, int subject, int? manager,
        IEnumerable<Supervisor> supervisors, IEnumerable<Supervisor> companyUsers, string? selectedSupervisor,
        IEnumerable<int>? groupMembers = null)
    {
        var ids = new HashSet<int>();
        var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!ValidAudience(audience)) return new Plan(ids, users); // fail closed
        if (IncludesSelf(audience)) ids.Add(subject);
        // For AllEmployees the caller supplies the full scoped, active company set.
        // Specific/Group receive only the validated explicit IDs; never an implicit broadcast.
        if (audience is Group or Specific or AllEmployees or Voters or VotersAndSupervisors)
        {
            foreach (var id in groupMembers ?? []) if (id > 0) ids.Add(id);
            if (audience != VotersAndSupervisors) return new Plan(ids, users);
        }

        var selected = selectedSupervisor?.Trim();
        foreach (var supervisor in IncludesSupervisors(audience) ? supervisors : [])
        {
            if (!string.IsNullOrEmpty(selected) && !supervisor.Username.Equals(selected, StringComparison.OrdinalIgnoreCase)) continue;
            if (supervisor.EmployeeId == subject && !IncludesSelf(audience) && audience != VotersAndSupervisors) continue;
            if (supervisor.EmployeeId is int id) ids.Add(id);
            if (supervisor.BackOfficeAllowed && !string.IsNullOrWhiteSpace(supervisor.Username)) users.Add(supervisor.Username);
        }
        if (IncludesManager(audience) && manager is int managerId && managerId != subject)
        {
            ids.Add(managerId);
            foreach (var user in companyUsers.Where(u => u.EmployeeId == managerId && u.BackOfficeAllowed && !string.IsNullOrWhiteSpace(u.Username))) users.Add(user.Username);
        }
        return new Plan(ids, users);
    }

    public static DateOnly? NextAnnualDate(DateOnly? date, DateOnly today)
    {
        if (date is not { } value || value > today) return null;
        DateOnly InYear(int year) => new(year, value.Month, Math.Min(value.Day, DateTime.DaysInMonth(year, value.Month)));
        var target = InYear(today.Year);
        return target < today ? InYear(today.Year + 1) : target;
    }
}
