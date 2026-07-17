using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Leave;

/// <summary>
/// Default leave entitlements per the Iraqi Labour Law No. 37 of 2015.
/// Values are intentionally hard-coded for this stage (per-company policy tables
/// can be layered on later without changing callers). Only the types that carry a
/// real balance return a value; all other types return <c>null</c> (not tracked
/// against a balance — e.g. unpaid or official/public holidays).
/// </summary>
public static class IraqiLeavePolicy
{
    /// <summary>Annual paid leave — Article 68 (21 days).</summary>
    public const decimal AnnualDays = 21m;

    /// <summary>Sick leave with pay — up to 30 days per year.</summary>
    public const decimal SickDays = 30m;

    /// <summary>The leave types that are tracked against a yearly balance.</summary>
    public static readonly IReadOnlyList<LeaveType> TrackedTypes = new[]
    {
        LeaveType.Annual,
        LeaveType.Sick
    };

    /// <summary>
    /// Default yearly entitlement for a leave type, or <c>null</c> when the type is
    /// not balance-tracked.
    /// </summary>
    public static decimal? GetDefaultEntitlement(LeaveType leaveType) => leaveType switch
    {
        LeaveType.Annual => AnnualDays,
        LeaveType.Sick => SickDays,
        _ => null
    };

    public static bool IsTracked(LeaveType leaveType) => GetDefaultEntitlement(leaveType).HasValue;
}
