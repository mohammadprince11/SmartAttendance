using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class Department : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int CompanyId { get; set; }

    public Company Company { get; set; } = null!;

    public int? BranchId { get; set; }

    public Branch? Branch { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
