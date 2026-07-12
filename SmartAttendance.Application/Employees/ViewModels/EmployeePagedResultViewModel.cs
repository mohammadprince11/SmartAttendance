namespace SmartAttendance.Application.Employees.ViewModels;

public class EmployeePagedResultViewModel
{
    public List<EmployeeListViewModel> Items { get; set; } = new();

    public int TotalEmployees { get; set; }

    public int FilteredEmployees { get; set; }

    public int ActiveEmployees { get; set; }

    public int InactiveEmployees { get; set; }

    public int NewThisYear { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 25;

    public int TotalPages { get; set; }
}
