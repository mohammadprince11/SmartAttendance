using SmartAttendance.Application.Common.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// عزل الشركات البنيوي: شركة الموظف تُشتقّ من فرعه ولا تُخمَّن أبداً.
/// إسناد غير متسق ⟹ رفض الحفظ، لا كتابة صفّ بشركة مُختارة عشوائياً.
/// </summary>
public class EmployeeCompanyGuardTests
{
    [Fact]
    public void MatchingBranchAndDepartment_ResolvesToThatCompany() =>
        Assert.Equal(7, EmployeeCompanyGuard.Resolve(branchCompanyId: 7, departmentCompanyId: 7));

    /// <summary>قسم من شركة أخرى ⟹ لا نختار أحدهما، نرفض.</summary>
    [Fact]
    public void ConflictingCompanies_AreRejected() =>
        Assert.Null(EmployeeCompanyGuard.Resolve(branchCompanyId: 1, departmentCompanyId: 2));

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    [InlineData(0, 0)]
    [InlineData(-1, 5)]
    public void MissingOrInvalidAnchors_AreRejected(int branchCompanyId, int departmentCompanyId) =>
        Assert.Null(EmployeeCompanyGuard.Resolve(branchCompanyId, departmentCompanyId));

    // ===== كشف الانحراف بعد النقل بين الفروع =====

    [Fact]
    public void StoredCompanyMatchingBranch_IsConsistent() =>
        Assert.True(EmployeeCompanyGuard.IsConsistent(storedCompanyId: 3, branchCompanyId: 3));

    [Fact]
    public void StaleCompanyAfterBranchTransfer_IsDetected() =>
        Assert.False(EmployeeCompanyGuard.IsConsistent(storedCompanyId: 1, branchCompanyId: 2));

    /// <summary>الصفوف التاريخية قبل التعبئة (NULL) ليست متسقة ⟹ تظهر بالتقارير.</summary>
    [Fact]
    public void NullCompany_IsNotConsistent() =>
        Assert.False(EmployeeCompanyGuard.IsConsistent(storedCompanyId: null, branchCompanyId: 2));

    [Fact]
    public void InvalidBranchCompany_IsNotConsistent() =>
        Assert.False(EmployeeCompanyGuard.IsConsistent(storedCompanyId: 0, branchCompanyId: 0));
}
