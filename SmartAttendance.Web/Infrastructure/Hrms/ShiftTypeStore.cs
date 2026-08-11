using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// أنواع المناوبات (نمط كيان — قسم 11 بدراسة الحضور): المناوبة تُعرَّف يوماً-بيوم
/// بمصفوفة 7 أيام (السبت..الجمعة)، كل يوم له نوع (عمل/عطلة أسبوعية/راحة) ووقت
/// دخول/خروج وساعات عمل. المناوبة إما «ثابتة» (أوقات محددة لكل يوم) أو «مرنة»
/// (عدد ساعات مطلوب يومياً بلا أوقات). لكل مناوبة لون يميزها بمستعرض الحضور.
/// هذا الكيان الجديد يوازي جدول Shifts القديم البسيط ولا يمسه — الترحيل لاحقاً.
/// ملاحظة: «الفترات المتعددة» (سبليت شفت) مؤجلة للمرحلة التالية.
/// </summary>
public static class ShiftTypeStore
{
    /// <summary>أيام الأسبوع بترتيب المنطقة (0=السبت .. 6=الجمعة) — نفس ترتيب كيان.</summary>
    public static readonly string[] DayNames =
        { "السبت", "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };

    /// <summary>ألوان المناوبات الجاهزة (مطابقة لفكرة كيان: 8 ألوان مميزة للمستعرض).</summary>
    public static readonly string[] Colors =
        { "#12D9E3", "#4ade80", "#facc15", "#f97316", "#f87171", "#a78bfa", "#60a5fa", "#f472b6" };

    /// <summary>نوع إجراء تعارض المغادرات (تبويب 3 نمط كيان): تسجيله مغادرة أو اقتطاع.</summary>
    public static readonly (string Key, string Label)[] ConflictActions =
    {
        ("Permission", "المغادرات"),
        ("Deduction", "اقتطاع")
    };

    /// <summary>حقول معايير الاستحقاق (تبويب 4 نمط كيان) — سمات الموظف المتاحة عندنا.</summary>
    public static readonly (string Key, string Label)[] EligibilityFields =
    {
        ("Department", "القسم"),
        ("Branch", "الفرع"),
        ("Position", "المنصب"),
        ("ContractType", "نوع العقد"),
        ("Nationality", "الجنسية"),
        ("MaritalStatus", "الحالة الاجتماعية"),
        ("Employee", "موظف محدد")
    };

    public static string LabelOf((string Key, string Label)[] list, string key) =>
        list.FirstOrDefault(x => x.Key == key).Label ?? key;

    /// <summary>
    /// «استبدال وأرشفة» (سيناريو تغيير سياسة الدوام مثل 9⟵8 ساعات): المناوبة القديمة
    /// المرتبطة برواتب مغلقة لا تُحذف أبداً — تُخفى من الفرشاة والخدمة الذاتية
    /// (AvailableInRoster/RequestableFromEss=0 مع بقاء IsActive حتى تظل الأشهر
    /// التاريخية قابلة لإعادة الاشتقاق)، وتُرحَّل خلايا الروستر **من تاريخ السريان
    /// فصاعداً** للبديلة (الماضي يبقى على القديمة فالتاريخ المالي سليم)، ومعها
    /// الإسناد الافتراضي اختيارياً. يرجع (خلايا مرحَّلة، إسنادات مرحَّلة).
    /// </summary>
    public static async Task<(int Cells, int Assignments)> ReplaceAndArchiveAsync(
        ApplicationDbContext dbContext, int oldShiftId, int newShiftId,
        DateOnly effectiveFrom, bool migrateAssignments)
    {
        if (oldShiftId <= 0 || newShiftId <= 0 || oldShiftId == newShiftId) return (0, 0);
        await EnsureAsync(dbContext);
        await RosterStore.EnsureAsync(dbContext);

        var cells = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
UPDATE RosterCells SET ShiftTypeId = @New, UpdatedAt = SYSUTCDATETIME()
WHERE ShiftTypeId = @Old AND WorkDate >= @From;
SELECT @@ROWCOUNT;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Old", oldShiftId);
                HrmsDatabase.AddParameter(command, "@New", newShiftId);
                HrmsDatabase.AddParameter(command, "@From", effectiveFrom);
            });

        var assignments = 0;
        if (migrateAssignments)
        {
            await EmployeeShiftTypeStore.EnsureAsync(dbContext);
            assignments = await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                "UPDATE EmployeeShiftTypes SET ShiftTypeId = @New WHERE ShiftTypeId = @Old; SELECT @@ROWCOUNT;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Old", oldShiftId);
                    HrmsDatabase.AddParameter(command, "@New", newShiftId);
                });
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE ShiftTypes SET AvailableInRoster = 0, RequestableFromEss = 0 WHERE Id = @Old;",
            command => HrmsDatabase.AddParameter(command, "@Old", oldShiftId));

        return (cells, assignments);
    }

    /// <summary>
    /// عائلة المناوبة من وقت بدايتها (لتجميع منتقي الفرشاة بالروستر): صباحية
    /// [5–12)، مسائية [12–18)، ليلية [18–5). نقية قابلة للاختبار.
    /// </summary>
    public static string FamilyOf(string? startTime, bool isFlexible)
    {
        if (isFlexible) return "مرنة";
        if (startTime is not { Length: >= 2 } || !int.TryParse(startTime[..2], out var hour) || hour is < 0 or > 23)
            return "أخرى";
        return hour is >= 5 and < 12 ? "صباحية" : hour is >= 12 and < 18 ? "مسائية" : "ليلية";
    }

    public sealed class ShiftDay
    {
        public int DayIndex { get; set; }                 // 0=السبت .. 6=الجمعة
        public string DayKind { get; set; } = "Work";     // Work | Weekend | Rest
        public string? StartTime { get; set; }            // "HH:mm" — للثابتة بيوم عمل
        public string? EndTime { get; set; }
        public decimal WorkHours { get; set; }            // ساعات اليوم (تُشتق أو تُدخل)

        public string DayName => DayNames[Math.Clamp(DayIndex, 0, 6)];
        public bool IsWork => DayKind == "Work";
    }

    /// <summary>فترة عمل بمناوبة «بفترات متعددة» (سبليت شفت) — بداية/نهاية مشتركة لكل أيام العمل.</summary>
    public sealed class ShiftPeriod
    {
        public int Ordinal { get; set; }
        public string StartTime { get; set; } = "09:00";
        public string EndTime { get; set; } = "13:00";
        public decimal Hours => ComputeHours(StartTime, EndTime);
    }

    /// <summary>قاعدة استحقاق واحدة: حقل موظف = قيمة، ضمن مجموعة (GroupNo). المجموعات OR، وداخلها AND.</summary>
    public sealed class EligibilityRule
    {
        public int GroupNo { get; set; }
        public string Field { get; set; } = "Department";   // انظر EligibilityFields
        public string Value { get; set; } = string.Empty;   // معرّف (قسم/فرع/منصب/موظف) أو نص (جنسية…)
    }

    public sealed class ShiftType
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public string ColorHex { get; set; } = "#12D9E3";
        public bool IsFlexible { get; set; }
        public decimal FlexDailyHours { get; set; }       // للمرنة: ساعات مطلوبة يومياً
        public bool MultiPeriod { get; set; }             // مناوبة بفترات متعددة (سبليت شفت)

        // ===== قواعد سلوك المناوبة (أسفل اختيار الأيام بكيان) =====
        public bool FillMissingCheckIn { get; set; }          // تعبئة وقت بدء المناوبة لبصمة دخول مفقودة
        public bool FillMissingCheckOut { get; set; }         // تعبئة وقت انتهاء المناوبة لبصمة خروج مفقودة
        public bool StripSemantics { get; set; }              // تجريد فترة الدلالات (استراحة/صلاة…) من الحضور
        public bool ConsiderPermissionsOutsideShift { get; set; } // احتساب ساعات المغادرة خارج المناوبة
        public bool ExcludePermsOutsideStartFromLate { get; set; } // استثناء المغادرات خارج بداية المناوبة من التأخير
        public string TotalDurationMode { get; set; } = "WorkOnly"; // WorkOnly | IncludeOff | Both
        public bool AvailableInRoster { get; set; } = true;   // متاحة بجدول الحضور (الروستر)
        public bool RequestableFromEss { get; set; }          // قابلة للطلب من الخدمة الذاتية

        // ===== السماحيات والحدود (فترة السماح + نافذة البصم + منتصف المناوبة) =====
        public int LatenessGraceMinutes { get; set; }         // فترة السماح للتأخير (دقائق)
        public int EarlyLeaveGraceMinutes { get; set; }       // فترة السماح للخروج المبكر (دقائق)
        // سياسة تجاوز السماحية (تُخصَّص لكل شركة/مناوبة):
        // Subtract = يُطرح المسموح من الفارق · Full = يُحتسب الفارق كاملاً من بدء/انتهاء المناوبة
        public string GraceExceededPolicy { get; set; } = "Subtract";
        public string? TimeLimitFrom { get; set; }            // حد وقت بدء المناوبة HH:mm (أبكر بصمة صالحة)
        public bool TimeLimitFromDayBefore { get; set; }      // مرساة الحد: من اليوم السابق
        public string? TimeLimitTo { get; set; }              // حد وقت انتهاء المناوبة HH:mm (أحدث بصمة صالحة)
        public bool TimeLimitToDayAfter { get; set; }         // مرساة الحد: إلى اليوم التالي
        public string? MidShiftTime { get; set; }             // وقت منتصف المناوبة HH:mm (تقسيم بصمات المناوبة العابرة لمنتصف الليل)

        // ===== تبويب 3: قواعد التعارض مع المغادرات (نمط كيان) =====
        // سيناريوهان لعدم تطابق المغادرة المعتمدة مع الحضور الفعلي، لكل منهما إجراء.
        public bool ConflictLateReturnEnabled { get; set; }          // تأخّر العودة من المغادرة
        public string ConflictLateReturnAction { get; set; } = "Deduction"; // Permission | Deduction
        public decimal ConflictLateReturnValue { get; set; }
        // ═══ سعر ساعة العمل الإضافي بسياق اليوم (نظير كيان «سعر ساعة العمل») ═══
        //
        // **null = غير محدَّد** ⟹ يسقط لـ`PayrollTransactionStore.DefaultRateFactor`،
        // وهو ما يفعله النظام اليوم لكل ساعة. فالحقول الأربعة **لا تغيّر رقماً** حتى
        // تُملأ عمداً لهذه المناوبة بالذات — لا افتراضيّ ولا ترحيل قيم.
        //
        // السياق يُحسم بـ`ShiftRuleStore.EffectiveContext` نفسها التي تحسم انطباق
        // القواعد: عطلة رسمية ← إجازة ← نوع اليوم. مصدرٌ واحد لمعنى «أيّ يومٍ كان
        // هذا»، وإلا أمكن أن تنطبق قاعدة «العطلة» بينما يُحتسب الأجر بمعامل «الراحة».
        public decimal? OvertimeRateWeekend { get; set; }            // في عطلة نهاية الأسبوع
        public decimal? OvertimeRateRest { get; set; }               // في يوم الراحة
        public decimal? OvertimeRateHoliday { get; set; }            // في عطلة رسمية
        public decimal? OvertimeRateLeave { get; set; }              // في إجازة

        public bool ConflictEarlyLeaveEnabled { get; set; }          // الذهاب مبكراً للمغادرة
        public string ConflictEarlyLeaveAction { get; set; } = "Deduction";
        public decimal ConflictEarlyLeaveValue { get; set; }

        public bool IsActive { get; set; } = true;
        public List<ShiftDay> Days { get; set; } = new();
        public List<ShiftPeriod> Periods { get; set; } = new();

        // ===== تبويب 4: معايير الاستحقاق (شرائح موظفين — مجموعات OR، داخلها AND) =====
        public List<EligibilityRule> Eligibility { get; set; } = new();

        /// <summary>أسماء أيام العطلة/الراحة — لعمود القائمة (مثل عرض كيان).</summary>
        public string OffDaysText => string.Join(" ",
            Days.Where(d => !d.IsWork).Select(d => d.DayName)) is { Length: > 0 } text ? text : "—";
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('ShiftTypes', 'U') IS NULL
BEGIN
    CREATE TABLE ShiftTypes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        NameEn nvarchar(150) NULL,
        ColorHex nvarchar(9) NOT NULL DEFAULT(N'#12D9E3'),
        IsFlexible bit NOT NULL DEFAULT(0),
        FlexDailyHours decimal(5,2) NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('ShiftTypeDays', 'U') IS NULL
BEGIN
    CREATE TABLE ShiftTypeDays
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ShiftTypeId int NOT NULL,
        DayIndex int NOT NULL,                -- 0=السبت .. 6=الجمعة
        DayKind nvarchar(20) NOT NULL DEFAULT(N'Work'),
        StartTime nvarchar(5) NULL,           -- HH:mm
        EndTime nvarchar(5) NULL,
        WorkHours decimal(5,2) NOT NULL DEFAULT(0)
    );
    CREATE UNIQUE INDEX UX_ShiftTypeDays_Shift_Day ON ShiftTypeDays (ShiftTypeId, DayIndex);
END;

-- الفترات المتعددة (سبليت شفت) — idempotent
IF COL_LENGTH('ShiftTypes','MultiPeriod') IS NULL ALTER TABLE ShiftTypes ADD MultiPeriod bit NOT NULL CONSTRAINT DF_ShiftTypes_MultiPeriod DEFAULT(0);
IF OBJECT_ID('ShiftTypePeriods', 'U') IS NULL
BEGIN
    CREATE TABLE ShiftTypePeriods
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ShiftTypeId int NOT NULL,
        Ordinal int NOT NULL,
        StartTime nvarchar(5) NOT NULL,       -- HH:mm
        EndTime nvarchar(5) NOT NULL
    );
    CREATE INDEX IX_ShiftTypePeriods_Shift ON ShiftTypePeriods (ShiftTypeId, Ordinal);
END;

-- قواعد سلوك المناوبة (idempotent)
IF COL_LENGTH('ShiftTypes','FillMissingCheckIn') IS NULL ALTER TABLE ShiftTypes ADD FillMissingCheckIn bit NOT NULL CONSTRAINT DF_ST_FMI DEFAULT(0);
IF COL_LENGTH('ShiftTypes','FillMissingCheckOut') IS NULL ALTER TABLE ShiftTypes ADD FillMissingCheckOut bit NOT NULL CONSTRAINT DF_ST_FMO DEFAULT(0);
IF COL_LENGTH('ShiftTypes','StripSemantics') IS NULL ALTER TABLE ShiftTypes ADD StripSemantics bit NOT NULL CONSTRAINT DF_ST_STS DEFAULT(0);
IF COL_LENGTH('ShiftTypes','ConsiderPermissionsOutsideShift') IS NULL ALTER TABLE ShiftTypes ADD ConsiderPermissionsOutsideShift bit NOT NULL CONSTRAINT DF_ST_CPO DEFAULT(0);
IF COL_LENGTH('ShiftTypes','ExcludePermsOutsideStartFromLate') IS NULL ALTER TABLE ShiftTypes ADD ExcludePermsOutsideStartFromLate bit NOT NULL CONSTRAINT DF_ST_EPL DEFAULT(0);
IF COL_LENGTH('ShiftTypes','TotalDurationMode') IS NULL ALTER TABLE ShiftTypes ADD TotalDurationMode nvarchar(20) NOT NULL CONSTRAINT DF_ST_TDM DEFAULT(N'WorkOnly');
IF COL_LENGTH('ShiftTypes','AvailableInRoster') IS NULL ALTER TABLE ShiftTypes ADD AvailableInRoster bit NOT NULL CONSTRAINT DF_ST_AIR DEFAULT(1);
IF COL_LENGTH('ShiftTypes','RequestableFromEss') IS NULL ALTER TABLE ShiftTypes ADD RequestableFromEss bit NOT NULL CONSTRAINT DF_ST_RFE DEFAULT(0);
IF COL_LENGTH('ShiftTypes','LatenessGraceMinutes') IS NULL ALTER TABLE ShiftTypes ADD LatenessGraceMinutes int NOT NULL CONSTRAINT DF_ST_LGM DEFAULT(0);
IF COL_LENGTH('ShiftTypes','EarlyLeaveGraceMinutes') IS NULL ALTER TABLE ShiftTypes ADD EarlyLeaveGraceMinutes int NOT NULL CONSTRAINT DF_ST_ELG DEFAULT(0);
IF COL_LENGTH('ShiftTypes','GraceExceededPolicy') IS NULL ALTER TABLE ShiftTypes ADD GraceExceededPolicy nvarchar(20) NOT NULL CONSTRAINT DF_ST_GXP DEFAULT(N'Subtract');
IF COL_LENGTH('ShiftTypes','TimeLimitFrom') IS NULL ALTER TABLE ShiftTypes ADD TimeLimitFrom nvarchar(5) NULL;
IF COL_LENGTH('ShiftTypes','TimeLimitFromDayBefore') IS NULL ALTER TABLE ShiftTypes ADD TimeLimitFromDayBefore bit NOT NULL CONSTRAINT DF_ST_TLFB DEFAULT(0);
IF COL_LENGTH('ShiftTypes','TimeLimitTo') IS NULL ALTER TABLE ShiftTypes ADD TimeLimitTo nvarchar(5) NULL;
IF COL_LENGTH('ShiftTypes','TimeLimitToDayAfter') IS NULL ALTER TABLE ShiftTypes ADD TimeLimitToDayAfter bit NOT NULL CONSTRAINT DF_ST_TLTA DEFAULT(0);
IF COL_LENGTH('ShiftTypes','MidShiftTime') IS NULL ALTER TABLE ShiftTypes ADD MidShiftTime nvarchar(5) NULL;

-- تبويب 3: قواعد التعارض مع المغادرات (idempotent)
IF COL_LENGTH('ShiftTypes','ConflictLateReturnEnabled') IS NULL ALTER TABLE ShiftTypes ADD ConflictLateReturnEnabled bit NOT NULL CONSTRAINT DF_ST_CLRE DEFAULT(0);
IF COL_LENGTH('ShiftTypes','ConflictLateReturnAction') IS NULL ALTER TABLE ShiftTypes ADD ConflictLateReturnAction nvarchar(20) NOT NULL CONSTRAINT DF_ST_CLRA DEFAULT(N'Deduction');
IF COL_LENGTH('ShiftTypes','ConflictLateReturnValue') IS NULL ALTER TABLE ShiftTypes ADD ConflictLateReturnValue decimal(12,2) NOT NULL CONSTRAINT DF_ST_CLRV DEFAULT(0);
IF COL_LENGTH('ShiftTypes','ConflictEarlyLeaveEnabled') IS NULL ALTER TABLE ShiftTypes ADD ConflictEarlyLeaveEnabled bit NOT NULL CONSTRAINT DF_ST_CELE DEFAULT(0);
IF COL_LENGTH('ShiftTypes','ConflictEarlyLeaveAction') IS NULL ALTER TABLE ShiftTypes ADD ConflictEarlyLeaveAction nvarchar(20) NOT NULL CONSTRAINT DF_ST_CELA DEFAULT(N'Deduction');
IF COL_LENGTH('ShiftTypes','ConflictEarlyLeaveValue') IS NULL ALTER TABLE ShiftTypes ADD ConflictEarlyLeaveValue decimal(12,2) NOT NULL CONSTRAINT DF_ST_CELV DEFAULT(0);

-- تبويب 4: معايير الاستحقاق (شرائح موظفين)
IF OBJECT_ID('ShiftEligibilityRules', 'U') IS NULL
BEGIN
    CREATE TABLE ShiftEligibilityRules
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ShiftTypeId int NOT NULL,
        GroupNo int NOT NULL DEFAULT(0),
        Field nvarchar(30) NOT NULL,
        Value nvarchar(200) NOT NULL DEFAULT(N'')
    );
    CREATE INDEX IX_ShiftEligibilityRules_Shift ON ShiftEligibilityRules (ShiftTypeId, GroupNo);
END;
""");
    }

    /// <summary>
    /// سرد أنواع المناوبات <b>محصوراً بنطاق المستخدم</b> — لشاشات الإدارة (8D–8M).
    ///
    /// <para>⚠️ <b>لماذا مسارٌ منفصل عن <see cref="ListAsync"/>:</b> تلك يستدعيها
    /// محرّك الحضور والرواتب (<c>DayAttendanceStore</c> · <c>RecommendationStore</c> ·
    /// <c>AttendanceTransactionStore</c>) وهو يحتسب **لكل موظف** لا لمستخدمٍ ينظر —
    /// فحصرُها بنطاق المتصفّح يُسقط مناوبات موظفين من الاحتساب ويُفسد المسير. العزل
    /// يخصّ ما **يُعرض ويُحرَّر**، لا ما يُحتسَب.</para>
    ///
    /// <para><c>CompanyId IS NULL</c> = تهيئة مشتركة تبقى مرئية للكل (السلوك القديم).</para>
    /// </summary>
    public static async Task<List<ShiftType>> ListInScopeAsync(
        ApplicationDbContext dbContext,
        Security.CompanyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var all = await ListAsync(dbContext);
        if (scope.IsUnrestricted)
        {
            return all;
        }

        var allowedIds = new HashSet<int>(await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT Id FROM ShiftTypes WHERE {scope.ToSharedConfigSqlPredicate("CompanyId")};",
            command => { },
            reader => reader.GetInt32(0)));

        return all.Where(shift => allowedIds.Contains(shift.Id)).ToList();
    }

    /// <summary>
    /// حارس ملكية: أنوعُ المناوبة هذا داخل نطاق المستخدم؟ مغلق الفشل — المعرّف
    /// المجهول أو التابع لشركة أخرى يعيد <c>false</c> فيردّ المستدعي NotFound.
    /// </summary>
    public static async Task<bool> IsInScopeAsync(
        ApplicationDbContext dbContext,
        int id,
        Security.CompanyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (id <= 0)
        {
            return false;
        }

        var found = await HrmsDatabase.ScalarAsync<int?>(
            dbContext,
            $"SELECT TOP 1 1 FROM ShiftTypes WHERE Id = @Id AND {scope.ToSharedConfigSqlPredicate("CompanyId")};",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        return found is not null;
    }

    /// <summary>
    /// ينسب نوع مناوبة لشركة — يُستدعى بعد الإنشاء فينعزل الجديد، بينما يبقى القديم
    /// (<c>NULL</c>) مشتركاً. مفصولٌ عن <c>INSERT</c> كي لا تتغيّر قائمة أعمدته.
    /// </summary>
    public static async Task AssignCompanyAsync(ApplicationDbContext dbContext, int id, int? companyId)
    {
        if (companyId is null)
        {
            return;
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE ShiftTypes SET CompanyId = @Company WHERE Id = @Id AND CompanyId IS NULL;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Company", companyId.Value);
                HrmsDatabase.AddParameter(command, "@Id", id);
            });
    }

    public static async Task<List<ShiftType>> ListAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);

        var shifts = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ShiftTypes ORDER BY Name;",
            command => { },
            ReadShift);

        var days = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ShiftTypeDays ORDER BY ShiftTypeId, DayIndex;",
            command => { },
            reader => new
            {
                ShiftTypeId = HrmsDatabase.GetInt(reader, "ShiftTypeId"),
                Day = ReadDay(reader)
            });

        var periods = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ShiftTypePeriods ORDER BY ShiftTypeId, Ordinal;",
            command => { },
            reader => new
            {
                ShiftTypeId = HrmsDatabase.GetInt(reader, "ShiftTypeId"),
                Period = new ShiftPeriod
                {
                    Ordinal = HrmsDatabase.GetInt(reader, "Ordinal"),
                    StartTime = HrmsDatabase.GetString(reader, "StartTime") is { Length: > 0 } s ? s : "09:00",
                    EndTime = HrmsDatabase.GetString(reader, "EndTime") is { Length: > 0 } e ? e : "13:00"
                }
            });

        var eligibility = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ShiftEligibilityRules ORDER BY ShiftTypeId, GroupNo, Id;",
            command => { },
            reader => new
            {
                ShiftTypeId = HrmsDatabase.GetInt(reader, "ShiftTypeId"),
                Rule = new EligibilityRule
                {
                    GroupNo = HrmsDatabase.GetInt(reader, "GroupNo"),
                    Field = HrmsDatabase.GetString(reader, "Field") is { Length: > 0 } f ? f : "Department",
                    Value = HrmsDatabase.GetString(reader, "Value")
                }
            });

        var daysByShift = days.GroupBy(d => d.ShiftTypeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Day).ToList());
        var periodsByShift = periods.GroupBy(p => p.ShiftTypeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Period).ToList());
        var eligByShift = eligibility.GroupBy(e => e.ShiftTypeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Rule).ToList());

        foreach (var shift in shifts)
        {
            shift.Days = daysByShift.TryGetValue(shift.Id, out var list) ? list : new();
            shift.Periods = periodsByShift.TryGetValue(shift.Id, out var pl) ? pl : new();
            shift.Eligibility = eligByShift.TryGetValue(shift.Id, out var el) ? el : new();
        }
        return shifts;
    }

    public static async Task<int> SaveAsync(ApplicationDbContext dbContext, ShiftType shift)
    {
        await EnsureAsync(dbContext);

        // مناوبة بفترات متعددة: ساعات كل يوم عمل = مجموع ساعات الفترات المشتركة،
        // ووقتا اليوم = بداية أول فترة/نهاية آخر فترة (للعرض ومرساة المحرك).
        if (shift.MultiPeriod && shift.Periods.Count > 0)
        {
            var ordered = shift.Periods.OrderBy(p => p.Ordinal).ToList();
            var total = ordered.Sum(p => p.Hours);
            var firstStart = ordered.First().StartTime;
            var lastEnd = ordered.Last().EndTime;
            foreach (var day in shift.Days.Where(d => d.IsWork))
            {
                day.StartTime = firstStart;
                day.EndTime = lastEnd;
                day.WorkHours = total;
            }
        }

        int id;
        if (shift.Id > 0)
        {
            id = shift.Id;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE ShiftTypes
SET Name = @Name, NameEn = @NameEn, ColorHex = @Color,
    IsFlexible = @Flex, FlexDailyHours = @FlexHours, MultiPeriod = @Multi,
    FillMissingCheckIn = @FMI, FillMissingCheckOut = @FMO, StripSemantics = @STS,
    ConsiderPermissionsOutsideShift = @CPO, ExcludePermsOutsideStartFromLate = @EPL,
    TotalDurationMode = @TDM, AvailableInRoster = @AIR, RequestableFromEss = @RFE,
    LatenessGraceMinutes = @LGM, EarlyLeaveGraceMinutes = @ELG, GraceExceededPolicy = @GXP,
    TimeLimitFrom = @TLF, TimeLimitFromDayBefore = @TLFB, TimeLimitTo = @TLT, TimeLimitToDayAfter = @TLTA, MidShiftTime = @MST,
    ConflictLateReturnEnabled = @CLRE, ConflictLateReturnAction = @CLRA, ConflictLateReturnValue = @CLRV,
    ConflictEarlyLeaveEnabled = @CELE, ConflictEarlyLeaveAction = @CELA, ConflictEarlyLeaveValue = @CELV,
    OvertimeRateWeekend = @ORW, OvertimeRateRest = @ORR, OvertimeRateHoliday = @ORH, OvertimeRateLeave = @ORL,
    IsActive = @Active
WHERE Id = @Id;
DELETE FROM ShiftTypeDays WHERE ShiftTypeId = @Id;
DELETE FROM ShiftTypePeriods WHERE ShiftTypeId = @Id;
DELETE FROM ShiftEligibilityRules WHERE ShiftTypeId = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", shift.Id);
                    AddShiftParameters(command, shift);
                });
        }
        else
        {
            id = await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                """
INSERT INTO ShiftTypes (Name, NameEn, ColorHex, IsFlexible, FlexDailyHours, MultiPeriod,
    FillMissingCheckIn, FillMissingCheckOut, StripSemantics, ConsiderPermissionsOutsideShift,
    ExcludePermsOutsideStartFromLate, TotalDurationMode, AvailableInRoster, RequestableFromEss,
    LatenessGraceMinutes, EarlyLeaveGraceMinutes, GraceExceededPolicy, TimeLimitFrom, TimeLimitFromDayBefore, TimeLimitTo, TimeLimitToDayAfter, MidShiftTime,
    ConflictLateReturnEnabled, ConflictLateReturnAction, ConflictLateReturnValue,
    ConflictEarlyLeaveEnabled, ConflictEarlyLeaveAction, ConflictEarlyLeaveValue,
    OvertimeRateWeekend, OvertimeRateRest, OvertimeRateHoliday, OvertimeRateLeave, IsActive)
VALUES (@Name, @NameEn, @Color, @Flex, @FlexHours, @Multi,
    @FMI, @FMO, @STS, @CPO, @EPL, @TDM, @AIR, @RFE,
    @LGM, @ELG, @GXP, @TLF, @TLFB, @TLT, @TLTA, @MST,
    @CLRE, @CLRA, @CLRV, @CELE, @CELA, @CELV,
    @ORW, @ORR, @ORH, @ORL, @Active);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                command => AddShiftParameters(command, shift));
        }

        if (shift.MultiPeriod)
        {
            var ord = 0;
            foreach (var period in shift.Periods.OrderBy(p => p.Ordinal))
            {
                var current = period;
                var thisOrd = ord++;
                await HrmsDatabase.ExecuteAsync(
                    dbContext,
                    "INSERT INTO ShiftTypePeriods (ShiftTypeId, Ordinal, StartTime, EndTime) VALUES (@S, @O, @St, @En);",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@S", id);
                        HrmsDatabase.AddParameter(command, "@O", thisOrd);
                        HrmsDatabase.AddParameter(command, "@St", current.StartTime);
                        HrmsDatabase.AddParameter(command, "@En", current.EndTime);
                    });
            }
        }

        foreach (var day in shift.Days.OrderBy(d => d.DayIndex))
        {
            var current = day;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO ShiftTypeDays (ShiftTypeId, DayIndex, DayKind, StartTime, EndTime, WorkHours)
VALUES (@ShiftTypeId, @DayIndex, @DayKind, @StartTime, @EndTime, @WorkHours);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@ShiftTypeId", id);
                    HrmsDatabase.AddParameter(command, "@DayIndex", current.DayIndex);
                    HrmsDatabase.AddParameter(command, "@DayKind", current.DayKind);
                    HrmsDatabase.AddParameter(command, "@StartTime", (object?)current.StartTime ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@EndTime", (object?)current.EndTime ?? DBNull.Value);
                    HrmsDatabase.AddParameter(command, "@WorkHours", current.WorkHours);
                });
        }

        // معايير الاستحقاق: صفوف بمجموعات (تجاهل القواعد الفارغة القيمة)
        foreach (var rule in shift.Eligibility.Where(r => !string.IsNullOrWhiteSpace(r.Value)))
        {
            var current = rule;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO ShiftEligibilityRules (ShiftTypeId, GroupNo, Field, Value) VALUES (@S, @G, @F, @V);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@S", id);
                    HrmsDatabase.AddParameter(command, "@G", current.GroupNo);
                    HrmsDatabase.AddParameter(command, "@F", current.Field);
                    HrmsDatabase.AddParameter(command, "@V", current.Value);
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
DELETE FROM ShiftTypeDays WHERE ShiftTypeId = @Id;
DELETE FROM ShiftTypePeriods WHERE ShiftTypeId = @Id;
DELETE FROM ShiftEligibilityRules WHERE ShiftTypeId = @Id;
DELETE FROM ShiftTypes WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    /// <summary>يحسب ساعات اليوم من وقتي الدخول/الخروج (يدعم عبور منتصف الليل).</summary>
    public static decimal ComputeHours(string? start, string? end)
    {
        if (!TimeSpan.TryParse(start, out var s) || !TimeSpan.TryParse(end, out var e)) return 0;
        var span = e - s;
        if (span < TimeSpan.Zero) span += TimeSpan.FromDays(1); // مناوبة ليلية تعبر منتصف الليل
        return Math.Round((decimal)span.TotalHours, 2);
    }

    private static ShiftType ReadShift(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        Name = HrmsDatabase.GetString(reader, "Name"),
        NameEn = HrmsDatabase.GetString(reader, "NameEn"),
        ColorHex = HrmsDatabase.GetString(reader, "ColorHex") is { Length: > 0 } c ? c : "#12D9E3",
        IsFlexible = HrmsDatabase.GetBool(reader, "IsFlexible"),
        FlexDailyHours = reader["FlexDailyHours"] is decimal f ? f : 0,
        MultiPeriod = HrmsDatabase.GetBool(reader, "MultiPeriod"),
        FillMissingCheckIn = HrmsDatabase.GetBool(reader, "FillMissingCheckIn"),
        FillMissingCheckOut = HrmsDatabase.GetBool(reader, "FillMissingCheckOut"),
        StripSemantics = HrmsDatabase.GetBool(reader, "StripSemantics"),
        ConsiderPermissionsOutsideShift = HrmsDatabase.GetBool(reader, "ConsiderPermissionsOutsideShift"),
        ExcludePermsOutsideStartFromLate = HrmsDatabase.GetBool(reader, "ExcludePermsOutsideStartFromLate"),
        TotalDurationMode = HrmsDatabase.GetString(reader, "TotalDurationMode") is { Length: > 0 } td ? td : "WorkOnly",
        AvailableInRoster = HrmsDatabase.GetBool(reader, "AvailableInRoster"),
        RequestableFromEss = HrmsDatabase.GetBool(reader, "RequestableFromEss"),
        LatenessGraceMinutes = HrmsDatabase.GetInt(reader, "LatenessGraceMinutes"),
        EarlyLeaveGraceMinutes = HrmsDatabase.GetInt(reader, "EarlyLeaveGraceMinutes"),
        GraceExceededPolicy = HrmsDatabase.GetString(reader, "GraceExceededPolicy") is { Length: > 0 } gxp ? gxp : "Subtract",
        TimeLimitFrom = HrmsDatabase.GetString(reader, "TimeLimitFrom") is { Length: > 0 } tlf ? tlf : null,
        TimeLimitFromDayBefore = HrmsDatabase.GetBool(reader, "TimeLimitFromDayBefore"),
        TimeLimitTo = HrmsDatabase.GetString(reader, "TimeLimitTo") is { Length: > 0 } tlt ? tlt : null,
        TimeLimitToDayAfter = HrmsDatabase.GetBool(reader, "TimeLimitToDayAfter"),
        MidShiftTime = HrmsDatabase.GetString(reader, "MidShiftTime") is { Length: > 0 } mst ? mst : null,
        ConflictLateReturnEnabled = HrmsDatabase.GetBool(reader, "ConflictLateReturnEnabled"),
        ConflictLateReturnAction = HrmsDatabase.GetString(reader, "ConflictLateReturnAction") is { Length: > 0 } clra ? clra : "Deduction",
        ConflictLateReturnValue = reader["ConflictLateReturnValue"] is decimal clrv ? clrv : 0,
        ConflictEarlyLeaveEnabled = HrmsDatabase.GetBool(reader, "ConflictEarlyLeaveEnabled"),
        ConflictEarlyLeaveAction = HrmsDatabase.GetString(reader, "ConflictEarlyLeaveAction") is { Length: > 0 } cela ? cela : "Deduction",
        ConflictEarlyLeaveValue = reader["ConflictEarlyLeaveValue"] is decimal celv ? celv : 0,
        // نمط `is decimal x ? x : null` يُبقي DBNull على null — وهو المقصود هنا:
        // «غير محدَّد» يختلف عن صفر (انظر التعليق عند الحقول).
        OvertimeRateWeekend = reader["OvertimeRateWeekend"] is decimal orw ? orw : null,
        OvertimeRateRest = reader["OvertimeRateRest"] is decimal orr ? orr : null,
        OvertimeRateHoliday = reader["OvertimeRateHoliday"] is decimal orh ? orh : null,
        OvertimeRateLeave = reader["OvertimeRateLeave"] is decimal orl ? orl : null,
        IsActive = HrmsDatabase.GetBool(reader, "IsActive")
    };

    /// <summary>
    /// معامل ساعة العمل الإضافي بسياق اليوم (نظير كيان «سعر ساعة العمل»).
    ///
    /// <para>السياق يأتي من <see cref="ShiftRuleStore.EffectiveContext"/> — نفس الدالة
    /// التي تحسم انطباق قواعد المناوبات، فلا يقع أن تنطبق قاعدة «العطلة الرسمية»
    /// بينما يُحتسب الأجر بمعامل «يوم الراحة».</para>
    ///
    /// <para><b>غير محدَّد ⟹ الافتراضي</b> (<c>PayrollTransactionStore.DefaultRateFactor</c>)
    /// — وهو ما يفعله النظام اليوم لكل ساعة. فمناوبةٌ لم تُملأ حقولها تعطي القيمة
    /// نفسها بالضبط، ولا يتغيّر أي مسير قائم.</para>
    ///
    /// <para>دالّة نقيّة كي تُختبَر بلا قاعدة — وهي حسابٌ ماليّ.</para>
    /// </summary>
    public static decimal ResolveOvertimeFactor(ShiftType? shift, string effectiveContext)
    {
        var configured = effectiveContext switch
        {
            "Weekend" => shift?.OvertimeRateWeekend,
            "Rest" => shift?.OvertimeRateRest,
            "Holiday" => shift?.OvertimeRateHoliday,
            "Leave" => shift?.OvertimeRateLeave,
            // «يوم عمل» (ومعه العمل عن بُعد ورحلة العمل) لا معامل خاصّ له بكيان ولا
            // عندنا: الأوفرتايم بيوم عملٍ عاديّ يبقى على الافتراضي.
            _ => null
        };

        // معاملٌ سالب لا معنى له — يُهمَل ويسقط للافتراضي بدل أن يقلب إشارة الأجر.
        // والصفر يُقبَل عمداً: «لا أجر إضافيّ لهذه الساعة» قرارٌ صالح.
        return configured is { } factor && factor >= 0
            ? factor
            : PayrollTransactionStore.DefaultRateFactor;
    }

    private static ShiftDay ReadDay(System.Data.Common.DbDataReader reader) => new()
    {
        DayIndex = HrmsDatabase.GetInt(reader, "DayIndex"),
        DayKind = HrmsDatabase.GetString(reader, "DayKind") is { Length: > 0 } k ? k : "Work",
        StartTime = HrmsDatabase.GetString(reader, "StartTime") is { Length: > 0 } s ? s : null,
        EndTime = HrmsDatabase.GetString(reader, "EndTime") is { Length: > 0 } e ? e : null,
        WorkHours = reader["WorkHours"] is decimal w ? w : 0
    };

    private static void AddShiftParameters(System.Data.Common.DbCommand command, ShiftType shift)
    {
        HrmsDatabase.AddParameter(command, "@Name", shift.Name);
        HrmsDatabase.AddParameter(command, "@NameEn", (object?)shift.NameEn ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Color", shift.ColorHex);
        HrmsDatabase.AddParameter(command, "@Flex", shift.IsFlexible ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@FlexHours", shift.FlexDailyHours);
        HrmsDatabase.AddParameter(command, "@Multi", shift.MultiPeriod ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@FMI", shift.FillMissingCheckIn ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@FMO", shift.FillMissingCheckOut ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@STS", shift.StripSemantics ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@CPO", shift.ConsiderPermissionsOutsideShift ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@EPL", shift.ExcludePermsOutsideStartFromLate ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@TDM", string.IsNullOrWhiteSpace(shift.TotalDurationMode) ? "WorkOnly" : shift.TotalDurationMode);
        HrmsDatabase.AddParameter(command, "@AIR", shift.AvailableInRoster ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@RFE", shift.RequestableFromEss ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@LGM", shift.LatenessGraceMinutes);
        HrmsDatabase.AddParameter(command, "@ELG", shift.EarlyLeaveGraceMinutes);
        HrmsDatabase.AddParameter(command, "@GXP", shift.GraceExceededPolicy == "Full" ? "Full" : "Subtract");
        HrmsDatabase.AddParameter(command, "@TLF", (object?)shift.TimeLimitFrom ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@TLFB", shift.TimeLimitFromDayBefore ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@TLT", (object?)shift.TimeLimitTo ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@TLTA", shift.TimeLimitToDayAfter ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@MST", (object?)shift.MidShiftTime ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@CLRE", shift.ConflictLateReturnEnabled ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@CLRA", string.IsNullOrWhiteSpace(shift.ConflictLateReturnAction) ? "Deduction" : shift.ConflictLateReturnAction);
        HrmsDatabase.AddParameter(command, "@CLRV", shift.ConflictLateReturnValue);
        HrmsDatabase.AddParameter(command, "@CELE", shift.ConflictEarlyLeaveEnabled ? 1 : 0);
        HrmsDatabase.AddParameter(command, "@CELA", string.IsNullOrWhiteSpace(shift.ConflictEarlyLeaveAction) ? "Deduction" : shift.ConflictEarlyLeaveAction);
        HrmsDatabase.AddParameter(command, "@CELV", shift.ConflictEarlyLeaveValue);
        // null يُكتب NULL لا صفراً: الصفر معامل صالح («لا أجر لهذه الساعة») بينما
        // NULL يعني «غير محدَّد ⟹ استعمل الافتراضي». خلطهما يجعل حقلاً فارغاً يصفّر أجراً.
        HrmsDatabase.AddParameter(command, "@ORW", (object?)shift.OvertimeRateWeekend ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@ORR", (object?)shift.OvertimeRateRest ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@ORH", (object?)shift.OvertimeRateHoliday ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@ORL", (object?)shift.OvertimeRateLeave ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Active", shift.IsActive ? 1 : 0);
    }

    /// <summary>
    /// هل الموظف مؤهّل لهذه المناوبة حسب معايير الاستحقاق؟ لا قواعد ⇒ الكل مؤهل.
    /// المجموعات OR، وداخل المجموعة AND. attrs = قيم حقول الموظف (Department/Branch/…)
    /// كنصوص (معرّفات للحقول المرجعية). قاعدة Employee تطابق معرّف الموظف.
    /// </summary>
    public static bool EmployeeMatchesEligibility(ShiftType shift, IReadOnlyDictionary<string, string?> attrs)
    {
        if (shift.Eligibility.Count == 0) return true;

        return shift.Eligibility
            .GroupBy(r => r.GroupNo)
            .Any(group => group.All(rule =>
                attrs.TryGetValue(rule.Field, out var actual)
                && !string.IsNullOrWhiteSpace(actual)
                && string.Equals(actual.Trim(), rule.Value.Trim(), StringComparison.OrdinalIgnoreCase)));
    }
}
