using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

/// <summary>
/// A family member / dependent of an employee (spouse, child, relative). Field set
/// mirrors the Kayan family form, including the flags that drive payroll/benefits
/// (dependent), emergency-contact directory, and special-needs handling.
/// First panel of the 360° employee file.
/// </summary>
public class EmployeeDependent : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public DependentRelation Relation { get; set; }

    /// <summary>Name in Arabic (primary).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Name in another language (optional).</summary>
    public string? NameOther { get; set; }

    public DateOnly? BirthDate { get; set; }

    /// <summary>Marriage date — relevant for a spouse.</summary>
    public DateOnly? MarriageDate { get; set; }

    public string? Religion { get; set; }

    public string? Nationality { get; set; }

    public string? NationalId { get; set; }

    public string? PassportNo { get; set; }

    public bool IsCitizen { get; set; }

    /// <summary>Residency permit number — relevant for non-citizen family members.</summary>
    public string? ResidencyNo { get; set; }

    /// <summary>Male / female — Kayan asks it for children and relatives.</summary>
    public string? Gender { get; set; }

    /// <summary>Currently studying — drives education allowances.</summary>
    public bool IsStudent { get; set; }

    public string? MaritalStatus { get; set; }

    /// <summary>Listed as an emergency contact.</summary>
    public bool IsEmergencyContact { get; set; }

    public bool IsSpecialNeeds { get; set; }

    /// <summary>Is currently employed.</summary>
    public bool IsWorking { get; set; }

    /// <summary>Financially dependent (معال) — drives allowances/tax.</summary>
    public bool IsDependent { get; set; }

    public string? MobilePhone { get; set; }

    public string? CompanyName { get; set; }

    public string? Note { get; set; }
}
