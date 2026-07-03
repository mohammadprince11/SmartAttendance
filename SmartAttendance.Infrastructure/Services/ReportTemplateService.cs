using System.Text.Json;
using SmartAttendance.Application.ReportBuilder.Services;
using SmartAttendance.Application.ReportBuilder.ViewModels;

namespace SmartAttendance.Infrastructure.Services;

public class ReportTemplateService : IReportTemplateService
{
    private readonly string _filePath;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public ReportTemplateService()
    {
        var folderPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "App_Data",
            "ReportTemplates");

        Directory.CreateDirectory(folderPath);

        _filePath = Path.Combine(folderPath, "report-templates.json");
    }

    public async Task<List<ReportTemplateViewModel>> GetAllAsync()
    {
        var userTemplates = await ReadUserTemplatesAsync();
        var systemTemplates = GetSystemTemplates();

        return systemTemplates
            .Concat(userTemplates)
            .OrderByDescending(x => x.IsSystem)
            .ThenBy(x => x.ReportType)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public async Task<ReportTemplateViewModel?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var systemTemplate = GetSystemTemplates()
            .FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (systemTemplate != null)
            return Clone(systemTemplate);

        var userTemplates = await ReadUserTemplatesAsync();

        return userTemplates.FirstOrDefault(x =>
            x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ReportTemplateViewModel> SaveAsync(ReportTemplateViewModel template)
    {
        var userTemplates = await ReadUserTemplatesAsync();

        if (template.IsSystem || template.Id.StartsWith("system:", StringComparison.OrdinalIgnoreCase))
        {
            template.Id = string.Empty;
            template.IsSystem = false;
        }

        if (string.IsNullOrWhiteSpace(template.Id))
        {
            template.Id = Guid.NewGuid().ToString("N");
            template.CreatedAt = DateTime.Now;
        }

        template.IsSystem = false;
        template.UpdatedAt = DateTime.Now;

        var existingIndex = userTemplates.FindIndex(x =>
            x.Id.Equals(template.Id, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            template.CreatedAt = userTemplates[existingIndex].CreatedAt;
            userTemplates[existingIndex] = template;
        }
        else
        {
            userTemplates.Add(template);
        }

        await WriteUserTemplatesAsync(userTemplates);

        return template;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        if (id.StartsWith("system:", StringComparison.OrdinalIgnoreCase))
            return false;

        var userTemplates = await ReadUserTemplatesAsync();

        var removed = userTemplates.RemoveAll(x =>
            x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
            return false;

        await WriteUserTemplatesAsync(userTemplates);

        return true;
    }

    private async Task<List<ReportTemplateViewModel>> ReadUserTemplatesAsync()
    {
        if (!File.Exists(_filePath))
            return new List<ReportTemplateViewModel>();

        var json = await File.ReadAllTextAsync(_filePath);

        if (string.IsNullOrWhiteSpace(json))
            return new List<ReportTemplateViewModel>();

        return JsonSerializer.Deserialize<List<ReportTemplateViewModel>>(json, _jsonOptions)
               ?? new List<ReportTemplateViewModel>();
    }

    private async Task WriteUserTemplatesAsync(List<ReportTemplateViewModel> templates)
    {
        var json = JsonSerializer.Serialize(
            templates.Where(x => !x.IsSystem).ToList(),
            _jsonOptions);

        await File.WriteAllTextAsync(_filePath, json);
    }

    private static ReportTemplateViewModel Clone(ReportTemplateViewModel template)
    {
        return new ReportTemplateViewModel
        {
            Id = template.Id,
            Name = template.Name,
            ReportType = template.ReportType,
            FromDate = template.FromDate,
            ToDate = template.ToDate,
            SearchTerm = template.SearchTerm,
            BranchId = template.BranchId,
            DepartmentId = template.DepartmentId,
            ShiftId = template.ShiftId,
            StatusFilter = template.StatusFilter,
            ActiveFilter = template.ActiveFilter,
            SelectedColumns = template.SelectedColumns.ToList(),
            IsSystem = template.IsSystem,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }

    private static List<ReportTemplateViewModel> GetSystemTemplates()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        return new List<ReportTemplateViewModel>
        {
            new()
            {
                Id = "system:employee-basic",
                Name = "Employee Basic Report",
                ReportType = "Employees",
                ActiveFilter = "Active",
                SelectedColumns = new()
                {
                    "EmployeeNo",
                    "EmployeeName",
                    "Branch",
                    "Department",
                    "HireDate",
                    "IsActive"
                },
                IsSystem = true
            },
            new()
            {
                Id = "system:employee-contact-list",
                Name = "Employee Contact List",
                ReportType = "Employees",
                ActiveFilter = "Active",
                SelectedColumns = new()
                {
                    "EmployeeNo",
                    "EmployeeName",
                    "Branch",
                    "Department",
                    "Phone",
                    "Email"
                },
                IsSystem = true
            },
            new()
            {
                Id = "system:new-joiners",
                Name = "New Joiners Report",
                ReportType = "Employees",
                ActiveFilter = "Active",
                SelectedColumns = new()
                {
                    "EmployeeNo",
                    "EmployeeName",
                    "Branch",
                    "Department",
                    "HireDate",
                    "Phone"
                },
                IsSystem = true
            },
            new()
            {
                Id = "system:daily-attendance",
                Name = "Daily Attendance Report",
                ReportType = "Attendance",
                FromDate = today,
                ToDate = today,
                SelectedColumns = new()
                {
                    "Date",
                    "EmployeeNo",
                    "EmployeeName",
                    "Branch",
                    "Department",
                    "Shift",
                    "CheckIn",
                    "CheckOut",
                    "WorkHours",
                    "CalculatedStatus"
                },
                IsSystem = true
            },
            new()
            {
                Id = "system:late-report",
                Name = "Late Employees Report",
                ReportType = "Attendance",
                FromDate = today,
                ToDate = today,
                StatusFilter = "Late",
                SelectedColumns = new()
                {
                    "Date",
                    "EmployeeNo",
                    "EmployeeName",
                    "Branch",
                    "Department",
                    "Shift",
                    "CheckIn",
                    "LateMinutes",
                    "LateHours",
                    "CalculatedStatus"
                },
                IsSystem = true
            },
            new()
            {
                Id = "system:absent-report",
                Name = "Absent Report",
                ReportType = "Attendance",
                FromDate = today,
                ToDate = today,
                StatusFilter = "Absent",
                SelectedColumns = new()
                {
                    "Date",
                    "EmployeeNo",
                    "EmployeeName",
                    "Branch",
                    "Department",
                    "Shift",
                    "CalculatedStatus"
                },
                IsSystem = true
            },
            new()
            {
                Id = "system:missing-checkout",
                Name = "Missing Check Out Report",
                ReportType = "Attendance",
                FromDate = today,
                ToDate = today,
                StatusFilter = "Missing Check Out",
                SelectedColumns = new()
                {
                    "Date",
                    "EmployeeNo",
                    "EmployeeName",
                    "Branch",
                    "Department",
                    "Shift",
                    "CheckIn",
                    "CheckOut",
                    "CalculatedStatus"
                },
                IsSystem = true
            },
            new()
            {
                Id = "system:work-on-weekly-off",
                Name = "Work On Weekly Off Report",
                ReportType = "Attendance",
                FromDate = today,
                ToDate = today,
                StatusFilter = "Work On Weekly Off",
                SelectedColumns = new()
                {
                    "Date",
                    "EmployeeNo",
                    "EmployeeName",
                    "Branch",
                    "Department",
                    "Shift",
                    "WeeklyOff",
                    "CheckIn",
                    "CheckOut",
                    "WorkHours",
                    "CalculatedStatus"
                },
                IsSystem = true
            },
            new()
            {
                Id = "system:payroll-attendance-summary",
                Name = "Payroll Attendance Summary",
                ReportType = "Attendance",
                FromDate = today,
                ToDate = today,
                SelectedColumns = new()
                {
                    "Date",
                    "EmployeeNo",
                    "EmployeeName",
                    "Branch",
                    "Department",
                    "WorkHours",
                    "LateMinutes",
                    "LateHours",
                    "EarlyLeaveMinutes",
                    "EarlyLeaveHours",
                    "CalculatedStatus"
                },
                IsSystem = true
            }
        };
    }
}
