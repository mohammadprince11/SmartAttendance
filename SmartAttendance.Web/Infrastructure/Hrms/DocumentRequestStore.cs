using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// طلبات الوثائق من الخدمة الذاتية (منشئ الوثائق — م٣).
///
/// الطلب **كيان مستقل** لا حقلٌ على الوثيقة: طلبٌ مرفوض لا وثيقة له، وطلبٌ معلّق
/// كذلك. والربط بالوثيقة يقع عند الاعتماد فقط، فيبقى «طُلبت ورُفضت ولماذا» مُجاباً.
///
/// المنطق كلّه بـ<see cref="DocumentRequestPolicy"/>؛ هذا الملف تخزينٌ ووصل.
/// </summary>
public static class DocumentRequestStore
{
    public sealed record Request(
        int Id,
        int EmployeeId,
        string EmployeeName,
        string EmployeeNo,
        int TemplateId,
        string? TemplateName,
        string? Reason,
        string? AttachmentKey,
        string? AttachmentName,
        string Status,
        DateTime RequestedAt,
        string? ReviewedBy,
        DateTime? ReviewedAt,
        string? ReviewNote,
        int? GeneratedDocumentId)
    {
        public bool IsPending => DocumentRequestPolicy.CanReview(Status);
        public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentKey);
        public string StatusLabel => DocumentRequestPolicy.StatusLabel(Status);
    }

    public static async Task<List<Request>> LoadAsync(
        ApplicationDbContext db, int? employeeId = null, string? status = null)
    {
        var filters = new List<string>();
        if (employeeId is not null) filters.Add("r.EmployeeId = @EmployeeId");
        if (!string.IsNullOrWhiteSpace(status)) filters.Add("r.Status = @Status");
        var where = filters.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", filters);

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT r.Id, r.EmployeeId, ISNULL(e.FullName, N'') AS EmployeeName,
       ISNULL(e.EmployeeNo, N'') AS EmployeeNo, r.TemplateId, r.TemplateName,
       r.Reason, r.AttachmentKey, r.AttachmentName, r.Status, r.RequestedAt,
       r.ReviewedBy, r.ReviewedAt, r.ReviewNote, r.GeneratedDocumentId
FROM DocumentRequests r
LEFT JOIN Employees e ON e.Id = r.EmployeeId{where}
ORDER BY CASE WHEN r.Status = N'Pending' THEN 0 ELSE 1 END, r.Id DESC;
""",
            command =>
            {
                if (employeeId is not null) HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                if (!string.IsNullOrWhiteSpace(status)) HrmsDatabase.AddParameter(command, "@Status", status);
            },
            reader => new Request(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetString(reader, "EmployeeName"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetInt(reader, "TemplateId"),
                HrmsDatabase.GetString(reader, "TemplateName"),
                HrmsDatabase.GetString(reader, "Reason"),
                HrmsDatabase.GetString(reader, "AttachmentKey"),
                HrmsDatabase.GetString(reader, "AttachmentName"),
                HrmsDatabase.GetString(reader, "Status"),
                HrmsDatabase.GetDateTime(reader, "RequestedAt") ?? DateTime.MinValue,
                HrmsDatabase.GetString(reader, "ReviewedBy"),
                HrmsDatabase.GetDateTime(reader, "ReviewedAt"),
                HrmsDatabase.GetString(reader, "ReviewNote"),
                HrmsDatabase.GetNullableInt(reader, "GeneratedDocumentId")));
    }

    public static async Task<Request?> FindAsync(ApplicationDbContext db, int id) =>
        (await LoadAsync(db)).FirstOrDefault(row => row.Id == id);

    /// <summary>
    /// القوالب التي يحقّ لهذا الموظف طلبها — تُرشَّح بشروط الجمهور نفسها.
    /// فلا يرى الموظف وثيقةً لا يستحقّها أصلاً بدل أن يطلبها فتُرفض.
    /// </summary>
    public static async Task<List<DocumentTemplateStore.Template>> RequestableAsync(
        ApplicationDbContext db, int employeeId, DateOnly asOf)
    {
        var templates = await DocumentTemplateStore.LoadTemplatesAsync(
            db, activeOnly: true, kind: DocumentTemplateStore.KindDocument);

        var candidates = templates.Where(template => template.AllowEmployeeRequest).ToList();
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var rows = await HrConditionFacts.LoadAsync(db, employeeId);
        if (rows.Count == 0)
        {
            return new List<DocumentTemplateStore.Template>();
        }

        var facts = HrConditionFacts.Build(rows[0], asOf);

        return candidates
            .Where(template => DocumentRequestPolicy.CanRequest(
                template.IsActive, template.AllowEmployeeRequest, template.Conditions, facts))
            .ToList();
    }

    public static async Task<int> CreateAsync(
        ApplicationDbContext db,
        int employeeId,
        DocumentTemplateStore.Template template,
        string? reason,
        string? attachmentKey,
        string? attachmentName) =>
        await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO DocumentRequests
    (EmployeeId, TemplateId, TemplateName, Reason, AttachmentKey, AttachmentName, Status)
OUTPUT INSERTED.Id
VALUES (@EmployeeId, @TemplateId, @TemplateName, @Reason, @Key, @FileName, N'Pending');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@TemplateId", template.Id);
                HrmsDatabase.AddParameter(command, "@TemplateName", template.Name);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@Key", attachmentKey);
                HrmsDatabase.AddParameter(command, "@FileName", attachmentName);
            });

    /// <summary>
    /// اعتماد الطلب ⟵ **توليد الوثيقة فوراً وربطها به**. الاعتماد بلا توليد يترك
    /// موظفاً ينتظر وثيقةً «معتمدة» لا وجود لها.
    ///
    /// المعاملة واحدة: اعتمادٌ سُجِّل ووثيقةٌ لم تُولَّد حالةٌ لا تُصلَّح إلا يدوياً.
    /// </summary>
    public static async Task<(bool Ok, int DocumentId, string Message)> ApproveAsync(
        ApplicationDbContext db, int requestId, string? reviewer, DateOnly issuedOn)
    {
        var request = await FindAsync(db, requestId);

        if (request is null || !request.IsPending)
        {
            return (false, 0, "الطلب غير موجود أو حُسم مسبقاً.");
        }

        var template = await DocumentTemplateStore.FindTemplateAsync(db, request.TemplateId);
        if (template is null)
        {
            return (false, 0, "القالب لم يعد موجوداً — لا يمكن الاعتماد.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        var (documentId, _, unresolved) = await DocumentTemplateStore.GenerateAsync(
            db, template, request.EmployeeId, reviewer, request.Reason, issuedOn,
            persist: true, source: "طلب موظف");

        if (documentId <= 0)
        {
            return (false, 0, "تعذّر توليد الوثيقة.");
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE DocumentRequests
SET Status = N'Approved', ReviewedBy = @By, ReviewedAt = SYSUTCDATETIME(),
    GeneratedDocumentId = @DocId
WHERE Id = @Id AND Status = N'Pending';
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", requestId);
                HrmsDatabase.AddParameter(command, "@By", reviewer);
                HrmsDatabase.AddParameter(command, "@DocId", documentId);
            });

        await transaction.CommitAsync();

        var message = unresolved.Count == 0
            ? "اعتُمد الطلب وصدرت الوثيقة."
            : $"اعتُمد الطلب وصدرت الوثيقة — لكن {unresolved.Count} رمزاً بقي بلا قيمة: {string.Join("، ", unresolved)}";

        // الـPIN يُعرض مرّة واحدة للمعتمِد ليسلّمه للموظف.
        if (DocumentTemplateStore.LastIssuedPin is { } pin)
        {
            message += $" — رمز PIN للتحقق: {pin} (يُعرض مرّة واحدة فقط).";
        }

        return (true, documentId, message);
    }

    public static async Task<(bool Ok, string Message)> RejectAsync(
        ApplicationDbContext db, int requestId, string? reviewer, string? note)
    {
        if (DocumentRequestPolicy.ValidateRejection(note) is { } error)
        {
            return (false, error);
        }

        var request = await FindAsync(db, requestId);
        if (request is null || !request.IsPending)
        {
            return (false, "الطلب غير موجود أو حُسم مسبقاً.");
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE DocumentRequests
SET Status = N'Rejected', ReviewedBy = @By, ReviewedAt = SYSUTCDATETIME(), ReviewNote = @Note
WHERE Id = @Id AND Status = N'Pending';
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", requestId);
                HrmsDatabase.AddParameter(command, "@By", reviewer);
                HrmsDatabase.AddParameter(command, "@Note", note);
            });

        return (true, "رُفض الطلب مع تسجيل السبب.");
    }

    /// <summary>سحب الموظف طلبه. مشروطٌ بمعرّفه فلا يسحب أحدٌ طلب غيره.</summary>
    public static async Task<bool> WithdrawAsync(ApplicationDbContext db, int requestId, int employeeId)
    {
        var request = await FindAsync(db, requestId);

        if (request is null || request.EmployeeId != employeeId || !DocumentRequestPolicy.CanWithdraw(request.Status))
        {
            return false;
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            "DELETE FROM DocumentRequests WHERE Id = @Id AND EmployeeId = @EmployeeId AND Status = N'Pending';",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", requestId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });

        return true;
    }

    public static async Task<int> PendingCountAsync(ApplicationDbContext db) =>
        await HrmsDatabase.ScalarAsync<int>(
            db, "SELECT COUNT(*) FROM DocumentRequests WHERE Status = N'Pending';");
}
