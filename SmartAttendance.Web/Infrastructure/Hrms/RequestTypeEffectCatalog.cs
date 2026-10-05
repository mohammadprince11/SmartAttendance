using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Stable execution identity for dynamic request types. Display names are editable and
/// must never decide which attendance/payroll engine executes a request.
/// </summary>
public static class RequestTypeEffectCatalog
{
    public const string LeaveAnnual = "LeaveAnnual";
    public const string LeaveSick = "LeaveSick";
    public const string LeaveUnpaid = "LeaveUnpaid";
    public const string LeaveOther = "LeaveOther";
    public const string ExitPermission = "ExitPermission";
    public const string BusinessTrip = "BusinessTrip";
    public const string WorkFromHome = "WorkFromHome";
    public const string Overtime = "Overtime";
    public const string ShiftChange = "ShiftChange";

    public sealed record Option(string Code, string Label);

    public static IReadOnlyList<Option> Options { get; } = new[]
    {
        new Option(LeaveAnnual, "إجازة سنوية"),
        new Option(LeaveSick, "إجازة مرضية"),
        new Option(LeaveUnpaid, "إجازة غير مدفوعة"),
        new Option(LeaveOther, "إجازة أخرى"),
        new Option(ExitPermission, "مغادرة / إذن زمني"),
        new Option(BusinessTrip, "مهمة / رحلة عمل"),
        new Option(WorkFromHome, "عمل من المنزل"),
        new Option(Overtime, "عمل إضافي"),
        new Option(ShiftChange, "تغيير مناوبة")
    };

    public static bool IsKnown(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Options.Any(x => x.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string? Normalize(string? code) =>
        IsKnown(code) ? Options.First(x => x.Code.Equals(code!.Trim(), StringComparison.OrdinalIgnoreCase)).Code : null;

    /// <summary>Compatibility inference for rows created before EffectCode existed.</summary>
    public static string? InferLegacy(string? name, string? paidMode)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) return null;

        if (value.Contains("مغادرة", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("خروج", StringComparison.OrdinalIgnoreCase))
            return ExitPermission;
        if (value.Contains("مهمة عمل", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("رحلة عمل", StringComparison.OrdinalIgnoreCase))
            return BusinessTrip;
        if (value.Contains("المنزل", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("عن بعد", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("عن بُعد", StringComparison.OrdinalIgnoreCase))
            return WorkFromHome;
        if (value.Contains("إضافي", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("overtime", StringComparison.OrdinalIgnoreCase))
            return Overtime;
        if (value.Contains("مناوبة", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("shift", StringComparison.OrdinalIgnoreCase))
            return ShiftChange;
        if (value.Contains("سنوي", StringComparison.OrdinalIgnoreCase)) return LeaveAnnual;
        if (value.Contains("مرض", StringComparison.OrdinalIgnoreCase)) return LeaveSick;
        if (value.Contains("إجازة", StringComparison.OrdinalIgnoreCase) || value.Contains("اجازة", StringComparison.OrdinalIgnoreCase))
            return string.Equals(paidMode, "unpaid", StringComparison.OrdinalIgnoreCase) ? LeaveUnpaid : LeaveOther;
        return null;
    }

    public static string? EffectiveCode(RequestTypeStore.ReqType type) =>
        Normalize(type.EffectCode) ?? InferLegacy(type.Name, type.PaidMode);

    public static LeaveType? ToLeaveType(string? effectCode) => Normalize(effectCode) switch
    {
        LeaveAnnual => LeaveType.Annual,
        LeaveSick => LeaveType.Sick,
        LeaveUnpaid => LeaveType.Unpaid,
        LeaveOther => LeaveType.Emergency,
        _ => null
    };
}
