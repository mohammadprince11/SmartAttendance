using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// استيراد لائحة جزاءات الشركة إلى الجداول — من <see cref="DisciplinaryPolicyPack"/>.
///
/// ⚠️ **لا يمسّ ما أدخله المستخدم**: المطابقة بالاسم، والموجود يُترك كما هو ولا
/// يُدهَس. من عدّل درجةً بالشاشة فعدّلها عن قصد، وإعادة الاستيراد يجب أن تضيف
/// الناقص لا أن تُلغي القرارات.
///
/// ⚠️ **الجزاءات تُكتب لمخالفةٍ لا جزاء لها فقط**: لو كان لها سلّمٌ قائم فالسلّم
/// قرارُ الشركة، واستبداله بالحدّ الأقصى القانونيّ رفعٌ للعقوبة من حيث لا تُقصد
/// (مادة 2: المذكور هو الحدّ الأقصى الجائز، وللإدارة النزول عنه).
/// </summary>
public static class DisciplinaryPolicyImporter
{
    public sealed record Result(int Categories, int Violations, int Rules, int SkippedViolations)
    {
        public string Message =>
            Categories + Violations + Rules == 0
                ? "المثال موجود بالكامل — لا جديد يُضاف."
                : $"أُضيف المثال: {Categories} فئة · {Violations} مخالفة · {Rules} درجة جزاء"
                  + (SkippedViolations > 0 ? $" (تُركت {SkippedViolations} مخالفة لها سلّمٌ قائم كما هي)." : ".");
    }

    public static async Task<Result> ImportAsync(ApplicationDbContext db)
    {
        await DisciplinarySchema.EnsureAsync(db);

        var categoryIds = await ImportCategoriesAsync(db);
        var (violations, rules, skipped) = await ImportViolationsAsync(db, categoryIds);
        var categoriesAdded = categoryIds.Count(x => x.Value.Added);

        await ImportSettingsAsync(db);

        return new Result(categoriesAdded, violations, rules, skipped);
    }

    /// <summary>
    /// دمج الفئات المكرّرة بالفئات المرقّمة بحروف اللائحة.
    ///
    /// خلّفها زرّ «اللائحة الافتراضية» القديم: «مخالفات نظام العمل» بجانب «ب —
    /// مخالفات نظام العمل»، فيحتار المسجِّل أيّهما يختار وتتوزّع الحالات على
    /// بابين لنفس الباب.
    ///
    /// ⚠️ **نقلٌ لا حذف**: مخالفات الفئة المكرّرة تُنقل إلى نظيرتها المرقّمة،
    /// ولا تُحذف إلا إن كان لها اسمٌ مطابق هناك أصلاً **ولا حالات عليها**. حذفُ
    /// مخالفةٍ سُجّلت عليها حالة يقطع سند حالةٍ قائمة وكتابٍ صدر عنها.
    /// </summary>
    public static async Task<string> MergeDuplicateCategoriesAsync(ApplicationDbContext db)
    {
        await DisciplinarySchema.EnsureAsync(db);

        int moved = 0, dropped = 0, removedCategories = 0, blocked = 0;

        var categories = await HrmsDatabase.QueryAsync(
            db,
            "SELECT Id, Name FROM DisciplinaryViolationCategories ORDER BY Id;",
            command => { },
            reader => (Id: HrmsDatabase.GetInt(reader, "Id"), Name: HrmsDatabase.GetString(reader, "Name")));

        // الفئات المرقّمة هي الوجهة؛ وما عداها مرشَّحٌ للدمج.
        var lettered = categories.Where(c => IsLettered(c.Name)).ToList();
        var duplicates = categories.Where(c => !IsLettered(c.Name)).ToList();

        foreach (var duplicate in duplicates)
        {
            var target = lettered.FirstOrDefault(c => SameTheme(c.Name, duplicate.Name));

            var violations = await HrmsDatabase.QueryAsync(
                db,
                "SELECT Id, Name FROM DisciplinaryViolationTypes WHERE CategoryId = @Id;",
                command => HrmsDatabase.AddParameter(command, "@Id", duplicate.Id),
                reader => (Id: HrmsDatabase.GetInt(reader, "Id"), Name: HrmsDatabase.GetString(reader, "Name")));

            foreach (var violation in violations)
            {
                if (target.Id == 0)
                {
                    blocked++;
                    continue;
                }

                var twinElsewhere = await HrmsDatabase.ScalarAsync<int>(
                    db,
                    """
SELECT COUNT(1) FROM DisciplinaryViolationTypes
WHERE Name = @Name AND CategoryId = @Target AND Id <> @Id;
""",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Name", violation.Name);
                        HrmsDatabase.AddParameter(command, "@Target", target.Id);
                        HrmsDatabase.AddParameter(command, "@Id", violation.Id);
                    });

                var cases = await CaseCountAsync(db, violation.Id);

                if (twinElsewhere > 0 && cases == 0)
                {
                    await HrmsDatabase.ExecuteAsync(
                        db,
                        "DELETE FROM DisciplinaryPenaltyRules WHERE ViolationTypeId = @Id; DELETE FROM DisciplinaryViolationTypes WHERE Id = @Id;",
                        command => HrmsDatabase.AddParameter(command, "@Id", violation.Id));
                    dropped++;
                    continue;
                }

                await HrmsDatabase.ExecuteAsync(
                    db,
                    "UPDATE DisciplinaryViolationTypes SET CategoryId = @Target WHERE Id = @Id;",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Target", target.Id);
                        HrmsDatabase.AddParameter(command, "@Id", violation.Id);
                    });
                moved++;
            }

            var remaining = await HrmsDatabase.ScalarAsync<int>(
                db,
                "SELECT COUNT(1) FROM DisciplinaryViolationTypes WHERE CategoryId = @Id;",
                command => HrmsDatabase.AddParameter(command, "@Id", duplicate.Id));

            if (remaining == 0)
            {
                await HrmsDatabase.ExecuteAsync(
                    db,
                    "DELETE FROM DisciplinaryViolationCategories WHERE Id = @Id;",
                    command => HrmsDatabase.AddParameter(command, "@Id", duplicate.Id));
                removedCategories++;
            }
        }

        if (moved + dropped + removedCategories == 0)
        {
            return "لا فئات مكرّرة — الأبواب كلّها مرقّمة بحروف اللائحة.";
        }

        var parts = new List<string>();
        if (removedCategories > 0) parts.Add($"دُمجت {removedCategories} فئة مكرّرة");
        if (moved > 0) parts.Add($"نُقلت {moved} مخالفة");
        if (dropped > 0) parts.Add($"حُذفت {dropped} مكرّرة");
        if (blocked > 0) parts.Add($"تُركت {blocked} بلا نظيرٍ مرقّم");

        return string.Join(" · ", parts) + ".";
    }

    /// <summary>الفئة المرقّمة تبدأ بحرف اللائحة متبوعاً بشرطة — «أ — …».</summary>
    private static bool IsLettered(string name) =>
        name.StartsWith("أ ", StringComparison.Ordinal)
        || name.StartsWith("ب ", StringComparison.Ordinal)
        || name.StartsWith("ج ", StringComparison.Ordinal)
        || name.StartsWith("ت ", StringComparison.Ordinal);

    /// <summary>
    /// نظير الفئة المكرّرة بالمرقّمة — بالكلمة المميّزة لا بالتطابق الحرفيّ،
    /// فالاسم القديم «مخالفات نظام العمل» والجديد «ب — مخالفات نظام العمل».
    /// </summary>
    private static bool SameTheme(string lettered, string duplicate)
    {
        var keys = new (string Word, string Letter)[]
        {
            ("الحضور", "أ"), ("المظهر", "أ"), ("السلامة", "ب"),
            ("نظام العمل", "ب"), ("سلوك", "ج")
        };

        foreach (var (word, letter) in keys)
        {
            if (duplicate.Contains(word, StringComparison.Ordinal)
                && lettered.StartsWith(letter + " ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<int> CaseCountAsync(ApplicationDbContext db, int violationId) =>
        await HrmsDatabase.ScalarAsync<int>(
            db,
            """
SELECT CASE WHEN OBJECT_ID('EmployeeViolationCases','U') IS NULL THEN 0
       ELSE (SELECT COUNT(1) FROM EmployeeViolationCases WHERE ViolationTypeId = @Id) END;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", violationId));


    private static async Task<Dictionary<int, (int Id, bool Added)>> ImportCategoriesAsync(ApplicationDbContext db)
    {
        var map = new Dictionary<int, (int Id, bool Added)>();

        for (var index = 0; index < DisciplinaryPolicyPack.Categories.Length; index++)
        {
            var (name, nameEn, order) = DisciplinaryPolicyPack.Categories[index];

            var existing = await HrmsDatabase.ScalarAsync<int>(
                db,
                "SELECT TOP 1 Id FROM DisciplinaryViolationCategories WHERE Name = @Name;",
                command => HrmsDatabase.AddParameter(command, "@Name", name));

            if (existing > 0)
            {
                map[index] = (existing, false);
                continue;
            }

            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO DisciplinaryViolationCategories (Name, NameEn, DisplayOrder, IsSystem, IsActive, CreatedAt)
VALUES (@Name, @NameEn, @Order, 0, 1, SYSUTCDATETIME());
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Name", name);
                    HrmsDatabase.AddParameter(command, "@NameEn", nameEn);
                    HrmsDatabase.AddParameter(command, "@Order", order);
                });

            var created = await HrmsDatabase.ScalarAsync<int>(
                db,
                "SELECT TOP 1 Id FROM DisciplinaryViolationCategories WHERE Name = @Name ORDER BY Id DESC;",
                command => HrmsDatabase.AddParameter(command, "@Name", name));

            map[index] = (created, true);
        }

        return map;
    }

    private static async Task<(int Violations, int Rules, int Skipped)> ImportViolationsAsync(
        ApplicationDbContext db,
        Dictionary<int, (int Id, bool Added)> categories)
    {
        int violationsAdded = 0, rulesAdded = 0, skipped = 0;

        foreach (var (categoryIndex, name, steps) in DisciplinaryPolicyPack.Violations)
        {
            if (!categories.TryGetValue(categoryIndex, out var category)) continue;

            var violationId = await HrmsDatabase.ScalarAsync<int>(
                db,
                "SELECT TOP 1 Id FROM DisciplinaryViolationTypes WHERE Name = @Name;",
                command => HrmsDatabase.AddParameter(command, "@Name", name));

            if (violationId == 0)
            {
                await HrmsDatabase.ExecuteAsync(
                    db,
                    """
INSERT INTO DisciplinaryViolationTypes
(CategoryId, Name, Description, Severity, ValidityMonths, CountingPeriod, IncludeInEvaluation, ShowToEmployee, IsActive, CreatedAt)
VALUES (@CategoryId, @Name, N'', @Severity, 12, N'Yearly', 1, 1, 1, SYSUTCDATETIME());
""",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@CategoryId", category.Id);
                        HrmsDatabase.AddParameter(command, "@Name", name);
                        HrmsDatabase.AddParameter(command, "@Severity", Severity(categoryIndex, steps));
                    });

                violationId = await HrmsDatabase.ScalarAsync<int>(
                    db,
                    "SELECT TOP 1 Id FROM DisciplinaryViolationTypes WHERE Name = @Name ORDER BY Id DESC;",
                    command => HrmsDatabase.AddParameter(command, "@Name", name));

                violationsAdded++;
            }

            if (violationId == 0) continue;

            var hasRules = await HrmsDatabase.ScalarAsync<int>(
                db,
                "SELECT COUNT(1) FROM DisciplinaryPenaltyRules WHERE ViolationTypeId = @Id;",
                command => HrmsDatabase.AddParameter(command, "@Id", violationId));

            if (hasRules > 0)
            {
                skipped++;
                continue;
            }

            rulesAdded += await ImportStepsAsync(db, violationId, categoryIndex, steps);
        }

        return (violationsAdded, rulesAdded, skipped);
    }

    private static async Task<int> ImportStepsAsync(
        ApplicationDbContext db,
        int violationId,
        int categoryIndex,
        string[] steps)
    {
        var added = 0;

        for (var i = 0; i < steps.Length; i++)
        {
            var step = PenaltyStepSyntax.Parse(steps[i]);
            if (step.IsEmpty) continue;

            var occurrence = i + 1;

            // آخر درجةٍ باللائحة تمتدّ لما بعدها: «تجاوز التكرار أربع مرات ⟹ الفصل
            // أو عدم التجديد». ولولا المدى لبقيت المخالفة الخامسة بلا جزاءٍ أصلاً.
            var isLast = i == steps.Length - 1;
            var occurrenceTo = isLast ? 999 : occurrence;

            var dropMonths = DisciplinaryPolicyPack.DropMonths(step.ActionType, categoryIndex);

            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO DisciplinaryPenaltyRules
(ViolationTypeId, OccurrenceFrom, OccurrenceTo, CountingPeriod, PenaltyAction, ActionType,
 FinancialImpactType, FinancialValue, ValidityMonths, CalculationMode, RequiresApproval,
 BasePoolJson, WorkDaysBasis, WorkDaysFixed, ExcludeHolidays,
 DropMode, DropEvery, DropUnit, DropAnchor, IsActive, CreatedAt)
VALUES
(@ViolationTypeId, @From, @To, N'Yearly', @Title, @ActionType,
 @ImpactType, @Value, @DropMonths, N'Cumulative', @RequiresApproval,
 N'', N'Fixed', 30, N'None',
 N'Once', @DropMonths, N'Months', N'ActionDate', 1, SYSUTCDATETIME());
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@ViolationTypeId", violationId);
                    HrmsDatabase.AddParameter(command, "@From", occurrence);
                    HrmsDatabase.AddParameter(command, "@To", occurrenceTo);
                    HrmsDatabase.AddParameter(command, "@Title", step.Title);
                    HrmsDatabase.AddParameter(command, "@ActionType", step.ActionType);
                    HrmsDatabase.AddParameter(command, "@ImpactType", step.ImpactType);
                    HrmsDatabase.AddParameter(command, "@Value", step.Value);
                    HrmsDatabase.AddParameter(command, "@DropMonths", dropMonths);

                    // الفصل قرارٌ لا رجعة فيه — يُحجز على اعتماد اللجنة دائماً.
                    HrmsDatabase.AddParameter(
                        command, "@RequiresApproval", PenaltyAction.IsTermination(step.ActionType));
                });

            added++;
        }

        return added;
    }

    /// <summary>
    /// خطورة المخالفة تُشتقّ من فئتها ومن أشدّ درجاتها — لا تُطلب من المستخدم
    /// مرّةً أخرى وهي مستنتَجة أصلاً.
    /// </summary>
    private static string Severity(int categoryIndex, string[] steps)
    {
        if (steps.Any(s => PenaltyStepSyntax.Parse(s).ActionType == PenaltyAction.Termination))
        {
            return steps.Length == 1 ? "FinalWarning" : "C";
        }

        return categoryIndex switch
        {
            0 => "A",
            2 => "C",
            _ => "B"
        };
    }

    private static async Task ImportSettingsAsync(ApplicationDbContext db)
    {
        foreach (var (key, value) in DisciplinaryPolicyPack.Settings)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
IF EXISTS (SELECT 1 FROM DisciplinarySettings WHERE [Key] = @Key)
    UPDATE DisciplinarySettings SET [Value] = @Value, UpdatedAt = SYSUTCDATETIME() WHERE [Key] = @Key;
ELSE
    INSERT INTO DisciplinarySettings([Key], [Value], UpdatedAt) VALUES (@Key, @Value, SYSUTCDATETIME());
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Key", key);
                    HrmsDatabase.AddParameter(command, "@Value", value);
                });
        }
    }
}
