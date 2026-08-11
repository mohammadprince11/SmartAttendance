using System.Security.Claims;
using SmartAttendance.Application.LeaveRequests.Services;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// يبني <see cref="LeaveRequestAccessScope"/> من نطاق شركات الطلب وهوية المستخدم.
///
/// <para>مصدر النطاق هو <see cref="ICompanyScopeProvider"/> نفسه الذي يستهلكه بقيّة
/// النظام — فلا مصدر حقيقةٍ ثانٍ يتباعد عنه.</para>
///
/// <para><b>مغلق الفشل:</b> دور <c>Employee</c> بلا مطالبة <c>EmployeeId</c> صالحة
/// يحصل على <see cref="LeaveRequestAccessScope.DeniedAll"/> لا على نطاق الشركة —
/// فهويةٌ ناقصة لا تُترجَم إلى صلاحيةٍ أوسع.</para>
/// </summary>
public static class LeaveRequestScopeFactory
{
    public const string EmployeeRole = "Employee";

    public static async Task<LeaveRequestAccessScope> BuildAsync(
        ICompanyScopeProvider companyScopeProvider,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var companyScope = await companyScopeProvider.GetAsync(cancellationToken);

        var scope = companyScope.IsUnrestricted
            ? LeaveRequestAccessScope.Unrestricted()
            : LeaveRequestAccessScope.ForCompanies(companyScope.AllowedCompanyIds);

        var role = user.FindFirstValue(ClaimTypes.Role);

        if (!string.Equals(role, EmployeeRole, StringComparison.OrdinalIgnoreCase))
        {
            return scope;
        }

        // الموظّف لا يرى إلا طلباته — قيدٌ **فوق** قيد الشركة لا بديلٌ عنه.
        return int.TryParse(user.FindFirstValue("EmployeeId"), out var employeeId) && employeeId > 0
            ? scope.RestrictedToEmployee(employeeId)
            : LeaveRequestAccessScope.DeniedAll();
    }
}
