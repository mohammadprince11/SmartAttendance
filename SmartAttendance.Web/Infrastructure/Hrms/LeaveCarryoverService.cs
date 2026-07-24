using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Domain.Leave;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// ترحيل رصيد الإجازات السنوي: رصيد الموظف المتبقّي في سنة N
/// (المستحق + المُرحّل − المستهلَك) يُكتب كـ<see cref="LeaveBalance.CarriedOverDays"/>
/// لسنة N+1 (بسقف اختياري، للأنواع المختارة). عملية آمنة للإعادة: تُعيد الحساب من
/// المصدر (السنة N) وتضبط مُرحّل N+1 (لا تُراكم)، فتكرارها لا يُضاعف. المستحق لسنة
/// N+1 يبقى تجاوزَه اليدوي إن وُجد، وإلا الافتراضي من <see cref="IraqiLeavePolicy"/>.
/// </summary>
public static class LeaveCarryoverService
{
    public sealed class CarryoverResult
    {
        public int EmployeesProcessed { get; set; }
        public decimal TotalCarriedDays { get; set; }
        public Dictionary<LeaveType, decimal> ByType { get; set; } = new();
    }

    /// <summary>
    /// يرحّل أرصدة موظفي الشركة النشطين من <paramref name="fromYear"/> إلى التي تليها.
    /// </summary>
    public static async Task<CarryoverResult> CarryOverAsync(
        ApplicationDbContext db,
        int companyId,
        int fromYear,
        IReadOnlyCollection<LeaveType> types,
        decimal? cap,
        string userName)
    {
        await LeaveBalanceSchema.EnsureAsync(db);

        var toYear = fromYear + 1;
        var trackedTypes = types
            .Where(t => IraqiLeavePolicy.TrackedTypes.Contains(t))
            .Distinct()
            .ToList();

        var result = new CarryoverResult();
        if (trackedTypes.Count == 0) return result;

        var employeeIds = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive && !e.IsDeleted && e.Branch.CompanyId == companyId)
            .Select(e => e.Id)
            .ToListAsync();
        if (employeeIds.Count == 0) return result;

        // صفوف سنة الوجهة الحالية (للحفاظ على تجاوز المستحق اليدوي إن وُجد).
        var targetRows = await db.LeaveBalances
            .Where(b => b.Year == toYear && employeeIds.Contains(b.EmployeeId))
            .ToListAsync();
        var targetLookup = targetRows
            .GroupBy(b => b.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(b => b.LeaveType, b => b));

        var now = DateTime.UtcNow;
        var note = $"مُرحّل تلقائياً من رصيد {fromYear}";

        foreach (var employeeId in employeeIds)
        {
            var balances = await LeaveBalanceCalculator.ForEmployeeAsync(db, employeeId, fromYear);
            var carriedForEmployee = 0m;

            foreach (var type in trackedTypes)
            {
                var source = balances.FirstOrDefault(b => b.Type == type);
                var remaining = source?.Remaining ?? 0;
                var carried = remaining > 0 ? remaining : 0;
                if (cap.HasValue && carried > cap.Value) carried = cap.Value;

                targetLookup.TryGetValue(employeeId, out var byType);
                LeaveBalance? row = null;
                byType?.TryGetValue(type, out row);

                if (row == null)
                {
                    db.LeaveBalances.Add(new LeaveBalance
                    {
                        EmployeeId = employeeId,
                        Year = toYear,
                        LeaveType = type,
                        EntitledDays = IraqiLeavePolicy.GetDefaultEntitlement(type) ?? 0,
                        CarriedOverDays = carried,
                        Note = note,
                        CreatedAt = now,
                        CreatedBy = userName
                    });
                }
                else
                {
                    // نضبط المُرحّل فقط — المستحق (قد يكون تجاوزاً يدوياً) يبقى كما هو.
                    row.CarriedOverDays = carried;
                    row.Note = string.IsNullOrWhiteSpace(row.Note) ? note : row.Note;
                    row.UpdatedAt = now;
                    row.UpdatedBy = userName;
                }

                carriedForEmployee += carried;
                result.ByType[type] = result.ByType.GetValueOrDefault(type) + carried;
            }

            if (carriedForEmployee > 0) result.EmployeesProcessed++;
            result.TotalCarriedDays += carriedForEmployee;
        }

        await db.SaveChangesAsync();
        return result;
    }
}
