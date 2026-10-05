using Microsoft.Data.SqlClient;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Platform;

public sealed record TenantEmployeeCapacity(long UsedEmployees, int MaxEmployees)
{
    public bool CanAdd => MaxEmployees > 0 && UsedEmployees < MaxEmployees;

    public static string LimitMessage(TenantEmployeeCapacity? capacity) =>
        capacity is null
            ? "لا يمكن إضافة موظف لأن ترخيص المنظومة غير مكتمل. راجع إدارة المنصة."
            : $"لا يمكن إضافة موظف جديد: وصل عدد الموظفين إلى حد الترخيص ({capacity.UsedEmployees} من {capacity.MaxEmployees}). راجع إدارة المنصة لزيادة الحد المسموح.";

    public static async Task<TenantEmployeeCapacity?> LoadAsync(
        ApplicationDbContext db, int tenantId)
    {
        // The caller supplies the authenticated tenant, never a query/form tenant.
        if (tenantId <= 0) return null;

        var rows = await HrmsDatabase.QueryAsync(db, """
SELECT license.MaxEmployees,
       (SELECT COUNT_BIG(*)
        FROM dbo.Employees employee
        LEFT JOIN dbo.Branches branch ON branch.Id = employee.BranchId
        INNER JOIN dbo.Companies company
            ON company.Id = COALESCE(employee.CompanyId, branch.CompanyId)
        WHERE company.TenantId = @TenantId AND employee.IsDeleted = 0) AS UsedEmployees
FROM dbo.TenantLicenses license
WHERE license.TenantId = @TenantId;
""",
            command => HrmsDatabase.AddParameter(command, "@TenantId", tenantId),
            reader => new TenantEmployeeCapacity(
                HrmsDatabase.GetLong(reader, "UsedEmployees"),
                HrmsDatabase.GetInt(reader, "MaxEmployees")));
        return rows.FirstOrDefault();
    }

    public static bool IsLimitException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 51042 }) return true;
        }
        return false;
    }
}
