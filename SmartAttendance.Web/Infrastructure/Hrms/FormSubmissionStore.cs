using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تعبئة النماذج وتخزين الإجابات — تُكمل باني النماذج فيصير قابلاً للاستعمال.
///
/// ⭐ <b>القرار المحوري: الإجابة تُخزَّن مع نصّ سؤالها.</b> تعديل نصّ السؤال بعد
/// سنة يجب ألا يغيّر معنى إجابةٍ أُعطيت — استبيانٌ يُقرأ بعد عام بأسئلةٍ عُدِّلت
/// هو استبيانٌ مزوَّر بلا قصد. نفس مبدأ الوثيقة الصادرة التي تُخزَّن بنصّها النهائي.
/// </summary>
public static class FormSubmissionStore
{
    public const string StatusSubmitted = "Submitted";
    public const string StatusApproved = "Approved";
    public const string StatusRejected = "Rejected";

    public static string StatusLabel(string? status) => status switch
    {
        StatusApproved => "معتمد",
        StatusRejected => "مرفوض",
        "Returned" => "مُعاد للتعديل",
        "Cancelled" => "ملغي",
        _ => "مُستلَم"
    };

    public sealed record Submission(
        int Id,
        int? RequestId,
        int TemplateId,
        string? TemplateName,
        string? FormType,
        int EmployeeId,
        string EmployeeName,
        string EmployeeNo,
        string Status,
        DateTime SubmittedAt,
        string? SubmittedBy,
        string? ReviewedBy,
        DateTime? ReviewedAt,
        string? ReviewNote)
    {
        public bool IsPending => string.Equals(Status, StatusSubmitted, StringComparison.OrdinalIgnoreCase);
        public string StatusText => StatusLabel(Status);
        public bool IsSurvey => FormBuilder.IsSurveyLike(FormType);
        public bool UsesApprovalWorkflow => RequestId is > 0;
    }

    public sealed record Answer(int Id, int SubmissionId, int? FieldId, string? FieldLabel, string? ControlType, string? Value);
    public sealed record SubmitResult(
        int SubmissionId, int? RequestId, ApprovalWorkflowEngine.ActionResult? Workflow, bool IsDuplicate = false);

    // ── الكتابة ────────────────────────────────────────────────────────────────

    /// <summary>
    /// يحفظ تعبئةً كاملة بمعاملة واحدة. إجاباتٌ حُفظت بلا رأسها (أو العكس) حالةٌ
    /// لا تُصلَّح إلا يدوياً.
    ///
    /// <paramref name="answers"/> = (معرّف الحقل، النصّ، النوع، القيمة، الترتيب).
    /// </summary>
    public static async Task<SubmitResult> SubmitAsync(
        ApplicationDbContext db,
        FormTemplateStore.Template template,
        int employeeId,
        IReadOnlyList<(int FieldId, string Label, string ControlType, string? Value, int SortOrder)> answers,
        string? submittedBy,
        Guid clientRequestToken)
    {
        if (clientRequestToken == Guid.Empty) clientRequestToken = Guid.NewGuid();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var existing = await HrmsDatabase.QueryAsync(db, """
SELECT TOP(1) Id,RequestId
FROM FormSubmissions WITH(UPDLOCK,HOLDLOCK)
WHERE ClientRequestToken=@Token AND EmployeeId=@EmployeeId AND TemplateId=@TemplateId;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Token", clientRequestToken);
            HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            HrmsDatabase.AddParameter(command, "@TemplateId", template.Id);
        }, reader => (Id: HrmsDatabase.GetInt(reader, "Id"), RequestId: HrmsDatabase.GetNullableInt(reader, "RequestId")));
        var duplicate = existing.FirstOrDefault();
        if (duplicate.Id > 0)
        {
            ApprovalWorkflowEngine.ActionResult? existingWorkflow = null;
            if (duplicate.RequestId is > 0)
            {
                var state = (await HrmsDatabase.QueryAsync(db, """
SELECT r.Status,r.ReviewNote,
       CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM ApprovalRequestFlows f WHERE f.RequestId=r.Id) THEN 1 ELSE 0 END) HasFlow
FROM SelfServiceRequests r WHERE r.Id=@RequestId AND r.EmployeeId=@EmployeeId;
""", command =>
                {
                    HrmsDatabase.AddParameter(command, "@RequestId", duplicate.RequestId.Value);
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                }, reader => (
                    Status: HrmsDatabase.GetString(reader, "Status"),
                    ReviewNote: HrmsDatabase.GetString(reader, "ReviewNote"),
                    HasFlow: HrmsDatabase.GetBool(reader, "HasFlow")))).FirstOrDefault();
                existingWorkflow = state.HasFlow
                    ? new ApprovalWorkflowEngine.ActionResult(true, "سبق استلام الطلب وبدء مسار الموافقة.")
                    : new ApprovalWorkflowEngine.ActionResult(
                        false, state.ReviewNote ?? $"سبق حفظ الطلب بحالة {state.Status} لكن مسار الموافقة غير نشط.",
                        Rejected: string.Equals(state.Status, "Rejected", StringComparison.OrdinalIgnoreCase));
            }
            await transaction.CommitAsync();
            return new SubmitResult(duplicate.Id, duplicate.RequestId, existingWorkflow, IsDuplicate: true);
        }

        int? requestId = null;
        if (!template.IsSurvey)
        {
            var requestType = $"طلب مخصص — {template.Name}";
            requestId = await HrmsDatabase.ScalarAsync<int>(
                db,
                """
INSERT INTO SelfServiceRequests
    (EmployeeId, RequestType, RequestDate, Reason, Status, CurrentStep, CreatedBy, RequestSource)
OUTPUT INSERTED.Id
VALUES
    (@EmployeeId, @RequestType, CAST(SYSUTCDATETIME() AS date), @Reason,
     N'Pending', N'بدء المسار', @By, N'SelfService');
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@RequestType", requestType);
                    HrmsDatabase.AddParameter(command, "@Reason", string.IsNullOrWhiteSpace(template.Description)
                        ? $"تعبئة النموذج: {template.Name}"
                        : template.Description);
                    HrmsDatabase.AddParameter(command, "@By", submittedBy);
                });
        }

        var submissionId = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO FormSubmissions (RequestId, ClientRequestToken, TemplateId, TemplateName, FormType, EmployeeId, Status, SubmittedBy)
OUTPUT INSERTED.Id
VALUES (@RequestId, @Token, @TemplateId, @TemplateName, @FormType, @EmployeeId, N'Submitted', @By);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RequestId", (object?)requestId ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Token", clientRequestToken);
                HrmsDatabase.AddParameter(command, "@TemplateId", template.Id);
                HrmsDatabase.AddParameter(command, "@TemplateName", template.Name);
                HrmsDatabase.AddParameter(command, "@FormType", template.FormType);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@By", submittedBy);
            });

        foreach (var (fieldId, label, controlType, value, sortOrder) in answers)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO FormAnswers (SubmissionId, FieldId, FieldLabel, ControlType, Answer, SortOrder)
VALUES (@SubmissionId, @FieldId, @Label, @Control, @Answer, @Sort);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@SubmissionId", submissionId);
                    HrmsDatabase.AddParameter(command, "@FieldId", fieldId);
                    // نصّ السؤال يُنسخ لحظة الإجابة — تعديله لاحقاً لا يغيّر المعنى.
                    HrmsDatabase.AddParameter(command, "@Label", label);
                    HrmsDatabase.AddParameter(command, "@Control", controlType);
                    HrmsDatabase.AddParameter(command, "@Answer", value);
                    HrmsDatabase.AddParameter(command, "@Sort", sortOrder);
                });
        }

        ApprovalWorkflowEngine.ActionResult? workflow = null;
        if (requestId is > 0)
        {
            workflow = await ApprovalWorkflowEngine.StartAsync(db, requestId.Value, "CustomRequest", employeeId);
        }

        // الطلب ولقطة الإجابات وسريان الموافقة وحدة ذرّية واحدة. أي خطأ تقني في
        // إنشاء السريان يعيد الكل، فلا يبقى طلب Pending بلا خطوات موافقة.
        await transaction.CommitAsync();

        return new SubmitResult(submissionId, requestId, workflow);
    }

    /// <summary>مراجعة تعبئة (للطلبات). المحسوم لا يُعاد حسمه.</summary>
    public static async Task<bool> ReviewAsync(
        ApplicationDbContext db, int submissionId, bool approve, string? reviewer, string? note,
        CompanyScope? scope = null)
    {
        if (scope is not null && !await EmployeeCompanyGuard.CanAccessOwnedRowAsync(
                db, "FormSubmissions", "Id", submissionId, scope))
        {
            return false;
        }

        var status = approve ? StatusApproved : StatusRejected;

        var changed = await HrmsDatabase.ScalarAsync<int>(
            db,
            $"""
UPDATE FormSubmissions
SET Status = @Status, ReviewedBy = @By, ReviewedAt = SYSUTCDATETIME(), ReviewNote = @Note
WHERE Id = @Id AND RequestId IS NULL AND Status = N'Submitted';
SELECT @@ROWCOUNT;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", submissionId);
                HrmsDatabase.AddParameter(command, "@Status", status);
                HrmsDatabase.AddParameter(command, "@By", reviewer);
                HrmsDatabase.AddParameter(command, "@Note", note);
            });

        return changed == 1;
    }

    // ── القراءة ────────────────────────────────────────────────────────────────

    public static async Task<List<Submission>> LoadAsync(
        ApplicationDbContext db, int? templateId = null, int? employeeId = null, string? status = null,
        CompanyScope? scope = null)
    {
        var filters = new List<string>();
        if (scope is not null) filters.Add(EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId"));
        if (templateId is not null) filters.Add("s.TemplateId = @TemplateId");
        if (employeeId is not null) filters.Add("s.EmployeeId = @EmployeeId");
        if (!string.IsNullOrWhiteSpace(status)) filters.Add("s.Status = @Status");
        var where = filters.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", filters);

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT s.Id, s.RequestId, s.TemplateId, s.TemplateName, s.FormType, s.EmployeeId,
       ISNULL(e.FullName, N'') AS EmployeeName, ISNULL(e.EmployeeNo, N'') AS EmployeeNo,
       s.Status, s.SubmittedAt, s.SubmittedBy, s.ReviewedBy, s.ReviewedAt, s.ReviewNote
FROM FormSubmissions s
LEFT JOIN Employees e ON e.Id = s.EmployeeId{where}
ORDER BY CASE WHEN s.Status = N'Submitted' THEN 0 ELSE 1 END, s.Id DESC;
""",
            command =>
            {
                if (templateId is not null) HrmsDatabase.AddParameter(command, "@TemplateId", templateId);
                if (employeeId is not null) HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                if (!string.IsNullOrWhiteSpace(status)) HrmsDatabase.AddParameter(command, "@Status", status);
            },
            reader => new Submission(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetNullableInt(reader, "RequestId"),
                HrmsDatabase.GetInt(reader, "TemplateId"),
                HrmsDatabase.GetString(reader, "TemplateName"),
                HrmsDatabase.GetString(reader, "FormType"),
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetString(reader, "EmployeeName"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "Status"),
                HrmsDatabase.GetDateTime(reader, "SubmittedAt") ?? DateTime.MinValue,
                HrmsDatabase.GetString(reader, "SubmittedBy"),
                HrmsDatabase.GetString(reader, "ReviewedBy"),
                HrmsDatabase.GetDateTime(reader, "ReviewedAt"),
                HrmsDatabase.GetString(reader, "ReviewNote")));
    }

    /// <summary>إجابات الطلبات المخصصة مفهرسة بمعرّف طلب الموافقة للتحميل الدفعي.</summary>
    public static async Task<Dictionary<int, List<Answer>>> LoadAnswersForRequestsAsync(
        ApplicationDbContext db, IEnumerable<int> requestIds, CompanyScope scope)
    {
        var ids = requestIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, List<Answer>>();

        var parameters = ids.Select((_, index) => $"@RequestId{index}").ToList();
        var rows = await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT s.RequestId,a.Id,a.SubmissionId,a.FieldId,a.FieldLabel,a.ControlType,a.Answer
FROM FormSubmissions s
INNER JOIN FormAnswers a ON a.SubmissionId=s.Id
INNER JOIN SelfServiceRequests r ON r.Id=s.RequestId
INNER JOIN Employees e ON e.Id=r.EmployeeId AND ISNULL(e.IsDeleted,0)=0
WHERE s.RequestId IN ({string.Join(",", parameters)})
  AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
ORDER BY s.RequestId,a.SortOrder,a.Id;
""",
            command =>
            {
                for (var index = 0; index < ids.Count; index++)
                    HrmsDatabase.AddParameter(command, parameters[index], ids[index]);
            },
            reader => (
                RequestId: HrmsDatabase.GetInt(reader, "RequestId"),
                Answer: new Answer(
                    HrmsDatabase.GetInt(reader, "Id"),
                    HrmsDatabase.GetInt(reader, "SubmissionId"),
                    HrmsDatabase.GetNullableInt(reader, "FieldId"),
                    HrmsDatabase.GetString(reader, "FieldLabel"),
                    HrmsDatabase.GetString(reader, "ControlType"),
                    HrmsDatabase.GetString(reader, "Answer"))));

        return rows.GroupBy(row => row.RequestId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Answer).ToList());
    }

    public static async Task<Submission?> FindAsync(
        ApplicationDbContext db, int id, CompanyScope? scope = null) =>
        (await LoadAsync(db, scope: scope)).FirstOrDefault(row => row.Id == id);

    public static async Task<List<Answer>> LoadAnswersAsync(ApplicationDbContext db, int submissionId) =>
        await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, SubmissionId, FieldId, FieldLabel, ControlType, Answer
FROM FormAnswers WHERE SubmissionId = @Id ORDER BY SortOrder, Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", submissionId),
            reader => new Answer(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "SubmissionId"),
                HrmsDatabase.GetNullableInt(reader, "FieldId"),
                HrmsDatabase.GetString(reader, "FieldLabel"),
                HrmsDatabase.GetString(reader, "ControlType"),
                HrmsDatabase.GetString(reader, "Answer")));

    /// <summary>
    /// النماذج المتاحة لموظف — ترشيح بالجمهور نفسه. فلا يرى ما لا يستحقّه.
    /// </summary>
    public static async Task<List<FormTemplateStore.Template>> AvailableForAsync(
        ApplicationDbContext db, int employeeId, DateOnly asOf)
    {
        var templates = (await FormTemplateStore.LoadTemplatesAsync(db, activeOnly: true))
            .Where(template => template.AllowEmployeeRequest)
            .ToList();

        if (templates.Count == 0)
        {
            return templates;
        }

        var rows = await HrConditionFacts.LoadAsync(db, employeeId);
        if (rows.Count == 0)
        {
            return new List<FormTemplateStore.Template>();
        }

        var facts = HrConditionFacts.Build(rows[0], asOf);

        return templates
            .Where(template => HrConditions.Matches(template.Conditions, facts, matchWhenEmpty: true))
            .ToList();
    }

    /// <summary>
    /// متوسّط إجابات أسئلة التقييم بنموذج — أبسط تحليل مفيد للاستبيانات.
    /// **الإجابات غير الرقمية تُتجاهل** لا تُعامَل صفراً؛ صفرٌ بمكان «لم يُجب»
    /// يسحب المتوسّط لأسفل ويكذب على قارئه.
    /// </summary>
    public static async Task<Dictionary<string, (decimal Average, int Count)>> RatingAveragesAsync(
        ApplicationDbContext db, int templateId, CompanyScope? scope = null)
    {
        var scopeFilter = scope is null
            ? "1 = 1"
            : EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");
        var rows = await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT a.FieldLabel, a.Answer
FROM FormAnswers a
INNER JOIN FormSubmissions s ON s.Id = a.SubmissionId
INNER JOIN Employees e ON e.Id = s.EmployeeId
WHERE s.TemplateId = @TemplateId AND a.ControlType = N'Rating'
  AND {scopeFilter};
""",
            command => HrmsDatabase.AddParameter(command, "@TemplateId", templateId),
            reader => (
                Label: HrmsDatabase.GetString(reader, "FieldLabel"),
                Answer: HrmsDatabase.GetString(reader, "Answer")));

        return rows
            .Select(row => (row.Label, Ok: decimal.TryParse(row.Answer, out var value), Value: value))
            .Where(row => row.Ok)
            .GroupBy(row => row.Label)
            .ToDictionary(
                group => group.Key,
                group => (Math.Round(group.Average(row => row.Value), 2, MidpointRounding.AwayFromZero), group.Count()));
    }
}
