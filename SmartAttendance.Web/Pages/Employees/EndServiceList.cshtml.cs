using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

public class EndServiceListModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public EndServiceListModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string? SearchTerm { get; set; }
    public string? EndServiceType { get; set; }
    public string? ClearanceStatus { get; set; }
    public List<EndServiceRow> Records { get; set; } = new();
    public int TotalRecords { get; set; }
    public int CompletedClearance { get; set; }
    public int PendingClearance { get; set; }
    public int CurrentMonthRecords { get; set; }

    public async Task OnGetAsync(string? searchTerm, string? endServiceType, string? clearanceStatus)
    {
        SearchTerm = searchTerm?.Trim();
        EndServiceType = endServiceType?.Trim();
        ClearanceStatus = clearanceStatus?.Trim();

        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await EnsureSchemaAsync();
        await LoadKpisAsync();
        await LoadRecordsAsync();
    }

    private async Task LoadKpisAsync()
    {
        TotalRecords = await HrmsDatabase.ScalarAsync<int>(_dbContext, @"SELECT COUNT(*) FROM EmployeeEndServices;");
        CompletedClearance = await HrmsDatabase.ScalarAsync<int>(_dbContext, @"SELECT COUNT(*) FROM EmployeeEndServices WHERE ClearanceStatus = 'Completed';");
        PendingClearance = await HrmsDatabase.ScalarAsync<int>(_dbContext, @"SELECT COUNT(*) FROM EmployeeEndServices WHERE ISNULL(ClearanceStatus,'') <> 'Completed';");
        CurrentMonthRecords = await HrmsDatabase.ScalarAsync<int>(_dbContext, @"SELECT COUNT(*) FROM EmployeeEndServices WHERE CreatedAt >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);");
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
    ISNULL(d.Name,'') AS DepartmentName,
    ISNULL(b.Name,'') AS BranchName
FROM EmployeeEndServices es
LEFT JOIN Employees e ON es.EmployeeId = e.Id
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
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
                HrmsDatabase.AddParameter(command, "@SearchTerm", SearchTerm ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@EndServiceType", EndServiceType ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@ClearanceStatus", ClearanceStatus ?? string.Empty);
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
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName")
            });
    }

    private async Task EnsureSchemaAsync()
    {
        await HrmsDatabase.ExecuteAsync(_dbContext, @"
IF OBJECT_ID('EmployeeEndServices','U') IS NULL
BEGIN
    CREATE TABLE EmployeeEndServices
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        EmployeeNo nvarchar(80) NULL,
        EmployeeName nvarchar(250) NULL,
        EndServiceType nvarchar(80) NOT NULL,
        EndServiceTypeText nvarchar(150) NULL,
        LastWorkingDate date NOT NULL,
        Reason nvarchar(1000) NOT NULL,
        HrNotes nvarchar(2000) NULL,
        ClearanceAssets bit NOT NULL DEFAULT(0),
        ClearanceDocuments bit NOT NULL DEFAULT(0),
        ClearanceAccommodation bit NOT NULL DEFAULT(0),
        ClearanceDevices bit NOT NULL DEFAULT(0),
        ClearanceBadge bit NOT NULL DEFAULT(0),
        ClearanceFinance bit NOT NULL DEFAULT(0),
        ClearanceStatus nvarchar(80) NULL,
        CreatedBy nvarchar(200) NULL,
        IpAddress nvarchar(80) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(GETDATE())
    );
END;");
    }

    public string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
    public string DisplayDate(DateOnly? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";
    public string DisplayDateTime(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : "-";

    public string TypeText(string? value, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
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

    public string ClearanceText(string? value) => value == "Completed" ? "مكتملة" : "معلقة";
    public string ClearanceClass(string? value) => value == "Completed" ? "ok" : "warn";

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
        public string DepartmentName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
    }
}

