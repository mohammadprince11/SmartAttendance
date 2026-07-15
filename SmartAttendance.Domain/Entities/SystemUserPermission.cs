using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

public class SystemUserPermission : AuditableEntity
{
    public int SystemUserId { get; set; }

    public SystemUser SystemUser { get; set; } = null!;

    public int PermissionId { get; set; }

    public Permission Permission { get; set; } = null!;

    public PermissionEffect Effect { get; set; } = PermissionEffect.Allow;

    public DateTime? ValidFromUtc { get; set; }

    public DateTime? ValidToUtc { get; set; }

    public PeopleDataScopeType ScopeType { get; set; } = PeopleDataScopeType.All;

    public int? ScopeCompanyId { get; set; }

    public int? ScopeBranchId { get; set; }

    public int? ScopeDepartmentId { get; set; }

    public int? ScopeEmployeeId { get; set; }
}
