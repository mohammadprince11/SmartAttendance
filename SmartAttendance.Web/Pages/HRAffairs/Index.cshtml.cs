using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HRAffairs;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public int ActiveEmployees { get; set; }
    public int InactiveEmployees { get; set; }
    public int MissingStructure { get; set; }
    public int AttendanceExceptions { get; set; }
    public int PendingRequests { get; set; }
    public int ExpiredDocuments { get; set; }
    public int ExpiringDocuments { get; set; }
    public int ContractRisk { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        ActiveEmployees = await CountAsync("SELECT COUNT(*) FROM Employees WHERE IsActive = 1");
        InactiveEmployees = await CountAsync("SELECT COUNT(*) FROM Employees WHERE IsActive = 0");
        MissingStructure = await CountAsync("SELECT COUNT(*) FROM Employees WHERE IsActive = 1 AND (DepartmentId IS NULL OR LTRIM(RTRIM(ISNULL(Position, ''))) = '')");
        AttendanceExceptions = await CountAsync("""
SELECT COUNT(*)
FROM AttendanceRecords
WHERE AttendanceDate >= DATEADD(day, -30, CAST(GETDATE() AS date))
  AND (Status IN (2,3) OR CheckIn IS NULL OR CheckOut IS NULL OR ISNULL(Notes, '') <> '')
""");
        PendingRequests = await CountAsync("SELECT COUNT(*) FROM SelfServiceRequests WHERE Status = 'Pending'");
        ExpiredDocuments = await CountAsync("SELECT COUNT(*) FROM EmployeeDocuments WHERE ExpiryDate IS NOT NULL AND ExpiryDate < CAST(GETDATE() AS date)");
        ExpiringDocuments = await CountAsync("SELECT COUNT(*) FROM EmployeeDocuments WHERE ExpiryDate IS NOT NULL AND ExpiryDate >= CAST(GETDATE() AS date) AND ExpiryDate <= DATEADD(day, 30, CAST(GETDATE() AS date))");
        ContractRisk = await CountAsync("SELECT COUNT(*) FROM Employees WHERE IsActive = 1 AND ContractEndDate IS NOT NULL AND ContractEndDate <= DATEADD(day, 30, CAST(GETDATE() AS date))");
    }

    private async Task<int> CountAsync(string sql)
    {
        return await HrmsDatabase.ScalarAsync<int>(_dbContext, sql);
    }
}
