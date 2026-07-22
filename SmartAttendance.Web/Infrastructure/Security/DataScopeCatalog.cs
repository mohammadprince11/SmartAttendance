namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// Catalog for Data access-roles (row-level scope). A Data role picks entities
/// and, for each, the breadth of records the assigned users may see — from the
/// whole company down to their own record. Enforcement (query filtering) is
/// applied in a later batch; this defines the vocabulary and is stored as grants
/// (GrantKey = entity code, Payload = { "scope": "OwnBranch" }).
/// </summary>
public static class DataScopeCatalog
{
    public sealed record ScopeLevel(string Key, string Label);

    public sealed record DataEntity(string Code, string Label);

    /// <summary>Scope breadth, widest first. "None" means the role grants no access to that entity.</summary>
    public static readonly IReadOnlyList<ScopeLevel> ScopeLevels = new List<ScopeLevel>
    {
        new("None", "لا يشمل"),
        new("All", "كل السجلات"),
        new("OwnCompany", "شركة المستخدم"),
        new("OwnBranch", "فرع المستخدم"),
        new("OwnDepartment", "قسم المستخدم"),
        new("Self", "سجلاته فقط"),
    };

    public static readonly IReadOnlyList<DataEntity> Entities = new List<DataEntity>
    {
        new("Employees", "الموظفون"),
        new("Leaves", "الإجازات"),
        new("Departures", "المغادرات"),
        new("Attendance", "سجلات الحضور"),
        new("SalaryComponents", "عناصر الراتب"),
        new("Documents", "الوثائق"),
        new("Assets", "العهد"),
        new("CustomRequests", "طلبات الموظفين المخصصة"),
    };

    public static bool IsValidEntity(string code) =>
        Entities.Any(e => e.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public static bool IsValidScope(string key) =>
        ScopeLevels.Any(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
