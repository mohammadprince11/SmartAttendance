using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قوالب الوثائق وأرشيف المولَّد منها (منشئ الوثائق — المرحلة الأولى).
///
/// ⭐ <b>القرار المحوري: الوثيقة المولَّدة تُخزَّن بنصّها النهائي لا بمرجعٍ للقالب.</b>
/// شهادةٌ صدرت وسُلِّمت للبنك واقعةٌ تاريخية؛ لو حُسبت عند كل عرض لتغيّر مضمونها
/// بتعديل القالب أو بترقية راتب — فتصير الوثيقة المؤرشفة كذبةً على نفسها.
/// </summary>
public static class DocumentTemplateStore
{
    /// <summary>
    /// أنواع القوالب — جدول واحد بثلاثة أنواع لا ثلاثة جداول: بنية الترويسة
    /// والتذييل مطابقة للوثيقة (اسم + نصّ + رموز دمج) كما أثبت الفحص الحيّ لكيان.
    /// </summary>
    public const string KindDocument = "Document";
    public const string KindHeader = "Header";
    public const string KindFooter = "Footer";

    public static string KindLabel(string? kind) => kind switch
    {
        KindHeader => "ترويسة",
        KindFooter => "تذييل",
        _ => "وثيقة"
    };

    public sealed record Template(
        int Id,
        string Name,
        string? NameEn,
        string? Description,
        string? Body,
        string ConditionsJson,
        string? RefPrefix,
        bool AllowEmployeeRequest,
        bool IsActive,
        string Kind,
        int? HeaderTemplateId,
        int? FooterTemplateId,
        string? StampKey,
        string? StampFileName,
        bool IsReasonRequired = false,
        bool IsAttachmentRequired = false,
        bool EnableVerification = false,
        string? VerificationFactor = null,
        int? VerifyValidDays = null)
    {
        public HrConditions.ConditionSet Conditions => HrConditions.Deserialize(ConditionsJson);
        public List<string> Tokens => DocumentTokenEngine.ExtractTokens(Body);
        public bool IsDocument => Kind == KindDocument;
        public bool HasStamp => !string.IsNullOrWhiteSpace(StampKey);
    }

    public sealed record Generated(
        int Id,
        string? ReferenceNo,
        int TemplateId,
        string? TemplateName,
        int EmployeeId,
        string EmployeeName,
        string EmployeeNo,
        string? BodyHtml,
        string? UnresolvedTokens,
        DateOnly IssuedOn,
        string? IssuedBy,
        string? Source,
        string? Notes,
        string? HeaderHtml = null,
        string? FooterHtml = null,
        string? StampKey = null,
        int? EmployeeDocumentId = null,
        string? VerifyToken = null,
        string? VerifyFactor = null,
        DateOnly? VerifyValidUntil = null,
        DateTime? RevokedAt = null,
        string? RevokedBy = null)
    {
        public bool HasUnresolved => !string.IsNullOrWhiteSpace(UnresolvedTokens);
        public bool HasStamp => !string.IsNullOrWhiteSpace(StampKey);
        public bool IsFiled => EmployeeDocumentId is > 0;
        public bool IsVerifiable => !string.IsNullOrWhiteSpace(VerifyToken);
        public bool IsRevoked => RevokedAt is not null;
    }

    // ── القوالب ────────────────────────────────────────────────────────────────

    public static async Task<List<Template>> LoadTemplatesAsync(
        ApplicationDbContext db, bool activeOnly = false, string? kind = null)
    {
        var filters = new List<string>();
        if (activeOnly) filters.Add("IsActive = 1");
        // Kind الفارغ = وثيقة: صفوف المرحلة الأولى كُتبت قبل وجود العمود.
        if (kind is not null) filters.Add("ISNULL(Kind, N'Document') = @Kind");
        var filter = filters.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", filters);

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT Id, Name, NameEn, Description, Body, ISNULL(ConditionsJson, N'') AS ConditionsJson,
       RefPrefix, AllowEmployeeRequest, IsActive, ISNULL(Kind, N'Document') AS Kind,
       HeaderTemplateId, FooterTemplateId, StampKey, StampFileName,
       ISNULL(IsReasonRequired, 0) AS IsReasonRequired,
       ISNULL(IsAttachmentRequired, 0) AS IsAttachmentRequired,
       ISNULL(EnableVerification, 0) AS EnableVerification,
       ISNULL(VerificationFactor, N'None') AS VerificationFactor,
       VerifyValidDays
FROM DocumentTemplates
WHERE ISNULL(IsDeleted, 0) = 0{filter}
ORDER BY Name;
""",
            command =>
            {
                if (kind is not null) HrmsDatabase.AddParameter(command, "@Kind", kind);
            },
            reader => new Template(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Name"),
                HrmsDatabase.GetString(reader, "NameEn"),
                HrmsDatabase.GetString(reader, "Description"),
                HrmsDatabase.GetString(reader, "Body"),
                HrmsDatabase.GetString(reader, "ConditionsJson"),
                HrmsDatabase.GetString(reader, "RefPrefix"),
                HrmsDatabase.GetBool(reader, "AllowEmployeeRequest"),
                HrmsDatabase.GetBool(reader, "IsActive"),
                HrmsDatabase.GetString(reader, "Kind"),
                HrmsDatabase.GetNullableInt(reader, "HeaderTemplateId"),
                HrmsDatabase.GetNullableInt(reader, "FooterTemplateId"),
                HrmsDatabase.GetString(reader, "StampKey"),
                HrmsDatabase.GetString(reader, "StampFileName"),
                HrmsDatabase.GetBool(reader, "IsReasonRequired"),
                HrmsDatabase.GetBool(reader, "IsAttachmentRequired"),
                HrmsDatabase.GetBool(reader, "EnableVerification"),
                HrmsDatabase.GetString(reader, "VerificationFactor"),
                HrmsDatabase.GetNullableInt(reader, "VerifyValidDays")));
    }

    public static async Task<Template?> FindTemplateAsync(ApplicationDbContext db, int id) =>
        (await LoadTemplatesAsync(db)).FirstOrDefault(row => row.Id == id);

    /// <summary>يحفظ القالب بعد **تنقية** نصّه — لا يُخزَّن HTML خام أبداً.</summary>
    public static async Task<int> SaveTemplateAsync(
        ApplicationDbContext db,
        int id,
        string name,
        string? nameEn,
        string? description,
        string? body,
        HrConditions.ConditionSet conditions,
        string? refPrefix,
        bool allowEmployeeRequest,
        bool isActive,
        string? user,
        string kind = KindDocument,
        int? headerTemplateId = null,
        int? footerTemplateId = null,
        string? stampKey = null,
        string? stampFileName = null,
        bool isReasonRequired = false,
        bool isAttachmentRequired = false,
        bool enableVerification = false,
        string? verificationFactor = null,
        int? verifyValidDays = null)
    {
        var clean = DocumentHtmlSanitizer.Sanitize(body);

        if (id > 0)
        {
            // الختم يُحدَّث **فقط إن رُفع جديد** — حفظُ تعديلٍ بلا اختيار ملف كان
            // سيمسح ختم الشركة ويترك وثائق بلا ختم بلا أن يلاحظ أحد.
            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE DocumentTemplates
SET Name = @Name, NameEn = @NameEn, Description = @Description, Body = @Body,
    ConditionsJson = @Conditions, RefPrefix = @RefPrefix,
    AllowEmployeeRequest = @AllowRequest, IsActive = @IsActive, Kind = @Kind,
    HeaderTemplateId = @HeaderId, FooterTemplateId = @FooterId,
    StampKey = ISNULL(@StampKey, StampKey), StampFileName = ISNULL(@StampFileName, StampFileName),
    IsReasonRequired = @ReasonRequired, IsAttachmentRequired = @AttachmentRequired,
    EnableVerification = @EnableVerify, VerificationFactor = @VerifyFactor, VerifyValidDays = @VerifyDays,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
                command => Bind(command, id, name, nameEn, description, clean, conditions, refPrefix,
                    allowEmployeeRequest, isActive, user, kind, headerTemplateId, footerTemplateId, stampKey, stampFileName,
                    isReasonRequired, isAttachmentRequired, enableVerification, verificationFactor, verifyValidDays));

            return id;
        }

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO DocumentTemplates
    (Name, NameEn, Description, Body, ConditionsJson, RefPrefix, AllowEmployeeRequest, IsActive,
     Kind, HeaderTemplateId, FooterTemplateId, StampKey, StampFileName,
     IsReasonRequired, IsAttachmentRequired, EnableVerification, VerificationFactor, VerifyValidDays, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@Name, @NameEn, @Description, @Body, @Conditions, @RefPrefix, @AllowRequest, @IsActive,
        @Kind, @HeaderId, @FooterId, @StampKey, @StampFileName,
        @ReasonRequired, @AttachmentRequired, @EnableVerify, @VerifyFactor, @VerifyDays, @CreatedBy);
""",
            command => Bind(command, id, name, nameEn, description, clean, conditions, refPrefix,
                allowEmployeeRequest, isActive, user, kind, headerTemplateId, footerTemplateId, stampKey, stampFileName,
                isReasonRequired, isAttachmentRequired, enableVerification, verificationFactor, verifyValidDays));
    }

    private static void Bind(
        System.Data.Common.DbCommand command,
        int id, string name, string? nameEn, string? description, string body,
        HrConditions.ConditionSet conditions, string? refPrefix, bool allowRequest, bool isActive, string? user,
        string kind, int? headerId, int? footerId, string? stampKey, string? stampFileName,
        bool reasonRequired, bool attachmentRequired, bool enableVerify, string? verifyFactor, int? verifyDays)
    {
        if (id > 0) HrmsDatabase.AddParameter(command, "@Id", id);
        HrmsDatabase.AddParameter(command, "@Name", name);
        HrmsDatabase.AddParameter(command, "@NameEn", nameEn);
        HrmsDatabase.AddParameter(command, "@Description", description);
        HrmsDatabase.AddParameter(command, "@Body", body);
        HrmsDatabase.AddParameter(command, "@Conditions", HrConditions.Serialize(conditions));
        HrmsDatabase.AddParameter(command, "@RefPrefix", refPrefix);
        HrmsDatabase.AddParameter(command, "@AllowRequest", allowRequest);
        HrmsDatabase.AddParameter(command, "@IsActive", isActive);
        HrmsDatabase.AddParameter(command, "@Kind", kind);
        HrmsDatabase.AddParameter(command, "@HeaderId", headerId);
        HrmsDatabase.AddParameter(command, "@FooterId", footerId);
        HrmsDatabase.AddParameter(command, "@StampKey", stampKey);
        HrmsDatabase.AddParameter(command, "@StampFileName", stampFileName);
        HrmsDatabase.AddParameter(command, "@ReasonRequired", reasonRequired);
        HrmsDatabase.AddParameter(command, "@AttachmentRequired", attachmentRequired);
        HrmsDatabase.AddParameter(command, "@EnableVerify", enableVerify);
        HrmsDatabase.AddParameter(command, "@VerifyFactor", verifyFactor ?? DocumentVerificationPolicy.FactorNone);
        HrmsDatabase.AddParameter(command, "@VerifyDays", verifyDays);
        if (id <= 0) HrmsDatabase.AddParameter(command, "@CreatedBy", user);
    }

    public static async Task DeleteTemplateAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE DocumentTemplates SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

    // ── مصدر بيانات الرموز ─────────────────────────────────────────────────────

    /// <summary>
    /// يقرأ ما لا يحمله <see cref="HrConditionFacts.EmployeeRow"/> من بيانات الرموز
    /// (الأسماء والمسمّيات والعقد الحالي). استعلامٌ واحد لموظف واحد — التوليد
    /// الجماعي يستدعيه بحلقة، وهو مقبول لأن التوليد فعلٌ مقصود لا صفحةُ عرض.
    /// </summary>
    /// <param name="fileUrl">
    /// باني رابط الملف المحميّ (<c>IProtectedFileService.BuildUrl</c>) — يُمرَّر
    /// لأن الرابط موقَّع بمُحافظ بيانات لا يمكن بناؤه بدالّة ساكنة. إغفاله يعني
    /// توقيعاً فارغاً بالوثيقة لا رابطاً مكسوراً.
    /// </param>
    public static async Task<DocumentTokenEngine.EmployeeExtras?> LoadExtrasAsync(
        ApplicationDbContext db, int employeeId, Func<int, string?, string>? fileUrl = null)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT e.FullName, e.FirstNameEn, e.LastNameEn, e.FirstName, e.LastName,
       e.NationalId, e.PassportNo, e.Phone, e.Email, e.Position,
       ISNULL(d.Name, N'') AS DepartmentName, ISNULL(b.Name, N'') AS BranchName,
       ISNULL(m.FullName, N'') AS ManagerName,
       ISNULL(p.Name, N'') AS PositionName,
       ec.BankName, e.SignaturePath,
       c.ContractNo, c.FromDate AS ContractStart
FROM Employees e
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
LEFT JOIN Employees m ON m.Id = e.DirectManagerId
LEFT JOIN Positions p ON p.Id = e.PositionId
LEFT JOIN EmployeeCompensations ec ON ec.EmployeeId = e.Id
OUTER APPLY (
    SELECT TOP 1 ContractNo, FromDate
    FROM EmployeeContracts
    WHERE EmployeeId = e.Id AND ISNULL(IsDeleted, 0) = 0
    ORDER BY IsCurrent DESC, FromDate DESC, Id DESC
) c
WHERE e.Id = @EmployeeId AND ISNULL(e.IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader =>
            {
                var positionName = HrmsDatabase.GetString(reader, "PositionName");
                if (positionName.Length == 0) positionName = HrmsDatabase.GetString(reader, "Position");

                var firstEn = HrmsDatabase.GetString(reader, "FirstNameEn");
                var lastEn = HrmsDatabase.GetString(reader, "LastNameEn");
                var fullEn = string.Join(' ', new[] { firstEn, lastEn }.Where(part => part.Length > 0));

                return new DocumentTokenEngine.EmployeeExtras(
                    HrmsDatabase.GetString(reader, "FullName"),
                    fullEn,
                    HrmsDatabase.GetString(reader, "FirstName"),
                    HrmsDatabase.GetString(reader, "LastName"),
                    HrmsDatabase.GetString(reader, "NationalId"),
                    HrmsDatabase.GetString(reader, "PassportNo"),
                    HrmsDatabase.GetString(reader, "Phone"),
                    HrmsDatabase.GetString(reader, "Email"),
                    positionName,
                    HrmsDatabase.GetString(reader, "DepartmentName"),
                    HrmsDatabase.GetString(reader, "BranchName"),
                    HrmsDatabase.GetString(reader, "ManagerName"),
                    HrmsDatabase.GetString(reader, "ContractNo"),
                    HrmsDatabase.GetDateOnly(reader, "ContractStart"),
                    HrmsDatabase.GetString(reader, "BankName"),
                    fileUrl?.Invoke(employeeId, HrmsDatabase.GetString(reader, "SignaturePath")));
            });

        return rows.FirstOrDefault();
    }

    // ── التوليد ────────────────────────────────────────────────────────────────

    /// <summary>
    /// يولّد وثيقةً لموظف ويؤرشفها. يُرجع المعرّف والرموز غير المحلولة.
    /// <paramref name="persist"/> = false يعطي **معاينة** بلا كتابة — والمعاينة
    /// قبل الاعتماد هي ما يمنع إصدار مئة شهادة بحقلٍ فارغ.
    /// </summary>
    public static async Task<(int Id, string Html, IReadOnlyList<string> Unresolved)> GenerateAsync(
        ApplicationDbContext db,
        Template template,
        int employeeId,
        string? issuedBy,
        string? notes,
        DateOnly issuedOn,
        bool persist,
        string source = "مباشر",
        Func<int, string?, string>? fileUrl = null)
    {
        var rows = await HrConditionFacts.LoadAsync(db, employeeId);
        var extras = await LoadExtrasAsync(db, employeeId, fileUrl);

        if (rows.Count == 0 || extras is null)
        {
            return (0, string.Empty, new[] { "الموظف غير موجود" });
        }

        var reference = persist
            ? await NextReferenceAsync(db, template.RefPrefix, issuedOn.Year)
            : $"{(string.IsNullOrWhiteSpace(template.RefPrefix) ? "DOC" : template.RefPrefix)}{issuedOn.Year % 100:00}-معاينة";

        var context = new DocumentTokenEngine.DocumentContext(
            reference, issuedOn, issuedBy, await CompanyNameAsync(db, rows[0].CompanyId), extras.BranchName, null);

        var tokens = DocumentTokenEngine.Build(rows[0], extras, context, issuedOn);
        var render = DocumentTokenEngine.Render(template.Body, tokens);

        // الترويسة والتذييل يمرّان **بنفس محرّك الرموز**: ترويسة تحمل اسم الفرع أو
        // تاريخ الإصدار تحتاجهما تماماً كما يحتاجهما المتن.
        var all = new List<string>(render.UnresolvedTokens);
        var header = await RenderPartAsync(db, template.HeaderTemplateId, tokens, all);
        var footer = await RenderPartAsync(db, template.FooterTemplateId, tokens, all);

        if (!persist)
        {
            return (0, ComposePreview(header, render.Html, footer), all);
        }

        // التحقق: رمزٌ وPIN يُولَّدان **عند الإصدار** لا عند العرض — الوثيقة تُطبَع
        // وتخرج من الشركة، فرمزها يجب أن يكون ثابتاً لا مشتقّاً قابلاً للتغيّر.
        string? verifyToken = null;
        string? pinHash = null;
        DateOnly? validUntil = null;
        LastIssuedPin = null;

        if (template.EnableVerification)
        {
            verifyToken = DocumentVerificationPolicy.NewToken();
            validUntil = DocumentVerificationPolicy.ValidUntil(issuedOn, template.VerifyValidDays);

            if (template.VerificationFactor == DocumentVerificationPolicy.FactorPin)
            {
                // الـPIN يُعرض **مرّة واحدة** بعد الإصدار ولا يُخزَّن خاماً؛ فقدُه
                // يعني إعادة إصدار، وهو الثمن الصحيح لسرٍّ لا يُسترجَع.
                LastIssuedPin = DocumentVerificationPolicy.NewPin();
                pinHash = DocumentVerificationPolicy.HashPin(LastIssuedPin, verifyToken);
            }
        }

        var id = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO GeneratedDocuments
    (ReferenceNo, TemplateId, TemplateName, EmployeeId, BodyHtml, UnresolvedTokens,
     IssuedOn, IssuedBy, Source, Notes, HeaderHtml, FooterHtml, StampKey,
     VerifyToken, VerifyPinHash, VerifyFactor, VerifyValidUntil)
OUTPUT INSERTED.Id
VALUES (@Ref, @TemplateId, @TemplateName, @EmployeeId, @Body, @Unresolved,
        @IssuedOn, @IssuedBy, @Source, @Notes, @Header, @Footer, @StampKey,
        @VerifyToken, @PinHash, @VerifyFactor, @ValidUntil);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Ref", reference);
                HrmsDatabase.AddParameter(command, "@TemplateId", template.Id);
                HrmsDatabase.AddParameter(command, "@TemplateName", template.Name);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Body", render.Html);
                HrmsDatabase.AddParameter(command, "@Unresolved", all.Count == 0 ? null : string.Join(", ", all));
                HrmsDatabase.AddParameter(command, "@IssuedOn", issuedOn.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@IssuedBy", issuedBy);
                HrmsDatabase.AddParameter(command, "@Source", source);
                HrmsDatabase.AddParameter(command, "@Notes", notes);
                // تُخزَّن **بالوثيقة** لا تُقرأ من القالب عند العرض: تغيير الترويسة
                // بعد سنة يجب ألا يغيّر شهادةً صدرت وسُلِّمت.
                HrmsDatabase.AddParameter(command, "@Header", header);
                HrmsDatabase.AddParameter(command, "@Footer", footer);
                HrmsDatabase.AddParameter(command, "@StampKey", template.StampKey);
                HrmsDatabase.AddParameter(command, "@VerifyToken", verifyToken);
                HrmsDatabase.AddParameter(command, "@PinHash", pinHash);
                HrmsDatabase.AddParameter(command, "@VerifyFactor",
                    template.EnableVerification ? template.VerificationFactor : null);
                HrmsDatabase.AddParameter(command, "@ValidUntil", validUntil?.ToDateTime(TimeOnly.MinValue));
            });

        return (id, render.Html, all);
    }

    /// <summary>يرسم ترويسةً أو تذييلاً بنفس الرموز، ويضمّ ما لم يُحَلّ لقائمة الوثيقة.</summary>
    private static async Task<string?> RenderPartAsync(
        ApplicationDbContext db, int? templateId, IReadOnlyDictionary<string, string> tokens, List<string> unresolved)
    {
        if (templateId is not { } id)
        {
            return null;
        }

        var part = await FindTemplateAsync(db, id);
        if (part is null)
        {
            return null;
        }

        var rendered = DocumentTokenEngine.Render(part.Body, tokens);

        foreach (var token in rendered.UnresolvedTokens)
        {
            if (!unresolved.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                unresolved.Add(token);
            }
        }

        return rendered.Html;
    }

    private static string ComposePreview(string? header, string body, string? footer)
    {
        var builder = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(header)) builder.Append(header);
        builder.Append(body);
        if (!string.IsNullOrWhiteSpace(footer)) builder.Append(footer);
        return builder.ToString();
    }

    /// <summary>
    /// يودع الوثيقة الصادرة **ملف الموظف** (<c>EmployeeDocuments</c>) فتظهر بملفه
    /// 360 لا بشاشة التوليد وحدها. الإيداع صريح لا تلقائي: ليست كل وثيقة تُولَّد
    /// جديرةً بأرشيف الموظف الدائم (معاينات · مسودّات · نسخ بديلة).
    /// </summary>
    public static async Task<bool> FileToEmployeeAsync(ApplicationDbContext db, int generatedId, string? user)
    {
        var document = await FindGeneratedAsync(db, generatedId);

        if (document is null || document.IsFiled)
        {
            return false;
        }

        var employeeDocumentId = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO EmployeeDocuments (EmployeeId, DocumentType, FileName, StoredPath, Notes, UploadedBy)
OUTPUT INSERTED.Id
VALUES (@EmployeeId, @Type, @FileName, @Path, @Notes, @By);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", document.EmployeeId);
                HrmsDatabase.AddParameter(command, "@Type", document.TemplateName ?? "وثيقة مولَّدة");
                HrmsDatabase.AddParameter(command, "@FileName", $"{document.ReferenceNo}.html");
                // مسار داخلي لصفحة العرض لا ملفّ على القرص: الوثيقة نصّها بالقاعدة،
                // وتوليد ملفٍ لكل وثيقة يضاعف التخزين بلا فائدة.
                HrmsDatabase.AddParameter(command, "@Path", $"/Documents/View?id={document.Id}");
                HrmsDatabase.AddParameter(command, "@Notes", document.Notes);
                HrmsDatabase.AddParameter(command, "@By", user);
            });

        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE GeneratedDocuments SET EmployeeDocumentId = @DocId WHERE Id = @Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@DocId", employeeDocumentId);
                HrmsDatabase.AddParameter(command, "@Id", generatedId);
            });

        return true;
    }

    /// <summary>ترقيم سنوي متسلسل — يُعاد من واحد كل سنة فيبقى الرقم قصيراً.</summary>
    private static async Task<string> NextReferenceAsync(ApplicationDbContext db, string? prefix, int year)
    {
        var count = await HrmsDatabase.ScalarAsync<int>(
            db,
            "SELECT COUNT(*) FROM GeneratedDocuments WHERE YEAR(IssuedOn) = @Year;",
            command => HrmsDatabase.AddParameter(command, "@Year", year));

        return ViolationConfigPolicy.ReferenceNumber(
            string.IsNullOrWhiteSpace(prefix) ? "DOC" : prefix, year, count + 1);
    }

    private static async Task<string?> CompanyNameAsync(ApplicationDbContext db, int? companyId) =>
        companyId is null
            ? null
            : await HrmsDatabase.ScalarAsync<string>(
                db,
                "SELECT TOP 1 Name FROM Companies WHERE Id = @Id;",
                command => HrmsDatabase.AddParameter(command, "@Id", companyId));

    // ── الأرشيف ────────────────────────────────────────────────────────────────

    public static async Task<List<Generated>> LoadGeneratedAsync(
        ApplicationDbContext db, int? employeeId = null, int? templateId = null)
    {
        var filters = new List<string>();
        if (employeeId is not null) filters.Add("g.EmployeeId = @EmployeeId");
        if (templateId is not null) filters.Add("g.TemplateId = @TemplateId");
        var where = filters.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", filters);

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT g.Id, g.ReferenceNo, g.TemplateId, g.TemplateName, g.EmployeeId,
       ISNULL(e.FullName, N'') AS EmployeeName, ISNULL(e.EmployeeNo, N'') AS EmployeeNo,
       g.BodyHtml, g.UnresolvedTokens, g.IssuedOn, g.IssuedBy, g.Source, g.Notes,
       g.HeaderHtml, g.FooterHtml, g.StampKey, g.EmployeeDocumentId,
       g.VerifyToken, g.VerifyFactor, g.VerifyValidUntil, g.RevokedAt, g.RevokedBy
FROM GeneratedDocuments g
LEFT JOIN Employees e ON e.Id = g.EmployeeId
WHERE ISNULL(g.IsDeleted, 0) = 0{where}
ORDER BY g.Id DESC;
""",
            command =>
            {
                if (employeeId is not null) HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                if (templateId is not null) HrmsDatabase.AddParameter(command, "@TemplateId", templateId);
            },
            reader => new Generated(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "ReferenceNo"),
                HrmsDatabase.GetInt(reader, "TemplateId"),
                HrmsDatabase.GetString(reader, "TemplateName"),
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetString(reader, "EmployeeName"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "BodyHtml"),
                HrmsDatabase.GetString(reader, "UnresolvedTokens"),
                HrmsDatabase.GetDateOnly(reader, "IssuedOn") ?? default,
                HrmsDatabase.GetString(reader, "IssuedBy"),
                HrmsDatabase.GetString(reader, "Source"),
                HrmsDatabase.GetString(reader, "Notes"),
                HrmsDatabase.GetString(reader, "HeaderHtml"),
                HrmsDatabase.GetString(reader, "FooterHtml"),
                HrmsDatabase.GetString(reader, "StampKey"),
                HrmsDatabase.GetNullableInt(reader, "EmployeeDocumentId"),
                HrmsDatabase.GetString(reader, "VerifyToken"),
                HrmsDatabase.GetString(reader, "VerifyFactor"),
                HrmsDatabase.GetDateOnly(reader, "VerifyValidUntil"),
                HrmsDatabase.GetDateTime(reader, "RevokedAt"),
                HrmsDatabase.GetString(reader, "RevokedBy")));
    }

    public static async Task<Generated?> FindGeneratedAsync(ApplicationDbContext db, int id) =>
        (await LoadGeneratedAsync(db)).FirstOrDefault(row => row.Id == id);

    /// <summary>
    /// الـPIN الصادر بآخر عملية توليد — يُعرض **مرّة واحدة** للمُصدِر ثم يُنسى.
    /// حقلٌ ساكن لأن الاستدعاء متسلسل داخل الطلب الواحد؛ ولا يُقرأ إلا فور التوليد.
    /// </summary>
    [ThreadStatic] public static string? LastIssuedPin;

    /// <summary>
    /// البحث برمز التحقق — المدخل مطبَّع فتُقبل الشرطات والمسافات وحالة الأحرف.
    /// <b>لا يُرجع نصّ الوثيقة</b>: التحقق إثباتُ أصالةٍ لا نافذةُ بيانات.
    /// </summary>
    public static async Task<Generated?> FindByVerifyTokenAsync(ApplicationDbContext db, string? token)
    {
        var normalized = DocumentVerificationPolicy.NormalizeToken(token);
        if (normalized.Length == 0)
        {
            return null;
        }

        return (await LoadGeneratedAsync(db))
            .FirstOrDefault(row => DocumentVerificationPolicy.NormalizeToken(row.VerifyToken) == normalized);
    }

    /// <summary>تجزئة الـPIN المخزَّنة — تُقرأ وحدها فلا تُحمَل بكل قراءة أرشيف.</summary>
    public static async Task<string?> LoadPinHashAsync(ApplicationDbContext db, int generatedId) =>
        await HrmsDatabase.ScalarAsync<string>(
            db,
            "SELECT TOP 1 VerifyPinHash FROM GeneratedDocuments WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", generatedId));

    /// <summary>
    /// سحب الوثيقة: يُبطل تحقّقها فوراً **بلا حذفها** من الأرشيف — الوثيقة الصادرة
    /// واقعة تاريخية، والسحب واقعة أخرى فوقها لا محوٌ للأولى.
    /// </summary>
    public static async Task RevokeAsync(ApplicationDbContext db, int id, string? user) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE GeneratedDocuments
SET RevokedAt = SYSUTCDATETIME(), RevokedBy = @By
WHERE Id = @Id AND RevokedAt IS NULL;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@By", user);
            });

    public static async Task DeleteGeneratedAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE GeneratedDocuments SET IsDeleted = 1 WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
}
