using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

public class LifecycleModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public LifecycleModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public int Id { get; set; }

    public EmployeeLifecycleCard? Employee { get; set; }

    public List<EmployeeTimelineRow> TimelineRows { get; set; } = new();

    public int EndServiceCount { get; set; }

    public int RehireCount { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(int id)
    {
        Id = id;

        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await EmployeeLifecycleSchema.EnsureAsync(_dbContext);

        Employee = await LoadEmployeeAsync(id);

        if (Employee == null)
        {
            ErrorMessage = "لم يتم العثور على الموظف المطلوب.";
            return;
        }

        await LoadTimelineAsync(id);

        TimelineRows = TimelineRows
            .OrderBy(row => row.EventDate ?? DateTime.MinValue)
            .ThenBy(row => row.CreatedAt ?? DateTime.MinValue)
            .ToList();
    }

    private async Task<EmployeeLifecycleCard?> LoadEmployeeAsync(int employeeId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 1
    e.Id,
    e.EmployeeNo,
    e.FullName,
    e.HireDate,
    e.IsActive,
    ISNULL(e.Position, '') AS Position,
    ISNULL(e.EmploymentStatus, '') AS EmploymentStatus,
    e.ServiceEndDate,
    e.LastRehireDate,
    ISNULL(e.RehireCount, 0) AS RehireCount,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(c.Name, '') AS CompanyName
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON e.BranchId = b.Id
LEFT JOIN Companies c ON b.CompanyId = c.Id
WHERE e.Id = @EmployeeId;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeeLifecycleCard
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                ServiceEndDate = HrmsDatabase.GetDateOnly(reader, "ServiceEndDate"),
                LastRehireDate = HrmsDatabase.GetDateOnly(reader, "LastRehireDate"),
                RehireCount = HrmsDatabase.GetInt(reader, "RehireCount"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                CompanyName = HrmsDatabase.GetString(reader, "CompanyName")
            });

        return rows.FirstOrDefault();
    }

    private async Task LoadTimelineAsync(int employeeId)
    {
        if (Employee?.HireDate != null)
        {
            TimelineRows.Add(new EmployeeTimelineRow
            {
                EventType = "Hire",
                EventClass = "hire",
                Title = "التعيين الأول",
                Subtitle = "بداية خدمة الموظف في النظام",
                EventDate = Employee.HireDate.Value.ToDateTime(TimeOnly.MinValue),
                Details = "تم إنشاء الموظف أو تسجيل تاريخ التعيين الأساسي.",
                CreatedBy = "-",
                CreatedAt = null,
                BadgeText = "تعيين"
            });
        }

        var endServices = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT
    Id,
    ISNULL(EndServiceType, '') AS EndServiceType,
    ISNULL(EndServiceTypeText, '') AS EndServiceTypeText,
    LastWorkingDate,
    ISNULL(Reason, '') AS Reason,
    ISNULL(HrNotes, '') AS HrNotes,
    ISNULL(ClearanceStatus, '') AS ClearanceStatus,
    ISNULL(CreatedBy, '') AS CreatedBy,
    CreatedAt
FROM EmployeeEndServices
WHERE EmployeeId = @EmployeeId
ORDER BY CreatedAt ASC;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeeEndServiceTimelineRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EndServiceType = HrmsDatabase.GetString(reader, "EndServiceType"),
                EndServiceTypeText = HrmsDatabase.GetString(reader, "EndServiceTypeText"),
                LastWorkingDate = HrmsDatabase.GetDateOnly(reader, "LastWorkingDate"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                HrNotes = HrmsDatabase.GetString(reader, "HrNotes"),
                ClearanceStatus = HrmsDatabase.GetString(reader, "ClearanceStatus"),
                CreatedBy = HrmsDatabase.GetString(reader, "CreatedBy"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });

        foreach (var row in endServices)
        {
            TimelineRows.Add(new EmployeeTimelineRow
            {
                EventType = "EndService",
                EventClass = "end",
                Title = "إنهاء خدمة",
                Subtitle = EndServiceTypeTextDisplay(row.EndServiceType, row.EndServiceTypeText),
                EventDate = row.LastWorkingDate?.ToDateTime(TimeOnly.MinValue) ?? row.CreatedAt,
                Details = row.Reason,
                Notes = row.HrNotes,
                CreatedBy = row.CreatedBy,
                CreatedAt = row.CreatedAt,
                BadgeText = ClearanceStatusText(row.ClearanceStatus),
                BadgeClass = ClearanceStatusClass(row.ClearanceStatus)
            });
        }

        var rehires = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT
    Id,
    PreviousHireDate,
    RehireDate,
    ISNULL(PreviousEmploymentStatus, '') AS PreviousEmploymentStatus,
    ISNULL(Reason, '') AS Reason,
    ISNULL(HrNotes, '') AS HrNotes,
    ISNULL(CreatedBy, '') AS CreatedBy,
    CreatedAt
FROM EmployeeRehires
WHERE EmployeeId = @EmployeeId
ORDER BY CreatedAt ASC;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeeRehireTimelineRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                PreviousHireDate = HrmsDatabase.GetDateOnly(reader, "PreviousHireDate"),
                RehireDate = HrmsDatabase.GetDateOnly(reader, "RehireDate"),
                PreviousEmploymentStatus = HrmsDatabase.GetString(reader, "PreviousEmploymentStatus"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                HrNotes = HrmsDatabase.GetString(reader, "HrNotes"),
                CreatedBy = HrmsDatabase.GetString(reader, "CreatedBy"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });

        foreach (var row in rehires)
        {
            TimelineRows.Add(new EmployeeTimelineRow
            {
                EventType = "Rehire",
                EventClass = "rehire",
                Title = "إعادة تعيين",
                Subtitle = "عودة الموظف بنفس الكود والبيانات",
                EventDate = row.RehireDate?.ToDateTime(TimeOnly.MinValue) ?? row.CreatedAt,
                Details = row.Reason,
                Notes = row.HrNotes,
                CreatedBy = row.CreatedBy,
                CreatedAt = row.CreatedAt,
                BadgeText = "مباشرة جديدة",
                BadgeClass = "ok",
                ExtraInfo = "تاريخ التعيين السابق: " + DisplayDate(row.PreviousHireDate)
            });
        }

        EndServiceCount = endServices.Count;
        RehireCount = rehires.Count;
    }

    public string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    public string DisplayDate(DateOnly? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";
    }

    public string DisplayDate(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";
    }

    public string DisplayDateTime(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : "-";
    }

    public string EndServiceTypeTextDisplay(string? value, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return value switch
        {
            "Resignation" => "استقالة",
            "ContractEnd" => "انتهاء عقد",
            "Termination" => "فصل",
            "NonRenewal" => "عدم تجديد عقد",
            "JobAbandonment" => "ترك عمل",
            "Death" => "وفاة",
            "Other" => "أخرى",
            _ => "-"
        };
    }

    public string ClearanceStatusText(string? value)
    {
        return value switch
        {
            "Completed" => "تصفية مكتملة",
            "Pending" => "تصفية معلقة",
            _ => "تصفية معلقة"
        };
    }

    public string ClearanceStatusClass(string? value)
    {
        return value switch
        {
            "Completed" => "ok",
            _ => "warn"
        };
    }

    public class EmployeeLifecycleCard
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public DateOnly? HireDate { get; set; }

        public bool IsActive { get; set; }

        public string Position { get; set; } = string.Empty;

        public string EmploymentStatus { get; set; } = string.Empty;

        public DateOnly? ServiceEndDate { get; set; }

        public DateOnly? LastRehireDate { get; set; }

        public int RehireCount { get; set; }

        public string DepartmentName { get; set; } = string.Empty;

        public string BranchName { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;
    }

    public class EmployeeTimelineRow
    {
        public string EventType { get; set; } = string.Empty;

        public string EventClass { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Subtitle { get; set; } = string.Empty;

        public DateTime? EventDate { get; set; }

        public string Details { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        public string CreatedBy { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }

        public string BadgeText { get; set; } = string.Empty;

        public string BadgeClass { get; set; } = string.Empty;

        public string ExtraInfo { get; set; } = string.Empty;
    }

    private class EmployeeEndServiceTimelineRow
    {
        public int Id { get; set; }

        public string EndServiceType { get; set; } = string.Empty;

        public string EndServiceTypeText { get; set; } = string.Empty;

        public DateOnly? LastWorkingDate { get; set; }

        public string Reason { get; set; } = string.Empty;

        public string HrNotes { get; set; } = string.Empty;

        public string ClearanceStatus { get; set; } = string.Empty;

        public string CreatedBy { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }
    }

    private class EmployeeRehireTimelineRow
    {
        public int Id { get; set; }

        public DateOnly? PreviousHireDate { get; set; }

        public DateOnly? RehireDate { get; set; }

        public string PreviousEmploymentStatus { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public string HrNotes { get; set; } = string.Empty;

        public string CreatedBy { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }
    }
}

