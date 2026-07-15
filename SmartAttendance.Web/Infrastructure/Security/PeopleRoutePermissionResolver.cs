using SmartAttendance.Application.Common.Security;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class PeopleRoutePermissionResolver
{
    public static string? Resolve(HttpContext context, string normalizedPath)
    {
        if (normalizedPath == "/employees" || normalizedPath == "/employees/index")
        {
            return PeoplePermissionCodes.ViewDirectory;
        }

        if (normalizedPath.StartsWith("/employees/create", StringComparison.Ordinal))
        {
            return PeoplePermissionCodes.Create;
        }

        if (normalizedPath.StartsWith("/employees/edit", StringComparison.Ordinal))
        {
            return PeoplePermissionCodes.Edit;
        }

        if (normalizedPath.StartsWith("/employees/delete", StringComparison.Ordinal))
        {
            return PeoplePermissionCodes.Delete;
        }

        if (normalizedPath.StartsWith("/employees/import", StringComparison.Ordinal))
        {
            return PeoplePermissionCodes.Import;
        }

        if (normalizedPath.StartsWith("/employees/endservicelist", StringComparison.Ordinal))
        {
            return PeoplePermissionCodes.ViewLifecycle;
        }

        if (normalizedPath.StartsWith("/employees/endservice", StringComparison.Ordinal))
        {
            return PeoplePermissionCodes.EndService;
        }

        if (normalizedPath.StartsWith("/employees/rehire", StringComparison.Ordinal))
        {
            return PeoplePermissionCodes.Rehire;
        }

        if (normalizedPath.StartsWith("/employees/lifecycle", StringComparison.Ordinal))
        {
            return PeoplePermissionCodes.ViewLifecycle;
        }

        if (normalizedPath.StartsWith("/employees/profile", StringComparison.Ordinal))
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                var handler = context.Request.Query["handler"].ToString();

                if (handler.Equals("ReassignFromModal", StringComparison.OrdinalIgnoreCase))
                {
                    return PeoplePermissionCodes.ChangeAssignment;
                }

                return PeoplePermissionCodes.Edit;
            }

            return PeoplePermissionCodes.ViewProfile;
        }

        if (normalizedPath.StartsWith("/employeefile", StringComparison.Ordinal))
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                var handler = context.Request.Query["handler"].ToString();

                if (handler.Equals("UploadDocument", StringComparison.OrdinalIgnoreCase))
                {
                    return PeoplePermissionCodes.UploadDocument;
                }

                return PeoplePermissionCodes.Edit;
            }

            return PeoplePermissionCodes.ViewProfile;
        }

        if (normalizedPath.StartsWith("/employeepermissions", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("/userpermissions", StringComparison.Ordinal))
        {
            return PeoplePermissionCodes.ManagePermissions;
        }

        if (normalizedPath.StartsWith("/employees/", StringComparison.Ordinal))
        {
            return PeoplePermissionCodes.ViewDirectory;
        }

        return null;
    }
}
