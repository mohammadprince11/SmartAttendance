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
        _ => "مُستلَم"
    };

    public sealed record Submission(
        int Id,
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
    }

    public sealed record Answer(int Id, int SubmissionId, int? FieldId, string? FieldLabel, string? ControlType, string? Value);

    // ── الكتابة ────────────────────────────────────────────────────────────────

    /// <summary>
    /// يحفظ تعبئةً كاملة بمعاملة واحدة. إجاباتٌ حُفظت بلا رأسها (أو العكس) حالةٌ
    /// لا تُصلَّح إلا يدوياً.
    ///
    /// <paramref name="answers"/> = (معرّف الحقل، النصّ، النوع، القيمة، الترتيب).
    /// </summary>
    public static async Task<int> SubmitAsync(
        ApplicationDbContext db,
        FormTemplateStore.Template template,
        int employeeId,
        IReadOnlyList<(int FieldId, string Label, string ControlType, string? Value, int SortOrder)> answers,
        string? submittedBy)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        var submissionId = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO FormSubmissions (TemplateId, TemplateName, FormType, EmployeeId, Status, SubmittedBy)
OUTPUT INSERTED.Id
VALUES (@TemplateId, @TemplateName, @FormType, @EmployeeId, N'Submitted', @By);
""",
            command =>
            {
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

        await transaction.CommitAsync();
        return submissionId;
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

        await HrmsDatabase.ExecuteAsync(
            db,
            $"""
UPDATE FormSubmissions
SET Status = @Status, ReviewedBy = @By, ReviewedAt = SYSUTCDATETIME(), ReviewNote = @Note
WHERE Id = @Id AND Status = N'Submitted';
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", submissionId);
                HrmsDatabase.AddParameter(command, "@Status", status);
                HrmsDatabase.AddParameter(command, "@By", reviewer);
                HrmsDatabase.AddParameter(command, "@Note", note);
            });

        return true;
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
SELECT s.Id, s.TemplateId, s.TemplateName, s.FormType, s.EmployeeId,
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
