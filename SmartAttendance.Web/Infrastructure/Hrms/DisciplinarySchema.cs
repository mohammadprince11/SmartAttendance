using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class DisciplinarySchema
{
    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('DisciplinaryViolationCategories', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryViolationCategories
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(180) NOT NULL,
        Description nvarchar(max) NULL,
        DisplayOrder int NOT NULL DEFAULT(10),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryViolationTypes', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryViolationTypes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CategoryId int NOT NULL,
        Name nvarchar(250) NOT NULL,
        Description nvarchar(max) NULL,
        Severity nvarchar(40) NOT NULL DEFAULT('B'),
        ValidityMonths int NOT NULL DEFAULT(6),
        CountingPeriod nvarchar(40) NOT NULL DEFAULT('Monthly'),
        IncludeInEvaluation bit NOT NULL DEFAULT(1),
        ShowToEmployee bit NOT NULL DEFAULT(1),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

-- رقم الفقرة باللائحة (مثال: 3/ب). كيان يعرضه بقائمة المخالفات ويستشهد به
-- الكتاب الرسمي، فبدونه تصدر العقوبة بلا سندٍ نصّيّ من لائحة الجزاءات.
-- إضافة محضة: عمود nullable بلا مساس بصفٍّ قائم.
IF COL_LENGTH('DisciplinaryViolationTypes','ArticleNo') IS NULL
    ALTER TABLE DisciplinaryViolationTypes ADD ArticleNo nvarchar(40) NULL;

-- توسيعٌ إلى ستّين محرفاً قبل دمج `RegulationClause` فيه: كان العمود القديم
-- بستّين والجديد بأربعين، ودمجٌ بلا توسيع **يقصّ سند العقوبة** بصمت.
-- (COL_LENGTH بالبايتات: nvarchar(40) = 80.)
IF COL_LENGTH('DisciplinaryViolationTypes','ArticleNo') < 120
    ALTER TABLE DisciplinaryViolationTypes ALTER COLUMN ArticleNo nvarchar(60) NULL;

-- «معايير الاستحقاق» (نظير EligibilityCriteriaID بكيان): المخالفة نفسها قد لا
-- تنطبق على كل الموظفين — «التأخر» لا معنى له لمن عقده «عن بعد»، و«الزيّ» لا
-- يخصّ الإداريين. تُخزَّن كـJSON بنفس صيغة <see cref="HrConditions"/> المستعملة
-- بالوثائق والنماذج والسياسات، لا كجدولٍ سادس بمحرّك شرطٍ سادس.
-- إضافة محضة: عمود nullable، والفراغ يعني «تنطبق على الجميع» كما كانت.
IF COL_LENGTH('DisciplinaryViolationTypes','ConditionsJson') IS NULL
    ALTER TABLE DisciplinaryViolationTypes ADD ConditionsJson nvarchar(max) NULL;

-- الاسم بالإنجليزية على الفئة والمخالفة: كيان تفرض عمودين، ونظامنا White-Label
-- يُباع لشركاتٍ تصدر كتبها بلغتين. إضافة محضة، والفراغ يعني «استعمل العربي».
IF COL_LENGTH('DisciplinaryViolationCategories','NameEn') IS NULL
    ALTER TABLE DisciplinaryViolationCategories ADD NameEn nvarchar(180) NULL;

-- معايير الاستحقاق على **الفئة** أيضاً (كيان تضعها بالمستويين).
IF COL_LENGTH('DisciplinaryViolationCategories','ConditionsJson') IS NULL
    ALTER TABLE DisciplinaryViolationCategories ADD ConditionsJson nvarchar(max) NULL;

-- فئات النظام: «الحضور والانصراف» تُولَّد عليها المخالفات آلياً من قواعد المناوبات،
-- فحذفها يقطع التوليد بصمت. تُعلَّم فتُمنع إعادة تسميتها وحذفها.
IF COL_LENGTH('DisciplinaryViolationCategories','IsSystem') IS NULL
    ALTER TABLE DisciplinaryViolationCategories ADD IsSystem bit NOT NULL
        CONSTRAINT DF_DisciplinaryViolationCategories_IsSystem DEFAULT(0);

IF COL_LENGTH('DisciplinaryViolationTypes','NameEn') IS NULL
    ALTER TABLE DisciplinaryViolationTypes ADD NameEn nvarchar(250) NULL;

-- قالب الرسالة **عند الأتمتة** مستقلّ عن اليدوي: مخالفةٌ يولّدها المحرّك لا
-- يوقّعها مسجِّل، فنصّها يختلف.
IF COL_LENGTH('DisciplinaryViolationTypes','AutoMessageTemplateId') IS NULL
    ALTER TABLE DisciplinaryViolationTypes ADD AutoMessageTemplateId int NULL;

IF OBJECT_ID('DisciplinaryPenaltyRules', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryPenaltyRules
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ViolationTypeId int NOT NULL,
        OccurrenceFrom int NOT NULL,
        OccurrenceTo int NOT NULL,
        CountingPeriod nvarchar(40) NOT NULL DEFAULT('Monthly'),
        PenaltyAction nvarchar(250) NOT NULL,
        FinancialImpactType nvarchar(40) NOT NULL DEFAULT('None'),
        FinancialValue decimal(18,2) NOT NULL DEFAULT(0),
        ValidityMonths int NOT NULL DEFAULT(6),
        CalculationMode nvarchar(40) NOT NULL DEFAULT('Cumulative'),
        RequiresApproval bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

-- ══════════ الجزاء يصير كائناً لا نصّاً ══════════
--
-- `PenaltyAction` نصٌّ حرّ **لا يقود شيئاً**: لا يعرف النظام أهو إنذارٌ أم خصمٌ
-- أم إنهاء خدمة. يبقى عنواناً للعرض، ويقود السلوكَ `ActionType` أدناه.
IF COL_LENGTH('DisciplinaryPenaltyRules','ActionType') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD ActionType nvarchar(40) NOT NULL
        CONSTRAINT DF_DisciplinaryPenaltyRules_ActionType DEFAULT(N'VerbalWarning');

-- سبب الإيقاف — لإجراء «إنهاء الخدمة» وحده، مربوط بـZynoraTerminationReasons.
IF COL_LENGTH('DisciplinaryPenaltyRules','TerminationReasonId') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD TerminationReasonId int NULL;

-- وجهة الخصم بالمسير: عنصر راتب من نوع اقتطاع. بدونه الخصم رقمٌ لا يعرف أين
-- يُرحَّل، ويظهر بالقسيمة سطراً عامّاً بلا حساب.
IF COL_LENGTH('DisciplinaryPenaltyRules','SalaryItemId') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD SalaryItemId int NULL;

-- وعاء الخصم: [{key,percent}] بمفاتيح SalaryBaseComposer. الفراغ = الأساسي 100%
-- (سلوك ما قبل الميزة بالضبط).
IF COL_LENGTH('DisciplinaryPenaltyRules','BasePoolJson') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD BasePoolJson nvarchar(max) NULL;

-- مقام معدّل اليوم: كان `÷ 30` مثبّتاً بالكود، و«30» قرارٌ لا حقيقة.
IF COL_LENGTH('DisciplinaryPenaltyRules','WorkDaysBasis') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD WorkDaysBasis nvarchar(30) NOT NULL
        CONSTRAINT DF_DisciplinaryPenaltyRules_WorkDaysBasis DEFAULT(N'Fixed');

IF COL_LENGTH('DisciplinaryPenaltyRules','WorkDaysFixed') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD WorkDaysFixed int NOT NULL
        CONSTRAINT DF_DisciplinaryPenaltyRules_WorkDaysFixed DEFAULT(30);

IF COL_LENGTH('DisciplinaryPenaltyRules','ExcludeHolidays') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD ExcludeHolidays nvarchar(40) NOT NULL
        CONSTRAINT DF_DisciplinaryPenaltyRules_ExcludeHolidays DEFAULT(N'None');

-- سياسة الإسقاط: كانت رقم أشهرٍ واحداً (`ValidityMonths`) بلا نمطٍ ولا مرساة.
IF COL_LENGTH('DisciplinaryPenaltyRules','DropMode') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD DropMode nvarchar(20) NOT NULL
        CONSTRAINT DF_DisciplinaryPenaltyRules_DropMode DEFAULT(N'Once');

IF COL_LENGTH('DisciplinaryPenaltyRules','DropEvery') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD DropEvery int NOT NULL
        CONSTRAINT DF_DisciplinaryPenaltyRules_DropEvery DEFAULT(0);

IF COL_LENGTH('DisciplinaryPenaltyRules','DropUnit') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD DropUnit nvarchar(20) NOT NULL
        CONSTRAINT DF_DisciplinaryPenaltyRules_DropUnit DEFAULT(N'Months');

IF COL_LENGTH('DisciplinaryPenaltyRules','DropAnchor') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD DropAnchor nvarchar(40) NOT NULL
        CONSTRAINT DF_DisciplinaryPenaltyRules_DropAnchor DEFAULT(N'ActionDate');

-- قالب الكتاب لكل درجةِ جزاء (إنذارٌ أوّل ونهائيّ لا يُكتبان بنصٍّ واحد).
IF COL_LENGTH('DisciplinaryPenaltyRules','MessageTemplateId') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD MessageTemplateId int NULL;

IF COL_LENGTH('DisciplinaryPenaltyRules','AutoMessageTemplateId') IS NULL
    ALTER TABLE DisciplinaryPenaltyRules ADD AutoMessageTemplateId int NULL;

IF OBJECT_ID('DisciplinaryMessageTemplates', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryMessageTemplates
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(180) NOT NULL,
        TemplateType nvarchar(60) NOT NULL DEFAULT('PenaltyNotice'),
        Subject nvarchar(250) NOT NULL,
        Body nvarchar(max) NOT NULL,
        IsDefault bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryTemplateTypes', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryTemplateTypes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(180) NOT NULL,
        Code nvarchar(80) NOT NULL,
        Description nvarchar(max) NULL,
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinarySettings', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinarySettings
    (
        [Key] nvarchar(120) NOT NULL PRIMARY KEY,
        [Value] nvarchar(max) NULL,
        UpdatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryFormTextBlocks', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryFormTextBlocks
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Area nvarchar(30) NOT NULL DEFAULT('Body'),
        Text nvarchar(max) NOT NULL,
        XPercent decimal(18,2) NOT NULL DEFAULT(8),
        YPercent decimal(18,2) NOT NULL DEFAULT(25),
        WidthPercent decimal(18,2) NOT NULL DEFAULT(84),
        FontFamily nvarchar(80) NOT NULL DEFAULT('Tahoma'),
        FontSize int NOT NULL DEFAULT(14),
        FontColor nvarchar(20) NOT NULL DEFAULT('#0b1d31'),
        IsBold bit NOT NULL DEFAULT(0),
        TextAlign nvarchar(20) NOT NULL DEFAULT('right'),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;
""");

        await BackfillPenaltyModelAsync(dbContext);
    }

    /// <summary>
    /// ترحيلٌ **لمرّة واحدة** لصفوف الجزاءات القديمة إلى النموذج الجديد.
    ///
    /// ⚠️ استدعاءان لا واحد: الأعمدة المضافة أعلاه لا يمكن أن تُذكَر بنفس الدفعة
    /// التي تضيفها — SQL Server يحلّل أسماء الأعمدة عند التحليل لا عند التنفيذ،
    /// فتفشل الدفعة كلّها بـ«Invalid column name».
    ///
    /// ⚠️ ومحكومٌ براية بالإعدادات لا بشرطٍ على البيانات: شرطٌ مثل
    /// «حيث النوع = تنبيه شفوي» يُعاد تطبيقه بكل إقلاع، فيدوس على تصنيفٍ **اختاره
    /// المستخدم بنفسه**. الراية تجعل الترحيل حدثاً تاريخياً لا سلوكاً دائماً.
    /// </summary>
    private static async Task BackfillPenaltyModelAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF NOT EXISTS (SELECT 1 FROM DisciplinarySettings WHERE [Key] = N'PenaltyModelBackfilled')
BEGIN
    -- `ValidityMonths` كان يحمل معنى «يسقط بعد N شهراً» — يُنقل بدل أن يُهجَر.
    UPDATE DisciplinaryPenaltyRules
    SET DropEvery = ValidityMonths,
        DropUnit = N'Months',
        DropAnchor = N'ActionDate',
        DropMode = N'Once'
    WHERE DropEvery = 0 AND ISNULL(ValidityMonths, 0) > 0;

    -- النوع يُستنتج من النصّ الحرّ مرّةً واحدة. الأثر المالي أقوى دليل: صفٌّ
    -- يخصم فعلاً هو اقتطاع مهما كان نصّه. وما لا يُطابَق يبقى «تنبيه شفوي» —
    -- **الافتراض الآمن أن لا يُخصم** لا أن يُخصم.
    UPDATE DisciplinaryPenaltyRules
    SET ActionType =
        CASE
            WHEN ISNULL(FinancialImpactType, N'None') <> N'None' THEN N'SalaryDeduction'
            WHEN PenaltyAction LIKE N'%فصل%' OR PenaltyAction LIKE N'%طرد%'
              OR PenaltyAction LIKE N'%إنهاء%' OR PenaltyAction LIKE N'%انهاء%' THEN N'Termination'
            WHEN PenaltyAction LIKE N'%خصم%' OR PenaltyAction LIKE N'%اقتطاع%' THEN N'SalaryDeduction'
            WHEN PenaltyAction LIKE N'%زيادة%' OR PenaltyAction LIKE N'%علاوة%' THEN N'RaiseDenial'
            WHEN PenaltyAction LIKE N'%نهائي%' THEN N'FinalWarning'
            WHEN PenaltyAction LIKE N'%إنذار%' OR PenaltyAction LIKE N'%انذار%' THEN N'Warning'
            WHEN PenaltyAction LIKE N'%خطي%' OR PenaltyAction LIKE N'%خطّي%' THEN N'WrittenWarning'
            ELSE N'VerbalWarning'
        END;

    INSERT INTO DisciplinarySettings([Key], [Value], UpdatedAt)
    VALUES (N'PenaltyModelBackfilled', N'true', SYSUTCDATETIME());
END;

-- ⚠️ دمج «رقم الفقرة» المزدوج مرّةً واحدة.
--
-- كان لنفس المعنى عمودان على نفس الجدول: `ArticleNo` تحرّره لائحة المخالفات
-- **وهو ما يستشهد به كتاب العقوبة**، و`RegulationClause` تحرّره شاشة تهيئة
-- المخالفات **ولا يقرؤه أحد**. فمن ملأ الثانية ظنّ السند مثبّتاً، ويخرج الكتاب
-- بفقرة «—».
--
-- ⚠️ **النقل يملأ ولا يدهس**: القيمة التي يستشهد بها الكتاب فعلاً هي الأصحّ عند
-- التعارض، لا المهجورة.
--
-- والعمود القديم **يبقى ولا يُحذف**: إسقاط عمودٍ بقاعدةٍ مشتركة فعلٌ لا رجعة
-- فيه ولا مكسب منه — يكفي أن يتوقّف كل شيء عن قراءته والكتابة فيه.
IF NOT EXISTS (SELECT 1 FROM DisciplinarySettings WHERE [Key] = N'ArticleNoMerged')
BEGIN
    IF COL_LENGTH('DisciplinaryViolationTypes','RegulationClause') IS NOT NULL
        EXEC sp_executesql N'
UPDATE DisciplinaryViolationTypes
SET ArticleNo = LTRIM(RTRIM(RegulationClause))
WHERE ISNULL(LTRIM(RTRIM(ArticleNo)), N'''') = N''''
  AND ISNULL(LTRIM(RTRIM(RegulationClause)), N'''') <> N'''';';

    INSERT INTO DisciplinarySettings([Key], [Value], UpdatedAt)
    VALUES (N'ArticleNoMerged', N'true', SYSUTCDATETIME());
END;

-- ⚠️ رفع قفل «فئة نظام» مرّةً واحدة.
--
-- قُفلت فئة الحضور بحجّة أن التوليد الآلي يبني عليها، والتوليد يكتب اسمها
-- **نصّاً حرفياً بالحالة** ولا يقرأ هذا الجدول أصلاً — فالقفل كان يمنع
-- المستخدم من تعديل اسمٍ أو حذف بابٍ لا يستعمله، ولا يحمي شيئاً.
-- الفئات كلّها أمثلةٌ يُبدأ منها، لا هيكلٌ ثابت.
IF NOT EXISTS (SELECT 1 FROM DisciplinarySettings WHERE [Key] = N'CategoryLockLifted')
BEGIN
    UPDATE DisciplinaryViolationCategories SET IsSystem = 0 WHERE IsSystem = 1;

    INSERT INTO DisciplinarySettings([Key], [Value], UpdatedAt)
    VALUES (N'CategoryLockLifted', N'true', SYSUTCDATETIME());
END;
""");
    }
}
