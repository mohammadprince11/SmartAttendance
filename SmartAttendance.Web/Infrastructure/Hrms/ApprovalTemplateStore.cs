using SmartAttendance.Infrastructure.Persistence;

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
        public string RequestType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public bool IsActive { get; set; } = true;
        public int Priority { get; set; }
        public bool HasConditions { get; set; }
        public int? CondBranchId { get; set; }
        public int? CondDepartmentId { get; set; }
        public string? CondWorkType { get; set; }
        public bool AutoRejectUnknownCommittee { get; set; }
        public int? CancelLimitDays { get; set; }
        public bool CommentRequiredOnReject { get; set; }
        public bool AttachmentRequiredOnRequest { get; set; }
        public int? EscalationDays { get; set; }
        public string? EscalationTo { get; set; }
        public string? NotifyJson { get; set; }
        public List<StepRow> Steps { get; set; } = new();
        public List<WatcherRow> Watchers { get; set; } = new();
    }

    public sealed class StepRow
    {
        public int StepOrder { get; set; }
        public string ApproverType { get; set; } = "DirectManager"; // DirectManager | Role | User
        public string? RoleName { get; set; }
        public string? UserName { get; set; }
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
        RequestType nvarchar(64) NOT NULL,
        Name nvarchar(150) NOT NULL,
        NameEn nvarchar(150) NULL,
        IsActive bit NOT NULL DEFAULT(1),
        Priority int NOT NULL DEFAULT(0),
        HasConditions bit NOT NULL DEFAULT(0),
        CondBranchId int NULL,
        CondDepartmentId int NULL,
        CondWorkType nvarchar(50) NULL,
        AutoRejectUnknownCommittee bit NOT NULL DEFAULT(0),
        CancelLimitDays int NULL,
        CommentRequiredOnReject bit NOT NULL DEFAULT(0),
        AttachmentRequiredOnRequest bit NOT NULL DEFAULT(0),
        EscalationDays int NULL,
        EscalationTo nvarchar(30) NULL,
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
    public static async Task<Dictionary<string, int>> CountsAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT RequestType, COUNT(*) AS Cnt FROM ApprovalTemplates GROUP BY RequestType;",
            command => { },
            reader => new
            {
                Type = HrmsDatabase.GetString(reader, "RequestType") ?? string.Empty,
                Count = HrmsDatabase.GetInt(reader, "Cnt")
            });
        return rows.ToDictionary(r => r.Type, r => r.Count, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<List<TemplateRow>> ListAsync(ApplicationDbContext dbContext, string requestType)
    {
        await EnsureAsync(dbContext);
        var templates = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ApprovalTemplates WHERE RequestType = @Type ORDER BY Priority, Id;",
            command => HrmsDatabase.AddParameter(command, "@Type", requestType),
            ReadTemplate);

        foreach (var template in templates)
        {
            await LoadChildrenAsync(dbContext, template);
        }
        return templates;
    }

    public static async Task<TemplateRow?> GetAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ApprovalTemplates WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            ReadTemplate);

        var template = rows.FirstOrDefault();
        if (template != null) await LoadChildrenAsync(dbContext, template);
        return template;
    }

    public static async Task<int> SaveAsync(ApplicationDbContext dbContext, TemplateRow template)
    {
        await EnsureAsync(dbContext);

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
    AutoRejectUnknownCommittee = @AutoReject, CancelLimitDays = @CancelLimitDays,
    CommentRequiredOnReject = @CommentReq, AttachmentRequiredOnRequest = @AttachReq,
    EscalationDays = @EscDays, EscalationTo = @EscTo, NotifyJson = @NotifyJson
WHERE Id = @Id;
DELETE FROM ApprovalTemplateSteps WHERE TemplateId = @Id;
DELETE FROM ApprovalTemplateWatchers WHERE TemplateId = @Id;
""",
                command => AddTemplateParameters(command, template, includeId: true));
        }
        else
        {
            id = await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                """
INSERT INTO ApprovalTemplates
(RequestType, Name, NameEn, IsActive, Priority, HasConditions, CondBranchId, CondDepartmentId, CondWorkType,
 AutoRejectUnknownCommittee, CancelLimitDays, CommentRequiredOnReject, AttachmentRequiredOnRequest,
 EscalationDays, EscalationTo, NotifyJson)
VALUES
(@RequestType, @Name, @NameEn, @IsActive,
 (SELECT ISNULL(MAX(Priority), 0) + 1 FROM ApprovalTemplates WHERE RequestType = @RequestType),
 @HasConditions, @CondBranchId, @CondDepartmentId, @CondWorkType,
 @AutoReject, @CancelLimitDays, @CommentReq, @AttachReq, @EscDays, @EscTo, @NotifyJson);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                command => AddTemplateParameters(command, template, includeId: false));
        }

        var order = 0;
        foreach (var step in template.Steps)
        {
            order++;
            var stepOrder = order;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO ApprovalTemplateSteps (TemplateId, StepOrder, ApproverType, RoleName, UserName, DisplayName)
VALUES (@TemplateId, @StepOrder, @ApproverType, @RoleName, @UserName, @DisplayName);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@TemplateId", id);
                    HrmsDatabase.AddParameter(command, "@StepOrder", stepOrder);
                    HrmsDatabase.AddParameter(command, "@ApproverType", step.ApproverType);
                    HrmsDatabase.AddParameter(command, "@RoleName", (object?)step.RoleName ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@UserName", (object?)step.UserName ?? DBNull.Value);
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

        return id;
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
DELETE FROM ApprovalTemplateSteps WHERE TemplateId = @Id;
DELETE FROM ApprovalTemplateWatchers WHERE TemplateId = @Id;
DELETE FROM ApprovalTemplates WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>إعادة ترتيب أولوية قوالب نوع واحد حسب تسلسل المعرّفات المرسل (سحب وإفلات).</summary>
    public static async Task ReorderAsync(ApplicationDbContext dbContext, string requestType, IReadOnlyList<int> orderedIds)
    {
        await EnsureAsync(dbContext);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            var priority = index + 1;
            var id = orderedIds[index];
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "UPDATE ApprovalTemplates SET Priority = @Priority WHERE Id = @Id AND RequestType = @Type;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Priority", priority);
                    HrmsDatabase.AddParameter(command, "@Id", id);
                    HrmsDatabase.AddParameter(command, "@Type", requestType);
                });
        }
    }

    /// <summary>
    /// محرك الاختيار: أول قالب نشط بترتيب الأولوية تنطبق شروطه على الموظف
    /// (القالب غير الشرطي ينطبق دائماً). null = لا قالب معرّفاً للنوع.
    /// </summary>
    public static async Task<TemplateRow?> ResolveAsync(
        ApplicationDbContext dbContext, string requestType, int? branchId, int? departmentId, string? workType)
    {
        var templates = await ListAsync(dbContext, requestType);
        foreach (var template in templates.Where(t => t.IsActive))
        {
            if (!template.HasConditions) return template;

            var branchOk = template.CondBranchId == null || template.CondBranchId == branchId;
            var departmentOk = template.CondDepartmentId == null || template.CondDepartmentId == departmentId;
            var workTypeOk = string.IsNullOrWhiteSpace(template.CondWorkType) ||
                             string.Equals(template.CondWorkType, workType, StringComparison.OrdinalIgnoreCase);

            if (branchOk && departmentOk && workTypeOk) return template;
        }
        return null;
    }

    private static TemplateRow ReadTemplate(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        RequestType = HrmsDatabase.GetString(reader, "RequestType") ?? string.Empty,
        Name = HrmsDatabase.GetString(reader, "Name") ?? string.Empty,
        NameEn = HrmsDatabase.GetString(reader, "NameEn"),
        IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
        Priority = HrmsDatabase.GetInt(reader, "Priority"),
        HasConditions = HrmsDatabase.GetBool(reader, "HasConditions"),
        CondBranchId = HrmsDatabase.GetNullableInt(reader, "CondBranchId"),
        CondDepartmentId = HrmsDatabase.GetNullableInt(reader, "CondDepartmentId"),
        CondWorkType = HrmsDatabase.GetString(reader, "CondWorkType"),
        AutoRejectUnknownCommittee = HrmsDatabase.GetBool(reader, "AutoRejectUnknownCommittee"),
        CancelLimitDays = HrmsDatabase.GetNullableInt(reader, "CancelLimitDays"),
        CommentRequiredOnReject = HrmsDatabase.GetBool(reader, "CommentRequiredOnReject"),
        AttachmentRequiredOnRequest = HrmsDatabase.GetBool(reader, "AttachmentRequiredOnRequest"),
        EscalationDays = HrmsDatabase.GetNullableInt(reader, "EscalationDays"),
        EscalationTo = HrmsDatabase.GetString(reader, "EscalationTo"),
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
                ApproverType = HrmsDatabase.GetString(reader, "ApproverType") ?? "DirectManager",
                RoleName = HrmsDatabase.GetString(reader, "RoleName"),
                UserName = HrmsDatabase.GetString(reader, "UserName"),
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
        if (includeId) HrmsDatabase.AddParameter(command, "@Id", template.Id);
        else HrmsDatabase.AddParameter(command, "@RequestType", template.RequestType);
        HrmsDatabase.AddParameter(command, "@Name", template.Name);
        HrmsDatabase.AddParameter(command, "@NameEn", (object?)template.NameEn ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@IsActive", template.IsActive ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@HasConditions", template.HasConditions ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@CondBranchId", (object?)template.CondBranchId ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@CondDepartmentId", (object?)template.CondDepartmentId ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@CondWorkType", (object?)template.CondWorkType ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@AutoReject", template.AutoRejectUnknownCommittee ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@CancelLimitDays", (object?)template.CancelLimitDays ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@CommentReq", template.CommentRequiredOnReject ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@AttachReq", template.AttachmentRequiredOnRequest ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@EscDays", (object?)template.EscalationDays ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@EscTo", (object?)template.EscalationTo ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@NotifyJson", (object?)template.NotifyJson ?? DBNull.Value);
    }
}
