using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class AnnouncementAudienceRule : BaseEntity
{
    public int AnnouncementGroupId { get; set; }

    public AnnouncementGroup AnnouncementGroup { get; set; } = null!;

    public AnnouncementAudienceType AudienceType { get; set; }

    public bool IsExcluded { get; set; }

    public int? CompanyId { get; set; }

    public Company? Company { get; set; }

    public int? BranchId { get; set; }

    public Branch? Branch { get; set; }

    public int? DepartmentId { get; set; }

    public Department? Department { get; set; }

    public int? PositionId { get; set; }

    public HrJobPosition? Position { get; set; }

    public int? EmployeeId { get; set; }

    public Employee? Employee { get; set; }

    public int DisplayOrder { get; set; }
}
