using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

/// <summary>
/// A recurring allowance (علاوة) granted to an employee — Kayan "العلاوات" panel in
/// the financial file. The item name comes from the shared salary-items lookup, the
/// amount is per-employee, and the active window is a date range: an open ToDate
/// means the allowance runs indefinitely; EndAfterDate stops it automatically after
/// the range. Active allowances feed the employee's total salary.
/// </summary>
public class EmployeeAllowance : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    /// <summary>Salary item name from the "salaryitems" lookup (بدل سكن، بدل مواصلات…).</summary>
    public string ItemName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateOnly FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    /// <summary>Stop the allowance automatically once ToDate passes.</summary>
    public bool EndAfterDate { get; set; }

    public string? Note { get; set; }

    public string? AttachmentName { get; set; }

    public string? AttachmentPath { get; set; }

    /// <summary>نشط الآن؟ توقفت إذا انتهى نطاقها (مع إنهاء بعد التاريخ) أو لم تبدأ بعد.</summary>
    public bool IsActiveOn(DateOnly today) =>
        FromDate <= today && (ToDate == null || !EndAfterDate || today <= ToDate.Value);
}
