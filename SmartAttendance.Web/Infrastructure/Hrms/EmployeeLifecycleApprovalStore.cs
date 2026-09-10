using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Connects employee Onboarding/Offboarding with the generic approval engine.
///
/// Lifecycle is deliberately split into two concerns:
/// 1) ApprovalWorkflowEngine answers "who must approve?"
/// 2) EmployeeTasks answers "what work must be done?"
///
/// At submission time the active task template is snapshotted. Final approval
/// materializes that snapshot into EmployeeTasks exactly once.
/// </summary>
public static class EmployeeLifecycleApprovalStore
{
    public const string OnboardingRequestType = "Onboarding";
    public const string OffboardingRequestType = "Offboarding";

    public sealed record OperationResult(bool Ok, string Message, int? RequestId = null);

    public sealed class LifecycleRequestRow
    {
        public int RequestId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public HrProcessType ProcessType { get; set; }
        public string RequestType { get; set; } = string.Empty;
        public DateOnly StartDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string CurrentStep { get; set; } = string.Empty;
        public int SnapshotTaskCount { get; set; }
        public DateTime? AppliedAt { get; set; }
        public DateTime? CreatedAt { get; set; }

        public string ProcessLabel =>
            ProcessType == HrProcessType.Onboarding
                ? "Onboarding / التعيين"
                : "Offboarding / إنهاء الخدمة";

        public bool TasksGenerated => AppliedAt.HasValue;
    }

    public static string RequestTypeFor(HrProcessType processType) =>
        processType switch
        {
            HrProcessType.Onboarding => OnboardingRequestType,
            HrProcessType.Offboarding => OffboardingRequestType,
            _ => throw new ArgumentOutOfRangeException(nameof(processType))
        };

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await EmployeeTasksSchema.EnsureAsync(dbContext);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeLifecycleRequests', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeLifecycleRequests
    (
        RequestId int NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        ProcessType int NOT NULL,
        StartDate date NOT NULL,
        SubmittedBy nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        AppliedAt datetime2 NULL,
        AppliedBy nvarchar(150) NULL
    );

    CREATE INDEX IX_EmployeeLifecycleRequests_Employee_Process
        ON EmployeeLifecycleRequests(EmployeeId, ProcessType, CreatedAt DESC);
END;

IF OBJECT_ID('EmployeeLifecycleRequestTasks', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeLifecycleRequestTasks
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RequestId int NOT NULL,
        Title nvarchar(200) NOT NULL,
        Description nvarchar(500) NULL,
        AssigneeRole nvarchar(100) NULL,
        DueDays int NOT NULL DEFAULT(0),
        SortOrder int NOT NULL DEFAULT(0)
    );

    CREATE INDEX IX_EmployeeLifecycleRequestTasks_Request
        ON EmployeeLifecycleRequestTasks(RequestId, SortOrder, Id);
END;
""");
    }

    public static async Task<OperationResult> SubmitAsync(
        ApplicationDbContext dbContext,
        int employeeId,
        HrProcessType processType,
        DateOnly startDate,
        string actor)
    {
        await EnsureAsync(dbContext);

        if (employeeId <= 0 ||
            processType is not (HrProcessType.Onboarding or HrProcessType.Offboarding))
        {
            return new OperationResult(false, "بيانات عملية دورة حياة الموظف غير صالحة.");
        }

        actor = string.IsNullOrWhiteSpace(actor) ? "HR" : actor.Trim();
        var processValue = (int)processType;
        var requestType = RequestTypeFor(processType);
        var processLabel = processType == HrProcessType.Onboarding
            ? "Onboarding"
            : "Offboarding";

        var templateCount = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
SELECT COUNT(1)
FROM HrTaskTemplates
WHERE ProcessType=@ProcessType
  AND IsActive=1
  AND ISNULL(IsDeleted,0)=0;
""",
            command => HrmsDatabase.AddParameter(command, "@ProcessType", processValue));

        if (templateCount <= 0)
        {
            return new OperationResult(
                false,
                $"لا توجد مهام فعّالة لقالب {processLabel}. أضف مهام القالب أولاً.");
        }

        var openTasks = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
SELECT COUNT(1)
FROM EmployeeTasks
WHERE EmployeeId=@EmployeeId
  AND ProcessType=@ProcessType
  AND IsDone=0
  AND ISNULL(IsDeleted,0)=0;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@ProcessType", processValue);
            });

        if (openTasks > 0)
        {
            return new OperationResult(
                false,
                "توجد مهام مفتوحة من نفس النوع لهذا الموظف.");
        }

        var existingRequestId = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
SELECT TOP 1 lr.RequestId
FROM EmployeeLifecycleRequests lr
INNER JOIN SelfServiceRequests r ON r.Id=lr.RequestId
WHERE lr.EmployeeId=@EmployeeId
  AND lr.ProcessType=@ProcessType
  AND ISNULL(r.Status,'Pending') IN ('Pending','Draft','Returned','WaitingRevision')
ORDER BY lr.CreatedAt DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@ProcessType", processValue);
            });

        if (existingRequestId > 0)
        {
            return new OperationResult(
                false,
                $"توجد عملية من نفس النوع بانتظار الإكمال أو الموافقة (طلب #{existingRequestId}).",
                existingRequestId);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var reason = processType == HrProcessType.Onboarding
            ? "بدء دورة Onboarding للموظف."
            : "بدء دورة Offboarding للموظف.";

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
INSERT INTO SelfServiceRequests
(EmployeeId, RequestType, RequestDate, FromDate, ToDate, Reason,
 Status, CurrentStep, CreatedBy, RequestSource)
VALUES
(@EmployeeId, @RequestType, @StartDate, @StartDate, @StartDate, @Reason,
 'Pending', 'Pending Approval', @Actor, N'Admin');

DECLARE @RequestId int = SCOPE_IDENTITY();

INSERT INTO ApprovalHistories
(RequestId, StepName, Action, ActionBy, Notes)
VALUES
(@RequestId, 'Lifecycle Submission', 'Submitted', @Actor, @Reason);

SELECT @RequestId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@RequestType", requestType);
                HrmsDatabase.AddParameter(command, "@StartDate", startDate);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
            });

        if (requestId <= 0)
        {
            await transaction.RollbackAsync();
            return new OperationResult(false, "تعذّر إنشاء طلب دورة حياة الموظف.");
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
INSERT INTO EmployeeLifecycleRequests
(RequestId, EmployeeId, ProcessType, StartDate, SubmittedBy)
VALUES
(@RequestId, @EmployeeId, @ProcessType, @StartDate, @Actor);

INSERT INTO EmployeeLifecycleRequestTasks
(RequestId, Title, Description, AssigneeRole, DueDays, SortOrder)
SELECT
    @RequestId,
    Title,
    Description,
    AssigneeRole,
    DueDays,
    SortOrder
FROM HrTaskTemplates
WHERE ProcessType=@ProcessType
  AND IsActive=1
  AND ISNULL(IsDeleted,0)=0
ORDER BY SortOrder, Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@ProcessType", processValue);
                HrmsDatabase.AddParameter(command, "@StartDate", startDate);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
            });

        await transaction.CommitAsync();

        // ApprovalWorkflowEngine freezes the approval route itself after the
        // lifecycle/task snapshot has been committed.
        var start = await ApprovalWorkflowEngine.StartAsync(
            dbContext,
            requestId,
            requestType,
            employeeId);

        if (!start.Ok)
        {
            return new OperationResult(
                false,
                $"تم إنشاء طلب #{requestId} لكن لم يبدأ مسار الموافقة: {start.Message}",
                requestId);
        }

        return new OperationResult(
            true,
            $"تم إرسال طلب {processLabel} رقم #{requestId} إلى مسار الموافقات. لن تُنشأ المهام إلا بعد الاعتماد النهائي.",
            requestId);
    }

    /// <summary>
    /// Final-approval effect. Idempotent: AppliedAt is the once-only gate.
    /// The snapshotted checklist is copied into EmployeeTasks in one transaction.
    /// </summary>
    public static async Task<bool> ApplyIfLifecycleAsync(
        ApplicationDbContext dbContext,
        CompanyScope scope,
        int requestId,
        string actor,
        string? ipAddress)
    {
        await EnsureAsync(dbContext);

        var scopeFilter = EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");
        var inScope = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            $"""
SELECT COUNT(1)
FROM EmployeeLifecycleRequests lr
INNER JOIN Employees e ON e.Id=lr.EmployeeId AND ISNULL(e.IsDeleted,0)=0
WHERE lr.RequestId=@RequestId
  AND {scopeFilter};
""",
            command => HrmsDatabase.AddParameter(command, "@RequestId", requestId));

        if (inScope != 1)
        {
            return false;
        }

        actor = string.IsNullOrWhiteSpace(actor) ? "HR" : actor.Trim();

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
DECLARE @EmployeeId int = NULL;
DECLARE @ProcessType int = NULL;
DECLARE @StartDate date = NULL;

SELECT
    @EmployeeId = lr.EmployeeId,
    @ProcessType = lr.ProcessType,
    @StartDate = lr.StartDate
FROM EmployeeLifecycleRequests lr WITH (UPDLOCK, HOLDLOCK)
INNER JOIN SelfServiceRequests r ON r.Id=lr.RequestId
WHERE lr.RequestId=@RequestId
  AND lr.AppliedAt IS NULL
  AND r.Status='Approved';

IF @EmployeeId IS NOT NULL
BEGIN
    INSERT INTO EmployeeTasks
    (EmployeeId, ProcessType, Title, Description, AssigneeRole, DueDate,
     IsDone, Note, CreatedAt, CreatedBy, IsDeleted)
    SELECT
        @EmployeeId,
        @ProcessType,
        snapshot.Title,
        snapshot.Description,
        snapshot.AssigneeRole,
        DATEADD(DAY, snapshot.DueDays, @StartDate),
        0,
        CONCAT(N'Generated from lifecycle approval request #', @RequestId),
        SYSUTCDATETIME(),
        @Actor,
        0
    FROM EmployeeLifecycleRequestTasks snapshot
    WHERE snapshot.RequestId=@RequestId
    ORDER BY snapshot.SortOrder, snapshot.Id;

    UPDATE EmployeeLifecycleRequests
    SET AppliedAt=SYSUTCDATETIME(),
        AppliedBy=@Actor
    WHERE RequestId=@RequestId
      AND AppliedAt IS NULL;

    INSERT INTO AuditLogs
    (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
    VALUES
    ('EmployeeLifecycleRequest',
     CAST(@RequestId AS nvarchar(80)),
     'Generate Lifecycle Tasks',
     CONCAT(N'ProcessType=', @ProcessType, N'; EmployeeId=', @EmployeeId),
     @Actor,
     @IpAddress);
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RequestId", requestId);
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(
                    command,
                    "@IpAddress",
                    (object?)ipAddress ?? DBNull.Value);
            });

        await transaction.CommitAsync();

        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
SELECT COUNT(1)
FROM EmployeeLifecycleRequests
WHERE RequestId=@RequestId
  AND AppliedAt IS NOT NULL;
""",
            command => HrmsDatabase.AddParameter(command, "@RequestId", requestId)) == 1;
    }

    public static async Task<OperationResult> ResubmitAsync(
        ApplicationDbContext dbContext,
        CompanyScope scope,
        int requestId)
    {
        await EnsureAsync(dbContext);

        var scopeFilter = EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT TOP 1
    lr.EmployeeId,
    lr.StartDate,
    r.Status
FROM EmployeeLifecycleRequests lr
INNER JOIN SelfServiceRequests r ON r.Id=lr.RequestId
INNER JOIN Employees e ON e.Id=lr.EmployeeId AND ISNULL(e.IsDeleted,0)=0
WHERE lr.RequestId=@RequestId
  AND {scopeFilter};
""",
            command => HrmsDatabase.AddParameter(command, "@RequestId", requestId),
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                StartDate = HrmsDatabase.GetDateOnly(reader, "StartDate") ?? default,
                Status = HrmsDatabase.GetString(reader, "Status")
            });

        var request = rows.FirstOrDefault();
        if (request is null)
        {
            return new OperationResult(false, "الطلب غير موجود أو خارج نطاق صلاحيتك.");
        }

        if (request.Status is not ("Returned" or "WaitingRevision"))
        {
            return new OperationResult(false, "يمكن إعادة تقديم الطلبات المعادة للمراجعة فقط.");
        }

        var date = request.StartDate.ToDateTime(TimeOnly.MinValue);
        var result = await ApprovalWorkflowEngine.ResubmitReturnedAsync(
            dbContext,
            requestId,
            request.EmployeeId,
            "إعادة تقديم عملية دورة حياة الموظف.",
            date,
            date);

        return new OperationResult(result.Ok, result.Message, requestId);
    }

    public static async Task<List<LifecycleRequestRow>> ListAsync(
        ApplicationDbContext dbContext,
        CompanyScope scope)
    {
        await EnsureAsync(dbContext);

        var scopeFilter = EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");

        return await HrmsDatabase.QueryAsync(
            dbContext,
            $"""
SELECT TOP 150
    lr.RequestId,
    lr.EmployeeId,
    e.EmployeeNo,
    e.FullName,
    lr.ProcessType,
    r.RequestType,
    lr.StartDate,
    ISNULL(r.Status,'Pending') AS Status,
    ISNULL(r.CurrentStep,'') AS CurrentStep,
    (SELECT COUNT(1)
       FROM EmployeeLifecycleRequestTasks snapshot
      WHERE snapshot.RequestId=lr.RequestId) AS SnapshotTaskCount,
    lr.AppliedAt,
    r.CreatedAt
FROM EmployeeLifecycleRequests lr
INNER JOIN SelfServiceRequests r ON r.Id=lr.RequestId
INNER JOIN Employees e ON e.Id=lr.EmployeeId AND ISNULL(e.IsDeleted,0)=0
WHERE {scopeFilter}
ORDER BY r.CreatedAt DESC, lr.RequestId DESC;
""",
            null,
            reader => new LifecycleRequestRow
            {
                RequestId = HrmsDatabase.GetInt(reader, "RequestId"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                ProcessType = (HrProcessType)HrmsDatabase.GetInt(reader, "ProcessType"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                StartDate = HrmsDatabase.GetDateOnly(reader, "StartDate") ?? default,
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),
                SnapshotTaskCount = HrmsDatabase.GetInt(reader, "SnapshotTaskCount"),
                AppliedAt = HrmsDatabase.GetDateTime(reader, "AppliedAt"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }
}