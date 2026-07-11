using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

public class Branch : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    public int CompanyId { get; set; }

    public Company Company { get; set; } = null!;

    public ICollection<Department> Departments { get; set; } = new List<Department>();

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();

    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
