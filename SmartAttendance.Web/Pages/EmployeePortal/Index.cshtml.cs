using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeePortal;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "home";

    [BindProperty]
    public FeedbackInput Feedback { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public EmployeeRow Employee { get; private set; } = EmployeeRow.Empty;
    public CompensationRow Compensation { get; private set; } = new();
    public List<AnnouncementRow> Announcements { get; private set; } = new();
    public List<FeedbackRow> FeedbackItems { get; private set; } = new();
    public List<PollRow> Polls { get; private set; } = new();
    public List<RequestRow> Requests { get; private set; } = new();
    public List<AttendanceRow> Attendance { get; private set; } = new();
    public List<TeamRow> Team { get; private set; } = new();

    public string RoleDisplay => Request.Cookies["SA.Role"] ?? "Employee";
    public string Initials => GetInitials(Employee.FullName);

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostFeedbackAsync()
    {
        await EnsureTablesAsync();
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن الحساب غير مربوط بموظف.";
            return RedirectToPage(new { tab = "feedback" });
        }

        var type = string.IsNullOrWhiteSpace(Feedback.Type) ? "اقتراح" : Feedback.Type.Trim();
        var priority = string.IsNullOrWhiteSpace(Feedback.Priority) ? "متوسط" : Feedback.Priority.Trim();
        var title = Feedback.Title?.Trim() ?? string.Empty;
        var message = Feedback.Message?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            StatusMessage = "يرجى إدخال العنوان والتفاصيل.";
            return RedirectToPage(new { tab = "feedback" });
        }

        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
INSERT INTO EmployeeFeedbackItems
(EmployeeId, Type, Title, Message, Priority, Status, CreatedAt)
VALUES
(@EmployeeId, @Type, @Title, @Message, @Priority, 'Open', SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Type", type);
                HrmsDatabase.AddParameter(command, "@Title", title);
                HrmsDatabase.AddParameter(command, "@Message", message);
                HrmsDatabase.AddParameter(command, "@Priority", priority);
            });

        StatusMessage = "تم إرسال الطلب وسيظهر لمسؤول النظام للرد عليه.";
        return RedirectToPage(new { tab = "feedback" });
    }

    public async Task<IActionResult> OnPostVotePollAsync(int pollId, int optionId)
    {
        await EnsureTablesAsync();
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن التصويت لأن الحساب غير مربوط بموظف.";
            return RedirectToPage(new { tab = "polls" });
        }

        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
IF EXISTS (SELECT 1 FROM EmployeePollVotes WHERE PollId = @PollId AND EmployeeId = @EmployeeId)
BEGIN
    UPDATE EmployeePollVotes
    SET OptionId = @OptionId,
        VotedAt = SYSUTCDATETIME()
    WHERE PollId = @PollId AND EmployeeId = @EmployeeId;
END
ELSE
BEGIN
    INSERT INTO EmployeePollVotes (PollId, OptionId, EmployeeId, VotedAt)
    VALUES (@PollId, @OptionId, @EmployeeId, SYSUTCDATETIME());
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PollId", pollId);
                HrmsDatabase.AddParameter(command, "@OptionId", optionId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });

        StatusMessage = "تم تسجيل التصويت بنجاح.";
        return RedirectToPage(new { tab = "polls" });
    }

    private async Task LoadAsync()
    {
        await EnsureTablesAsync();
        if (string.IsNullOrWhiteSpace(Tab)) Tab = "home";

        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees WHERE IsActive = 1 ORDER BY Id");
        }

        if (employeeId <= 0)
        {
            Employee = EmployeeRow.Empty;
            return;
        }

        Employee = await LoadEmployeeAsync(employeeId) ?? EmployeeRow.Empty;
        Compensation = await LoadCompensationAsync(employeeId);
        Announcements = await LoadAnnouncementsAsync(Employee);
        Polls = await LoadPollsAsync(Employee);
        FeedbackItems = await LoadFeedbackAsync(employeeId);
        Requests = await LoadRequestsAsync(employeeId);
        Attendance = await LoadAttendanceAsync(employeeId);
        Team = await LoadTeamAsync(employeeId);
    }

    private async Task EnsureTablesAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
IF OBJECT_ID('EmployeePortalAnnouncements', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePortalAnnouncements
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title nvarchar(250) NOT NULL,
        Body nvarchar(max) NULL,
        Category nvarchar(80) NULL,
        TargetType nvarchar(50) NULL,
        TargetValue nvarchar(max) NULL,
        IsPublished bit NOT NULL DEFAULT(1),
        PublishDate datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('EmployeeFeedbackItems', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeFeedbackItems
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        Type nvarchar(50) NOT NULL,
        Title nvarchar(250) NOT NULL,
        Message nvarchar(max) NULL,
        Priority nvarchar(50) NULL,
        Status nvarchar(50) NOT NULL DEFAULT('Open'),
        AdminReply nvarchar(max) NULL,
        RepliedBy nvarchar(150) NULL,
        RepliedAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('EmployeeCompensations', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeCompensations
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        BasicSalary decimal(18,2) NULL,
        Allowances decimal(18,2) NULL,
        Deductions decimal(18,2) NULL,
        PaymentMethod nvarchar(80) NULL,
        BankName nvarchar(150) NULL,
        BankAccount nvarchar(150) NULL,
        Currency nvarchar(30) NULL,
        UpdatedAt datetime2 NULL
    );
END;

IF OBJECT_ID('EmployeePolls', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePolls
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title nvarchar(250) NOT NULL,
        Question nvarchar(max) NULL,
        Category nvarchar(80) NULL,
        TargetType nvarchar(50) NULL,
        TargetValue nvarchar(max) NULL,
        IsPublished bit NOT NULL DEFAULT(1),
        PublishDate datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('EmployeePollOptions', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePollOptions
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PollId int NOT NULL,
        OptionText nvarchar(300) NOT NULL,
        DisplayOrder int NOT NULL DEFAULT(1)
    );
END;

IF OBJECT_ID('EmployeePollVotes', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePollVotes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PollId int NOT NULL,
        OptionId int NOT NULL,
        EmployeeId int NOT NULL,
        VotedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_EmployeePollVotes_PollEmployee' AND object_id = OBJECT_ID('EmployeePollVotes'))
BEGIN
    CREATE UNIQUE INDEX UX_EmployeePollVotes_PollEmployee ON EmployeePollVotes(PollId, EmployeeId);
END;

""");
    }

    private async Task<int> ResolveEmployeeIdAsync()
    {
        if (int.TryParse(Request.Cookies["SA.EmployeeId"], out var cookieEmployeeId) && cookieEmployeeId > 0)
        {
            return cookieEmployeeId;
        }

        var username = Request.Cookies["SA.UserName"];
        if (!string.IsNullOrWhiteSpace(username))
        {
            return await HrmsDatabase.ScalarAsync<int>(_dbContext,
                "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @Username AND IsActive = 1",
                command => HrmsDatabase.AddParameter(command, "@Username", username));
        }

        return 0;
    }

    private async Task<EmployeeRow?> LoadEmployeeAsync(int employeeId)
    {
        var list = await HrmsDatabase.QueryAsync(_dbContext,
            """
SELECT TOP 1
    e.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(e.NationalId, '') AS NationalId,
    ISNULL(e.Phone, '') AS Phone,
    ISNULL(e.Email, '') AS Email,
    ISNULL(e.Position, '') AS Position,
    e.HireDate,
    e.BirthDate,
    e.IsActive,
    e.DepartmentId,
    ISNULL(d.BranchId, 0) AS BranchId,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(m.FullName, '') AS ManagerName
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
LEFT JOIN Employees m ON m.Id = e.DirectManagerId
WHERE e.Id = @EmployeeId;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeeRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                NationalId = HrmsDatabase.GetString(reader, "NationalId"),
                Phone = HrmsDatabase.GetString(reader, "Phone"),
                Email = HrmsDatabase.GetString(reader, "Email"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                HireDate = HrmsDatabase.GetDateTime(reader, "HireDate"),
                BirthDate = HrmsDatabase.GetDateTime(reader, "BirthDate"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                ManagerName = HrmsDatabase.GetString(reader, "ManagerName")
            });

        return list.FirstOrDefault();
    }

    private async Task<CompensationRow> LoadCompensationAsync(int employeeId)
    {
        var list = await HrmsDatabase.QueryAsync(_dbContext,
            """
SELECT TOP 1
    ISNULL(BasicSalary, 0) AS BasicSalary,
    ISNULL(Allowances, 0) AS Allowances,
    ISNULL(Deductions, 0) AS Deductions,
    ISNULL(PaymentMethod, '') AS PaymentMethod,
    ISNULL(BankName, '') AS BankName,
    ISNULL(BankAccount, '') AS BankAccount,
    ISNULL(Currency, '') AS Currency
FROM EmployeeCompensations
WHERE EmployeeId = @EmployeeId
ORDER BY Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new CompensationRow
            {
                BasicSalary = GetDecimal(reader, "BasicSalary"),
                Allowances = GetDecimal(reader, "Allowances"),
                Deductions = GetDecimal(reader, "Deductions"),
                PaymentMethod = HrmsDatabase.GetString(reader, "PaymentMethod"),
                BankName = HrmsDatabase.GetString(reader, "BankName"),
                BankAccount = HrmsDatabase.GetString(reader, "BankAccount"),
                Currency = HrmsDatabase.GetString(reader, "Currency")
            });
        return list.FirstOrDefault() ?? new CompensationRow();
    }

    private async Task<List<AnnouncementRow>> LoadAnnouncementsAsync(EmployeeRow employee)
    {
        return await HrmsDatabase.QueryAsync(_dbContext,
            """
SELECT TOP 20 Id, Title, ISNULL(Body, '') AS Body, ISNULL(Category, N'عام') AS Category, ISNULL(TargetType, 'All') AS TargetType, ISNULL(TargetValue, '') AS TargetValue, PublishDate
FROM EmployeePortalAnnouncements
WHERE IsPublished = 1
  AND
  (
      ISNULL(TargetType, 'All') IN ('All', 'الجميع')
      OR (TargetType = 'Employee' AND (',' + TargetValue + ',') LIKE '%,' + CAST(@EmployeeId AS nvarchar(20)) + ',%')
      OR (TargetType = 'Department' AND TargetValue = @DepartmentId)
      OR (TargetType = 'Branch' AND TargetValue = @BranchId)
  )
ORDER BY PublishDate DESC, Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employee.Id);
                HrmsDatabase.AddParameter(command, "@DepartmentId", employee.DepartmentId.ToString());
                HrmsDatabase.AddParameter(command, "@BranchId", employee.BranchId.ToString());
            },
            reader => new AnnouncementRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Body = HrmsDatabase.GetString(reader, "Body"),
                Category = HrmsDatabase.GetString(reader, "Category"),
                TargetType = HrmsDatabase.GetString(reader, "TargetType"),
                TargetValue = HrmsDatabase.GetString(reader, "TargetValue"),
                PublishDate = HrmsDatabase.GetDateTime(reader, "PublishDate")
            });
    }

    private async Task<List<PollRow>> LoadPollsAsync(EmployeeRow employee)
    {
        var polls = await HrmsDatabase.QueryAsync(_dbContext,
            """
SELECT TOP 20
    p.Id,
    p.Title,
    ISNULL(p.Question, '') AS Question,
    ISNULL(p.Category, N'استطلاع') AS Category,
    p.PublishDate,
    (SELECT TOP 1 OptionId FROM EmployeePollVotes v WHERE v.PollId = p.Id AND v.EmployeeId = @EmployeeId) AS SelectedOptionId
FROM EmployeePolls p
WHERE p.IsPublished = 1
  AND
  (
      ISNULL(p.TargetType, 'All') IN ('All', 'الجميع')
      OR (p.TargetType = 'Employee' AND (',' + p.TargetValue + ',') LIKE '%,' + CAST(@EmployeeId AS nvarchar(20)) + ',%')
      OR (p.TargetType = 'Department' AND p.TargetValue = @DepartmentId)
      OR (p.TargetType = 'Branch' AND p.TargetValue = @BranchId)
  )
ORDER BY p.PublishDate DESC, p.Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employee.Id);
                HrmsDatabase.AddParameter(command, "@DepartmentId", employee.DepartmentId.ToString());
                HrmsDatabase.AddParameter(command, "@BranchId", employee.BranchId.ToString());
            },
            reader => new PollRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Question = HrmsDatabase.GetString(reader, "Question"),
                Category = HrmsDatabase.GetString(reader, "Category"),
                PublishDate = HrmsDatabase.GetDateTime(reader, "PublishDate"),
                SelectedOptionId = HrmsDatabase.GetInt(reader, "SelectedOptionId")
            });

        foreach (var poll in polls)
        {
            poll.Options = await HrmsDatabase.QueryAsync(_dbContext,
                """
SELECT o.Id, o.OptionText, o.DisplayOrder,
       (SELECT COUNT(1) FROM EmployeePollVotes v WHERE v.OptionId = o.Id) AS VotesCount
FROM EmployeePollOptions o
WHERE o.PollId = @PollId
ORDER BY o.DisplayOrder, o.Id;
""",
                command => HrmsDatabase.AddParameter(command, "@PollId", poll.Id),
                reader => new PollOptionRow
                {
                    Id = HrmsDatabase.GetInt(reader, "Id"),
                    OptionText = HrmsDatabase.GetString(reader, "OptionText"),
                    DisplayOrder = HrmsDatabase.GetInt(reader, "DisplayOrder"),
                    VotesCount = HrmsDatabase.GetInt(reader, "VotesCount")
                });
        }

        return polls;
    }

    private async Task<List<FeedbackRow>> LoadFeedbackAsync(int employeeId) => await HrmsDatabase.QueryAsync(_dbContext,
        """
SELECT TOP 20 Id, Type, Title, ISNULL(Message, '') AS Message, ISNULL(Priority, '') AS Priority, Status, ISNULL(AdminReply, '') AS AdminReply, ISNULL(RepliedBy, '') AS RepliedBy, RepliedAt, CreatedAt
FROM EmployeeFeedbackItems
WHERE EmployeeId = @EmployeeId
ORDER BY CreatedAt DESC, Id DESC;
""",
        command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
        reader => new FeedbackRow
        {
            Id = HrmsDatabase.GetInt(reader, "Id"),
            Type = HrmsDatabase.GetString(reader, "Type"),
            Title = HrmsDatabase.GetString(reader, "Title"),
            Message = HrmsDatabase.GetString(reader, "Message"),
            Priority = HrmsDatabase.GetString(reader, "Priority"),
            Status = HrmsDatabase.GetString(reader, "Status"),
            AdminReply = HrmsDatabase.GetString(reader, "AdminReply"),
            RepliedBy = HrmsDatabase.GetString(reader, "RepliedBy"),
            RepliedAt = HrmsDatabase.GetDateTime(reader, "RepliedAt"),
            CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
        });

    private async Task<List<RequestRow>> LoadRequestsAsync(int employeeId) => await HrmsDatabase.QueryAsync(_dbContext,
        """
SELECT TOP 15 Id, RequestType, Status, CreatedAt, FromDate, ToDate, ISNULL(Reason, '') AS Reason
FROM SelfServiceRequests
WHERE EmployeeId = @EmployeeId
ORDER BY CreatedAt DESC, Id DESC;
""",
        command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
        reader => new RequestRow
        {
            Id = HrmsDatabase.GetInt(reader, "Id"),
            RequestType = HrmsDatabase.GetString(reader, "RequestType"),
            Status = HrmsDatabase.GetString(reader, "Status"),
            CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
            FromDate = HrmsDatabase.GetDateTime(reader, "FromDate"),
            ToDate = HrmsDatabase.GetDateTime(reader, "ToDate"),
            Reason = HrmsDatabase.GetString(reader, "Reason")
        });

    private async Task<List<AttendanceRow>> LoadAttendanceAsync(int employeeId) => await HrmsDatabase.QueryAsync(_dbContext,
        """
SELECT TOP 20 AttendanceDate, CheckIn, CheckOut, CAST(Status AS nvarchar(50)) AS Status, ISNULL(Notes, '') AS Notes
FROM AttendanceRecords
WHERE EmployeeId = @EmployeeId
ORDER BY AttendanceDate DESC, Id DESC;
""",
        command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
        reader => new AttendanceRow
        {
            AttendanceDate = HrmsDatabase.GetDateTime(reader, "AttendanceDate"),
            CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
            CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut"),
            Status = HrmsDatabase.GetString(reader, "Status"),
            Notes = HrmsDatabase.GetString(reader, "Notes")
        });

    private async Task<List<TeamRow>> LoadTeamAsync(int employeeId) => await HrmsDatabase.QueryAsync(_dbContext,
        """
SELECT TOP 8 Id, EmployeeNo, FullName, ISNULL(Position, '') AS Position
FROM Employees
WHERE Id <> @EmployeeId AND IsActive = 1 AND DepartmentId = (SELECT DepartmentId FROM Employees WHERE Id = @EmployeeId)
ORDER BY FullName;
""",
        command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
        reader => new TeamRow
        {
            Id = HrmsDatabase.GetInt(reader, "Id"),
            EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
            FullName = HrmsDatabase.GetString(reader, "FullName"),
            Position = HrmsDatabase.GetString(reader, "Position")
        });

    private static decimal GetDecimal(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    public string DisplayDate(DateTime? date) => date.HasValue ? date.Value.ToString("dd/MM/yyyy") : "-";
    public string DisplayTime(DateTime? date) => date.HasValue ? date.Value.ToString("HH:mm") : "-";
    public string DisplayMoney(decimal value) => value <= 0 ? "غير مدخل" : $"IQD {value:N0}";
    public string FeedbackStatusText(string status) => status.Equals("Open", StringComparison.OrdinalIgnoreCase) ? "مفتوحة" : status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ? "قيد المعالجة" : status.Equals("Answered", StringComparison.OrdinalIgnoreCase) ? "تم الرد" : status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ? "مغلقة" : status;
    public string StatusClass(string status) => status.Equals("Open", StringComparison.OrdinalIgnoreCase) ? "pending" : status.Equals("Answered", StringComparison.OrdinalIgnoreCase) ? "live" : status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ? "danger" : string.Empty;
    public string GetInitials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "م";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0][0].ToString() : $"{parts[0][0]}{parts[^1][0]}";
    }

    public record EmployeeRow
    {
        public static EmployeeRow Empty => new() { FullName = "موظف", EmployeeNo = "-", Position = "Employee", DepartmentName = "-", BranchName = "-" };
        public int Id { get; init; }
        public string EmployeeNo { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string NationalId { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public DateTime? HireDate { get; init; }
        public DateTime? BirthDate { get; init; }
        public bool IsActive { get; init; }
        public string DepartmentName { get; init; } = string.Empty;
        public string BranchName { get; init; } = string.Empty;
        public string ManagerName { get; init; } = string.Empty;
        public int DepartmentId { get; init; }
        public int BranchId { get; init; }
    }

    public class CompensationRow { public decimal BasicSalary { get; set; } public decimal Allowances { get; set; } public decimal Deductions { get; set; } public string PaymentMethod { get; set; } = string.Empty; public string BankName { get; set; } = string.Empty; public string BankAccount { get; set; } = string.Empty; public string Currency { get; set; } = string.Empty; }
    public class AnnouncementRow { public int Id { get; set; } public string Title { get; set; } = string.Empty; public string Body { get; set; } = string.Empty; public string Category { get; set; } = string.Empty; public string TargetType { get; set; } = string.Empty; public string TargetValue { get; set; } = string.Empty; public DateTime? PublishDate { get; set; } }
    public class FeedbackRow { public int Id { get; set; } public string Type { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Message { get; set; } = string.Empty; public string Priority { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public string AdminReply { get; set; } = string.Empty; public string RepliedBy { get; set; } = string.Empty; public DateTime? RepliedAt { get; set; } public DateTime? CreatedAt { get; set; } }
    public class PollRow { public int Id { get; set; } public string Title { get; set; } = string.Empty; public string Question { get; set; } = string.Empty; public string Category { get; set; } = string.Empty; public DateTime? PublishDate { get; set; } public int SelectedOptionId { get; set; } public List<PollOptionRow> Options { get; set; } = new(); }
    public class PollOptionRow { public int Id { get; set; } public string OptionText { get; set; } = string.Empty; public int DisplayOrder { get; set; } public int VotesCount { get; set; } }
    public class RequestRow { public int Id { get; set; } public string RequestType { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public DateTime? CreatedAt { get; set; } public DateTime? FromDate { get; set; } public DateTime? ToDate { get; set; } public string Reason { get; set; } = string.Empty; }
    public class AttendanceRow { public DateTime? AttendanceDate { get; set; } public DateTime? CheckIn { get; set; } public DateTime? CheckOut { get; set; } public string Status { get; set; } = string.Empty; public string Notes { get; set; } = string.Empty; }
    public class TeamRow { public int Id { get; set; } public string EmployeeNo { get; set; } = string.Empty; public string FullName { get; set; } = string.Empty; public string Position { get; set; } = string.Empty; }
    public class FeedbackInput { public string Type { get; set; } = "اقتراح"; public string Priority { get; set; } = "متوسط"; public string Title { get; set; } = string.Empty; public string Message { get; set; } = string.Empty; }
}
