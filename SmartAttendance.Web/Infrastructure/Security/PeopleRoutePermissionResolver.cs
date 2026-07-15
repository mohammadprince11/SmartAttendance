using SmartAttendance.Application.Common.Security;

namespace SmartAttendance.Web.Infrastructure.Security;

public enum PeoplePermissionScopeMode
{
    DataSet = 1,
    Employee = 2,
    Global = 3
}

public sealed record PeopleRoutePermissionRequirement(
    string PermissionCode,
    PeoplePermissionScopeMode ScopeMode);

public static class PeopleRoutePermissionResolver
{
    public static PeopleRoutePermissionRequirement? Resolve(
        HttpContext context,
        string normalizedPath)
    {
        if (normalizedPath == "/employees" ||
            normalizedPath == "/employees/index")
        {
            return DataSet(PeoplePermissionCodes.ViewDirectory);
        }

        if (normalizedPath.StartsWith("/employees/create", StringComparison.Ordinal))
        {
            return Global(PeoplePermissionCodes.Create);
        }

        if (normalizedPath.StartsWith("/employees/edit", StringComparison.Ordinal))
        {
            return Employee(PeoplePermissionCodes.Edit);
        }

        if (normalizedPath.StartsWith("/employees/delete", StringComparison.Ordinal))
        {
            return Employee(PeoplePermissionCodes.Delete);
        }

        if (normalizedPath.StartsWith("/employees/import", StringComparison.Ordinal))
        {
            return Global(PeoplePermissionCodes.Import);
        }

        if (normalizedPath.StartsWith("/employees/endservicelist", StringComparison.Ordinal))
        {
            return DataSet(PeoplePermissionCodes.ViewLifecycle);
        }

        if (normalizedPath.StartsWith("/employees/endservice", StringComparison.Ordinal))
        {
            return Employee(PeoplePermissionCodes.EndService);
        }

        if (normalizedPath.StartsWith("/employees/rehire", StringComparison.Ordinal))
        {
            return Employee(PeoplePermissionCodes.Rehire);
        }

        if (normalizedPath.StartsWith("/employees/lifecycle", StringComparison.Ordinal))
        {
            return Employee(PeoplePermissionCodes.ViewLifecycle);
        }

        if (normalizedPath.StartsWith("/employees/profile", StringComparison.Ordinal))
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                var handler = context.Request.Query["handler"].ToString();

                if (handler.Equals("ReassignFromModal", StringComparison.OrdinalIgnoreCase) ||
                    handler.Equals("ReassignFromModalV2", StringComparison.OrdinalIgnoreCase))
                {
                    return Employee(PeoplePermissionCodes.ChangeAssignment);
                }

                if (handler.Equals("UploadProfileAreaFile", StringComparison.OrdinalIgnoreCase))
                {
                    return Employee(PeoplePermissionCodes.UploadDocument);
                }

                if (handler.Equals("DeleteProfileAreaFile", StringComparison.OrdinalIgnoreCase))
                {
                    return Employee(PeoplePermissionCodes.DeleteDocument);
                }

                return Employee(PeoplePermissionCodes.Edit);
            }

            return Employee(PeoplePermissionCodes.ViewProfile);
        }

        if (normalizedPath.StartsWith("/employeefile", StringComparison.Ordinal))
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                var handler = context.Request.Query["handler"].ToString();

                if (handler.Equals("UploadDocument", StringComparison.OrdinalIgnoreCase))
                {
                    return Employee(PeoplePermissionCodes.UploadDocument);
                }

                return Employee(PeoplePermissionCodes.Edit);
            }

            return Employee(PeoplePermissionCodes.ViewProfile);
        }

        if (normalizedPath.StartsWith("/employeepermissions", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("/userpermissions", StringComparison.Ordinal))
        {
            return Global(PeoplePermissionCodes.ManagePermissions);
        }


        return null;
    }

    private static PeopleRoutePermissionRequirement DataSet(string permissionCode) =>
        new(permissionCode, PeoplePermissionScopeMode.DataSet);

    private static PeopleRoutePermissionRequirement Employee(string permissionCode) =>
        new(permissionCode, PeoplePermissionScopeMode.Employee);

    private static PeopleRoutePermissionRequirement Global(string permissionCode) =>
        new(permissionCode, PeoplePermissionScopeMode.Global);
}
