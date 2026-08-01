using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// مهاجر مخطط محكوم للجداول القديمة (SQL خام) — بديل «الشفاء الذاتي» الممنوع
/// بـ<c>docs/AI-DEVELOPMENT-RULES.md</c>: كل تغيير مخطط جديد يُكتب هنا كهجرة
/// معرَّفة بمعرّف ثابت، تُطبَّق <b>مرة واحدة</b> ويُسجَّل تطبيقها بجدول
/// <c>__SchemaMigrations</c>، وتعمل صراحةً عند إقلاع التطبيق لا بكل طلب.
///
/// قواعد كتابة الهجرة:
/// - المعرّف لا يتغيّر أبداً بعد الدمج (وإلا أُعيد تطبيقها).
/// - النص يجب أن يكون آمن التكرار (IF COL_LENGTH ... IS NULL) تحسّباً لقواعد
///   بيانات رُقّيت يدوياً قبل وجود السجل.
/// - الفشل يُرفع كاستثناء واضح ولا يُبتلع: مخطط ناقص = عطل صامت لاحقاً.
/// </summary>
public static class SqlSchemaMigrator
{
    public sealed record Migration(string Id, string Sql);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static volatile bool _applied;

    /// <summary>الهجرات بترتيب التطبيق. لا تُعدَّل هجرة منشورة — أضف واحدة جديدة.</summary>
    public static readonly IReadOnlyList<Migration> Migrations = new List<Migration>
    {
        // المرحلة 5: ختم أمان بهوية الدخول — تغييره يُبطل الكوكيز والتوكنات الصادرة.
        new(
            "20260731-01-login-security-stamp",
            """
IF OBJECT_ID('AppLoginUsers', 'U') IS NOT NULL
   AND COL_LENGTH('AppLoginUsers', 'SecurityStamp') IS NULL
    ALTER TABLE AppLoginUsers ADD SecurityStamp nvarchar(64) NULL;
"""),

        // نفس الختم يُختم داخل صفّ التوكن ليُقارَن بالحالي عند كل طلب API.
        new(
            "20260731-02-api-token-security-stamp",
            """
IF OBJECT_ID('ApiTokens', 'U') IS NOT NULL
   AND COL_LENGTH('ApiTokens', 'SecurityStamp') IS NULL
    ALTER TABLE ApiTokens ADD SecurityStamp nvarchar(64) NULL;
"""),

        // المرحلة 6: ملفات ملف الموظف تُخزَّن خارج wwwroot — عمود يميّز الصفوف
        // المحمية عن الصفوف التاريخية (StoredPath يبدأ بـ/uploads/).
        new(
            "20260731-03-employee-profile-file-protected-key",
            """
IF OBJECT_ID('EmployeeProfileFiles', 'U') IS NOT NULL
   AND COL_LENGTH('EmployeeProfileFiles', 'ProtectedKey') IS NULL
    ALTER TABLE EmployeeProfileFiles ADD ProtectedKey nvarchar(400) NULL;
"""),

        // عزل الشركات البنيوي: عمود شركة صريح على الموظف يُشتقّ من فرعه.
        // NULL عمداً بهذه الهجرة — التشديد لـNOT NULL بهجرة لاحقة بعد التحقق من
        // أن المتبقّي صفر بكل بيئة (تشخيص 2026-07-31: صفر شذوذ بالإنتاج).
        new(
            "20260731-04-employee-company-id",
            """
IF OBJECT_ID('Employees', 'U') IS NOT NULL
   AND COL_LENGTH('Employees', 'CompanyId') IS NULL
    ALTER TABLE Employees ADD CompanyId int NULL;
"""),

        // التعبئة من الفرع — الفرع هو مصدر الحقيقة الوحيد؛ لا تخمين لأي صفّ.
        // آمنة التكرار: تكتب فقط حيث القيمة غائبة أو مخالفة لشركة الفرع.
        new(
            "20260731-05-employee-company-id-backfill",
            """
IF OBJECT_ID('Employees', 'U') IS NOT NULL
   AND COL_LENGTH('Employees', 'CompanyId') IS NOT NULL
BEGIN
    UPDATE e
    SET e.CompanyId = b.CompanyId
    FROM Employees e
    INNER JOIN Branches b ON b.Id = e.BranchId
    WHERE e.CompanyId IS NULL OR e.CompanyId <> b.CompanyId;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Employees_CompanyId')
        CREATE INDEX IX_Employees_CompanyId ON Employees (CompanyId);
END;
"""),

        // تشديد العزل: العمود يصير إلزامياً بمفتاح أجنبي — بعد أن أثبت التحقق الحيّ
        // على الإنتاج أن المتبقّي صفر (1357/1357 وانحراف صفر، 2026-07-31).
        //
        // **حاجز أمان مقصود**: التشديد لا يجري إلا إذا كان الجدول نظيفاً بالفعل بهذه
        // البيئة. صفّ واحد بلا شركة ⟹ تُتخطّى الهجرة بلا فشل وبلا تعديل، فلا تكسر
        // بيئة تطوير ناقصة البيانات ولا تُسجَّل كأنها طُبِّقت على مخطط لم يتشدّد.
        new(
            "20260731-06-employee-company-id-not-null",
            """
IF OBJECT_ID('Employees', 'U') IS NOT NULL
   AND COL_LENGTH('Employees', 'CompanyId') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM Employees WHERE CompanyId IS NULL)
   AND EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID('Employees')
          AND name = 'CompanyId'
          AND is_nullable = 1)
BEGIN
    -- الفهرس الذي أنشأته الهجرة 05 يعتمد على العمود، وSQL Server يرفض تعديل عمود
    -- «تصل إليه كائنات أخرى» (Msg 5074). فيُسقَط ثم يُعاد بناؤه بعد التشديد.
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Employees_CompanyId')
        DROP INDEX IX_Employees_CompanyId ON Employees;

    ALTER TABLE Employees ALTER COLUMN CompanyId int NOT NULL;

    CREATE INDEX IX_Employees_CompanyId ON Employees (CompanyId);

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Employees_Companies_CompanyId')
        ALTER TABLE Employees
            ADD CONSTRAINT FK_Employees_Companies_CompanyId
            FOREIGN KEY (CompanyId) REFERENCES Companies (Id);
END;
"""),

        // عضوية أوعية الاحتساب: أي مكوّن قسيمة ينضمّ لوعاء الضريبة أو الضمان.
        // كانت مثبّتة بالكود داخل PayrollRunStore فصارت صفوفاً يحرّرها المستخدم.
        // **البذرة تُنتج السلوك القائم حرفياً** (ضريبة: الأساسي+العلاوات+الخاضع
        // بأنواعه · ضمان: الإجمالي) وإلا تغيّر اقتطاع كل قسيمة بأثر رجعي.
        // مشروطة بخلوّ الجدول فلا تدهس تعديلات المستخدم عند كل إقلاع.
        new(
            "20260731-07-salary-base-members",
            """
IF OBJECT_ID('SalaryBaseMembers', 'U') IS NULL
BEGIN
    CREATE TABLE SalaryBaseMembers (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BaseKey nvarchar(32) NOT NULL,
        ComponentKey nvarchar(32) NOT NULL,
        SortOrder int NOT NULL DEFAULT(0),
        CONSTRAINT UQ_SalaryBaseMembers UNIQUE (BaseKey, ComponentKey)
    );

    CREATE INDEX IX_SalaryBaseMembers_BaseKey ON SalaryBaseMembers (BaseKey);
END;

IF NOT EXISTS (SELECT 1 FROM SalaryBaseMembers)
BEGIN
    INSERT INTO SalaryBaseMembers (BaseKey, ComponentKey, SortOrder) VALUES
        (N'TaxBase',  N'Basic',            1),
        (N'TaxBase',  N'Allowances',       2),
        (N'TaxBase',  N'TaxableIncome',    3),
        (N'TaxBase',  N'TaxableOvertime',  4),
        (N'TaxBase',  N'SalaryDays',       5),
        (N'TaxBase',  N'LeaveEncashment',  6),
        (N'TaxBase',  N'FormulaAdd',       7),
        (N'GosiBase', N'Gross',            1);
END;
"""),

        // الوعاء صار خاصية **لكل ملف** ضريبة/ضمان لا إعداداً عاماً واحداً، لأن
        // المستخدم يحرّره من داخل نموذج إنشاء/تعديل الملف نفسه.
        // الصفوف المبذورة تبقى بـProfileId = 0 وتعمل كافتراضٍ عام يرثه أي ملف
        // لم تُحدَّد له عضوية — فلا ينكسر أي ملف قائم ولا يصير اقتطاعه صفراً.
        new(
            "20260731-08-salary-base-members-per-profile",
            """
IF OBJECT_ID('SalaryBaseMembers', 'U') IS NOT NULL
   AND COL_LENGTH('SalaryBaseMembers', 'ProfileId') IS NULL
BEGIN
    ALTER TABLE SalaryBaseMembers ADD ProfileId int NOT NULL CONSTRAINT DF_SalaryBaseMembers_ProfileId DEFAULT(0);
END;

IF OBJECT_ID('SalaryBaseMembers', 'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_SalaryBaseMembers')
BEGIN
    ALTER TABLE SalaryBaseMembers DROP CONSTRAINT UQ_SalaryBaseMembers;
END;

IF OBJECT_ID('SalaryBaseMembers', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_SalaryBaseMembers_Profile')
BEGIN
    ALTER TABLE SalaryBaseMembers
        ADD CONSTRAINT UQ_SalaryBaseMembers_Profile UNIQUE (BaseKey, ProfileId, ComponentKey);
END;
"""),

        // إسقاط الصفوف المبذورة بـProfileId = 0.
        // كانت تعمل «افتراضاً عاماً» يرثه أي ملف بلا عضوية، فتبيّن أنها حالة مخفية
        // قابلة للتلف: تعديلها يغيّر اقتطاع كل الملفات دفعةً وبلا أثر ظاهر — وهو ما
        // وقع فعلاً أثناء تجربة حيّة. الرجوع صار لافتراضات الكود مباشرة
        // (SalaryBaseComposer.Default*Members)، فمصدر الحقيقة لغير المهيَّأ واحد.
        new(
            "20260731-09-drop-global-salary-base-seed",
            """
IF OBJECT_ID('SalaryBaseMembers', 'U') IS NOT NULL
   AND COL_LENGTH('SalaryBaseMembers', 'ProfileId') IS NOT NULL
    DELETE FROM SalaryBaseMembers WHERE ProfileId = 0;
"""),

        // نطاق تشغيل المسير: كان المحرّك يحسب **كل** النشطين إجبارياً بلا أي اختيار،
        // فصار للتشغيل نطاقٌ محفوظ معه (لا مع الجلسة) لتلتزم به إعادة الاحتساب
        // ويبقى «لماذا غاب هذا الموظف؟» مُجاباً بعد شهر.
        // ⚠️ لا صفوف = «الكل»: القرار بالكود (PayrollRunScope.Includes) لا بصفٍّ
        // افتراضي بالقاعدة — نفس درس أوعية الاحتساب بالهجرة …-09.
        // والعمود ScopeMode توثيقي (كيف حُدِّد النطاق) لا مصدرٌ للحساب.
        new(
            "20260731-10-payroll-run-scope",
            """
IF OBJECT_ID('PayrollRunScopeMembers', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollRunScopeMembers (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RunId int NOT NULL,
        EmployeeId int NOT NULL,
        CONSTRAINT UQ_PayrollRunScopeMembers UNIQUE (RunId, EmployeeId)
    );

    CREATE INDEX IX_PayrollRunScopeMembers_Run ON PayrollRunScopeMembers (RunId);
END;

IF OBJECT_ID('PayrollRuns', 'U') IS NOT NULL
   AND COL_LENGTH('PayrollRuns', 'ScopeMode') IS NULL
    ALTER TABLE PayrollRuns ADD ScopeMode nvarchar(20) NULL;
"""),

        // طبقة «سياسة أمّ + نُسَخ مشروطة مرتّبة» (نمط كيان — راجع HrPolicyResolver).
        // جدول **واحد لكل السياسات** لأن بنية النسخة ثابتة مهما اختلفت السياسة:
        // اسم + شروط + حمولة + ترتيب. الشروط والحمولة JSON بعمودين لا بجدولين
        // فرعيين، لأنهما لا يُستعلَمان بالـSQL أبداً — يُقرآن كاملين ويُقيَّمان
        // بالذاكرة، فالتطبيع هنا كلفة بلا مقابل.
        //
        // ⚠️ **إضافيّ محض**: جدول جديد فقط، بلا مساس بأي عمود قائم. وخلوّه يعني
        // «لا نُسَخ» أي أن كل سياسة قائمة تبقى على سلوكها الحالي حرفياً — لا بذرة
        // ولا افتراض مخزَّن (درس الهجرة …-09).
        new(
            "20260801-11-hr-policy-overrides",
            """
IF OBJECT_ID('HrPolicyOverrides', 'U') IS NULL
BEGIN
    CREATE TABLE HrPolicyOverrides (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PolicyKey nvarchar(60) NOT NULL,
        Name nvarchar(150) NOT NULL,
        SortOrder int NOT NULL CONSTRAINT DF_HrPolicyOverrides_SortOrder DEFAULT(0),
        IsActive bit NOT NULL CONSTRAINT DF_HrPolicyOverrides_IsActive DEFAULT(1),
        ConditionsJson nvarchar(max) NULL,
        PayloadJson nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_HrPolicyOverrides_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL
    );

    CREATE INDEX IX_HrPolicyOverrides_Policy ON HrPolicyOverrides (PolicyKey, SortOrder, Id);
END;
"""),

        // سجلّ العقود: العقد كان **حالةً راهنة** تُدهَس عند التغيير، فصار **كياناً
        // له تاريخ**. الجدول EmployeeContracts موجود سلفاً؛ هذه الهجرة تضيف ما
        // يجعله سجلّاً حقيقياً:
        //  - PreviousContractId: سلسلة التجديد (من أي عقد وُلد هذا).
        //  - MovementKind: كيف وُلد (تمديد/تجديد/تعديل) — الفرق مالي على نهاية
        //    الخدمة وفترة التجربة، فلا يُستنتج بل يُسجَّل.
        //  - EffectiveDate + Notes: تاريخ سريان الحركة كما بكيان.
        // وأنواع العقود تكتسب **مدة افتراضية** فيُشتقّ تاريخ الانتهاء تلقائياً.
        //
        // ⚠️ إضافيّ محض: أعمدة nullable على جداول قائمة + جدول حركات جديد.
        // NULL بكل الأعمدة الجديدة = صفوف اليوم تبقى بمعناها حرفياً.
        new(
            "20260801-12-employee-contracts-register",
            """
IF OBJECT_ID('EmployeeContracts', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('EmployeeContracts', 'PreviousContractId') IS NULL
        ALTER TABLE EmployeeContracts ADD PreviousContractId int NULL;

    IF COL_LENGTH('EmployeeContracts', 'MovementKind') IS NULL
        ALTER TABLE EmployeeContracts ADD MovementKind nvarchar(20) NULL;

    IF COL_LENGTH('EmployeeContracts', 'EffectiveDate') IS NULL
        ALTER TABLE EmployeeContracts ADD EffectiveDate date NULL;
END;

-- المدة الافتراضية لنوع العقد: مصدر اشتقاق «يعتمد تاريخ الانتهاء على مدة العقد
-- الافتراضية». NULL = غير محدود، وهو التمثيل الصحيح لـ«بلا مدة».
IF OBJECT_ID('HrLookups', 'U') IS NOT NULL
   AND COL_LENGTH('HrLookups', 'DefaultMonths') IS NULL
    ALTER TABLE HrLookups ADD DefaultMonths int NULL;

IF OBJECT_ID('EmployeeContractMovements', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeContractMovements (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RefNo nvarchar(40) NULL,
        EmployeeId int NOT NULL,
        ContractId int NULL,
        ResultContractId int NULL,
        MovementKind nvarchar(20) NOT NULL,
        ContractType nvarchar(100) NULL,
        FromDate date NULL,
        ToDate date NULL,
        EffectiveDate date NULL,
        Source nvarchar(40) NULL,
        Status nvarchar(20) NOT NULL CONSTRAINT DF_ContractMovements_Status DEFAULT(N'Open'),
        Notes nvarchar(max) NULL,
        AttachmentName nvarchar(260) NULL,
        AttachmentPath nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_ContractMovements_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        LockedAt datetime2 NULL,
        LockedBy nvarchar(150) NULL
    );

    CREATE INDEX IX_ContractMovements_Employee ON EmployeeContractMovements (EmployeeId, Id);
    CREATE INDEX IX_ContractMovements_Status ON EmployeeContractMovements (Status, Id);
END;
"""),

        // الإقرارات: قالب + سجلّ إرسال لكل موظف.
        //
        // القيمة كلها بالإثبات — أن فلاناً **رأى ووافق بتاريخ محدَّد**. ولذلك
        // ViewedAt وAcceptedAt عمودان **منفصلان**: «أُرسل ولم يُفتح» حالةٌ قانونية
        // مستقلة عن «فُتح ولم يُوافَق عليه»، ودمجهما بعمود واحد يمحو أهمّ ما تحتاجه
        // الشركة بنزاع عمّالي.
        //
        // وتكرار الصفوف لنفس (الموظف، القالب) مقصود: الإقرار يُعاد إرساله دورياً
        // عند تحديث السياسة، وكل إرسال سجلّ مستقل — فلا قيد فريد عليهما.
        new(
            "20260801-13-employee-acknowledgments",
            """
IF OBJECT_ID('AcknowledgmentTemplates', 'U') IS NULL
BEGIN
    CREATE TABLE AcknowledgmentTemplates (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        Description nvarchar(1000) NULL,
        Title nvarchar(300) NULL,
        Body nvarchar(max) NULL,
        AttachmentName nvarchar(260) NULL,
        AttachmentPath nvarchar(500) NULL,
        SendToNewHires bit NOT NULL CONSTRAINT DF_AckTemplates_NewHires DEFAULT(0),
        IsActive bit NOT NULL CONSTRAINT DF_AckTemplates_Active DEFAULT(1),
        ConditionsJson nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_AckTemplates_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        UpdatedAt datetime2 NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_AckTemplates_Deleted DEFAULT(0)
    );
END;

IF OBJECT_ID('EmployeeAcknowledgments', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeAcknowledgments (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TemplateId int NOT NULL,
        EmployeeId int NOT NULL,
        SentAt datetime2 NOT NULL CONSTRAINT DF_EmpAck_SentAt DEFAULT(SYSUTCDATETIME()),
        SentBy nvarchar(150) NULL,
        ViewedAt datetime2 NULL,
        AcceptedAt datetime2 NULL,
        DeclinedAt datetime2 NULL,
        Note nvarchar(1000) NULL
    );

    CREATE INDEX IX_EmpAck_Employee ON EmployeeAcknowledgments (EmployeeId, Id);
    CREATE INDEX IX_EmpAck_Template ON EmployeeAcknowledgments (TemplateId, Id);
END;
"""),

        // رئيس وحدة مؤقت: نيابةٌ لمدى تواريخ تحلّ محلّ المدير المباشر لموظفي الوحدة.
        // ⚠️ **طبقة فوق DirectManagerId لا بديلاً عنه** — الأصل يبقى مصدر الحقيقة
        // والنيابة تنتهي بانتهاء تاريخها تلقائياً. دهسُ الأصل كان سيمنع رجوع المدير
        // الحقيقي بعد إجازته إلا بتدخّل يدوي.
        new(
            "20260801-14-temporary-unit-heads",
            """
IF OBJECT_ID('TemporaryUnitHeads', 'U') IS NULL
BEGIN
    CREATE TABLE TemporaryUnitHeads (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        DepartmentId int NOT NULL,
        HeadEmployeeId int NOT NULL,
        FromDate date NOT NULL,
        ToDate date NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_TempHeads_Active DEFAULT(1),
        Note nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_TempHeads_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL
    );

    CREATE INDEX IX_TempHeads_Dept ON TemporaryUnitHeads (DepartmentId, FromDate, ToDate);
END;
"""),

        // تهيئة المخالفات: ضمانات الانضباط التي كان غيابها يجعله إجراءً بلا حقوق.
        //  - NotifiedOn: تاريخ التبليغ — منه تبدأ مهلة الاعتراض لا من تاريخ الوقوع.
        //  - DecidedOn: تاريخ القرار — منه يُحسب سقوط العقوبة.
        //  - ObjectionAt/ObjectionText: طعن الموظف.
        //  - RegulationClause على أنواع المخالفات: رقم الفقرة من لائحة الجزاءات.
        //
        // ⚠️ إضافيّ محض. وكلّها NULL افتراضاً، والمدد تُقرأ من DisciplinarySettings
        // بقيمٍ **صفرية المعنى حتى يهيّئها المستخدم**: بلا تهيئة لا تسقط عقوبة ولا
        // يُغلق باب اعتراض — أي أن سلوك النظام القائم لا يتغيّر بمقدار يوم واحد.
        new(
            "20260801-15-violation-configuration",
            """
IF OBJECT_ID('EmployeeViolationCases', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('EmployeeViolationCases', 'NotifiedOn') IS NULL
        ALTER TABLE EmployeeViolationCases ADD NotifiedOn date NULL;

    IF COL_LENGTH('EmployeeViolationCases', 'DecidedOn') IS NULL
        ALTER TABLE EmployeeViolationCases ADD DecidedOn date NULL;

    IF COL_LENGTH('EmployeeViolationCases', 'ObjectionAt') IS NULL
        ALTER TABLE EmployeeViolationCases ADD ObjectionAt datetime2 NULL;

    IF COL_LENGTH('EmployeeViolationCases', 'ObjectionText') IS NULL
        ALTER TABLE EmployeeViolationCases ADD ObjectionText nvarchar(max) NULL;

    IF COL_LENGTH('EmployeeViolationCases', 'DroppedOn') IS NULL
        ALTER TABLE EmployeeViolationCases ADD DroppedOn date NULL;

    IF COL_LENGTH('EmployeeViolationCases', 'DroppedBy') IS NULL
        ALTER TABLE EmployeeViolationCases ADD DroppedBy nvarchar(150) NULL;
END;

IF OBJECT_ID('DisciplinaryViolationTypes', 'U') IS NOT NULL
   AND COL_LENGTH('DisciplinaryViolationTypes', 'RegulationClause') IS NULL
    ALTER TABLE DisciplinaryViolationTypes ADD RegulationClause nvarchar(60) NULL;
"""),

        // تحديثات الموظف: «بأثر رجعي» + مرفق. (تاريخ السريان كان موجوداً سلفاً.)
        //
        // الأثر الرجعي **علَمٌ صريح لا استنتاجٌ من التاريخ**: حركةٌ سريانها بالماضي
        // قد تكون تصحيحاً لخطأ إدخال (لا أثر مالي) أو قراراً بأثر رجعي (يستوجب
        // إعادة احتساب رواتب). دمجهما يجعل المسير يعيد حساب ما لا يجب أو يهمل ما يجب.
        new(
            "20260801-16-employee-update-retroactive",
            """
IF OBJECT_ID('EmployeeUpdateBatches', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('EmployeeUpdateBatches', 'IsRetroactive') IS NULL
        ALTER TABLE EmployeeUpdateBatches ADD IsRetroactive bit NULL;

    IF COL_LENGTH('EmployeeUpdateBatches', 'AttachmentName') IS NULL
        ALTER TABLE EmployeeUpdateBatches ADD AttachmentName nvarchar(260) NULL;

    IF COL_LENGTH('EmployeeUpdateBatches', 'AttachmentPath') IS NULL
        ALTER TABLE EmployeeUpdateBatches ADD AttachmentPath nvarchar(500) NULL;
END;
"""),
    };

    /// <summary>
    /// يطبّق الهجرات المعلّقة مرة واحدة لكل عملية تشغيل. يُستدعى صراحةً عند الإقلاع.
    /// </summary>
    public static async Task ApplyAsync(ApplicationDbContext dbContext)
    {
        if (_applied)
        {
            return;
        }

        await Gate.WaitAsync();

        try
        {
            if (_applied)
            {
                return;
            }

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
IF OBJECT_ID('__SchemaMigrations', 'U') IS NULL
BEGIN
    CREATE TABLE __SchemaMigrations
    (
        MigrationId nvarchar(200) NOT NULL PRIMARY KEY,
        AppliedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;
""");

            foreach (var migration in Migrations)
            {
                var alreadyApplied = await HrmsDatabase.ScalarAsync<int>(
                    dbContext,
                    "SELECT COUNT(*) FROM __SchemaMigrations WHERE MigrationId = @Id;",
                    command => HrmsDatabase.AddParameter(command, "@Id", migration.Id));

                if (alreadyApplied > 0)
                {
                    continue;
                }

                await HrmsDatabase.ExecuteAsync(dbContext, migration.Sql);

                await HrmsDatabase.ExecuteAsync(
                    dbContext,
                    "INSERT INTO __SchemaMigrations (MigrationId) VALUES (@Id);",
                    command => HrmsDatabase.AddParameter(command, "@Id", migration.Id));
            }

            _applied = true;
        }
        finally
        {
            Gate.Release();
        }
    }
}
