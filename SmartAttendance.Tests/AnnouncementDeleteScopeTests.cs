using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Services;

namespace SmartAttendance.Tests;

public sealed class AnnouncementDeleteScopeTests
{
    private static int[] Visible(AnnouncementManagementScope scope, params AnnouncementGroup[] groups)
    {
        var method = typeof(AnnouncementService).GetMethod("DeletionScope", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return ((IQueryable<AnnouncementGroup>)method.Invoke(null, [groups.AsQueryable(), scope])!).Select(g => g.Id).ToArray();
    }
    private static AnnouncementGroup Group(int id, params AnnouncementAudienceRule[] rules) => new() { Id = id, AudienceRules = rules };
    [Fact]
    public void RestrictedDeleteRejectsOtherMixedGlobalAndOrphanAudiences()
    {
        var groups = new[] {
            Group(1,new AnnouncementAudienceRule { CompanyId=7 }),
            Group(2,new AnnouncementAudienceRule { CompanyId=8 }),
            Group(3,new(){ CompanyId=7 },new(){ CompanyId=8 }),
            Group(4,new(){ CompanyId=7 },new(){ AudienceType=AnnouncementAudienceType.All }),
            Group(5,new(){ CompanyId=7 },new(){ EmployeeId=900 }),
            Group(6,new(){ CompanyId=7 },new(){ BranchId=900 }),
            Group(7,new AnnouncementAudienceRule { EmployeeId=901,Employee=new(){ Id=901,CompanyId=7 } }),
            Group(8,new(){ CompanyId=7 },new(){ CompanyId=8,IsExcluded=true })
        };
        Assert.Equal(new[]{1,7,8},Visible(new(){AllowedCompanyIds=[7]},groups));
        Assert.Empty(Visible(new(){AllowedCompanyIds=[]},groups));
        Assert.Equal(8,Visible(AnnouncementManagementScope.Unrestricted(),groups).Length);
    }
}
