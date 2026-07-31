using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// المرحلة 5 — إبطال الجلسات/التوكنات البائتة: الكوكي والتوكن يمرّان بنفس الدالة،
/// فكل حالة هنا تغطي المسارين معاً (تعطيل/قفل/تغيير ختم/حساب محذوف).
/// </summary>
public class SessionSecurityValidatorTests
{
    private const string Stamp = "a1b2c3";

    private static AccountSecurityState Active(
        string role = "HR Manager",
        string? stamp = Stamp) =>
        new(Exists: true, IsActive: true, IsLockedOut: false, Role: role, SecurityStamp: stamp);

    [Fact]
    public void MatchingStampAndRole_IsValid() =>
        Assert.Equal(
            SessionSecurityDecision.Valid,
            SessionSecurityValidator.Evaluate(Stamp, "HR Manager", Active()));

    [Fact]
    public void RoleComparison_IgnoresCaseAndPadding() =>
        Assert.Equal(
            SessionSecurityDecision.Valid,
            SessionSecurityValidator.Evaluate(Stamp, " hr manager ", Active()));

    // ===== الرفض =====

    [Fact]
    public void DisabledAccount_IsRejected() =>
        Assert.Equal(
            SessionSecurityDecision.Reject,
            SessionSecurityValidator.Evaluate(
                Stamp,
                "HR Manager",
                new AccountSecurityState(true, IsActive: false, false, "HR Manager", Stamp)));

    [Fact]
    public void LockedOutAccount_IsRejected() =>
        Assert.Equal(
            SessionSecurityDecision.Reject,
            SessionSecurityValidator.Evaluate(
                Stamp,
                "HR Manager",
                new AccountSecurityState(true, true, IsLockedOut: true, "HR Manager", Stamp)));

    [Fact]
    public void DeletedAccount_IsRejected() =>
        Assert.Equal(
            SessionSecurityDecision.Reject,
            SessionSecurityValidator.Evaluate(Stamp, "HR Manager", AccountSecurityState.Missing));

    [Fact]
    public void NullAccountState_IsRejected() =>
        Assert.Equal(
            SessionSecurityDecision.Reject,
            SessionSecurityValidator.Evaluate(Stamp, "HR Manager", null));

    /// <summary>تغيير كلمة المرور/الدور/سحب الصلاحية كلها تُبدّل الختم سيرفرياً.</summary>
    [Fact]
    public void ChangedSecurityStamp_IsRejected() =>
        Assert.Equal(
            SessionSecurityDecision.Reject,
            SessionSecurityValidator.Evaluate("old-stamp", "HR Manager", Active(stamp: "new-stamp")));

    [Fact]
    public void ChangedStamp_RejectedEvenWhenRoleStillMatches() =>
        Assert.Equal(
            SessionSecurityDecision.Reject,
            SessionSecurityValidator.Evaluate("old", "Admin", Active(role: "Admin", stamp: "new")));

    // ===== التجديد (لا طرد بلا سبب أمني) =====

    [Fact]
    public void TicketWithoutStamp_OnLiveAccount_IsRefreshed() =>
        Assert.Equal(
            SessionSecurityDecision.Refresh,
            SessionSecurityValidator.Evaluate(null, "HR Manager", Active()));

    [Fact]
    public void AccountWithoutStampYet_IsRefreshed() =>
        Assert.Equal(
            SessionSecurityDecision.Refresh,
            SessionSecurityValidator.Evaluate(Stamp, "HR Manager", Active(stamp: null)));

    [Fact]
    public void StaleRoleWithMatchingStamp_IsRefreshed() =>
        Assert.Equal(
            SessionSecurityDecision.Refresh,
            SessionSecurityValidator.Evaluate(Stamp, "HR Officer", Active(role: "Admin")));

    /// <summary>غياب الختم لا يتغلّب على التعطيل: الرفض أسبق من التجديد.</summary>
    [Fact]
    public void DisabledAccount_WithoutStamp_StillRejected() =>
        Assert.Equal(
            SessionSecurityDecision.Reject,
            SessionSecurityValidator.Evaluate(
                null,
                "Employee",
                new AccountSecurityState(true, IsActive: false, false, "Employee", null)));
}
