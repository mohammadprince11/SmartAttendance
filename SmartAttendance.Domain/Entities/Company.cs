using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class Company : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation Property
    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}