using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class Company : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? LogoPath { get; set; }

    public string? CountryCode { get; set; }

    public string? CurrencyCode { get; set; }

    public string? TimeZoneId { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();

    public ICollection<Department> Departments { get; set; } = new List<Department>();

    public CompanyPayrollSetting? PayrollSettings { get; set; }

    public ICollection<PayrollCutoffPolicy> PayrollCutoffPolicies { get; set; } = new List<PayrollCutoffPolicy>();
}
