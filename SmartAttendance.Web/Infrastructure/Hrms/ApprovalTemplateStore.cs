using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قوالب الموافقات (نمط كيان — قسم 18.1 بالدراسة): مركز قوالب حسب نوع الطلب.
/// القالب = لجنة موافقة مرتّبة + شروط تطبيق (فرع/قسم/نوع دوام) + مشاهدون +
/// مصفوفة إشعارات + تصعيد + قواعد طلب. قوالب متعددة لكل نوع تُرتّب بالأولوية،
/// والمحرك يختار أول قالب نشط تنطبق شروطه (ResolveAsync).
/// </summary>
public static class ApprovalTemplateStore
{
    public sealed record RequestTypeDef(string Key, string Label, string Module);

    /// <summary>كتالوج أنواع الطلبات مجمّعاً بالمودل (مقتبس من شجرة كيان بما يطابق شاشاتنا).</summary>
    public static readonly IReadOnlyList<RequestTypeDef> RequestTypes = new List<RequestTypeDef>
    {
        new("InfoChange",     "تعديل معلومات الموظف",   "الأشخاص"),
        new("CustomRequest",  "طلب مخصص",               "الأشخاص"),
        new("Violation",      "إجراء مخالفة",            "الأشخاص"),
        new("DocumentRequest","طلب وثيقة",               "الأشخاص"),
        new("Resignation",    "استقالة",                 "الأشخاص"),
        new("Transfer",       "نقل موظف",                "الأشخاص"),

        new("LeaveRequest",   "طلب إجازة",               "الإجازات"),
        new("LeaveCancel",    "إلغاء إجازة",             "الإجازات"),
        new("ReturnToWork",   "عودة للعمل",              "الإجازات"),

        new("MissingPunch",   "بصمة مفقودة",             "الحضور"),
        new("ExitPermission", "مغادرة أثناء الدوام",      "الحضور"),
        new("ShiftRequest",   "طلب مناوبة",              "الحضور"),
        new("ShiftChange",    "تغيير مناوبة",            "الحضور"),
        new("ShiftSwap",      "تبادل مناوبة مع زميل",     "الحضور"),
        new("WorkFromHome",   "عمل من المنزل",           "الحضور"),
        new("Overtime",       "عمل إضافي",               "الحضور"),

        new("Loan",           "قرض / سلفة",              "الرواتب"),
        new("SalaryIncrease", "زيادة راتب",              "الرواتب"),
        new("FinancialClaim", "مطالبة مالية",            "الرواتب"),
    };

    public sealed class TemplateRow
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string RequestType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public bool IsActive { get; set; } = true;
        public int Priority { get; set; }
        public bool HasConditions { get; set; }
        public int? CondBranchId { get; set; }
        public int? CondDepartmentId { get; set; }
        public string? CondWorkType { get; set; }
        public decimal? CondMinAmount { get; set; }
        public decimal? CondMaxAmount { get; set; }
        public string? CondChangedFieldKey { get; set; }
        public bool AutoRejectUnknownCommittee { get; set; }
        public int? CancelLimitDays { get; set; }
        public bool CommentRequiredOnReject { get; set; }
        public bool AttachmentRequiredOnRequest { get; set; }
        public int? ReminderHours { get; set; }
        public int? EscalationDays { get; set; }
        public string? EscalationTo { get; set; }
        public string? EscalationAlternateUser { get; set; }
        public string? NotifyJson { get; set; }
        public List<StepRow> Steps { get; set; } = new();
        public List<WatcherRow> Watchers { get; set; } = new();
    }

    public sealed class StepRow
    {
        public int StepOrder { get; set; }
        public int StageOrder { get; set; }
        public string ApproverType { get; set; } = "DirectManager"; // DirectManager | Role | User | CommitteeGroup | ExternalCommittee
        public string? RoleName { get; set; }
        public string? UserName { get; set; }
        public int? CommitteeGroupId { get; set; }
        public int? ExternalCommitteeId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    public sealed class WatcherRow
    {
        public string UserName { get; set; } = string.Empty;
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('ApprovalTemplates', 'U') IS NULL
BEGIN
    CREATE TABLE ApprovalTemplates
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL,
        RequestType nvarchar(64) NOT NULL,
        Name nvarchar(150) NOT NULL,
        NameEn nvarchar(150) NULL,
        IsActive bit NOT NULL DEFAULT(1),
        Priority int NOT NULL DEFAULT(0),
        HasConditions bit NOT NULL DEFAULT(0),
        CondBranchId int NULL,
        CondDepartmentId int NULL,
        CondWorkType nvarchar(50) NULL,
        CondMinAmount decimal(18,2) NULL,CondMaxAmount decimal(18,2) NULL,CondChangedFieldKey nvarchar(60) NULL,
        AutoRejectUnknownCommittee bit NOT NULL DEFAULT(0),
        CancelLimitDays int NULL,
        CommentRequiredOnReject bit NOT NULL DEFAULT(0),
        AttachmentRequiredOnRequest bit NOT NULL DEFAULT(0),
        ReminderHours int NULL,
        EscalationDays int NULL,
        EscalationTo nvarchar(30) NULL,
        EscalationAlternateUser nvarchar(100) NULL,
        NotifyJson nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('ApprovalTemplateSteps', 'U') IS NULL
BEGIN
    CREATE TABLE ApprovalTemplateSteps
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TemplateId int NOT NULL,
        StepOrder int NOT NULL,
        StageOrder int NOT NULL,
        ApproverType nvarchar(20) NOT NULL,
        RoleName nvarchar(50) NULL,
        UserName nvarchar(150) NULL,
        DisplayName nvarchar(150) NOT NULL
    );
END;

IF OBJECT_ID('ApprovalTemplateWatchers', 'U') IS NULL
BEGIN
    CREATE TABLE ApprovalTemplateWatchers
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TemplateId int NOT NULL,
        UserName nvarchar(150) NOT NULL
    );
END;
""");
    }

    /// <summary>عدد القوالب لكل نوع طلب (لعدّادات الكتالوج).</summary>
    public static async Task<Dictionary<string, int>> CountsAsync(ApplicationDbContext dbContext, CompanyScope scope, int companyId)
    {
        if (!scope.Allows(companyId)) throw new UnauthorizedAccessException();
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT RequestType,COUNT(*) AS Cnt FROM ApprovalTemplates WHERE CompanyId=@CompanyId GROUP BY RequestType;",
            command => HrmsDatabase.AddParameter(command,"@CompanyId",companyId),
            reader => new
            {
                Type = HrmsDatabase.GetString(reader, "RequestType") ?? string.Empty,
                Count = HrmsDatabase.GetInt(reader, "Cnt")
            });
        return rows.ToDictionary(r => r.Type, r => r.Count, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<List<TemplateRow>> ListAsync(ApplicationDbContext dbContext, int companyId, string requestType)
    {
        await EnsureAsync(dbContext);
        var templates = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ApprovalTemplates WHERE CompanyId=@CompanyId AND RequestType=@Type ORDER BY Priority,Id;",
            command => { HrmsDatabase.AddParameter(command,"@CompanyId",companyId); HrmsDatabase.AddParameter(command,"@Type",requestType); },
            ReadTemplate);

        foreach (var template in templates)
        {
            await LoadChildrenAsync(dbContext, template);
        }
        return templates;
    }

    public static async Task<TemplateRow?> GetAsync(ApplicationDbContext dbContext, CompanyScope scope, int companyId, int id)
    {
        if (!scope.Allows(companyId)) throw new UnauthorizedAccessException();
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ApprovalTemplates WHERE Id=@Id AND CompanyId=@CompanyId;",
            command => { HrmsDatabase.AddParameter(command,"@Id",id); HrmsDatabase.AddParameter(command,"@CompanyId",companyId); },
            ReadTemplate);

        var template = rows.FirstOrDefault();
        if (template != null) await LoadChildrenAsync(dbContext, template);
        return template;
    }

    public static async Task<int> SaveAsync(ApplicationDbContext dbContext, CompanyScope scope, TemplateRow template)
    {
        if (!scope.Allows(template.CompanyId)) throw new UnauthorizedAccessException();
        var validationError = Validate(template);
        if (validationError is not null) throw new ArgumentException(validationError, nameof(template));
        await EnsureAsync(dbContext);
        var referencedUsers=template.Steps.Where(step=>step.ApproverType=="User").Select(step=>step.UserName)
            .Concat(template.Watchers.Select(watcher=>watcher.UserName)).Append(template.EscalationAlternateUser)
            .Where(user=>!string.IsNullOrWhiteSpace(user)).Select(user=>user!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(referencedUsers.Length>0)
        {
            var parameters=referencedUsers.Select((_,index)=>$"@User{index}").ToArray();
            var count=await HrmsDatabase.ScalarAsync<int>(dbContext,$"""
SELECT COUNT(DISTINCT u.UserName) FROM SystemUsers u
INNER JOIN Employees e ON e.Id=u.EmployeeId AND ISNULL(e.IsDeleted,0)=0
WHERE u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0 AND e.CompanyId=@CompanyId
AND u.UserName IN ({string.Join(",",parameters)});
""",command=>
            {
                HrmsDatabase.AddParameter(command,"@CompanyId",template.CompanyId);
                for(var index=0;index<referencedUsers.Length;index++) HrmsDatabase.AddParameter(command,parameters[index],referencedUsers[index]);
            });
            if(count!=referencedUsers.Length) throw new ArgumentException("أحد مستخدمي المسار غير نشط أو خارج شركة القالب.",nameof(template));
        }
        var committeeGroupIds = template.Steps.Where(step => step.ApproverType == "CommitteeGroup")
            .Select(step => step.CommitteeGroupId).Where(id => id is > 0).Select(id => id!.Value).Distinct().ToArray();
        if (committeeGroupIds.Length > 0)
        {
            var parameters = committeeGroupIds.Select((_, index) => $"@Group{index}").ToArray();
            var count = await HrmsDatabase.ScalarAsync<int>(dbContext, $"""
SELECT COUNT(DISTINCT g.Id)
FROM ApprovalCommitteeGroups g
WHERE g.CompanyId=@CompanyId AND g.IsActive=1
  AND EXISTS(
      SELECT 1 FROM ApprovalCommitteeGroupMembers m
      INNER JOIN SystemUsers u ON u.UserName=m.UserName AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
      INNER JOIN Employees e ON e.Id=u.EmployeeId AND e.CompanyId=g.CompanyId AND ISNULL(e.IsDeleted,0)=0
      WHERE m.GroupId=g.Id)
  AND g.Id IN ({string.Join(",", parameters)});
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", template.CompanyId);
                for (var index = 0; index < committeeGroupIds.Length; index++)
                    HrmsDatabase.AddParameter(command, parameters[index], committeeGroupIds[index]);
            });
            if (count != committeeGroupIds.Length)
                throw new ArgumentException("إحدى مجموعات اللجان غير نشطة، بلا أعضاء، أو خارج شركة القالب.", nameof(template));
        }
        var externalCommitteeIds = template.Steps.Where(step => step.ApproverType == "ExternalCommittee")
            .Select(step => step.ExternalCommitteeId).Where(id => id is > 0).Select(id => id!.Value).Distinct().ToArray();
        if (externalCommitteeIds.Length > 0)
        {
            var parameters = externalCommitteeIds.Select((_, index) => $"@External{index}").ToArray();
            var count = await HrmsDatabase.ScalarAsync<int>(dbContext, $"""
SELECT COUNT(DISTINCT c.Id) FROM ApprovalExternalCommittees c
WHERE c.CompanyId=@CompanyId AND c.IsActive=1 AND c.Id IN ({string.Join(",", parameters)});
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", template.CompanyId);
                for (var index = 0; index < externalCommitteeIds.Length; index++)
                    HrmsDatabase.AddParameter(command, parameters[index], externalCommitteeIds[index]);
            });
            if (count != externalCommitteeIds.Length)
                throw new ArgumentException("إحدى اللجان الخارجية غير نشطة أو خارج شركة القالب.", nameof(template));
        }
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        int id;
        if (template.Id > 0)
        {
            id = template.Id;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE ApprovalTemplates SET
    Name = @Name, NameEn = @NameEn, IsActive = @IsActive,
    HasConditions = @HasConditions, CondBranchId = @CondBranchId,
    CondDepartmentId = @CondDepartmentId, CondWorkType = @CondWorkType,
    CondMinAmount=@CondMinAmount,CondMaxAmount=@CondMaxAmount,CondChangedFieldKey=@CondChangedFieldKey,
    AutoRejectUnknownCommittee = @AutoReject, CancelLimitDays = @CancelLimitDays,
    CommentRequiredOnReject = @CommentReq, AttachmentRequiredOnRequest = @AttachReq,
    ReminderHours=@ReminderHours,EscalationDays = @EscDays, EscalationTo = @EscTo,
    EscalationAlternateUser=@EscAltUser,NotifyJson = @NotifyJson
WHERE Id=@Id AND CompanyId=@CompanyId;
IF @@ROWCOUNT=0 THROW 50001, 'Approval template is outside company scope.', 1;
DELETE FROM ApprovalTemplateSteps WHERE TemplateId=@Id;
DELETE FROM ApprovalTemplateWatchers WHERE TemplateId=@Id;
""",
                command => AddTemplateParameters(command, template, includeId: true));
        }
        else
        {
            id = await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                """
INSERT INTO ApprovalTemplates
(CompanyId,RequestType, Name, NameEn, IsActive, Priority, HasConditions, CondBranchId, CondDepartmentId, CondWorkType,CondMinAmount,CondMaxAmount,CondChangedFieldKey,
 AutoRejectUnknownCommittee, CancelLimitDays, CommentRequiredOnReject, AttachmentRequiredOnRequest,
 ReminderHours,EscalationDays, EscalationTo, EscalationAlternateUser,NotifyJson)
VALUES
(@CompanyId,@RequestType, @Name, @NameEn, @IsActive,
 (SELECT ISNULL(MAX(Priority), 0) + 1 FROM ApprovalTemplates WHERE CompanyId=@CompanyId AND RequestType = @RequestType),
 @HasConditions, @CondBranchId, @CondDepartmentId, @CondWorkType,@CondMinAmount,@CondMaxAmount,@CondChangedFieldKey,
 @AutoReject, @CancelLimitDays, @CommentReq, @AttachReq, @ReminderHours,@EscDays, @EscTo,@EscAltUser,@NotifyJson);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                command => AddTemplateParameters(command, template, includeId: false));
        }

        var order = 0;
        var stageOrder = 0;
        int? previousRawStage = null;
        foreach (var step in template.Steps)
        {
            order++;
            var stepOrder = order;
            var rawStage=step.StageOrder>0?step.StageOrder:stepOrder;
            if(previousRawStage is null||rawStage!=previousRawStage) stageOrder++;
            previousRawStage=rawStage;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO ApprovalTemplateSteps (TemplateId, StepOrder, StageOrder, ApproverType, RoleName, UserName, CommitteeGroupId, ExternalCommitteeId, DisplayName)
VALUES (@TemplateId, @StepOrder, @StageOrder, @ApproverType, @RoleName, @UserName, @CommitteeGroupId, @ExternalCommitteeId, @DisplayName);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@TemplateId", id);
                    HrmsDatabase.AddParameter(command, "@StepOrder", stepOrder);
                    HrmsDatabase.AddParameter(command, "@StageOrder", stageOrder);
                    HrmsDatabase.AddParameter(command, "@ApproverType", step.ApproverType);
                    HrmsDatabase.AddParameter(command, "@RoleName", (object?)step.RoleName ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@UserName", (object?)step.UserName ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@CommitteeGroupId", (object?)step.CommitteeGroupId ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@ExternalCommitteeId", (object?)step.ExternalCommitteeId ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@DisplayName", step.DisplayName);
                });
        }

        foreach (var watcher in template.Watchers.Where(w => !string.IsNullOrWhiteSpace(w.UserName)))
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO ApprovalTemplateWatchers (TemplateId, UserName) VALUES (@TemplateId, @UserName);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@TemplateId", id);
                    HrmsDatabase.AddParameter(command, "@UserName", watcher.UserName);
                });
        }

        await transaction.CommitAsync();
        return id;
    }

    public static string? Validate(TemplateRow? template)
    {
        if (template is null) return "بيانات القالب مطلوبة.";
        if (!RequestTypes.Any(type => type.Key.Equals(template.RequestType, StringComparison.OrdinalIgnoreCase)))
            return "نوع الطلب غير معروف.";
        if (string.IsNullOrWhiteSpace(template.Name)) return "اسم القالب مطلوب.";
        if (template.Steps.Count == 0) return "لجنة الموافقة يجب أن تحتوي خطوة واحدة على الأقل.";
        if(template.ReminderHours is >0&&template.EscalationDays is >0&&template.ReminderHours>=template.EscalationDays*24)
            return "مهلة التذكير يجب أن تسبق مهلة التصعيد.";
        if(template.CondMinAmount.HasValue&&template.CondMaxAmount.HasValue&&template.CondMaxAmount<template.CondMinAmount)
            return "حد المبلغ الأعلى لا يمكن أن يقل عن الحد الأدنى.";
        if(!string.IsNullOrWhiteSpace(template.CondChangedFieldKey)&&!DataChangeRequestStore.Catalog.Any(field=>field.Key.Equals(template.CondChangedFieldKey,StringComparison.OrdinalIgnoreCase)))
            return "حقل التغيير المشروط غير معروف.";

        foreach (var step in template.Steps)
        {
            if (step.ApproverType is not ("DirectManager" or "Role" or "User" or "CommitteeGroup" or "ExternalCommittee"))
                return "نوع صاحب الموافقة غير صحيح.";
            if (step.ApproverType == "Role" && string.IsNullOrWhiteSpace(step.RoleName))
                return "يجب تحديد الدور لكل خطوة من نوع دور.";
            if (step.ApproverType == "User" && string.IsNullOrWhiteSpace(step.UserName))
                return "يجب تحديد المستخدم لكل خطوة من نوع مستخدم.";
            if (step.ApproverType == "CommitteeGroup" && step.CommitteeGroupId is not > 0)
                return "يجب تحديد مجموعة اللجنة لكل خطوة من نوع مجموعة داخلية.";
            if (step.ApproverType == "ExternalCommittee" && step.ExternalCommitteeId is not > 0)
                return "يجب تحديد اللجنة الخارجية لكل خطوة من هذا النوع.";
        }

        return null;
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, CompanyScope scope, int companyId, int id)
    {
        if (!scope.Allows(companyId)) throw new UnauthorizedAccessException();
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
DELETE s FROM ApprovalTemplateSteps s INNER JOIN ApprovalTemplates t ON t.Id=s.TemplateId WHERE t.Id=@Id AND t.CompanyId=@CompanyId;
DELETE w FROM ApprovalTemplateWatchers w INNER JOIN ApprovalTemplates t ON t.Id=w.TemplateId WHERE t.Id=@Id AND t.CompanyId=@CompanyId;
DELETE FROM ApprovalTemplates WHERE Id=@Id AND CompanyId=@CompanyId;
""",
            command => { HrmsDatabase.AddParameter(command,"@Id",id); HrmsDatabase.AddParameter(command,"@CompanyId",companyId); });
    }

    /// <summary>إعادة ترتيب أولوية قوالب نوع واحد حسب تسلسل المعرّفات المرسل (سحب وإفلات).</summary>
    public static async Task ReorderAsync(ApplicationDbContext dbContext, CompanyScope scope, int companyId, string requestType, IReadOnlyList<int> orderedIds)
    {
        if (!scope.Allows(companyId)) throw new UnauthorizedAccessException();
        await EnsureAsync(dbContext);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            var priority = index + 1;
            var id = orderedIds[index];
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "UPDATE ApprovalTemplates SET Priority=@Priority WHERE Id=@Id AND CompanyId=@CompanyId AND RequestType=@Type;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Priority", priority);
                    HrmsDatabase.AddParameter(command, "@Id", id);
                    HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                    HrmsDatabase.AddParameter(command, "@Type", requestType);
                });
        }
    }

    /// <summary>
    /// محرك الاختيار: أول قالب نشط بترتيب الأولوية تنطبق شروطه على الموظف
    /// (القالب غير الشرطي ينطبق دائماً). null = لا قالب معرّفاً للنوع.
    /// </summary>
    public static async Task<TemplateRow?> ResolveAsync(
        ApplicationDbContext dbContext, int companyId, string requestType, int? branchId, int? departmentId, string? workType,int? requestId=null)
    {
        var templates = await ListAsync(dbContext, companyId, requestType);
        foreach (var template in templates.Where(t => t.IsActive))
        {
            if (!template.HasConditions) return template;

            var branchOk = template.CondBranchId == null || template.CondBranchId == branchId;
            var departmentOk = template.CondDepartmentId == null || template.CondDepartmentId == departmentId;
            var workTypeOk = string.IsNullOrWhiteSpace(template.CondWorkType) ||
                             string.Equals(template.CondWorkType, workType, StringComparison.OrdinalIgnoreCase);

            var amountOk=true;
            if(template.CondMinAmount.HasValue||template.CondMaxAmount.HasValue)
            {
                if(requestId is not >0) amountOk=false;
                else
                {
                    var amount=(await HrmsDatabase.QueryAsync(dbContext,"""
DECLARE @Amount decimal(18,2)=NULL;
IF OBJECT_ID('FinancialRequestDetails','U') IS NOT NULL
 EXEC sp_executesql N'SELECT @Out=Amount FROM FinancialRequestDetails WHERE RequestId=@RequestId',N'@RequestId int,@Out decimal(18,2) OUTPUT',@RequestId=@Id,@Out=@Amount OUTPUT;
SELECT @Amount;
""",command=>HrmsDatabase.AddParameter(command,"@Id",requestId.Value),reader=>reader.IsDBNull(0)?(decimal?)null:reader.GetDecimal(0))).Single();
                    amountOk=amount.HasValue&&(!template.CondMinAmount.HasValue||amount>=template.CondMinAmount)&&(!template.CondMaxAmount.HasValue||amount<=template.CondMaxAmount);
                }
            }
            var changedFieldOk=true;
            if(!string.IsNullOrWhiteSpace(template.CondChangedFieldKey))
            {
                if(requestId is not >0) changedFieldOk=false;
                else changedFieldOk=await HrmsDatabase.ScalarAsync<int>(dbContext,"""
DECLARE @Found int=0;
IF OBJECT_ID('DataChangeRequestFields','U') IS NOT NULL
 EXEC sp_executesql N'SELECT @Out=COUNT(1) FROM DataChangeRequestFields WHERE RequestId=@RequestId AND FieldKey=@FieldKey',N'@RequestId int,@FieldKey nvarchar(60),@Out int OUTPUT',@RequestId=@Id,@FieldKey=@Field,@Out=@Found OUTPUT;
SELECT @Found;
""",command=>{HrmsDatabase.AddParameter(command,"@Id",requestId.Value);HrmsDatabase.AddParameter(command,"@Field",template.CondChangedFieldKey);})>0;
            }

            if (branchOk && departmentOk && workTypeOk&&amountOk&&changedFieldOk) return template;
        }
        return null;
    }

    private static TemplateRow ReadTemplate(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
        RequestType = HrmsDatabase.GetString(reader, "RequestType") ?? string.Empty,
        Name = HrmsDatabase.GetString(reader, "Name") ?? string.Empty,
        NameEn = HrmsDatabase.GetString(reader, "NameEn"),
        IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
        Priority = HrmsDatabase.GetInt(reader, "Priority"),
        HasConditions = HrmsDatabase.GetBool(reader, "HasConditions"),
        CondBranchId = HrmsDatabase.GetNullableInt(reader, "CondBranchId"),
        CondDepartmentId = HrmsDatabase.GetNullableInt(reader, "CondDepartmentId"),
        CondWorkType = HrmsDatabase.GetString(reader, "CondWorkType"),
        CondMinAmount=reader["CondMinAmount"] is decimal min?min:null,
        CondMaxAmount=reader["CondMaxAmount"] is decimal max?max:null,
        CondChangedFieldKey=HrmsDatabase.GetString(reader,"CondChangedFieldKey"),
        AutoRejectUnknownCommittee = HrmsDatabase.GetBool(reader, "AutoRejectUnknownCommittee"),
        CancelLimitDays = HrmsDatabase.GetNullableInt(reader, "CancelLimitDays"),
        CommentRequiredOnReject = HrmsDatabase.GetBool(reader, "CommentRequiredOnReject"),
        AttachmentRequiredOnRequest = HrmsDatabase.GetBool(reader, "AttachmentRequiredOnRequest"),
        ReminderHours = HrmsDatabase.GetNullableInt(reader,"ReminderHours"),
        EscalationDays = HrmsDatabase.GetNullableInt(reader, "EscalationDays"),
        EscalationTo = HrmsDatabase.GetString(reader, "EscalationTo"),
        EscalationAlternateUser=HrmsDatabase.GetString(reader,"EscalationAlternateUser"),
        NotifyJson = HrmsDatabase.GetString(reader, "NotifyJson")
    };

    private static async Task LoadChildrenAsync(ApplicationDbContext dbContext, TemplateRow template)
    {
        template.Steps = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ApprovalTemplateSteps WHERE TemplateId = @Id ORDER BY StepOrder;",
            command => HrmsDatabase.AddParameter(command, "@Id", template.Id),
            reader => new StepRow
            {
                StepOrder = HrmsDatabase.GetInt(reader, "StepOrder"),
                StageOrder = HrmsDatabase.GetInt(reader, "StageOrder"),
                ApproverType = HrmsDatabase.GetString(reader, "ApproverType") ?? "DirectManager",
                RoleName = HrmsDatabase.GetString(reader, "RoleName"),
                UserName = HrmsDatabase.GetString(reader, "UserName"),
                CommitteeGroupId = HrmsDatabase.GetNullableInt(reader, "CommitteeGroupId"),
                ExternalCommitteeId = HrmsDatabase.GetNullableInt(reader, "ExternalCommitteeId"),
                DisplayName = HrmsDatabase.GetString(reader, "DisplayName") ?? string.Empty
            });

        template.Watchers = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ApprovalTemplateWatchers WHERE TemplateId = @Id ORDER BY Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", template.Id),
            reader => new WatcherRow
            {
                UserName = HrmsDatabase.GetString(reader, "UserName") ?? string.Empty
            });
    }

    private static void AddTemplateParameters(System.Data.Common.DbCommand command, TemplateRow template, bool includeId)
    {
        HrmsDatabase.AddParameter(command, "@CompanyId", template.CompanyId);
        if (includeId) HrmsDatabase.AddParameter(command, "@Id", template.Id);
        else HrmsDatabase.AddParameter(command, "@RequestType", template.RequestType);
        HrmsDatabase.AddParameter(command, "@Name", template.Name);
        HrmsDatabase.AddParameter(command, "@NameEn", (object?)template.NameEn ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@IsActive", template.IsActive ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@HasConditions", template.HasConditions ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@CondBranchId", (object?)template.CondBranchId ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@CondDepartmentId", (object?)template.CondDepartmentId ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@CondWorkType", (object?)template.CondWorkType ?? DBNull.Value);
        HrmsDatabase.AddParameter(command,"@CondMinAmount",(object?)template.CondMinAmount??DBNull.Value);
        HrmsDatabase.AddParameter(command,"@CondMaxAmount",(object?)template.CondMaxAmount??DBNull.Value);
        HrmsDatabase.AddParameter(command,"@CondChangedFieldKey",(object?)template.CondChangedFieldKey??DBNull.Value);
        HrmsDatabase.AddParameter(command, "@AutoReject", template.AutoRejectUnknownCommittee ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@CancelLimitDays", (object?)template.CancelLimitDays ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@CommentReq", template.CommentRequiredOnReject ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@AttachReq", template.AttachmentRequiredOnRequest ? 1 : 0);
        HrmsDatabase.AddParameter(command,"@ReminderHours",(object?)template.ReminderHours??DBNull.Value);
        HrmsDatabase.AddParameter(command, "@EscDays", (object?)template.EscalationDays ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@EscTo", (object?)template.EscalationTo ?? DBNull.Value);
        HrmsDatabase.AddParameter(command,"@EscAltUser",(object?)template.EscalationAlternateUser??DBNull.Value);
        HrmsDatabase.AddParameter(command, "@NotifyJson", (object?)template.NotifyJson ?? DBNull.Value);
    }
}
