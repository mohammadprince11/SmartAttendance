namespace SmartAttendance.Domain.Entities;

public class HrJobPosition
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string ArabicName { get; set; } = string.Empty;

    public string? EnglishName { get; set; }

    public int? DepartmentId { get; set; }

    public bool IsActive { get; set; }
}
