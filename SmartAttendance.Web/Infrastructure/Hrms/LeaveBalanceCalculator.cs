using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Domain.Leave;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// حساب رصيد الإجازة المتبقّي لموظف/سنة (المتبقّي = المستحق + المرحّل − المستهلَك).
/// المستحق من <see cref="Domain.Entities.LeaveBalance"/> إن وُجد وإلا افتراضي
/// <see cref="IraqiLeavePolicy"/>، والمستهلَك مشتق من طلبات الإجازة المعتمدة —
/// نفس منطق صفحة أرصدة الإجازات، مُستخرَجٌ ليستهلكه أيضاً «بدل الإجازة» بالمسير.
/// </summary>
public static class LeaveBalanceCalculator
{
    public sealed record TypeBalance(LeaveType Type, decimal Entitled, decimal CarriedOver, decimal Used)
    {
        public decimal Remaining => Entitled + CarriedOver - Used;
    }

    /// <summary>أرصدة الأنواع المتتبَّعة (سنوية/مرضية) لموظف في سنة.</summary>
    public static async Task<List<TypeBalance>> ForEmployeeAsync(
        ApplicationDbContext db, int employeeId, int year)
    {
        await LeaveBalanceSchema.EnsureAsync(db);
        var trackedTypes = IraqiLeavePolicy.TrackedTypes;

        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        var overrides = await db.LeaveBalances.AsNoTracking()
            .Where(b => b.Year == year && b.EmployeeId == employeeId)
            .ToDictionaryAsync(b => b.LeaveType, b => b);

        var requests = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.Status == LeaveStatus.Approved
                     && r.EmployeeId == employeeId
                     && trackedTypes.Contains(r.LeaveType)
                     && r.FromDate <= yearEnd
                     && r.ToDate >= yearStart)
            .Select(r => new { r.LeaveType, r.FromDate, r.ToDate })
            .ToListAsync();

        var used = new Dictionary<LeaveType, decimal>();
        foreach (var r in requests)
        {
            var start = r.FromDate > yearStart ? r.FromDate : yearStart;
            var end = r.ToDate < yearEnd ? r.ToDate : yearEnd;
            var days = end.DayNumber - start.DayNumber + 1;
            if (days <= 0) continue;
            used[r.LeaveType] = used.GetValueOrDefault(r.LeaveType) + days;
        }

        var result = new List<TypeBalance>();
        foreach (var type in trackedTypes)
        {
            var hasOverride = overrides.TryGetValue(type, out var stored);
            var entitled = hasOverride ? stored!.EntitledDays : IraqiLeavePolicy.GetDefaultEntitlement(type) ?? 0;
            var carried = hasOverride ? stored!.CarriedOverDays : 0;
            result.Add(new TypeBalance(type, entitled, carried, used.GetValueOrDefault(type)));
        }
        return result;
    }
}
