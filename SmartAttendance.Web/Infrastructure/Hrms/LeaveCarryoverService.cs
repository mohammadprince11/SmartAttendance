using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Domain.Leave;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

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
        CompanyScope scope,
        int companyId,
        int fromYear,
        IReadOnlyCollection<LeaveType> types,
        decimal? cap,
        string userName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (companyId <= 0 || !scope.Allows(companyId))
        {
            throw new UnauthorizedAccessException("The selected company is outside the leave carry-over scope.");
        }

        await LeaveBalanceSchema.EnsureAsync(db);

        var toYear = fromYear + 1;
        var trackedTypes = types
            .Where(t => IraqiLeavePolicy.TrackedTypes.Contains(t))
            .Distinct()
            .ToList();

        var result = new CarryoverResult();
        if (trackedTypes.Count == 0) return result;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var employeeIds = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive && !e.IsDeleted)
            .Where(e => e.CompanyId == companyId ||
                (e.CompanyId == null && e.Branch.CompanyId == companyId))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        if (employeeIds.Count == 0) return result;

        var yearStart = new DateOnly(fromYear, 1, 1);
        var yearEnd = new DateOnly(fromYear, 12, 31);

        // Set-based source load: one balance query and one approved-request query.
        var sourceRows = await db.LeaveBalances.AsNoTracking()
            .Where(b => b.Year == fromYear && employeeIds.Contains(b.EmployeeId))
            .ToListAsync(cancellationToken);
        var sourceLookup = sourceRows
            .GroupBy(b => b.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(b => b.LeaveType, b => b));

        var requests = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.Status == LeaveStatus.Approved
                     && employeeIds.Contains(r.EmployeeId)
                     && trackedTypes.Contains(r.LeaveType)
                     && r.FromDate <= yearEnd
                     && r.ToDate >= yearStart)
            .Select(r => new { r.EmployeeId, r.LeaveType, r.FromDate, r.ToDate })
            .ToListAsync(cancellationToken);

        var usedLookup = new Dictionary<(int EmployeeId, LeaveType Type), decimal>();
        foreach (var request in requests)
        {
            var start = request.FromDate > yearStart ? request.FromDate : yearStart;
            var end = request.ToDate < yearEnd ? request.ToDate : yearEnd;
            var days = end.DayNumber - start.DayNumber + 1;
            if (days > 0)
            {
                var key = (request.EmployeeId, request.LeaveType);
                usedLookup[key] = usedLookup.GetValueOrDefault(key) + days;
            }
        }

        // صفوف سنة الوجهة الحالية (للحفاظ على تجاوز المستحق اليدوي إن وُجد).
        var targetRows = await db.LeaveBalances
            .Where(b => b.Year == toYear && employeeIds.Contains(b.EmployeeId))
            .ToListAsync(cancellationToken);
        var targetLookup = targetRows
            .GroupBy(b => b.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(b => b.LeaveType, b => b));

        var now = DateTime.UtcNow;
        var note = $"مُرحّل تلقائياً من رصيد {fromYear}";

        foreach (var employeeId in employeeIds)
        {
            var carriedForEmployee = 0m;
            sourceLookup.TryGetValue(employeeId, out var bySourceType);

            foreach (var type in trackedTypes)
            {
                LeaveBalance? source = null;
                var hasSource = bySourceType is not null &&
                    bySourceType.TryGetValue(type, out source);
                var entitled = hasSource
                    ? source!.EntitledDays
                    : IraqiLeavePolicy.GetDefaultEntitlement(type) ?? 0;
                var priorCarry = hasSource ? source!.CarriedOverDays : 0;
                var used = usedLookup.GetValueOrDefault((employeeId, type));
                var remaining = entitled + priorCarry - used;
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

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
