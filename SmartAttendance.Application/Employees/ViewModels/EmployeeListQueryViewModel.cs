using SmartAttendance.Application.Common.Security;

namespace SmartAttendance.Application.Employees.ViewModels;

public class EmployeeListQueryViewModel
{
    public PeopleDataScope? DataScope { get; set; }
    public int? CompanyId { get; set; }

    public string? SearchTerm { get; set; }

    public int? BranchId { get; set; }

    public int? DepartmentId { get; set; }

    public string StatusFilter { get; set; } = "active";

    public string SortBy { get; set; } = "name";

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}
