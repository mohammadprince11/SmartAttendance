using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class PeopleTargetEmployeeResolver
{
    private static readonly string[] IdKeys =
    {
        "id",
        "Id",
        "employeeId",
        "EmployeeId",
        "Employee.Id",
        "Input.Id",
        "Input.EmployeeId",
        "Document.EmployeeId"
    };

    private static readonly string[] EmployeeNoKeys =
    {
        "employeeNo",
        "EmployeeNo",
        "Employee.EmployeeNo",
        "Input.EmployeeNo",
        "Document.EmployeeNo"
    };

    public static async Task<int?> ResolveAsync(
        HttpContext context,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dbContext);

        var employeeId = ResolveFromRouteValues(context.Request.RouteValues);

        if (employeeId.HasValue)
        {
            return employeeId;
        }

        employeeId = ResolveFromValues(
            key => context.Request.Query[key].ToString());

        if (employeeId.HasValue)
        {
            return employeeId;
        }

        var employeeNo = ResolveEmployeeNo(
            key => context.Request.Query[key].ToString());

        if (context.Request.HasFormContentType)
        {
            try
            {
                var form = await context.Request.ReadFormAsync(cancellationToken);

                employeeId ??= ResolveFromValues(
                    key => form[key].ToString());

                employeeNo ??= ResolveEmployeeNo(
                    key => form[key].ToString());
            }
            catch (InvalidDataException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        if (employeeId.HasValue)
        {
            return employeeId;
        }

        if (string.IsNullOrWhiteSpace(employeeNo))
        {
            return null;
        }

        var normalizedEmployeeNo = employeeNo.Trim();

        var resolvedId = await dbContext.Employees
            .AsNoTracking()
            .Where(employee =>
                !employee.IsDeleted &&
                employee.EmployeeNo == normalizedEmployeeNo)
            .Select(employee => employee.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return resolvedId > 0 ? resolvedId : null;
    }

    private static int? ResolveFromRouteValues(RouteValueDictionary values)
    {
        foreach (var key in IdKeys)
        {
            if (values.TryGetValue(key, out var value) &&
                TryParsePositiveId(Convert.ToString(value), out var employeeId))
            {
                return employeeId;
            }
        }

        return null;
    }

    private static int? ResolveFromValues(Func<string, string?> valueAccessor)
    {
        foreach (var key in IdKeys)
        {
            if (TryParsePositiveId(valueAccessor(key), out var employeeId))
            {
                return employeeId;
            }
        }

        return null;
    }

    private static string? ResolveEmployeeNo(Func<string, string?> valueAccessor)
    {
        foreach (var key in EmployeeNoKeys)
        {
            var value = valueAccessor(key);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool TryParsePositiveId(string? value, out int employeeId)
    {
        return int.TryParse(value, out employeeId) && employeeId > 0;
    }
}
