namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// كتالوج «التقارير» لأدوار الوصول من النوع Reports. الدور يحدّد أي مجموعات تقارير
/// يصلها. يُخزَّن كمنح (GrantKey = رمز المجموعة). الإنفاذ يُربَط تدريجيّاً بمركز
/// التقارير عبر الحارس <c>AccessRoleStore.HasGrantAsync(..., TypeReports, code)</c>.
/// </summary>
public static class ReportsCatalog
{
    public sealed record ReportGroup(string Code, string Label);

    public static readonly IReadOnlyList<ReportGroup> Groups = new List<ReportGroup>
    {
        new("Attendance", "تقارير الحضور"),
        new("Payroll", "تقارير الرواتب"),
        new("Leaves", "تقارير الإجازات"),
        new("Employees", "تقارير الموظفين"),
    };

    public static bool IsValid(string code) =>
        Groups.Any(g => g.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}
