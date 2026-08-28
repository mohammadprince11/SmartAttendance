namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// كتالوج «الخدمة الذاتية» لأدوار الوصول من النوع SelfService. الدور يحدّد ما يتيحه
/// للموظّف في بوابته. يُخزَّن كمنح (GrantKey = رمز الإجراء). الإنفاذ يُربَط تدريجيّاً
/// بمودل البوابة عبر الحارس <c>AccessRoleStore.HasGrantAsync(..., TypeSelfService, code)</c>.
/// </summary>
public static class SelfServiceCatalog
{
    public sealed record SelfServiceAction(string Code, string Label);

    public static readonly IReadOnlyList<SelfServiceAction> Actions = new List<SelfServiceAction>
    {
        new("LeaveRequest", "طلب إجازة"),
        new("ExitPermission", "طلب مغادرة"),
        new("OvertimeRequest", "طلب عمل إضافي"),
        new("PunchCorrection", "تصحيح بصمة"),
        new("UpdateMyData", "تحديث بياناتي"),
        new("ViewPayslip", "عرض قسيمة الراتب"),
        new("ShiftRequest", "طلب تغيير المناوبة"),
        new("FinancialRequest", "طلب مالي"),
        new("DocumentRequest", "طلب وثيقة"),
        new("ViewPublishedReports", "عرض التقارير المنشورة"),
    };

    public static bool IsValid(string code) =>
        Actions.Any(a => a.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}
