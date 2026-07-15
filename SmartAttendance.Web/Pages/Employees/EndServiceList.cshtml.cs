using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

public class EndServiceListModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;
    private string _accessibleEmployeeIdsJson = "[]";
    private PeopleDataScope _profileScope = PeopleDataScope.Empty();

    public EndServiceListModel(
        ApplicationDbContext dbContext,
        IPermissionAuthorizationService permissionAuthorizationService)
    {
        _dbContext = dbContext;
        _permissionAuthorizationService = permissionAuthorizationService;
    }

    public string? SearchTerm { get; set; }
    public string? EndServiceType { get; set; }
    public string? ClearanceStatus { get; set; }
    public List<EndServiceRow> Records { get; set; } = new();
    public int TotalRecords { get; set; }
    public int CompletedClearance { get; set; }
    public int PendingClearance { get; set; }
    public int CurrentMonthRecords { get; set; }
    public bool CanViewDirectory { get; set; }

    public async Task OnGetAsync(
        string? searchTerm,
        string? endServiceType,
        string? clearanceStatus)
    {
        SearchTerm = searchTerm?.Trim();
        EndServiceType = endServiceType?.Trim();
        ClearanceStatus = clearanceStatus?.Trim();

        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await EmployeeLifecycleSchema.EnsureAsync(_dbContext);
        await LoadAccessibleEmployeeIdsAsync();
        await LoadKpisAsync();
        await LoadRecordsAsync();
    }

    private async Task LoadAccessibleEmployeeIdsAsync()
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        var scope = await _permissionAuthorizationService.GetPeopleDataScopeAsync(
            systemUserId,
            PeoplePermissionCodes.ViewLifecycle,
            PeopleCompatibilityAccess.IsAllowed(
                role,
                PeoplePermissionCodes.ViewLifecycle),
            HttpContext.RequestAborted);

        _profileScope = await _permissionAuthorizationService.GetPeopleDataScopeAsync(
            systemUserId,
            PeoplePermissionCodes.ViewProfile,
            PeopleCompatibilityAccess.IsAllowed(
                role,
                PeoplePermissionCodes.ViewProfile),
            HttpContext.RequestAborted);

        CanViewDirectory = await _permissionAuthorizationService.HasPermissionAsync(
            systemUserId,
            PeoplePermissionCodes.ViewDirectory,
            PeopleCompatibilityAccess.IsAllowed(
                role,
                PeoplePermissionCodes.ViewDirectory),
            HttpContext.RequestAborted);

        var employeeIds = await _dbContext.Employees
            .AsNoTracking()
            .Where(employee => !employee.IsDeleted)
            .ApplyPeopleDataScope(scope)
            .Select(employee => employee.Id)
            .ToListAsync(HttpContext.RequestAborted);

        _accessibleEmployeeIdsJson = JsonSerializer.Serialize(employeeIds);
    }

    private async Task LoadKpisAsync()
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT
    COUNT(*) AS TotalRecords,
    SUM(CASE WHEN es.ClearanceStatus = 'Completed' THEN 1 ELSE 0 END) AS CompletedClearance,
    SUM(CASE WHEN ISNULL(es.ClearanceStatus,'') <> 'Completed' THEN 1 ELSE 0 END) AS PendingClearance,
    SUM(CASE WHEN es.CreatedAt >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1) THEN 1 ELSE 0 END) AS CurrentMonthRecords
FROM EmployeeEndServices es
INNER JOIN OPENJSON(@AccessibleEmployeeIdsJson)
    WITH (EmployeeId int '$') allowed
    ON allowed.EmployeeId = es.EmployeeId;",
            command => HrmsDatabase.AddParameter(
                command,
                "@AccessibleEmployeeIdsJson",
                _accessibleEmployeeIdsJson),
            reader => new
            {
                TotalRecords = HrmsDatabase.GetInt(reader, "TotalRecords"),
                CompletedClearance = HrmsDatabase.GetInt(reader, "CompletedClearance"),
                PendingClearance = HrmsDatabase.GetInt(reader, "PendingClearance"),
                CurrentMonthRecords = HrmsDatabase.GetInt(reader, "CurrentMonthRecords")
            });

        var summary = rows.FirstOrDefault();
        TotalRecords = summary?.TotalRecords ?? 0;
        CompletedClearance = summary?.CompletedClearance ?? 0;
        PendingClearance = summary?.PendingClearance ?? 0;
        CurrentMonthRecords = summary?.CurrentMonthRecords ?? 0;
    }

    private async Task LoadRecordsAsync()
    {
        Records = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 300
    es.Id,
    es.EmployeeId,
    ISNULL(es.EmployeeNo,'') AS EmployeeNo,
    ISNULL(es.EmployeeName,'') AS EmployeeName,
    ISNULL(es.EndServiceType,'') AS EndServiceType,
    ISNULL(es.EndServiceTypeText,'') AS EndServiceTypeText,
    es.LastWorkingDate,
    ISNULL(es.Reason,'') AS Reason,
    ISNULL(es.ClearanceStatus,'') AS ClearanceStatus,
    ISNULL(es.CreatedBy,'') AS CreatedBy,
    es.CreatedAt,
    ISNULL(e.Position,'') AS Position,
    ISNULL(e.BranchId, 0) AS BranchId,
    ISNULL(e.DepartmentId, 0) AS DepartmentId,
    ISNULL(b.CompanyId, 0) AS CompanyId,
    ISNULL(d.Name,'') AS DepartmentName,
    ISNULL(b.Name,'') AS BranchName
FROM EmployeeEndServices es
INNER JOIN OPENJSON(@AccessibleEmployeeIdsJson)
    WITH (EmployeeId int '$') allowed
    ON allowed.EmployeeId = es.EmployeeId
LEFT JOIN Employees e ON es.EmployeeId = e.Id
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON e.BranchId = b.Id
WHERE
    (@SearchTerm = ''
        OR es.EmployeeNo LIKE '%' + @SearchTerm + '%'
        OR es.EmployeeName LIKE '%' + @SearchTerm + '%'
        OR es.Reason LIKE '%' + @SearchTerm + '%'
        OR ISNULL(e.Position,'') LIKE '%' + @SearchTerm + '%'
        OR ISNULL(d.Name,'') LIKE '%' + @SearchTerm + '%'
        OR ISNULL(b.Name,'') LIKE '%' + @SearchTerm + '%')
    AND (@EndServiceType = '' OR es.EndServiceType = @EndServiceType)
    AND (@ClearanceStatus = '' OR ISNULL(es.ClearanceStatus,'') = @ClearanceStatus)
ORDER BY es.CreatedAt DESC;",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@AccessibleEmployeeIdsJson",
                    _accessibleEmployeeIdsJson);
                HrmsDatabase.AddParameter(
                    command,
                    "@SearchTerm",
                    SearchTerm ?? string.Empty);
                HrmsDatabase.AddParameter(
                    command,
                    "@EndServiceType",
                    EndServiceType ?? string.Empty);
                HrmsDatabase.AddParameter(
                    command,
                    "@ClearanceStatus",
                    ClearanceStatus ?? string.Empty);
            },
            reader => new EndServiceRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "EmployeeName"),
                EndServiceType = HrmsDatabase.GetString(reader, "EndServiceType"),
                EndServiceTypeText = HrmsDatabase.GetString(reader, "EndServiceTypeText"),
                LastWorkingDate = HrmsDatabase.GetDateOnly(reader, "LastWorkingDate"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                ClearanceStatus = HrmsDatabase.GetString(reader, "ClearanceStatus"),
                CreatedBy = HrmsDatabase.GetString(reader, "CreatedBy"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName")
            });

        foreach (var record in Records)
        {
            record.CanViewProfile = _profileScope.AllowsEmployee(
                record.EmployeeId,
                record.CompanyId,
                record.BranchId,
                record.DepartmentId);
        }
    }

    public string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;

    public string DisplayDate(DateOnly? value) =>
        value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";

    public string DisplayDateTime(DateTime? value) =>
        value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : "-";

    public string TypeText(string? value, string? fallback)
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

    public string ClearanceText(string? value) =>
        value == "Completed" ? "مكتملة" : "معلقة";

    public string ClearanceClass(string? value) =>
        value == "Completed" ? "ok" : "warn";

    public class EndServiceRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string EndServiceType { get; set; } = string.Empty;
        public string EndServiceTypeText { get; set; } = string.Empty;
        public DateOnly? LastWorkingDate { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string ClearanceStatus { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public string Position { get; set; } = string.Empty;
        public int CompanyId { get; set; }
        public int BranchId { get; set; }
        public int DepartmentId { get; set; }
        public bool CanViewProfile { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
    }
}
