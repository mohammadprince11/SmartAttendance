using SmartAttendance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
        ["ShiftRequest"] = "ShiftRequest",
        ["طلب مناوبة"] = "ShiftRequest",
        ["تعديل البيانات"] = "InfoChange",
        // الطلبات المالية (FinancialRequestStore) — التسمية العربية ← مفتاح قالب اللجنة.
        ["قرض"] = "Loan",
        ["سُلفة"] = "Loan",
        ["بدل مالي"] = "FinancialClaim",
        ["استرداد نفقات"] = "FinancialClaim",
        ["زيادة راتب"] = "SalaryIncrease",
    };

    public sealed class StepState
    {
        public int Id { get; set; }
        public int RequestId { get; set; }
        public int StepOrder { get; set; }
        public int StageOrder { get; set; }
        public string ApproverType { get; set; } = "DirectManager";
        public string? RoleName { get; set; }
        public string? UserName { get; set; }
        public int? CommitteeGroupId { get; set; }
        public int? ExternalCommitteeId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public DateTime? CurrentSince { get; set; }
        public DateTime? ReminderSentAt { get; set; }
        public DateTime? EscalatedAt { get; set; }
        public string? EscalatedToRole { get; set; }
        public string? EscalatedToUser { get; set; }
    }

    public sealed class FlowState
    {
        public int RequestId { get; set; }
        public string TemplateName { get; set; } = string.Empty;
        public bool CommentRequiredOnReject { get; set; }
        public bool AttachmentRequiredOnRequest { get; set; }
        public int? CancelLimitDays { get; set; }
        public string? NotifyJson { get; set; }
        public int? ReminderHours { get; set; }
        public int? EscalationDays { get; set; }
        public string? EscalationTo { get; set; }
        public string? EscalationAlternateUser { get; set; }
        public bool Escalated { get; set; }
        public List<StepState> Steps { get; set; } = new();
        public StepState? Current => Steps.FirstOrDefault(s => s.Status == "Current");
        public IReadOnlyList<StepState> CurrentSteps => Steps.Where(s=>s.Status=="Current").OrderBy(s=>s.StepOrder).ToList();
    }

    public sealed class HistoryState
    {
        public int Id { get; set; }
        public int RequestId { get; set; }
        public string StepName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? ActionBy { get; set; }
        public DateTime ActionAt { get; set; }
        public string? Notes { get; set; }
        public string? DelegatedFrom { get; set; }
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
        ReminderHours int NULL,
        EscalationAlternateUser nvarchar(100) NULL,
        NotifyJson nvarchar(max) NULL,
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
        StageOrder int NOT NULL,
        ApproverType nvarchar(20) NOT NULL,
        RoleName nvarchar(50) NULL,
        UserName nvarchar(150) NULL,
        DisplayName nvarchar(150) NOT NULL,
        Status nvarchar(20) NOT NULL DEFAULT('Pending'),
        CurrentSince datetime2 NULL,
        ActionBy nvarchar(150) NULL,
        ActionAt datetime2 NULL,
        Note nvarchar(500) NULL
        ,ReminderSentAt datetime2 NULL,EscalatedAt datetime2 NULL,EscalatedToRole nvarchar(50) NULL,EscalatedToUser nvarchar(100) NULL
    );
    CREATE INDEX IX_ApprovalRequestSteps_Request ON ApprovalRequestSteps(RequestId, StepOrder);
END;

IF OBJECT_ID('ApprovalRequestWatchers', 'U') IS NULL
BEGIN
    CREATE TABLE ApprovalRequestWatchers
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RequestId int NOT NULL,
        UserName nvarchar(150) NOT NULL
    );
    CREATE UNIQUE INDEX UX_ApprovalRequestWatchers_RequestUser ON ApprovalRequestWatchers(RequestId,UserName);
END;
""");
    }

    /// <summary>يبدأ سريان الموافقة لطلب جديد: حلّ القالب وتجميد الخطوات وتعليم الأولى حالية.</summary>
    public static async Task<ActionResult> StartAsync(ApplicationDbContext dbContext, int requestId, string requestType, int employeeId)
    {
        await EnsureAsync(dbContext);

        var typeKey = ResolveRequestTypeKey(requestType);

        var employee = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT CompanyId,BranchId,DepartmentId,ISNULL(WorkType,'') AS WorkType FROM Employees WHERE Id=@Id AND IsDeleted=0;",
            command => HrmsDatabase.AddParameter(command, "@Id", employeeId),
            reader => new
            {
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
                WorkType = HrmsDatabase.GetString(reader, "WorkType")
            });
        var employeeInfo = employee.FirstOrDefault();
        if (employeeInfo is null)
            return new ActionResult(false, "تعذّر بدء الموافقة لأن الموظف غير موجود.");

        var template = await ApprovalTemplateStore.ResolveAsync(dbContext, employeeInfo.CompanyId, typeKey, employeeInfo.BranchId, employeeInfo.DepartmentId, employeeInfo.WorkType,requestId);

        // بلا قالب: السلسلة الافتراضية القديمة نفسها.
        var steps = template?.Steps.OrderBy(s => s.StepOrder).ToList()
            ?? new List<ApprovalTemplateStore.StepRow>
            {
                new() { StepOrder = 1, StageOrder=1, ApproverType = "DirectManager", DisplayName = "المدير المباشر" },
                new() { StepOrder = 2, StageOrder=2, ApproverType = "Role", RoleName = "HR Manager", DisplayName = "HR Manager" }
            };

        if (template?.AttachmentRequiredOnRequest == true && !await HasRequestAttachmentAsync(dbContext, requestId, employeeId))
        {
            await BlockSubmissionAsync(dbContext, requestId, "Draft", "يتطلب مرفقاً", "AttachmentRequired",
                "قالب الموافقة يشترط إرفاق ملف قبل تقديم الطلب.");
            return new ActionResult(false, "هذا الطلب يتطلب مرفقاً قبل الإرسال.");
        }

        var unresolved = await FindUnresolvedCommitteeAsync(dbContext, employeeId, employeeInfo.CompanyId, steps);
        if (unresolved.Count > 0 && template?.AutoRejectUnknownCommittee == true)
        {
            var reason = "تعذّر تحديد أعضاء لجنة الموافقة: " + string.Join("، ", unresolved);
            await BlockSubmissionAsync(dbContext, requestId, "Rejected", "Rejected", "AutoRejected", reason);
            return new ActionResult(false, "رُفض الطلب تلقائياً لأن أعضاء اللجنة غير معرّفين.", Rejected: true);
        }

        var firstStage=steps.Min(step=>step.StageOrder>0?step.StageOrder:step.StepOrder);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
DELETE m FROM ApprovalRequestStepMembers m INNER JOIN ApprovalRequestSteps s ON s.Id=m.StepId WHERE s.RequestId=@RequestId;
DELETE FROM ApprovalRequestSteps WHERE RequestId = @RequestId;
DELETE FROM ApprovalRequestWatchers WHERE RequestId = @RequestId;
DELETE FROM ApprovalRequestFlows WHERE RequestId = @RequestId;
INSERT INTO ApprovalRequestFlows
(RequestId, TemplateId, TemplateName, CommentRequiredOnReject, AttachmentRequiredOnRequest, CancelLimitDays, ReminderHours,EscalationDays, EscalationTo,EscalationAlternateUser,NotifyJson)
VALUES (@RequestId, @TemplateId, @TemplateName, @CommentReq, @AttachReq, @CancelLimit,@ReminderHours,@EscDays, @EscTo,@EscAltUser,@NotifyJson);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@TemplateId", (object?)template?.Id ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@TemplateName", template?.Name ?? "المسار الافتراضي");
                HrmsDatabase.AddParameter(command, "@CommentReq", template?.CommentRequiredOnReject == true ? 1 : 0);
                HrmsDatabase.AddParameter(command, "@AttachReq", template?.AttachmentRequiredOnRequest == true ? 1 : 0);
                HrmsDatabase.AddParameter(command, "@CancelLimit", (object?)template?.CancelLimitDays ?? DBNull.Value);
                HrmsDatabase.AddParameter(command,"@ReminderHours",(object?)template?.ReminderHours??DBNull.Value);
                HrmsDatabase.AddParameter(command, "@EscDays", (object?)template?.EscalationDays ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@EscTo", (object?)template?.EscalationTo ?? DBNull.Value);
                HrmsDatabase.AddParameter(command,"@EscAltUser",(object?)template?.EscalationAlternateUser??DBNull.Value);
                HrmsDatabase.AddParameter(command,"@NotifyJson",(object?)template?.NotifyJson??DBNull.Value);
            });

        foreach (var watcher in template?.Watchers.Select(w => w.UserName).Where(user => !string.IsNullOrWhiteSpace(user)).Distinct(StringComparer.OrdinalIgnoreCase) ?? Array.Empty<string>())
        {
            await HrmsDatabase.ExecuteAsync(dbContext,
                "INSERT INTO ApprovalRequestWatchers(RequestId,UserName) VALUES(@RequestId,@UserName);",
                command =>
                {
                    HrmsDatabase.AddParameter(command,"@RequestId",requestId);
                    HrmsDatabase.AddParameter(command,"@UserName",watcher);
                });
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var order = i + 1;
            var stageOrder=step.StageOrder>0?step.StageOrder:order;
            var isFirst = stageOrder == firstStage;
            var requestStepId = await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                """
INSERT INTO ApprovalRequestSteps (RequestId, StepOrder, StageOrder, ApproverType, RoleName, UserName, CommitteeGroupId, ExternalCommitteeId, DisplayName, Status, CurrentSince)
VALUES (@RequestId, @StepOrder, @StageOrder, @ApproverType, @RoleName, @UserName, @CommitteeGroupId, @ExternalCommitteeId, @DisplayName, @Status, @CurrentSince);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                    HrmsDatabase.AddParameter(command, "@StepOrder", order);
                    HrmsDatabase.AddParameter(command, "@StageOrder", stageOrder);
                    HrmsDatabase.AddParameter(command, "@ApproverType", step.ApproverType);
                    HrmsDatabase.AddParameter(command, "@RoleName", (object?)step.RoleName ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@UserName", (object?)step.UserName ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@CommitteeGroupId", (object?)step.CommitteeGroupId ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@ExternalCommitteeId", (object?)step.ExternalCommitteeId ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@DisplayName", step.DisplayName);
                    HrmsDatabase.AddParameter(command, "@Status", isFirst ? "Current" : "Pending");
                    HrmsDatabase.AddParameter(command, "@CurrentSince", isFirst ? DateTime.UtcNow : (object)DBNull.Value);
                });
            if (step.ApproverType == "CommitteeGroup" && step.CommitteeGroupId is > 0)
            {
                await HrmsDatabase.ExecuteAsync(dbContext, """
INSERT INTO ApprovalRequestStepMembers(StepId,UserName)
SELECT @StepId,u.UserName
FROM ApprovalCommitteeGroupMembers m
INNER JOIN ApprovalCommitteeGroups g ON g.Id=m.GroupId AND g.CompanyId=@CompanyId
INNER JOIN SystemUsers u ON u.UserName=m.UserName AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
INNER JOIN Employees e ON e.Id=u.EmployeeId AND e.CompanyId=@CompanyId AND ISNULL(e.IsDeleted,0)=0
WHERE m.GroupId=@GroupId;
""", command =>
                {
                    HrmsDatabase.AddParameter(command, "@StepId", requestStepId);
                    HrmsDatabase.AddParameter(command, "@GroupId", step.CommitteeGroupId.Value);
                    HrmsDatabase.AddParameter(command, "@CompanyId", employeeInfo.CompanyId);
                });
            }
        }

        // توافق: CurrentStep بالجدول القديم = اسم الخطوة الحالية.
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE SelfServiceRequests SET CurrentStep = @Step, Status = 'Pending', UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Step", string.Join(" + ",steps.Where(step=>(step.StageOrder>0?step.StageOrder:step.StepOrder)==firstStage).Select(step=>step.DisplayName)));
                HrmsDatabase.AddParameter(command, "@Id", requestId);
            });

        await NotifyCurrentApproversAsync(dbContext, requestId, "طلب بانتظار موافقتك",
            $"تم تقديم الطلب رقم {requestId} وهو بانتظار قرارك.");
        await DispatchConfiguredNotificationsAsync(dbContext, requestId, "Submit", "طلب جديد",
            $"تم تقديم الطلب رقم {requestId} وبدأ مسار الموافقة.");
        return new ActionResult(true, "تم بدء مسار الموافقة.");
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
                AttachmentRequiredOnRequest = HrmsDatabase.GetBool(reader, "AttachmentRequiredOnRequest"),
                CancelLimitDays = HrmsDatabase.GetNullableInt(reader, "CancelLimitDays"),
                NotifyJson = HrmsDatabase.GetString(reader, "NotifyJson"),
                ReminderHours=HrmsDatabase.GetNullableInt(reader,"ReminderHours"),
                EscalationDays = HrmsDatabase.GetNullableInt(reader, "EscalationDays"),
                EscalationTo = HrmsDatabase.GetString(reader, "EscalationTo"),
                EscalationAlternateUser=HrmsDatabase.GetString(reader,"EscalationAlternateUser"),
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

    /// <summary>سجل قرار الطلب، مع حارس شركة صريح حتى لا يصبح الطلب المعرّف وحده باب IDOR.</summary>
    public static Task<List<HistoryState>> GetHistoryAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, int requestId)
    {
        var scopeFilter = Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");
        return HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT h.Id,h.StepName,h.Action,h.ActionBy,h.ActionAt,h.Notes,h.DelegatedFrom
FROM ApprovalHistories h
INNER JOIN SelfServiceRequests r ON r.Id=h.RequestId
INNER JOIN Employees e ON e.Id=r.EmployeeId
WHERE h.RequestId=@RequestId AND {scopeFilter}
ORDER BY h.ActionAt,h.Id;
""",
            command => HrmsDatabase.AddParameter(command, "@RequestId", requestId),
            reader => new HistoryState
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                RequestId = requestId,
                StepName = HrmsDatabase.GetString(reader, "StepName"),
                Action = HrmsDatabase.GetString(reader, "Action"),
                ActionBy = HrmsDatabase.GetString(reader, "ActionBy"),
                ActionAt = HrmsDatabase.GetDateTime(reader, "ActionAt") ?? DateTime.MinValue,
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                DelegatedFrom = HrmsDatabase.GetString(reader, "DelegatedFrom")
            });
    }

    /// <summary>تحميل مجمّع للسجل لتفادي استعلام مستقل لكل بطاقة في مركز الموافقات.</summary>
    public static async Task<Dictionary<int, List<HistoryState>>> GetHistoriesAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, IEnumerable<int> requestIds)
    {
        var ids = requestIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0) return new();
        var parameters = ids.Select((_, index) => $"@Request{index}").ToArray();
        var scopeFilter = Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");
        var rows = await HrmsDatabase.QueryAsync(dbContext, $"""
SELECT h.Id,h.RequestId,h.StepName,h.Action,h.ActionBy,h.ActionAt,h.Notes,h.DelegatedFrom
FROM ApprovalHistories h
INNER JOIN SelfServiceRequests r ON r.Id=h.RequestId
INNER JOIN Employees e ON e.Id=r.EmployeeId
WHERE h.RequestId IN ({string.Join(",", parameters)}) AND {scopeFilter}
ORDER BY h.RequestId,h.ActionAt,h.Id;
""", command =>
        {
            for (var index = 0; index < ids.Length; index++)
                HrmsDatabase.AddParameter(command, parameters[index], ids[index]);
        }, reader => new HistoryState
        {
            Id = HrmsDatabase.GetInt(reader, "Id"),
            RequestId = HrmsDatabase.GetInt(reader, "RequestId"),
            StepName = HrmsDatabase.GetString(reader, "StepName"),
            Action = HrmsDatabase.GetString(reader, "Action"),
            ActionBy = HrmsDatabase.GetString(reader, "ActionBy"),
            ActionAt = HrmsDatabase.GetDateTime(reader, "ActionAt") ?? DateTime.MinValue,
            Notes = HrmsDatabase.GetString(reader, "Notes"),
            DelegatedFrom = HrmsDatabase.GetString(reader, "DelegatedFrom")
        });
        return ids.ToDictionary(id => id,
            id => rows.Where(row => row.RequestId == id).ToList());
    }

    /// <summary>هل يحق للمستخدم الحالي البتّ بالخطوة الحالية؟ (Admin/HR Manager تجاوز إداري)</summary>
    public static bool CanAct(StepState step, string userName, IEnumerable<string> roles, bool isRequesterManager)
    {
        var roleSet = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        if (roleSet.Contains("Admin") || roleSet.Contains("HR Manager")) return true;
        if(step.EscalatedAt is not null &&
           ((!string.IsNullOrWhiteSpace(step.EscalatedToUser)&&string.Equals(step.EscalatedToUser,userName,StringComparison.OrdinalIgnoreCase))||
            (!string.IsNullOrWhiteSpace(step.EscalatedToRole)&&roleSet.Contains(step.EscalatedToRole)))) return true;

        return step.ApproverType switch
        {
            "DirectManager" => isRequesterManager,
            "Role" => !string.IsNullOrWhiteSpace(step.RoleName) && roleSet.Contains(step.RoleName),
            "User" => string.Equals(step.UserName, userName, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private sealed record StepAuthorization(bool Allowed, string? DelegatedFrom = null);
    private sealed record AuthorizedStep(StepState Step,StepAuthorization Authorization);

    private static async Task<AuthorizedStep?> FindAuthorizedCurrentAsync(
        ApplicationDbContext dbContext,FlowState flow,int requestId,string actor,
        IEnumerable<string> actorRoles,int? actorEmployeeId)
    {
        var roles=actorRoles.ToArray();
        foreach(var step in flow.CurrentSteps)
        {
            var authorization=await ResolveAuthorizationAsync(dbContext,step,requestId,actor,roles,actorEmployeeId);
            if(authorization.Allowed) return new(step,authorization);
        }
        return null;
    }

    /// <summary>
    /// يحسم القرار بالهوية الحالية أولاً، ثم بتفويض زمني نشط داخل شركة الطلب.
    /// اسم المفوّض يُعاد منفصلاً كي يُختم في الخطوة وسجل القرار ولا يضيع أثر النيابة.
    /// </summary>
    private static async Task<StepAuthorization> ResolveAuthorizationAsync(
        ApplicationDbContext dbContext, StepState step, int requestId, string actor,
        IEnumerable<string> actorRoles, int? actorEmployeeId)
    {
        var roles=actorRoles.ToArray();
        if(CanAct(step,actor,roles,await IsRequesterManagerAsync(dbContext,requestId,actorEmployeeId)))
            return new(true);
        if (step.ApproverType == "CommitteeGroup" && await IsFrozenCommitteeMemberAsync(dbContext, step.Id, actor))
            return new(true);

        foreach(var delegator in await ApprovalDelegationStore.ActiveDelegatorsAsync(dbContext,requestId,actor))
        {
            if(await DelegatorCanActAsync(dbContext,step,requestId,delegator))
                return new(true,delegator);
        }
        return new(false);
    }

    private static async Task<bool> DelegatorCanActAsync(
        ApplicationDbContext dbContext, StepState step, int requestId, string delegator)
    {
        // مفوّض صاحب صلاحية التجاوز ينقل مهام موافقاته خلال النافذة فقط.
        if(await LoginUserHasRoleAsync(dbContext,delegator,"Admin","HR Manager")) return true;
        if(step.ApproverType.Equals("User",StringComparison.OrdinalIgnoreCase))
            return string.Equals(step.UserName,delegator,StringComparison.OrdinalIgnoreCase);
        if(step.ApproverType.Equals("Role",StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(step.RoleName) &&
                   await LoginUserHasRoleAsync(dbContext,delegator,step.RoleName!);
        if (step.ApproverType.Equals("CommitteeGroup", StringComparison.OrdinalIgnoreCase))
            return await IsFrozenCommitteeMemberAsync(dbContext, step.Id, delegator);
        if(!step.ApproverType.Equals("DirectManager",StringComparison.OrdinalIgnoreCase)) return false;

        return await HrmsDatabase.ScalarAsync<int>(dbContext,"""
SELECT COUNT(1)
FROM SelfServiceRequests r
INNER JOIN Employees requester ON requester.Id=r.EmployeeId AND ISNULL(requester.IsDeleted,0)=0
INNER JOIN SystemUsers u ON u.UserName=@Delegator AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
INNER JOIN Employees manager ON manager.Id=u.EmployeeId AND ISNULL(manager.IsDeleted,0)=0
WHERE r.Id=@RequestId AND requester.DirectManagerId=manager.Id AND requester.CompanyId=manager.CompanyId;
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@RequestId",requestId);
            HrmsDatabase.AddParameter(command,"@Delegator",delegator);
        })>0;
    }

    private static Task<bool> IsFrozenCommitteeMemberAsync(
        ApplicationDbContext dbContext, int stepId, string userName) => IsFrozenCommitteeMemberCoreAsync(dbContext, stepId, userName);

    private static async Task<bool> IsFrozenCommitteeMemberCoreAsync(
        ApplicationDbContext dbContext, int stepId, string userName) =>
        await HrmsDatabase.ScalarAsync<int>(dbContext, """
SELECT COUNT(1) FROM ApprovalRequestStepMembers WHERE StepId=@StepId AND UserName=@UserName;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@StepId", stepId);
            HrmsDatabase.AddParameter(command, "@UserName", userName);
        }) > 0;

    private static async Task<bool> LoginUserHasRoleAsync(
        ApplicationDbContext dbContext,string userName,params string[] roles)
    {
        if(roles.Length==0) return false;
        // AppLoginUsers قد لا يوجد في قاعدة اختبار مكوّن منفصل؛ dynamic SQL يمنع
        // ربط الاسم وقت compilation، والفشل عند غياب جدول الدخول يكون مغلقاً.
        var normalized=roles.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        for(var i=0;i<normalized.Length;i++)
        {
            var matched=await HrmsDatabase.ScalarAsync<int>(dbContext,"""
DECLARE @Matched int=0;
IF OBJECT_ID('AppLoginUsers','U') IS NOT NULL
BEGIN
 DECLARE @Sql nvarchar(max)=N'SELECT @Out=COUNT(1) FROM AppLoginUsers WHERE Username=@User AND IsActive=1 AND Role=@Role';
 EXEC sp_executesql @Sql,N'@User nvarchar(100),@Role nvarchar(50),@Out int OUTPUT',@User=@UserName,@Role=@RoleName,@Out=@Matched OUTPUT;
END;
SELECT @Matched;
""", command =>
            {
                HrmsDatabase.AddParameter(command,"@UserName",userName);
                HrmsDatabase.AddParameter(command,"@RoleName",normalized[i]);
            });
            if(matched>0) return true;
        }
        return false;
    }

    private static async Task AcquireDecisionLockAsync(ApplicationDbContext dbContext,int requestId)
    {
        var result=await HrmsDatabase.ScalarAsync<int>(dbContext,"""
DECLARE @Result int;
EXEC @Result=sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
SELECT @Result;
""",command=>HrmsDatabase.AddParameter(command,"@Resource",$"ZYNORA.ApprovalDecision.{requestId}"));
        if(result<0) throw new TimeoutException("تعذر حجز الطلب للقرار؛ حاول مرة أخرى.");
    }

    public sealed record ActionResult(bool Ok, string Message, bool FinalApproved = false, bool Rejected = false);

    public static async Task<ActionResult> ApproveAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, int requestId, string actor, string? note,
        IEnumerable<string> actorRoles, int? actorEmployeeId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        // الاعتماد يقدّم الطلب نحو أثرٍ ماليّ (قرض/بدل/زيادة) على موظف. المعرّف من
        // النموذج ⟹ يجب أن يخصّ موظفاً ضمن نطاق المُعتمِد. مغلق الفشل: طلبٌ خارج
        // النطاق يُعامَل كأنه غير موجود فلا يُستدلّ على طلبات شركةٍ أخرى.
        if (!await Security.EmployeeCompanyGuard.CanAccessOwnedRowAsync(
                dbContext, Security.EmployeeCompanyGuard.Tables.SelfServiceRequests, "Id", requestId, scope))
            return new ActionResult(false, "الطلب غير موجود أو خارج نطاق صلاحيتك.");

        var flow = await GetFlowAsync(dbContext, requestId);
        if (flow == null || flow.CurrentSteps.Count == 0)
        {
            return new ActionResult(false, "لا توجد خطوة حالية لهذا الطلب.");
        }

        if (await IsRequesterAsync(dbContext, requestId, actor, actorEmployeeId))
            return new ActionResult(false, "لا يمكن لصاحب الطلب اعتماد طلبه بنفسه.");

        var selected=await FindAuthorizedCurrentAsync(dbContext,flow,requestId,actor,actorRoles,actorEmployeeId);
        if (selected is null)
            return new ActionResult(false, "لا تملك صلاحية البتّ بالخطوة الحالية.");
        var current=selected.Step; var authorization=selected.Authorization;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await AcquireDecisionLockAsync(dbContext,requestId);
        var claimed = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
DECLARE @Changed int;
UPDATE ApprovalRequestSteps
SET Status = 'Approved', ActionBy = @Actor, ActionAt = SYSUTCDATETIME(), Note = @Note
    , DelegatedFrom = @DelegatedFrom
WHERE Id = @StepId AND Status = 'Current';
SET @Changed = @@ROWCOUNT;

IF @Changed = 1
INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes, DelegatedFrom)
VALUES (@RequestId, @StepName, 'Approved', @Actor, @Note, @DelegatedFrom);

SELECT @Changed;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@StepId", current.Id);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(command, "@Note", (object?)note ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@StepName", current.DisplayName);
                HrmsDatabase.AddParameter(command, "@DelegatedFrom", (object?)authorization.DelegatedFrom ?? DBNull.Value);
            });
        if (claimed != 1)
            return new ActionResult(false, "سبق البتّ بهذه الخطوة أو تغيّرت حالتها.");

        var refreshed=await GetFlowAsync(dbContext,requestId);
        if(refreshed!.CurrentSteps.Any(step=>step.StageOrder==current.StageOrder))
        {
            var remaining=refreshed.CurrentSteps.Count(step=>step.StageOrder==current.StageOrder);
            await HrmsDatabase.ExecuteAsync(dbContext,
                "UPDATE SelfServiceRequests SET CurrentStep=@Step,UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id AND Status='Pending';",
                command=>
                {
                    HrmsDatabase.AddParameter(command,"@Step",$"المرحلة {current.StageOrder}: بانتظار {remaining} قرار");
                    HrmsDatabase.AddParameter(command,"@Id",requestId);
                });
            await transaction.CommitAsync();
            await DispatchConfiguredNotificationsAsync(dbContext, requestId, "Approve", "تحديث موافقة",
                $"سُجّلت موافقة في الطلب رقم {requestId} وما زالت المرحلة بانتظار قرارات أخرى.");
            return new ActionResult(true,$"تم تسجيل قرارك؛ ما زالت المرحلة المتوازية بانتظار {remaining} قرار.");
        }

        var nextStage=refreshed.Steps.Where(step=>step.Status=="Pending")
            .OrderBy(step=>step.StageOrder).Select(step=>(int?)step.StageOrder).FirstOrDefault();
        if (nextStage is null)
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
            await transaction.CommitAsync();
            await DispatchConfiguredNotificationsAsync(dbContext, requestId, "Approve", "طلب معتمد",
                $"اكتملت الموافقات على الطلب رقم {requestId}.");
            return new ActionResult(true, "تم اعتماد الطلب نهائياً — اكتملت اللجنة.", FinalApproved: true);
        }

        var nextSteps=refreshed.Steps.Where(step=>step.Status=="Pending"&&step.StageOrder==nextStage.Value).OrderBy(step=>step.StepOrder).ToList();
        var nextName=string.Join(" + ",nextSteps.Select(step=>step.DisplayName));
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE ApprovalRequestSteps SET Status = 'Current', CurrentSince = SYSUTCDATETIME()
WHERE RequestId=@Id AND StageOrder=@NextStage AND Status='Pending';

UPDATE SelfServiceRequests
SET CurrentStep = @NextName, ManagerStatus = 'Approved', ManagerReviewedBy = @Actor, ManagerReviewedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@NextStage", nextStage.Value);
                HrmsDatabase.AddParameter(command, "@NextName", nextName);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(command, "@Id", requestId);
            });
        await transaction.CommitAsync();
        await NotifyCurrentApproversAsync(dbContext, requestId, "طلب بانتظار موافقتك", $"وصل الطلب إلى مرحلة: {nextName}.");
        await DispatchConfiguredNotificationsAsync(dbContext, requestId, "Approve", "انتقال طلب",
            $"انتقل الطلب رقم {requestId} إلى مرحلة: {nextName}.");
        return new ActionResult(true, $"تمت الموافقة وانتقل الطلب إلى: {nextName}.");
    }

    public static async Task<ActionResult> RejectAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, int requestId, string actor, string? note,
        IEnumerable<string> actorRoles, int? actorEmployeeId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        // الرفض كتابةٌ على طلب موظف بمعرّفٍ من النموذج — يُفحَص بالنطاق كالاعتماد.
        if (!await Security.EmployeeCompanyGuard.CanAccessOwnedRowAsync(
                dbContext, Security.EmployeeCompanyGuard.Tables.SelfServiceRequests, "Id", requestId, scope))
            return new ActionResult(false, "الطلب غير موجود أو خارج نطاق صلاحيتك.");

        var flow = await GetFlowAsync(dbContext, requestId);
        if (flow == null || flow.CurrentSteps.Count==0)
        {
            return new ActionResult(false, "لا توجد خطوة حالية لهذا الطلب.");
        }

        if (await IsRequesterAsync(dbContext, requestId, actor, actorEmployeeId))
            return new ActionResult(false, "لا يمكن لصاحب الطلب رفض طلبه من مركز الموافقات.");

        var selected=await FindAuthorizedCurrentAsync(dbContext,flow,requestId,actor,actorRoles,actorEmployeeId);
        if (selected is null)
            return new ActionResult(false, "لا تملك صلاحية البتّ بالخطوة الحالية.");
        var current=selected.Step; var authorization=selected.Authorization;

        if (flow.CommentRequiredOnReject && string.IsNullOrWhiteSpace(note))
        {
            return new ActionResult(false, "قالب الموافقة يشترط كتابة تعليق عند الرفض.");
        }

        await using var transaction=await dbContext.Database.BeginTransactionAsync();
        await AcquireDecisionLockAsync(dbContext,requestId);
        var claimed = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
DECLARE @Changed int;
UPDATE ApprovalRequestSteps
SET Status = 'Rejected', ActionBy = @Actor, ActionAt = SYSUTCDATETIME(), Note = @Note, DelegatedFrom=@DelegatedFrom
WHERE Id = @StepId AND Status = 'Current';
SET @Changed = @@ROWCOUNT;

IF @Changed = 1
BEGIN
UPDATE ApprovalRequestSteps SET Status = 'Skipped' WHERE RequestId = @RequestId AND Status IN ('Pending','Current');

UPDATE SelfServiceRequests
SET Status = 'Rejected', CurrentStep = 'Rejected', ReviewedBy = @Actor, ReviewNote = @Note,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @RequestId;

INSERT INTO ApprovalHistories (RequestId, StepName, Action, ActionBy, Notes, DelegatedFrom)
VALUES (@RequestId, @StepName, 'Rejected', @Actor, @Note, @DelegatedFrom);

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'طلب مرفوض', N'تم رفض الطلب في خطوة: ' + @StepName, 'Employee', '/SelfServices');
END;

SELECT @Changed;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@StepId", current.Id);
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(command, "@Note", (object?)note ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@StepName", current.DisplayName);
                HrmsDatabase.AddParameter(command, "@DelegatedFrom", (object?)authorization.DelegatedFrom ?? DBNull.Value);
            });
        if (claimed != 1)
            return new ActionResult(false, "سبق البتّ بهذه الخطوة أو تغيّرت حالتها.");
        await transaction.CommitAsync();
        await DispatchConfiguredNotificationsAsync(dbContext, requestId, "Reject", "طلب مرفوض",
            $"رُفض الطلب رقم {requestId} في خطوة {current.DisplayName}.");
        return new ActionResult(true, "تم رفض الطلب.", Rejected: true);
    }

    /// <summary>يعيد الطلب لصاحبه للتعديل مع تجميد الخطوة الحالية حتى إعادة التقديم.</summary>
    public static async Task<ActionResult> ReturnForRevisionAsync(
        ApplicationDbContext dbContext, Security.CompanyScope scope, int requestId, string actor, string? note,
        IEnumerable<string> actorRoles, int? actorEmployeeId)
    {
        if (string.IsNullOrWhiteSpace(note))
            return new ActionResult(false, "سبب الإرجاع للتعديل إلزامي.");
        if (!await Security.EmployeeCompanyGuard.CanAccessOwnedRowAsync(
                dbContext, Security.EmployeeCompanyGuard.Tables.SelfServiceRequests, "Id", requestId, scope))
            return new ActionResult(false, "الطلب غير موجود أو خارج نطاق صلاحيتك.");
        var flow = await GetFlowAsync(dbContext, requestId);
        if (flow is null||flow.CurrentSteps.Count==0) return new ActionResult(false, "لا توجد خطوة حالية لهذا الطلب.");
        if (await IsRequesterAsync(dbContext, requestId, actor, actorEmployeeId))
            return new ActionResult(false, "لا يمكن لصاحب الطلب إرجاع طلبه من مركز الموافقات.");
        var selected=await FindAuthorizedCurrentAsync(dbContext,flow,requestId,actor,actorRoles,actorEmployeeId);
        if (selected is null)
            return new ActionResult(false, "لا تملك صلاحية البتّ بالخطوة الحالية.");
        var current=selected.Step; var authorization=selected.Authorization;

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await AcquireDecisionLockAsync(dbContext,requestId);
        var changed = await HrmsDatabase.ScalarAsync<int>(dbContext, """
DECLARE @Changed int,@RequestChanged int;
UPDATE s SET Status='Returned',ActionBy=@Actor,ActionAt=SYSUTCDATETIME(),Note=@Note,DelegatedFrom=@DelegatedFrom
FROM ApprovalRequestSteps s
INNER JOIN SelfServiceRequests r WITH(UPDLOCK,HOLDLOCK) ON r.Id=s.RequestId
WHERE s.Id=@StepId AND s.RequestId=@RequestId AND s.Status='Current' AND r.Status='Pending';
SET @Changed=@@ROWCOUNT;
IF @Changed=1
BEGIN
 UPDATE ApprovalRequestSteps SET Status='WaitingRevision'
 WHERE RequestId=@RequestId AND StageOrder=@StageOrder AND Status='Current';
 UPDATE SelfServiceRequests SET Status='Returned',CurrentStep=N'بانتظار تعديل الموظف',ReviewNote=@Note,ReviewedBy=@Actor,UpdatedAt=SYSUTCDATETIME() WHERE Id=@RequestId AND Status='Pending';
 SET @RequestChanged=@@ROWCOUNT;
 IF @RequestChanged=1
 BEGIN
  INSERT INTO ApprovalHistories(RequestId,StepName,Action,ActionBy,Notes,DelegatedFrom) VALUES(@RequestId,@StepName,'Returned',@Actor,@Note,@DelegatedFrom);
  INSERT INTO SystemNotifications(Title,Message,TargetRole,Url) VALUES(N'طلب يحتاج تعديلاً',N'أُعيد طلبك للتعديل: '+@Note,'Employee','/EmployeePortal?tab=requests');
 END
 ELSE
 BEGIN
  UPDATE ApprovalRequestSteps SET Status='Current',ActionBy=NULL,ActionAt=NULL,Note=NULL,DelegatedFrom=NULL WHERE Id=@StepId AND Status='Returned';
  SET @Changed=0;
 END
END;
SELECT @Changed;
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@StepId",current.Id); HrmsDatabase.AddParameter(command,"@RequestId",requestId);
            HrmsDatabase.AddParameter(command,"@Actor",actor); HrmsDatabase.AddParameter(command,"@Note",note.Trim());
            HrmsDatabase.AddParameter(command,"@StepName",current.DisplayName);
            HrmsDatabase.AddParameter(command,"@StageOrder",current.StageOrder);
            HrmsDatabase.AddParameter(command,"@DelegatedFrom",(object?)authorization.DelegatedFrom??DBNull.Value);
        });
        if (changed != 1) return new ActionResult(false,"سبق البتّ بهذه الخطوة أو تغيّرت حالتها.");
        await transaction.CommitAsync();
        return new ActionResult(true,"أُعيد الطلب إلى الموظف للتعديل وإعادة التقديم.");
    }

    /// <summary>إعادة تقديم الموظف تحفظ لقطة المسار الأصلية وتعيد نفس الخطوة الحالية.</summary>
    public static async Task<ActionResult> ResubmitReturnedAsync(
        ApplicationDbContext dbContext, int requestId, int employeeId,
        string reason, DateTime? fromDate, DateTime? toDate)
    {
        if (employeeId <= 0 || string.IsNullOrWhiteSpace(reason))
            return new ActionResult(false,"اكتب تفاصيل التعديل قبل إعادة التقديم.");
        if (fromDate.HasValue && toDate.HasValue && toDate.Value.Date < fromDate.Value.Date)
            return new ActionResult(false,"تاريخ النهاية لا يمكن أن يسبق البداية.");
        await EnsureAsync(dbContext);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var changed = await HrmsDatabase.ScalarAsync<int>(dbContext,"""
DECLARE @StepId int,@StepName nvarchar(150),@StageOrder int;
SELECT TOP(1) @StepId=s.Id,@StepName=s.DisplayName,@StageOrder=s.StageOrder
FROM ApprovalRequestSteps s WITH(UPDLOCK,ROWLOCK)
INNER JOIN SelfServiceRequests r ON r.Id=s.RequestId
WHERE s.RequestId=@RequestId AND r.EmployeeId=@EmployeeId AND r.Status='Returned' AND s.Status='Returned'
ORDER BY s.StepOrder;
IF @StepId IS NULL BEGIN SELECT 0; RETURN; END;
UPDATE SelfServiceRequests SET Reason=@Reason,FromDate=COALESCE(@FromDate,FromDate),ToDate=COALESCE(@ToDate,ToDate),
 Status='Pending',CurrentStep=@StepName,UpdatedAt=SYSUTCDATETIME() WHERE Id=@RequestId AND EmployeeId=@EmployeeId AND Status='Returned';
IF @@ROWCOUNT=0 BEGIN SELECT 0; RETURN; END;
UPDATE ApprovalRequestSteps SET Status='Current',CurrentSince=SYSUTCDATETIME(),ActionBy=NULL,ActionAt=NULL,Note=NULL,DelegatedFrom=NULL,
 ReminderSentAt=NULL,EscalatedAt=NULL,EscalatedToRole=NULL,EscalatedToUser=NULL
WHERE RequestId=@RequestId AND StageOrder=@StageOrder AND Status IN ('Returned','WaitingRevision');
IF @@ROWCOUNT=0 BEGIN SELECT 0; RETURN; END;
INSERT INTO ApprovalHistories(RequestId,StepName,Action,ActionBy,Notes) VALUES(@RequestId,@StepName,'Resubmitted',CONVERT(nvarchar(30),@EmployeeId),@Reason);
SELECT 1;
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@RequestId",requestId); HrmsDatabase.AddParameter(command,"@EmployeeId",employeeId);
            HrmsDatabase.AddParameter(command,"@Reason",reason.Trim()); HrmsDatabase.AddParameter(command,"@FromDate",(object?)fromDate??DBNull.Value);
            HrmsDatabase.AddParameter(command,"@ToDate",(object?)toDate??DBNull.Value);
        });
        if (changed != 1) return new ActionResult(false,"الطلب ليس معاداً إليك أو سبق إعادة تقديمه.");
        await transaction.CommitAsync();
        return new ActionResult(true,"أُعيد تقديم الطلب إلى نفس خطوة الموافقة.");
    }

    /// <summary>
    /// إلغاء صاحب الطلب ضمن المهلة المجمدة وقت التقديم. لا يقبل معرّف موظف من
    /// النموذج بلا مطابقة مالك الصف، ولا يلغي طلباً منتهياً أو مطبق الأثر.
    /// </summary>
    public static async Task<ActionResult> CancelByRequesterAsync(
        ApplicationDbContext dbContext, int requestId, int employeeId, string actor, string? note = null)
    {
        if (requestId <= 0 || employeeId <= 0) return new ActionResult(false,"الطلب غير صالح.");
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(dbContext, """
SELECT r.Status,r.CreatedAt,f.CancelLimitDays
FROM SelfServiceRequests r
INNER JOIN Employees e ON e.Id=r.EmployeeId AND ISNULL(e.IsDeleted,0)=0
LEFT JOIN ApprovalRequestFlows f ON f.RequestId=r.Id
WHERE r.Id=@RequestId AND r.EmployeeId=@EmployeeId;
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@RequestId",requestId);
            HrmsDatabase.AddParameter(command,"@EmployeeId",employeeId);
        }, reader => new
        {
            Status=HrmsDatabase.GetString(reader,"Status"),
            CreatedAt=HrmsDatabase.GetDateTime(reader,"CreatedAt") ?? DateTime.UtcNow,
            Limit=HrmsDatabase.GetNullableInt(reader,"CancelLimitDays")
        });
        var request=rows.FirstOrDefault();
        if(request is null) return new ActionResult(false,"الطلب غير موجود أو لا يخصك.");
        if(request.Status is not ("Pending" or "Returned" or "Draft"))
            return new ActionResult(false,"لا يمكن إلغاء طلب منتهٍ أو مطبّق.");
        if(request.Limit is int days && DateTime.UtcNow > request.CreatedAt.AddDays(days))
            return new ActionResult(false,$"انتهت مهلة الإلغاء المحددة بـ {days} يوم.");

        await using var transaction=await dbContext.Database.BeginTransactionAsync();
        await AcquireDecisionLockAsync(dbContext,requestId);
        var changed=await HrmsDatabase.ScalarAsync<int>(dbContext,"""
UPDATE SelfServiceRequests
SET Status='Cancelled',CurrentStep='Cancelled',ReviewNote=@Note,ReviewedBy=@Actor,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@RequestId AND EmployeeId=@EmployeeId AND Status IN ('Pending','Returned','Draft');
DECLARE @Changed int=@@ROWCOUNT;
IF @Changed=1
BEGIN
 UPDATE ApprovalRequestSteps SET Status='Cancelled',ActionBy=@Actor,ActionAt=SYSUTCDATETIME(),Note=@Note
 WHERE RequestId=@RequestId AND Status IN ('Current','Pending','Returned','WaitingRevision');
 INSERT INTO ApprovalHistories(RequestId,StepName,Action,ActionBy,Notes)
 VALUES(@RequestId,N'صاحب الطلب','Cancelled',@Actor,@Note);
END;
SELECT @Changed;
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@RequestId",requestId);
            HrmsDatabase.AddParameter(command,"@EmployeeId",employeeId);
            HrmsDatabase.AddParameter(command,"@Actor",actor);
            HrmsDatabase.AddParameter(command,"@Note",(object?)note??DBNull.Value);
        });
        if(changed!=1) return new ActionResult(false,"تغيّرت حالة الطلب ولم يعد قابلاً للإلغاء.");
        await transaction.CommitAsync();
        await DispatchConfiguredNotificationsAsync(dbContext,requestId,"Cancel","طلب ملغي",
            $"ألغى صاحب الطلب الطلب رقم {requestId}.");
        return new ActionResult(true,"تم إلغاء الطلب.");
    }

    private static async Task<bool> HasRequestAttachmentAsync(ApplicationDbContext dbContext,int requestId,int employeeId) =>
        await HrmsDatabase.ScalarAsync<int>(dbContext,"""
SELECT COUNT(1) FROM SelfServiceRequests
WHERE Id=@RequestId AND EmployeeId=@EmployeeId AND NULLIF(LTRIM(RTRIM(ISNULL(AttachmentPath,N''))),N'') IS NOT NULL;
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@RequestId",requestId);
            HrmsDatabase.AddParameter(command,"@EmployeeId",employeeId);
        }) > 0;

    private static async Task<List<string>> FindUnresolvedCommitteeAsync(
        ApplicationDbContext dbContext,int employeeId,int companyId,IEnumerable<ApprovalTemplateStore.StepRow> steps)
    {
        var unresolved=new List<string>();
        foreach(var step in steps)
        {
            if(step.ApproverType.Equals("DirectManager",StringComparison.OrdinalIgnoreCase))
            {
                var found=await HrmsDatabase.ScalarAsync<int>(dbContext,"""
SELECT COUNT(1)
FROM Employees requester
INNER JOIN Employees manager ON manager.Id=requester.DirectManagerId AND manager.CompanyId=requester.CompanyId
INNER JOIN SystemUsers u ON u.EmployeeId=manager.Id AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
WHERE requester.Id=@EmployeeId AND requester.CompanyId=@CompanyId
 AND ISNULL(requester.IsDeleted,0)=0 AND ISNULL(manager.IsDeleted,0)=0 AND ISNULL(manager.IsActive,1)=1;
""", command =>
                {
                    HrmsDatabase.AddParameter(command,"@EmployeeId",employeeId);
                    HrmsDatabase.AddParameter(command,"@CompanyId",companyId);
                });
                if(found==0) unresolved.Add(step.DisplayName);
            }
            else if(step.ApproverType.Equals("User",StringComparison.OrdinalIgnoreCase))
            {
                var found=await HrmsDatabase.ScalarAsync<int>(dbContext,"""
SELECT COUNT(1) FROM SystemUsers u
INNER JOIN Employees e ON e.Id=u.EmployeeId AND ISNULL(e.IsDeleted,0)=0
WHERE u.UserName=@UserName AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0 AND e.CompanyId=@CompanyId;
""", command =>
                {
                    HrmsDatabase.AddParameter(command,"@UserName",step.UserName ?? string.Empty);
                    HrmsDatabase.AddParameter(command,"@CompanyId",companyId);
                });
                if(found==0) unresolved.Add(step.DisplayName);
            }
            else if (step.ApproverType.Equals("CommitteeGroup", StringComparison.OrdinalIgnoreCase))
            {
                var found = await HrmsDatabase.ScalarAsync<int>(dbContext, """
SELECT COUNT(1)
FROM ApprovalCommitteeGroups g
INNER JOIN ApprovalCommitteeGroupMembers m ON m.GroupId=g.Id
INNER JOIN SystemUsers u ON u.UserName=m.UserName AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
INNER JOIN Employees e ON e.Id=u.EmployeeId AND e.CompanyId=g.CompanyId AND ISNULL(e.IsDeleted,0)=0
WHERE g.Id=@GroupId AND g.CompanyId=@CompanyId AND g.IsActive=1;
""", command =>
                {
                    HrmsDatabase.AddParameter(command, "@GroupId", (object?)step.CommitteeGroupId ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                });
                if (found == 0) unresolved.Add(step.DisplayName);
            }
            else if (step.ApproverType.Equals("ExternalCommittee", StringComparison.OrdinalIgnoreCase))
            {
                var found = await HrmsDatabase.ScalarAsync<int>(dbContext, """
SELECT COUNT(1) FROM ApprovalExternalCommittees WHERE Id=@CommitteeId AND CompanyId=@CompanyId AND IsActive=1;
""", command =>
                {
                    HrmsDatabase.AddParameter(command, "@CommitteeId", (object?)step.ExternalCommitteeId ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                });
                if (found == 0) unresolved.Add(step.DisplayName);
            }
        }
        return unresolved.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Task BlockSubmissionAsync(ApplicationDbContext dbContext,int requestId,string status,string currentStep,string action,string reason) =>
        HrmsDatabase.ExecuteAsync(dbContext,"""
UPDATE SelfServiceRequests SET Status=@Status,CurrentStep=@CurrentStep,ReviewNote=@Reason,UpdatedAt=SYSUTCDATETIME() WHERE Id=@RequestId;
INSERT INTO ApprovalHistories(RequestId,StepName,Action,ActionBy,Notes)
VALUES(@RequestId,N'فحص التقديم',@Action,N'System',@Reason);
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@RequestId",requestId);
            HrmsDatabase.AddParameter(command,"@Status",status);
            HrmsDatabase.AddParameter(command,"@CurrentStep",currentStep);
            HrmsDatabase.AddParameter(command,"@Action",action);
            HrmsDatabase.AddParameter(command,"@Reason",reason);
        });

    private static async Task DispatchConfiguredNotificationsAsync(
        ApplicationDbContext dbContext,int requestId,string eventName,string title,string message)
    {
        var rows=await HrmsDatabase.QueryAsync(dbContext,
            "SELECT NotifyJson FROM ApprovalRequestFlows WHERE RequestId=@RequestId;",
            command=>HrmsDatabase.AddParameter(command,"@RequestId",requestId),
            reader=>HrmsDatabase.GetString(reader,"NotifyJson"));
        var notifyJson=rows.FirstOrDefault();
        var notifyEmployee=ShouldNotify(notifyJson,"Employee",eventName);
        var notifyCommittee=ShouldNotify(notifyJson,"Committee",eventName);

        // المراقبون لقطةٌ من القالب؛ تصلهم أحداث الدورة دائماً بصفة مشاهدة فقط.
        await HrmsDatabase.ExecuteAsync(dbContext,"""
INSERT INTO SystemNotifications(Title,Message,TargetUser,Url)
SELECT @Title,@Message,w.UserName,'/Approvals'
FROM ApprovalRequestWatchers w WHERE w.RequestId=@RequestId;
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@RequestId",requestId);
            HrmsDatabase.AddParameter(command,"@Title",title);
            HrmsDatabase.AddParameter(command,"@Message",message);
        });

        if(notifyEmployee)
            await HrmsDatabase.ExecuteAsync(dbContext,"""
INSERT INTO SystemNotifications(Title,Message,TargetUser,Url)
SELECT @Title,@Message,u.UserName,'/EmployeePortal?tab=requests'
FROM SelfServiceRequests r
INNER JOIN Employees e ON e.Id=r.EmployeeId AND ISNULL(e.IsDeleted,0)=0
INNER JOIN SystemUsers u ON u.EmployeeId=e.Id AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
WHERE r.Id=@RequestId;
""", command =>
            {
                HrmsDatabase.AddParameter(command,"@RequestId",requestId);
                HrmsDatabase.AddParameter(command,"@Title",title);
                HrmsDatabase.AddParameter(command,"@Message",message);
            });

        if(notifyCommittee)
        {
            var current=await GetFlowAsync(dbContext,requestId);
            var targets=current?.CurrentSteps.Count>0
                ? current.CurrentSteps
                : current?.Steps.Where(step=>step.Status is "Approved" or "Rejected" or "Returned" or "Cancelled").ToList()
                  ?? new List<StepState>();
            foreach(var step in targets)
            {
                if(step.ApproverType=="Role")
                    await InsertNotificationAsync(dbContext,title,message,step.RoleName,null,"/Approvals");
                else if(step.ApproverType=="User")
                    await InsertNotificationAsync(dbContext,title,message,null,step.UserName,"/Approvals");
                else if(step.ApproverType=="CommitteeGroup")
                {
                    foreach (var user in await FrozenCommitteeMembersAsync(dbContext, step.Id))
                        await InsertNotificationAsync(dbContext,title,message,null,user,"/Approvals");
                }
                else if(step.ApproverType=="ExternalCommittee")
                    await InsertNotificationAsync(dbContext,title,message,"HR Manager",null,"/Approvals");
                else if(step.ApproverType=="DirectManager")
                {
                    var users=await HrmsDatabase.QueryAsync(dbContext,"""
SELECT u.UserName FROM SelfServiceRequests r
INNER JOIN Employees requester ON requester.Id=r.EmployeeId AND ISNULL(requester.IsDeleted,0)=0
INNER JOIN Employees manager ON manager.Id=requester.DirectManagerId AND manager.CompanyId=requester.CompanyId AND ISNULL(manager.IsDeleted,0)=0
INNER JOIN SystemUsers u ON u.EmployeeId=manager.Id AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
WHERE r.Id=@RequestId;
""",command=>HrmsDatabase.AddParameter(command,"@RequestId",requestId),reader=>HrmsDatabase.GetString(reader,"UserName"));
                    foreach(var user in users) await InsertNotificationAsync(dbContext,title,message,null,user,"/Approvals");
                }
            }
        }
    }

    private static async Task NotifyCurrentApproversAsync(
        ApplicationDbContext dbContext, int requestId, string title, string message)
    {
        var flow = await GetFlowAsync(dbContext, requestId);
        if (flow is null) return;
        foreach (var step in flow.CurrentSteps)
        {
            if (step.ApproverType == "Role")
                await InsertNotificationAsync(dbContext, title, message, step.RoleName, null, "/Approvals");
            else if (step.ApproverType == "User")
                await InsertNotificationAsync(dbContext, title, message, null, step.UserName, "/Approvals");
            else if (step.ApproverType == "CommitteeGroup")
            {
                foreach (var user in await FrozenCommitteeMembersAsync(dbContext, step.Id))
                    await InsertNotificationAsync(dbContext, title, message, null, user, "/Approvals");
            }
            else if (step.ApproverType == "ExternalCommittee")
                await InsertNotificationAsync(dbContext, title, message, "HR Manager", null, "/Approvals");
            else if (step.ApproverType == "DirectManager")
            {
                var users = await HrmsDatabase.QueryAsync(dbContext, """
SELECT u.UserName FROM SelfServiceRequests r
INNER JOIN Employees requester ON requester.Id=r.EmployeeId AND ISNULL(requester.IsDeleted,0)=0
INNER JOIN Employees manager ON manager.Id=requester.DirectManagerId AND manager.CompanyId=requester.CompanyId AND ISNULL(manager.IsDeleted,0)=0
INNER JOIN SystemUsers u ON u.EmployeeId=manager.Id AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
WHERE r.Id=@RequestId;
""", command => HrmsDatabase.AddParameter(command, "@RequestId", requestId),
                    reader => HrmsDatabase.GetString(reader, "UserName"));
                foreach (var user in users) await InsertNotificationAsync(dbContext, title, message, null, user, "/Approvals");
            }
        }
    }

    private static Task<List<string>> FrozenCommitteeMembersAsync(ApplicationDbContext dbContext, int stepId) =>
        HrmsDatabase.QueryAsync(dbContext,
            "SELECT UserName FROM ApprovalRequestStepMembers WHERE StepId=@StepId ORDER BY Id;",
            command => HrmsDatabase.AddParameter(command, "@StepId", stepId),
            reader => HrmsDatabase.GetString(reader, "UserName"));

    private static async Task InsertNotificationAsync(ApplicationDbContext dbContext,string title,string message,string? role,string? user,string url)
    {
        if(string.IsNullOrWhiteSpace(role)&&string.IsNullOrWhiteSpace(user)) return;
        await HrmsDatabase.ExecuteAsync(dbContext,
            "INSERT INTO SystemNotifications(Title,Message,TargetRole,TargetUser,Url) VALUES(@Title,@Message,@Role,@User,@Url);",
            command=>
            {
                HrmsDatabase.AddParameter(command,"@Title",title);HrmsDatabase.AddParameter(command,"@Message",message);
                HrmsDatabase.AddParameter(command,"@Role",(object?)role??DBNull.Value);HrmsDatabase.AddParameter(command,"@User",(object?)user??DBNull.Value);
                HrmsDatabase.AddParameter(command,"@Url",url);
            });
    }

    private static bool ShouldNotify(string? json,string audience,string eventName)
    {
        if(string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document=JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(audience,out var events) && events.ValueKind==JsonValueKind.Array &&
                   events.EnumerateArray().Any(value=>string.Equals(value.GetString(),eventName,StringComparison.OrdinalIgnoreCase));
        }
        catch(JsonException){return false;}
    }

    /// <summary>
    /// الأنواع الداينمكية تحمل أسماء تفصيلية (إجازة سنوية/مغادرة شخصية)، بينما
    /// القوالب مفاتيح موديول ثابتة. التطبيع هنا يمنع سقوطها الصامت للمسار الافتراضي.
    /// </summary>
    public static string ResolveRequestTypeKey(string? requestType)
    {
        var value = requestType?.Trim() ?? string.Empty;
        if (RequestTypeMap.TryGetValue(value, out var mapped)) return mapped;
        if (value.Contains("تعديل", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("بيانات", StringComparison.OrdinalIgnoreCase)) return "InfoChange";
        if (value.Contains("نسيان بصمة", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("missing punch", StringComparison.OrdinalIgnoreCase)) return "MissingPunch";
        if (value.Contains("مغادرة", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("خروج", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("exit", StringComparison.OrdinalIgnoreCase)) return "ExitPermission";
        if (value.Contains("أوفر", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("إضافي", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("overtime", StringComparison.OrdinalIgnoreCase)) return "Overtime";
        if (value.Contains("إجاز", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("leave", StringComparison.OrdinalIgnoreCase)) return "LeaveRequest";
        return value;
    }

    private static async Task<bool> IsRequesterManagerAsync(
        ApplicationDbContext dbContext, int requestId, int? actorEmployeeId)
    {
        if (actorEmployeeId is not > 0) return false;
        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
SELECT COUNT(1)
FROM SelfServiceRequests r
JOIN Employees e ON e.Id = r.EmployeeId AND ISNULL(e.IsDeleted, 0) = 0
WHERE r.Id = @RequestId AND e.DirectManagerId = @ActorEmployeeId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@ActorEmployeeId", actorEmployeeId.Value);
            }) > 0;
    }

    private static async Task<bool> IsRequesterAsync(
        ApplicationDbContext dbContext, int requestId, string actor, int? actorEmployeeId) =>
        await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
SELECT COUNT(1)
FROM SelfServiceRequests r
WHERE r.Id = @RequestId
  AND ((@ActorEmployeeId IS NOT NULL AND r.EmployeeId = @ActorEmployeeId)
       OR (ISNULL(r.CreatedBy, N'') <> N'' AND r.CreatedBy = @Actor));
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(command, "@ActorEmployeeId", actorEmployeeId is > 0 ? actorEmployeeId.Value : DBNull.Value);
            }) > 0;

    public sealed record SlaResult(int Reminded,int Escalated);

    /// <summary>يرسل تذكيراً واحداً لكل خطوة، ثم يفعّل الدور/البديل للقرار عند تجاوز SLA.</summary>
    public static async Task<SlaResult> ProcessSlaAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        var rows=await HrmsDatabase.QueryAsync(
            dbContext,
            """
DECLARE @Reminded TABLE(StepId int,RequestId int,StepName nvarchar(150),ApproverType nvarchar(20),RoleName nvarchar(50),UserName nvarchar(150));
DECLARE @Escalated TABLE(RequestId int,StepName nvarchar(150),TargetRole nvarchar(50),TargetUser nvarchar(100));

UPDATE s SET ReminderSentAt=SYSUTCDATETIME()
OUTPUT inserted.Id,inserted.RequestId,inserted.DisplayName,inserted.ApproverType,inserted.RoleName,inserted.UserName INTO @Reminded
FROM ApprovalRequestSteps s
INNER JOIN ApprovalRequestFlows f ON f.RequestId=s.RequestId
WHERE s.Status='Current' AND s.ReminderSentAt IS NULL AND s.CurrentSince IS NOT NULL
 AND f.ReminderHours IS NOT NULL AND DATEDIFF(hour,s.CurrentSince,SYSUTCDATETIME())>=f.ReminderHours;

INSERT INTO SystemNotifications(Title,Message,TargetRole,TargetUser,Url)
SELECT N'تذكير موافقة',N'الطلب رقم '+CAST(m.RequestId AS nvarchar(20))+N' بانتظار قرارك: '+m.StepName,
 CASE WHEN m.ApproverType='Role' THEN m.RoleName WHEN m.ApproverType='DirectManager' AND managerUser.UserName IS NULL THEN 'HR' WHEN m.ApproverType='ExternalCommittee' THEN 'HR Manager' END,
 CASE WHEN m.ApproverType='User' THEN m.UserName WHEN m.ApproverType='DirectManager' THEN managerUser.UserName END,'/Approvals'
FROM @Reminded m
LEFT JOIN SelfServiceRequests r ON r.Id=m.RequestId
LEFT JOIN Employees requester ON requester.Id=r.EmployeeId
OUTER APPLY(SELECT TOP(1) u.UserName FROM SystemUsers u
 INNER JOIN Employees manager ON manager.Id=u.EmployeeId AND ISNULL(manager.IsDeleted,0)=0
 WHERE manager.Id=requester.DirectManagerId AND manager.CompanyId=requester.CompanyId
 AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0) managerUser;

INSERT INTO SystemNotifications(Title,Message,TargetUser,Url)
SELECT N'تذكير موافقة',N'الطلب رقم '+CAST(m.RequestId AS nvarchar(20))+N' بانتظار قرارك: '+m.StepName,
       member.UserName,'/Approvals'
FROM @Reminded m
INNER JOIN ApprovalRequestStepMembers member ON member.StepId=m.StepId
WHERE m.ApproverType='CommitteeGroup';

UPDATE s SET EscalatedAt=SYSUTCDATETIME(),EscalatedToRole=COALESCE(f.EscalationTo,'HR Manager'),EscalatedToUser=f.EscalationAlternateUser
OUTPUT inserted.RequestId,inserted.DisplayName,inserted.EscalatedToRole,inserted.EscalatedToUser INTO @Escalated
FROM ApprovalRequestSteps s INNER JOIN ApprovalRequestFlows f ON f.RequestId=s.RequestId
WHERE s.Status='Current' AND s.EscalatedAt IS NULL AND s.CurrentSince IS NOT NULL
 AND f.EscalationDays IS NOT NULL AND DATEDIFF(day,s.CurrentSince,SYSUTCDATETIME())>=f.EscalationDays;

UPDATE f SET Escalated=1 FROM ApprovalRequestFlows f WHERE EXISTS(SELECT 1 FROM @Escalated e WHERE e.RequestId=f.RequestId);

INSERT INTO SystemNotifications (Title, Message, TargetRole,TargetUser, Url)
SELECT N'تصعيد طلب متأخر',N'الطلب رقم '+CAST(RequestId AS nvarchar(20))+N' متأخر في خطوة: '+StepName,
       TargetRole,TargetUser,'/Approvals'
FROM @Escalated;

SELECT (SELECT COUNT(*) FROM @Reminded) AS Reminded,(SELECT COUNT(*) FROM @Escalated) AS Escalated;
""",null,reader=>new SlaResult(HrmsDatabase.GetInt(reader,"Reminded"),HrmsDatabase.GetInt(reader,"Escalated")));
        return rows.Single();
    }

    public static async Task<int> EscalateOverdueAsync(ApplicationDbContext dbContext)=>(await ProcessSlaAsync(dbContext)).Escalated;

    private static StepState ReadStep(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        RequestId = HrmsDatabase.GetInt(reader, "RequestId"),
        StepOrder = HrmsDatabase.GetInt(reader, "StepOrder"),
        StageOrder = HrmsDatabase.GetInt(reader, "StageOrder"),
        ApproverType = HrmsDatabase.GetString(reader, "ApproverType"),
        RoleName = HrmsDatabase.GetString(reader, "RoleName"),
        UserName = HrmsDatabase.GetString(reader, "UserName"),
        CommitteeGroupId = HrmsDatabase.GetNullableInt(reader, "CommitteeGroupId"),
        ExternalCommitteeId = HrmsDatabase.GetNullableInt(reader, "ExternalCommitteeId"),
        DisplayName = HrmsDatabase.GetString(reader, "DisplayName"),
        Status = HrmsDatabase.GetString(reader, "Status"),
        CurrentSince = HrmsDatabase.GetDateTime(reader, "CurrentSince")
        ,ReminderSentAt=HrmsDatabase.GetDateTime(reader,"ReminderSentAt")
        ,EscalatedAt=HrmsDatabase.GetDateTime(reader,"EscalatedAt")
        ,EscalatedToRole=HrmsDatabase.GetString(reader,"EscalatedToRole")
        ,EscalatedToUser=HrmsDatabase.GetString(reader,"EscalatedToUser")
    };
}
