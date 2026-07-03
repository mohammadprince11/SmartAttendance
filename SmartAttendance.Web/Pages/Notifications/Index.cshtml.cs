using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Notifications;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<NotificationRow> Notifications { get; set; } = new();

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostMarkReadAsync(int id)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "UPDATE SystemNotifications SET IsRead = 1 WHERE Id = @Id",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        await LoadAsync();

        return Page();
    }

    private async Task LoadAsync()
    {
        Notifications = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 200
    Id,
    Title,
    ISNULL(Message, '') AS Message,
    ISNULL(TargetRole, '') AS TargetRole,
    ISNULL(Url, '') AS Url,
    IsRead,
    CreatedAt
FROM SystemNotifications
ORDER BY CreatedAt DESC;
""",
            null,
            reader => new NotificationRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Message = HrmsDatabase.GetString(reader, "Message"),
                TargetRole = HrmsDatabase.GetString(reader, "TargetRole"),
                Url = HrmsDatabase.GetString(reader, "Url"),
                IsRead = HrmsDatabase.GetBool(reader, "IsRead"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    public class NotificationRow
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string TargetRole { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public bool IsRead { get; set; }

        public DateTime? CreatedAt { get; set; }
    }
}
