using SmartAttendance.Application.Common.Security;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس P0-3 — التوافقية لا تمنح العمليّات الحسّاسة ضمنيّاً: كانت تمنح HR Officer
/// وBranch Manager كلَّ شيءٍ عدا ManagePermissions (تصعيد صلاحية). الآن الحذف/إنهاء
/// الخدمة/إعادة التعيين/الاستيراد/التصدير/تغيير الإسناد تتطلّب تخصيصاً صريحاً، مع
/// بقاء القراءة والتعديل الأساسي للتوافق الخلفيّ. الأدمن وHR Manager بلا تغيير.
/// </summary>
public class PeopleCompatibilityAccessTests
{
    private static readonly string[] SensitiveCodes =
    {
        PeoplePermissionCodes.Delete,
        PeoplePermissionCodes.AdministrativeDelete,
        PeoplePermissionCodes.EndService,
        PeoplePermissionCodes.Rehire,
        PeoplePermissionCodes.Import,
        PeoplePermissionCodes.Export,
        PeoplePermissionCodes.ChangeAssignment,
        PeoplePermissionCodes.ManagePermissions,
        PeoplePermissionCodes.ViewCompensation,
        PeoplePermissionCodes.EditCompensation,
    };

    private static readonly string[] BasicCodes =
    {
        PeoplePermissionCodes.ViewDirectory,
        PeoplePermissionCodes.ViewProfile,
        PeoplePermissionCodes.Edit,
        PeoplePermissionCodes.ViewLifecycle,
        PeoplePermissionCodes.ViewHistory,
    };

    [Theory]
    [InlineData("Admin")]
    [InlineData("HR Manager")]
    public void AdminAndHrManager_KeepFullCompatibility(string role)
    {
        foreach (var code in PeoplePermissionCodes.All)
        {
            Assert.True(PeopleCompatibilityAccess.IsAllowed(role, code),
                $"{role} يجب أن يبقى مسموحاً له {code} (سلوك مشروع لا يُكسَر).");
        }
    }

    [Theory]
    [InlineData("HR Officer")]
    [InlineData("Branch Manager")]
    public void HrOfficerAndBranchManager_AreDeniedSensitiveOps(string role)
    {
        foreach (var code in SensitiveCodes)
        {
            Assert.False(PeopleCompatibilityAccess.IsAllowed(role, code),
                $"{role} يجب ألا يُمنَح {code} تلقائيّاً — يتطلّب تخصيصاً صريحاً (P0-3).");
        }
    }

    [Theory]
    [InlineData("HR Officer")]
    [InlineData("Branch Manager")]
    public void HrOfficerAndBranchManager_KeepBasicReadAndEdit(string role)
    {
        foreach (var code in BasicCodes)
        {
            Assert.True(PeopleCompatibilityAccess.IsAllowed(role, code),
                $"{role} يجب أن يبقى له {code} للتوافق الخلفيّ.");
        }
    }

    [Theory]
    [InlineData("Finance Viewer")]
    [InlineData("Employee")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Unknown Role")]
    public void OtherRoles_GetNoCompatibilityAtAll(string? role)
    {
        foreach (var code in PeoplePermissionCodes.All)
        {
            Assert.False(PeopleCompatibilityAccess.IsAllowed(role, code),
                $"«{role}» يجب ألا ينال أي صلاحية People بالتوافقية.");
        }
    }

    [Fact]
    public void UnknownPermissionCode_IsAlwaysDenied()
    {
        Assert.False(PeopleCompatibilityAccess.IsAllowed("Admin", "People.NotARealCode"));
        Assert.False(PeopleCompatibilityAccess.IsAllowed("HR Manager", ""));
    }
}
