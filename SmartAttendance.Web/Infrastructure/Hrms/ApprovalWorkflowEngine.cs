using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// محرك سريان الموافقات: عند تقديم الطلب يُحلّ القالب المناسب (ApprovalTemplateStore.ResolveAsync)
/// و«تُجمَّد» خطواته لقطةً على الطلب (نمط كيان — تعديل القالب لاحقاً لا يغيّر الطلبات الجارية).
/// بلا قالب → سلسلة افتراضية (المدير المباشر ← HR Manager) = نفس سلوك النظام القديم.
/// الموافقة تقدّم الخطوة التالية؛ الرفض نهائي ويُنفّذ قاعدة «التعليق مطلوب عند الرفض».
/// التصعيد: خطوة حالية تجاوزت EscalationDays تُعلَّم وتُشعَر جهة التصعيد (مرة واحدة).
/// أعمدة SelfServiceRequests القديمة (Status/CurrentStep) تُحدَّث للتوافق مع بقية الشاشات.
/// </summary>
public static class ApprovalWorkflowEngine
{
    /// <summary>تحويل قيم RequestType المخزّنة بالطلبات إلى مفاتيح كتالوج القوالب.</summary>
    private static readonly Dictionary<string, string> RequestTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Leave"] = "LeaveRequest",
        ["إجازة"] = "LeaveRequest",
        ["MissingPunch"] = "MissingPunch",
        ["نسيان بصمة"] = "MissingPunch",
        ["ExitPermission"] = "ExitPermission",
        ["خروج أثناء الدوام"] = "ExitPermission",
        ["Overtime"] = "Overtime",
        ["عمل إضافي"] = "Overtime",
    };

    public sealed class StepState
    {
        public int Id { get; set; }
        public int RequestId { get; set; }
        public int StepOrder { get; set; }
        public string ApproverType { get; set; } = "DirectManager";
        public string? RoleName { get; set; }
        public string? UserName { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public DateTime? CurrentSince { get; set; }
    }

    public sealed class FlowState
    {
        public int RequestId { get; set; }
        public string TemplateName { get; set; } = string.Empty;
        public bool CommentRequiredOnReject { get; set; }
        public int? EscalationDays { get; set; }
        public string? EscalationTo { get; set; }
        public bool Escalated { get; set; }
        public List<StepState> Steps { get; set; } = new();
        public StepState? Current => Steps.FirstOrDefault(s => s.Status == "Current");
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('ApprovalRequestFlows', 'U') IS NULL
BEGIN
    CREATE TABLE ApprovalRequestFlows
    (
        RequestId int NOT NULL PRIMARY KEY,
        TemplateId int NULL,
        TemplateName nvarchar(150) NOT NULL DEFAULT(N''),
        CommentRequiredOnReject bit NOT NULL DEFAULT(0),
        AttachmentRequiredOnRequest bit NOT NULL DEFAULT(0),
        CancelLimitDays int NULL,
        EscalationDays int NULL,
        EscalationTo nvarchar(30) NULL,
        Escalated bit NOT NULL DEFAULT(0),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('ApprovalRequestSteps', 'U') IS NULL
BEGIN
    CREATE TABLE ApprovalRequestSteps
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RequestId int NOT NULL,
        StepOrder int NOT NULL,
        ApproverType nvarchar(20) NOT NULL,
        RoleName nvarchar(50) NULL,
        UserName nvarchar(150) NULL,
        DisplayName nvarchar(150) NOT NULL,
        Status nvarchar(20) NOT NULL DEFAULT('Pending'),
        CurrentSince datetime2 NULL,
        ActionBy nvarchar(150) NULL,
        ActionAt datetime2 NULL,
        Note nvarchar(500) NULL
    );
    CREATE INDEX IX_ApprovalRequestSteps_Request ON ApprovalRequestSteps(RequestId, StepOrder);
END;
""");
    }

    /// <summary>يبدأ سريان الموافقة لطلب جديد: حلّ القالب وتجميد الخطوات وتعليم الأولى حالية.</summary>
    public static async Task StartAsync(ApplicationDbContext dbContext, int requestId, string requestType, int employeeId)
    {
        await EnsureAsync(dbContext);

        var typeKey = RequestTypeMap.TryGetValue(requestType?.Trim() ?? string.Empty, out var mapped)
            ? mapped
            : requestType?.Trim() ?? string.Empty;

        var employee = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT BranchId, DepartmentId, ISNULL(WorkType, '') AS WorkType FROM Employees WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", employeeId),
            reader => new
            {
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                WorkType = HrmsDatabase.GetString(reader, "WorkType")
            });
        var employeeInfo = employee.FirstOrDefault();

        var template = employeeInfo == null
            ? null
            : await ApprovalTemplateStore.ResolveAsync(dbContext, typeKey, employeeInfo.BranchId, employeeInfo.DepartmentId, employeeInfo.WorkType);

        // بلا قالب: السلسلة الافتراضية القديمة نفسها.
        var steps = template?.Steps.OrderBy(s => s.StepOrder).ToList()
            ?? new List<ApprovalTemplateStore.StepRow>
            {
                new() { StepOrder = 1, ApproverType = "DirectManager", DisplayName = "المدير المباشر" },
                new() { StepOrder = 2, ApproverType = "Role", RoleName = "HR Manager", DisplayName = "HR Manager" }
            };

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
DELETE FROM ApprovalRequestSteps WHERE RequestId = @RequestId;
DELETE FROM ApprovalRequestFlows WHERE RequestId = @RequestId;
INSERT INTO ApprovalRequestFlows
(RequestId, TemplateId, TemplateName, CommentRequiredOnReject, AttachmentRequiredOnRequest, CancelLimitDays, EscalationDays, EscalationTo)
VALUES (@RequestId, @TemplateId, @TemplateName, @CommentReq, @AttachReq, @CancelLimit, @EscDays, @EscTo);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@TemplateId", (object?)template?.Id ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@TemplateName", template?.Name ?? "المسار الافتراضي");
                HrmsDatabase.AddParameter(command, "@CommentReq", template?.CommentRequiredOnReject == true ? 1 : 0);
                HrmsDatabase.AddParameter(command, "@AttachReq", template?.AttachmentRequiredOnRequest == true ? 1 : 0);
                HrmsDatabase.AddParameter(command, "@CancelLimit", (object?)template?.CancelLimitDays ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@EscDays", (object?)template?.EscalationDays ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@EscTo", (object?)template?.EscalationTo ?? DBNull.Value);
            });

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var order = i + 1;
            var isFirst = i == 0;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO ApprovalRequestSteps (RequestId, StepOrder, ApproverType, RoleName, UserName, DisplayName, Status, CurrentSince)
VALUES (@RequestId, @StepOrder, @ApproverType, @RoleName, @UserName, @DisplayName, @Status, @CurrentSince);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                    HrmsDatabase.AddParameter(command, "@StepOrder", order);
                    HrmsDatabase.AddParameter(command, "@ApproverType", step.ApproverType);
                    HrmsDatabase.AddParameter(command, "@RoleName", (object?)step.RoleName ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@UserName", (object?)step.UserName ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@DisplayName", step.DisplayName);
                    HrmsDatabase.AddParameter(command, "@Status", isFirst ? "Current" : "Pending");
                    HrmsDatabase.AddParameter(command, "@CurrentSince", isFirst ? DateTime.UtcNow : (object)DBNull.Value);
                });
        }

        // توافق: CurrentStep بالجدول القديم = اسم الخطوة الحالية.
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE SelfServiceRequests SET CurrentStep = @Step, Status = 'Pending', UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Step", steps[0].DisplayName);
                HrmsDatabase.AddParameter(command, "@Id", requestId);
            });
    }

    public static async Task<FlowState?> GetFlowAsync(ApplicationDbContext dbContext, int requestId)
    {
        await EnsureAsync(dbContext);
        var flows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ApprovalRequestFlows WHERE RequestId = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", requestId),
            reader => new FlowState
            {
                RequestId = HrmsDatabase.GetInt(reader, "RequestId"),
                TemplateName = HrmsDatabase.GetString(reader, "TemplateName"),
                CommentRequiredOnReject = HrmsDatabase.GetBool(reader, "CommentRequiredOnReject"),
                EscalationDays = HrmsDatabase.GetNullableInt(reader, "EscalationDays"),
                EscalationTo = HrmsDatabase.GetString(reader, "EscalationTo"),
                Escalated = HrmsDatabase.GetBool(reader, "Escalated")
            });

        var flow = flows.FirstOrDefault();
        if (flow == null) return null;

        flow.Steps = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ApprovalRequestSteps WHERE RequestId = @Id ORDER BY StepOrder;",
            command => HrmsDatabase.AddParameter(command, "@Id", requestId),
            ReadStep);
        return flow;
    }

    /// <summary>هل يحق للمستخدم الحالي البتّ بالخطوة الحالية؟ (Admin/HR Manager تجاوز إداري)</summary>
    public static bool CanAct(StepState step, string userName, IEnumerable<string> roles, bool isRequesterManager)
    {
        var roleSet = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        if (roleSet.Contains("Admin") || roleSet.Contains("HR Manager")) return true;

        return step.ApproverType switch
        {
            "DirectManager" => isRequesterManager,
            "Role" => !string.IsNullOrWhiteSpace(step.RoleName) && roleSet.Contains(step.RoleName),
            "User" => string.Equals(step.UserName, userName, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public sealed record ActionResult(bool Ok, string Message, bool FinalApproved = false, bool Rejected = false);

    public static async Task<ActionResult> ApproveAsync(ApplicationDbContext dbContext, int requestId, string actor, string? note)
    {
        var flow = await GetFlowAsync(dbContext, requestId);
        var current = flow?.Current;
        if (flow == null || current == null)
        {
            return new ActionResult(false, "لا توجد خطوة حالية لهذا الطلب.");
        }

        var next = flow.Steps.Where(s => s.StepOrder > current.StepOrder && s.Status == "Pending")
                             .OrderBy(s => s.StepOrder)
                             .FirstOrDefault();

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE ApprovalRequestSteps
SET Status = 'Approved', ActionBy = @Actor, ActionAt = SYSUTCDATETIME(), Note = @Note
WHERE Id = @StepId;

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes)
VALUES (@RequestId, @StepName, 'Approved', @Actor, @Note);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@StepId", current.Id);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(command, "@Note", (object?)note ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@StepName", current.DisplayName);
            });

        if (next == null)
        {
            // آخر خطوة → اعتماد نهائي (تحديث أعمدة التوافق القديمة أيضاً).
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE SelfServiceRequests
SET Status = 'Approved', CurrentStep = 'Completed', ReviewedBy = @Actor, ReviewNote = @Note,
    HrStatus = 'Approved', HrReviewedBy = @Actor, HrReviewedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب معتمد', N'تم اعتماد الطلب نهائياً بعد اكتمال لجنة الموافقة', 'Employee', '/SelfServices');
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", requestId);
                    HrmsDatabase.AddParameter(command, "@Actor", actor);
                    HrmsDatabase.AddParameter(command, "@Note", (object?)note ?? DBNull.Value);
                });
            return new ActionResult(true, "تم اعتماد الطلب نهائياً — اكتملت اللجنة.", FinalApproved: true);
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE ApprovalRequestSteps SET Status = 'Current', CurrentSince = SYSUTCDATETIME() WHERE Id = @NextId;

UPDATE SelfServiceRequests
SET CurrentStep = @NextName, ManagerStatus = 'Approved', ManagerReviewedBy = @Actor, ManagerReviewedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب بانتظار موافقتك', N'وصل الطلب إلى خطوة: ' + @NextName, @TargetRole, '/Approvals');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@NextId", next.Id);
                HrmsDatabase.AddParameter(command, "@NextName", next.DisplayName);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(command, "@Id", requestId);
                HrmsDatabase.AddParameter(command, "@TargetRole", next.ApproverType == "Role" ? next.RoleName ?? "HR" : "HR");
            });
        return new ActionResult(true, $"تمت الموافقة وانتقل الطلب إلى: {next.DisplayName}.");
    }

    public static async Task<ActionResult> RejectAsync(ApplicationDbContext dbContext, int requestId, string actor, string? note)
    {
        var flow = await GetFlowAsync(dbContext, requestId);
        var current = flow?.Current;
        if (flow == null || current == null)
        {
            return new ActionResult(false, "لا توجد خطوة حالية لهذا الطلب.");
        }

        if (flow.CommentRequiredOnReject && string.IsNullOrWhiteSpace(note))
        {
            return new ActionResult(false, "قالب الموافقة يشترط كتابة تعليق عند الرفض.");
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE ApprovalRequestSteps
SET Status = 'Rejected', ActionBy = @Actor, ActionAt = SYSUTCDATETIME(), Note = @Note
WHERE Id = @StepId;

UPDATE ApprovalRequestSteps SET Status = 'Skipped' WHERE RequestId = @RequestId AND Status = 'Pending';

UPDATE SelfServiceRequests
SET Status = 'Rejected', CurrentStep = 'Rejected', ReviewedBy = @Actor, ReviewNote = @Note,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @RequestId;

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes)
VALUES (@RequestId, @StepName, 'Rejected', @Actor, @Note);

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب مرفوض', N'تم رفض الطلب في خطوة: ' + @StepName, 'Employee', '/SelfServices');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@StepId", current.Id);
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(command, "@Note", (object?)note ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@StepName", current.DisplayName);
            });
        return new ActionResult(true, "تم رفض الطلب.", Rejected: true);
    }

    /// <summary>تصعيد الخطوات المتأخرة (تشغيل كسولاً عند فتح شاشة الموافقات) — إشعار واحد لكل طلب.</summary>
    public static async Task<int> EscalateOverdueAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
DECLARE @Escalated TABLE (RequestId int, StepName nvarchar(150), EscalateTo nvarchar(30));

UPDATE f
SET f.Escalated = 1
OUTPUT inserted.RequestId, s.DisplayName, ISNULL(inserted.EscalationTo, 'HR Manager') INTO @Escalated
FROM ApprovalRequestFlows f
JOIN ApprovalRequestSteps s ON s.RequestId = f.RequestId AND s.Status = 'Current'
WHERE f.Escalated = 0
  AND f.EscalationDays IS NOT NULL
  AND s.CurrentSince IS NOT NULL
  AND DATEDIFF(day, s.CurrentSince, SYSUTCDATETIME()) >= f.EscalationDays;

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
SELECT N'تصعيد طلب متأخر',
       N'الطلب رقم ' + CAST(RequestId AS nvarchar(20)) + N' متأخر في خطوة: ' + StepName,
       EscalateTo, '/Approvals'
FROM @Escalated;

SELECT COUNT(*) FROM @Escalated;
""");
    }

    private static StepState ReadStep(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        RequestId = HrmsDatabase.GetInt(reader, "RequestId"),
        StepOrder = HrmsDatabase.GetInt(reader, "StepOrder"),
        ApproverType = HrmsDatabase.GetString(reader, "ApproverType"),
        RoleName = HrmsDatabase.GetString(reader, "RoleName"),
        UserName = HrmsDatabase.GetString(reader, "UserName"),
        DisplayName = HrmsDatabase.GetString(reader, "DisplayName"),
        Status = HrmsDatabase.GetString(reader, "Status"),
        CurrentSince = HrmsDatabase.GetDateTime(reader, "CurrentSince")
    };
}
