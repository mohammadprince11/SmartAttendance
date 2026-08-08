using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// الإقرارات (نظير شاشتَي كيان: «قالب الإقرارات» و«متابعة الإقرارات»).
///
/// **القيمة كلها بالإثبات**: أن فلاناً استلم ورأى ووافق بتاريخ محدَّد. أقرب ما كان
/// عندنا هو الإعلانات (تُقرأ بلا إثبات موافقة) وأرشيف الوثائق (رفع فقط)، ولا واحد
/// منهما يُنتج الصفّ الذي يُحسم به نزاع «هل أُبلغ الموظف باللائحة؟».
///
/// ⭐ **المشاهدة والموافقة حالتان منفصلتان** — «أُرسل ولم يُفتح» موقفٌ قانوني
/// مستقل تماماً عن «فُتح ولم يُوافَق عليه»، ودمجهما يمحو أهمّ ما تحتاجه الشركة.
/// </summary>
public static class AcknowledgmentStore
{
    public sealed record Template(
        int Id,
        string Name,
        string? Description,
        string? Title,
        string? Body,
        bool SendToNewHires,
        bool IsActive,
        string ConditionsJson)
    {
        public HrConditions.ConditionSet Conditions => HrConditions.Deserialize(ConditionsJson);
    }

    public sealed record Assignment(
        int Id,
        int TemplateId,
        string TemplateName,
        int EmployeeId,
        string EmployeeName,
        string EmployeeNo,
        DateTime SentAt,
        DateTime? ViewedAt,
        DateTime? AcceptedAt,
        DateTime? DeclinedAt,
        string? Title,
        string? Body)
    {
        public bool Viewed => ViewedAt is not null;
        public bool Accepted => AcceptedAt is not null;
        public bool Declined => DeclinedAt is not null;

        /// <summary>
        /// الحالة المعروضة. الترتيب مقصود: الرفض حالةٌ نهائية تسبق الموافقة بالعرض،
        /// و«أُرسل ولم يُشاهَد» تُميَّز عن «شوهد ولم يُوافَق» لأنهما مختلفتان قانونياً.
        /// </summary>
        public string StatusLabel =>
            Declined ? "رفض"
            : Accepted ? "موافق"
            : Viewed ? "شوهد — بلا موافقة"
            : "أُرسل — لم يُشاهَد";

        public bool NeedsAction => !Accepted && !Declined;
    }

    // ── القوالب ────────────────────────────────────────────────────────────────

    public static async Task<List<Template>> LoadTemplatesAsync(ApplicationDbContext db, bool activeOnly = false)
    {
        var filter = activeOnly ? " AND IsActive = 1" : string.Empty;

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT Id, Name, Description, Title, Body, SendToNewHires, IsActive,
       ISNULL(ConditionsJson, N'') AS ConditionsJson
FROM AcknowledgmentTemplates
WHERE ISNULL(IsDeleted, 0) = 0{filter}
ORDER BY Name;
""",
            command => { },
            reader => new Template(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Name"),
                HrmsDatabase.GetString(reader, "Description"),
                HrmsDatabase.GetString(reader, "Title"),
                HrmsDatabase.GetString(reader, "Body"),
                HrmsDatabase.GetBool(reader, "SendToNewHires"),
                HrmsDatabase.GetBool(reader, "IsActive"),
                HrmsDatabase.GetString(reader, "ConditionsJson")));
    }

    public static async Task<Template?> FindTemplateAsync(ApplicationDbContext db, int id) =>
        (await LoadTemplatesAsync(db)).FirstOrDefault(row => row.Id == id);

    public static async Task<int> SaveTemplateAsync(
        ApplicationDbContext db,
        int id,
        string name,
        string? description,
        string? title,
        string? body,
        bool sendToNewHires,
        bool isActive,
        HrConditions.ConditionSet conditions,
        string? user)
    {
        if (id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE AcknowledgmentTemplates
SET Name = @Name, Description = @Description, Title = @Title, Body = @Body,
    SendToNewHires = @SendToNewHires, IsActive = @IsActive, ConditionsJson = @Conditions,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", id);
                    HrmsDatabase.AddParameter(command, "@Name", name);
                    HrmsDatabase.AddParameter(command, "@Description", description);
                    HrmsDatabase.AddParameter(command, "@Title", title);
                    HrmsDatabase.AddParameter(command, "@Body", body);
                    HrmsDatabase.AddParameter(command, "@SendToNewHires", sendToNewHires);
                    HrmsDatabase.AddParameter(command, "@IsActive", isActive);
                    HrmsDatabase.AddParameter(command, "@Conditions", HrConditions.Serialize(conditions));
                });

            return id;
        }

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO AcknowledgmentTemplates
    (Name, Description, Title, Body, SendToNewHires, IsActive, ConditionsJson, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@Name, @Description, @Title, @Body, @SendToNewHires, @IsActive, @Conditions, @CreatedBy);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Name", name);
                HrmsDatabase.AddParameter(command, "@Description", description);
                HrmsDatabase.AddParameter(command, "@Title", title);
                HrmsDatabase.AddParameter(command, "@Body", body);
                HrmsDatabase.AddParameter(command, "@SendToNewHires", sendToNewHires);
                HrmsDatabase.AddParameter(command, "@IsActive", isActive);
                HrmsDatabase.AddParameter(command, "@Conditions", HrConditions.Serialize(conditions));
                HrmsDatabase.AddParameter(command, "@CreatedBy", user);
            });
    }

    /// <summary>حذف منطقي — إرسالاتٌ سابقة تشير للقالب، ومحوُه يتيّم إثباتاً قانونياً.</summary>
    public static async Task DeleteTemplateAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE AcknowledgmentTemplates SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

    // ── الإرسال والمتابعة ──────────────────────────────────────────────────────

    public static async Task<List<Assignment>> LoadAssignmentsAsync(
        ApplicationDbContext db,
        Security.CompanyScope scope,
        int? employeeId = null,
        int? templateId = null,
        bool pendingOnly = false)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<Assignment>();

        var filters = new List<string>();
        if (!scope.IsUnrestricted)
            filters.Add(Security.EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId"));
        if (employeeId is not null) filters.Add("a.EmployeeId = @EmployeeId");
        if (templateId is not null) filters.Add("a.TemplateId = @TemplateId");
        if (pendingOnly) filters.Add("a.AcceptedAt IS NULL AND a.DeclinedAt IS NULL");

        var where = filters.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", filters);

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT a.Id, a.TemplateId, ISNULL(t.Name, N'') AS TemplateName, a.EmployeeId,
       ISNULL(e.FullName, N'') AS EmployeeName, ISNULL(e.EmployeeNo, N'') AS EmployeeNo,
       a.SentAt, a.ViewedAt, a.AcceptedAt, a.DeclinedAt, t.Title, t.Body
FROM EmployeeAcknowledgments a
LEFT JOIN AcknowledgmentTemplates t ON t.Id = a.TemplateId
LEFT JOIN Employees e ON e.Id = a.EmployeeId
WHERE 1 = 1{where}
ORDER BY a.SentAt DESC, a.Id DESC;
""",
            command =>
            {
                if (employeeId is not null) HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                if (templateId is not null) HrmsDatabase.AddParameter(command, "@TemplateId", templateId);
            },
            reader => new Assignment(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "TemplateId"),
                HrmsDatabase.GetString(reader, "TemplateName"),
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetString(reader, "EmployeeName"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetDateTime(reader, "SentAt") ?? DateTime.MinValue,
                HrmsDatabase.GetDateTime(reader, "ViewedAt"),
                HrmsDatabase.GetDateTime(reader, "AcceptedAt"),
                HrmsDatabase.GetDateTime(reader, "DeclinedAt"),
                HrmsDatabase.GetString(reader, "Title"),
                HrmsDatabase.GetString(reader, "Body")));
    }

    /// <summary>
    /// يرسل القالب لمجموعة موظفين. يعيد عدد المُرسَل إليهم فعلاً.
    ///
    /// ⚠️ **لا يُطوى التكرار عمداً**: إعادة إرسال نفس الإقرار لنفس الموظف حالةٌ
    /// مشروعة ومقصودة (تحديث سياسة · إقرار سنوي)، وكل إرسال سجلّ مستقل بختمه
    /// الزمني. طيّ التكرار كان سيمنع الشركة من إثبات إبلاغٍ ثانٍ بعد تعديل اللائحة.
    /// </summary>
    public static async Task<int> SendAsync(
        ApplicationDbContext db,
        int templateId,
        IReadOnlyCollection<int> employeeIds,
        string? sentBy)
    {
        if (employeeIds.Count == 0)
        {
            return 0;
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        foreach (var employeeId in employeeIds.Distinct())
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO EmployeeAcknowledgments (TemplateId, EmployeeId, SentBy)
VALUES (@TemplateId, @EmployeeId, @SentBy);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@TemplateId", templateId);
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@SentBy", sentBy);
                });
        }

        await transaction.CommitAsync();
        return employeeIds.Distinct().Count();
    }

    /// <summary>
    /// من تنطبق عليه شروط القالب (بمحرّك الشروط العام).
    /// **قالب بلا شروط ⟶ كل النشطين** — نمط «أهلية» لا «نسخة سياسة»، فالإقرار بلا
    /// شرط مقصودٌ به الجميع.
    /// </summary>
    public static async Task<List<int>> ResolveAudienceAsync(
        ApplicationDbContext db,
        Security.CompanyScope scope,
        HrConditions.ConditionSet conditions,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new List<int>();

        var rows = await HrConditionFacts.LoadAsync(db);

        return rows
            .Where(row => row.IsActive)
            // جمهور «أرسل للجميع» = جميعُ نطاقك لا جميعُ النظام (نفس درس إشعارات الحضور).
            .Where(row => scope.Allows(row.CompanyId))
            .Where(row => HrConditions.Matches(conditions, HrConditionFacts.Build(row, asOf), matchWhenEmpty: true))
            .Select(row => row.Id)
            .ToList();
    }

    /// <summary>ختم المشاهدة يُكتب **مرّة واحدة** — أول فتح هو ما يُثبَت، لا آخره.</summary>
    public static async Task MarkViewedAsync(ApplicationDbContext db, int assignmentId, int employeeId) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE EmployeeAcknowledgments
SET ViewedAt = SYSUTCDATETIME()
WHERE Id = @Id AND EmployeeId = @EmployeeId AND ViewedAt IS NULL;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", assignmentId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });

    /// <summary>
    /// موافقة أو رفض. الشرط <c>EmployeeId</c> بالجملة ليس تجميلاً: بدونه يستطيع
    /// موظفٌ أن يوافق نيابةً عن غيره بتخمين المعرّف — وهو إثبات قانوني مزوَّر.
    /// وشرط «لم يُحسم بعد» يمنع تبديل الموافقة برفض لاحقاً وضياع الختم الأصلي.
    /// </summary>
    public static async Task RespondAsync(
        ApplicationDbContext db,
        int assignmentId,
        int employeeId,
        bool accepted,
        string? note) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            $"""
UPDATE EmployeeAcknowledgments
SET {(accepted ? "AcceptedAt" : "DeclinedAt")} = SYSUTCDATETIME(),
    ViewedAt = ISNULL(ViewedAt, SYSUTCDATETIME()),
    Note = @Note
WHERE Id = @Id AND EmployeeId = @EmployeeId
  AND AcceptedAt IS NULL AND DeclinedAt IS NULL;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", assignmentId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Note", note);
            });

    public static async Task DeleteAssignmentAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "DELETE FROM EmployeeAcknowledgments WHERE Id = @Id AND AcceptedAt IS NULL AND DeclinedAt IS NULL;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
}
