namespace SmartAttendance.Application.Setup.ViewModels;

public class SystemSetupViewModel
{
    public int CompaniesCount { get; set; }
    public int BranchesCount { get; set; }
    public int DepartmentsCount { get; set; }
    public int EmployeesCount { get; set; }
    public int ActiveEmployeesCount { get; set; }
    public int DevicesCount { get; set; }
    public int ShiftsCount { get; set; }
    public int EmployeeShiftsCount { get; set; }
    public int EmployeesWithoutCurrentShiftCount { get; set; }
    public int AttendanceRecordsCount { get; set; }
    public int HolidaysCount { get; set; }
    public int ApprovedLeavesCount { get; set; }

    public bool IsCompaniesReady => CompaniesCount > 0;
    public bool IsBranchesReady => BranchesCount > 0;
    public bool IsDepartmentsReady => DepartmentsCount > 0;
    public bool IsEmployeesReady => EmployeesCount > 0;
    public bool IsShiftsReady => ShiftsCount > 0;
    public bool IsEmployeeShiftsReady => ActiveEmployeesCount > 0 && EmployeesWithoutCurrentShiftCount == 0;
    public bool IsDevicesReady => DevicesCount > 0;

    public List<SetupStepViewModel> Steps { get; set; } = new();

    public List<SetupDropdownItemViewModel> Shifts { get; set; } = new();
}

public class SetupStepViewModel
{
    public int Order { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string LinkText { get; set; } = string.Empty;

    public string LinkUrl { get; set; } = string.Empty;
}

public class SetupDropdownItemViewModel
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;
}
