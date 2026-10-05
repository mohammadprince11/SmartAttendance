using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

/// <summary>
/// العميل المستقل الذي يملك منظومة ZYNORA واحدة. قد يحتوي العميل عدة شركات
/// قانونية، لكن بياناته لا تُعرض لأي عميل آخر.
/// </summary>
public sealed class Tenant : BaseEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<Company> Companies { get; set; } = new List<Company>();

    public ICollection<SystemUser> SystemUsers { get; set; } = new List<SystemUser>();
}
