using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات خريطة «الدور ← المسارات». مصدر الحقيقة الوحيد الذي يستهلكه
/// RoleSecurityMiddleware للإنفاذ والقائمة الجانبية للإظهار — فأي انحراف هنا
/// يعني إما منعاً لمن يستحق أو عرض رابط ينتهي بـ«لا صلاحية».
/// </summary>
public class RoleRouteCatalogTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    public void Admin_IsAllowedEverywhere(string role)
    {
        Assert.True(RoleRouteCatalog.IsAllowed(role, "/payroll/runs"));
        Assert.True(RoleRouteCatalog.IsAllowed(role, "/anything/unknown"));
    }

    [Theory]
    [InlineData("HR Manager")]
    [InlineData("HR Officer")]
    [InlineData("Branch Manager")]
    public void OperationalRoles_CanReachAttendanceOperations(string role)
    {
        // الشاشة التشغيلية الأم: /attendanceprocessing و/attendancecorrections
        // ليست صفحات بل تُعيد التوجيه إليها، فمنعها يقفل المودل بالكامل.
        Assert.True(RoleRouteCatalog.IsAllowed(role, "/attendanceoperations"));
        Assert.True(RoleRouteCatalog.IsAllowed(role, "/attendanceprocessing"));
        Assert.True(RoleRouteCatalog.IsAllowed(role, "/attendancecorrections"));
    }

    [Theory]
    [InlineData("/shiftoverrides")]
    [InlineData("/roster")]
    [InlineData("/employeegeolocations")]
    [InlineData("/attendanceviewer")]
    [InlineData("/monthattendance")]
    [InlineData("/shiftrules")]
    [InlineData("/shifttypes")]
    public void HrManager_CoversEveryAttendancePage(string path)
    {
        Assert.True(RoleRouteCatalog.IsAllowed("HR Manager", path));
    }

    [Fact]
    public void LowerRoles_DoNotGetAttendanceConfiguration()
    {
        // موظف الموارد البشرية تشغيلي لا يهيّئ المحرك
        Assert.False(RoleRouteCatalog.IsAllowed("HR Officer", "/shiftrules"));
        Assert.False(RoleRouteCatalog.IsAllowed("HR Officer", "/shifttypes"));
        Assert.False(RoleRouteCatalog.IsAllowed("Branch Manager", "/shiftrules"));
        Assert.False(RoleRouteCatalog.IsAllowed("Finance Viewer", "/dayattendance"));
    }

    [Fact]
    public void UnknownRole_GetsOnlyCommonRoutes()
    {
        Assert.True(RoleRouteCatalog.IsAllowed("Employee", "/"));
        Assert.False(RoleRouteCatalog.IsAllowed("Employee", "/employees"));
        Assert.False(RoleRouteCatalog.IsAllowed(null, "/employees"));
        Assert.False(RoleRouteCatalog.IsAllowed("", "/employees"));
    }

    [Fact]
    public void Matches_RespectsSegmentBoundary()
    {
        // «/shifts» يجب ألّا يفتح «/shiftrules» — مطابقة بحدود المقطع لا بادئة نصية
        Assert.True(RoleRouteCatalog.Matches("/shifts", "/shifts"));
        Assert.True(RoleRouteCatalog.Matches("/shifts/create", "/shifts"));
        Assert.False(RoleRouteCatalog.Matches("/shiftrules", "/shifts"));
    }

    [Fact]
    public void IsAllowed_IsCaseInsensitiveOnPath()
    {
        Assert.True(RoleRouteCatalog.IsAllowed("HR Manager", "/DayAttendance"));
        Assert.True(RoleRouteCatalog.IsAllowed("HR Manager", "/DAYATTENDANCE"));
    }
}
