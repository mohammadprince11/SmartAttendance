using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AuditLogs;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<AuditRow> Logs { get; set; } = new();

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        Logs = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 300
    Id,
    EntityName,
    ISNULL(EntityId, '') AS EntityId,
    Action,
    ISNULL(OldValues, '') AS OldValues,
    ISNULL(NewValues, '') AS NewValues,
    ISNULL(UserName, '') AS UserName,
    ISNULL(IpAddress, '') AS IpAddress,
    CreatedAt
FROM AuditLogs
WHERE
    (@Search IS NULL OR @Search = ''
     OR EntityName LIKE '%' + @Search + '%'
     OR Action LIKE '%' + @Search + '%'
     OR ISNULL(UserName, '') LIKE '%' + @Search + '%'
     OR ISNULL(NewValues, '') LIKE '%' + @Search + '%')
ORDER BY CreatedAt DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@Search", Search),
            reader => new AuditRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EntityName = HrmsDatabase.GetString(reader, "EntityName"),
                EntityId = HrmsDatabase.GetString(reader, "EntityId"),
                Action = HrmsDatabase.GetString(reader, "Action"),
                OldValues = HrmsDatabase.GetString(reader, "OldValues"),
                NewValues = HrmsDatabase.GetString(reader, "NewValues"),
                UserName = HrmsDatabase.GetString(reader, "UserName"),
                IpAddress = HrmsDatabase.GetString(reader, "IpAddress"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    public class AuditRow
    {
        public int Id { get; set; }

        public string EntityName { get; set; } = string.Empty;

        public string EntityId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string OldValues { get; set; } = string.Empty;

        public string NewValues { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string IpAddress { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }
    }
}
