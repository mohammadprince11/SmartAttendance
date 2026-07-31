using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تخزين نُسَخ السياسات (<see cref="HrPolicyResolver"/>) — جدول واحد لكل السياسات.
///
/// **لماذا جدول واحد؟** لأن النسخة بنيتها ثابتة مهما اختلفت السياسة: اسم + شروط +
/// حمولة + ترتيب. جدولٌ لكل سياسة يعني هجرة وStore وصفحة لكل ميزة — وهو بالضبط
/// التكرار الذي تحاول هذه الطبقة إنهاءه. و<c>PolicyKey</c> يفصل النطاقات.
///
/// مفاتيح السياسات المعروفة تُعرَّف هنا لا كنصوص متناثرة، فمفتاح مكتوب بخطأ
/// إملائي يعني نُسَخاً تُحفظ ولا تُقرأ أبداً — عطل صامت بامتياز.
/// </summary>
public static class HrPolicyOverrideStore
{
    public const string PolicyProbation = "ProbationPeriod";
    public const string PolicyNotice = "NoticePeriod";
    public const string PolicyAttendanceSalaryLink = "AttendanceSalaryLink";

    public static readonly IReadOnlyDictionary<string, string> PolicyLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PolicyProbation] = "فترة التجربة",
            [PolicyNotice] = "فترة الإنذار",
            [PolicyAttendanceSalaryLink] = "ربط الراتب بالحضور"
        };

    public sealed record Row(
        int Id,
        string PolicyKey,
        string Name,
        int SortOrder,
        bool IsActive,
        string ConditionsJson,
        string PayloadJson)
    {
        public HrConditions.ConditionSet Conditions => HrConditions.Deserialize(ConditionsJson);
        public Dictionary<string, string> Payload => HrPolicyResolver.DeserializePayload(PayloadJson);

        public HrPolicyResolver.Override ToOverride() =>
            new(Id, Name, SortOrder, IsActive, Conditions, Payload);
    }

    public static async Task<List<Row>> LoadAsync(ApplicationDbContext db, string policyKey) =>
        await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, PolicyKey, Name, SortOrder, IsActive, ISNULL(ConditionsJson, N'') AS ConditionsJson,
       ISNULL(PayloadJson, N'') AS PayloadJson
FROM HrPolicyOverrides
WHERE PolicyKey = @PolicyKey
ORDER BY SortOrder, Id;
""",
            command => HrmsDatabase.AddParameter(command, "@PolicyKey", policyKey),
            reader => new Row(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "PolicyKey"),
                HrmsDatabase.GetString(reader, "Name"),
                HrmsDatabase.GetInt(reader, "SortOrder"),
                HrmsDatabase.GetBool(reader, "IsActive"),
                HrmsDatabase.GetString(reader, "ConditionsJson"),
                HrmsDatabase.GetString(reader, "PayloadJson")));

    public static async Task<Row?> FindAsync(ApplicationDbContext db, int id)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, PolicyKey, Name, SortOrder, IsActive, ISNULL(ConditionsJson, N'') AS ConditionsJson,
       ISNULL(PayloadJson, N'') AS PayloadJson
FROM HrPolicyOverrides WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            reader => new Row(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "PolicyKey"),
                HrmsDatabase.GetString(reader, "Name"),
                HrmsDatabase.GetInt(reader, "SortOrder"),
                HrmsDatabase.GetBool(reader, "IsActive"),
                HrmsDatabase.GetString(reader, "ConditionsJson"),
                HrmsDatabase.GetString(reader, "PayloadJson")));

        return rows.FirstOrDefault();
    }

    /// <summary>نسخة جديدة تُضاف **آخر** الترتيب — فلا تسبق نسخةً قائمة بلا قصد من المستخدم.</summary>
    public static async Task<int> CreateAsync(
        ApplicationDbContext db,
        string policyKey,
        string name,
        HrConditions.ConditionSet conditions,
        IReadOnlyDictionary<string, string> payload,
        string? createdBy)
    {
        var next = await HrmsDatabase.ScalarAsync<int>(
            db,
            "SELECT ISNULL(MAX(SortOrder), 0) + 1 FROM HrPolicyOverrides WHERE PolicyKey = @PolicyKey;",
            command => HrmsDatabase.AddParameter(command, "@PolicyKey", policyKey));

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO HrPolicyOverrides (PolicyKey, Name, SortOrder, IsActive, ConditionsJson, PayloadJson, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@PolicyKey, @Name, @SortOrder, 1, @Conditions, @Payload, @CreatedBy);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PolicyKey", policyKey);
                HrmsDatabase.AddParameter(command, "@Name", name);
                HrmsDatabase.AddParameter(command, "@SortOrder", next);
                HrmsDatabase.AddParameter(command, "@Conditions", HrConditions.Serialize(conditions));
                HrmsDatabase.AddParameter(command, "@Payload", HrPolicyResolver.SerializePayload(payload));
                HrmsDatabase.AddParameter(command, "@CreatedBy", createdBy);
            });
    }

    public static async Task UpdateAsync(
        ApplicationDbContext db,
        int id,
        string name,
        bool isActive,
        HrConditions.ConditionSet conditions,
        IReadOnlyDictionary<string, string> payload) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE HrPolicyOverrides
SET Name = @Name, IsActive = @IsActive, ConditionsJson = @Conditions,
    PayloadJson = @Payload, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Name", name);
                HrmsDatabase.AddParameter(command, "@IsActive", isActive);
                HrmsDatabase.AddParameter(command, "@Conditions", HrConditions.Serialize(conditions));
                HrmsDatabase.AddParameter(command, "@Payload", HrPolicyResolver.SerializePayload(payload));
            });

    public static async Task DeleteAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "DELETE FROM HrPolicyOverrides WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

    /// <summary>«انشئ نسخة» — نمط كيان المتكرّر: الاستنساخ بديلٌ عن البناء من الصفر.</summary>
    public static async Task<int?> DuplicateAsync(ApplicationDbContext db, int id, string? createdBy)
    {
        var source = await FindAsync(db, id);
        if (source is null)
        {
            return null;
        }

        return await CreateAsync(
            db,
            source.PolicyKey,
            $"{source.Name} (نسخة)",
            source.Conditions,
            source.Payload,
            createdBy);
    }

    /// <summary>يطبّق ترتيب السحب. معاملة واحدة: ترتيبٌ نصفه مطبَّق يعني أسبقيةً مختلطة.</summary>
    public static async Task ReorderAsync(ApplicationDbContext db, string policyKey, IReadOnlyList<int> orderedIds)
    {
        var existing = (await LoadAsync(db, policyKey)).Select(row => row.ToOverride()).ToList();
        var ranks = HrPolicyResolver.Reorder(orderedIds, existing);
        if (ranks.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        foreach (var (id, rank) in ranks)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                "UPDATE HrPolicyOverrides SET SortOrder = @SortOrder WHERE Id = @Id AND PolicyKey = @PolicyKey;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", id);
                    HrmsDatabase.AddParameter(command, "@SortOrder", rank);
                    HrmsDatabase.AddParameter(command, "@PolicyKey", policyKey);
                });
        }

        await transaction.CommitAsync();
    }

    /// <summary>
    /// حسم السياسة لموظف بعينه — الاختصار الذي تستدعيه الميزات.
    /// <paramref name="asOf"/> صريح لأن «مدة الخدمة» تتغيّر بالزمن؛ راجع
    /// <see cref="HrConditionFacts"/>.
    /// </summary>
    public static async Task<HrPolicyResolver.Resolution> ResolveForEmployeeAsync(
        ApplicationDbContext db,
        string policyKey,
        IReadOnlyDictionary<string, string> parent,
        int employeeId,
        DateOnly asOf)
    {
        var overrides = (await LoadAsync(db, policyKey)).Select(row => row.ToOverride()).ToList();

        if (overrides.Count == 0)
        {
            return new HrPolicyResolver.Resolution(
                new Dictionary<string, string>(parent, StringComparer.OrdinalIgnoreCase), null, "سياسة الشركة");
        }

        var rows = await HrConditionFacts.LoadAsync(db, employeeId);
        if (rows.Count == 0)
        {
            return new HrPolicyResolver.Resolution(
                new Dictionary<string, string>(parent, StringComparer.OrdinalIgnoreCase), null, "سياسة الشركة");
        }

        var facts = HrConditionFacts.Build(rows[0], asOf);
        return HrPolicyResolver.Resolve(parent, overrides, facts);
    }

    /// <summary>
    /// عدد من تنطبق عليهم كل نسخة — يُعرض بالقائمة. بلا هذا الرقم يُنشئ المستخدم
    /// شرطاً لا يطابق أحداً ولا يكتشفه إلا بعد شهر عند اختلاف راتب.
    /// </summary>
    public static async Task<Dictionary<int, int>> CountMatchesAsync(
        ApplicationDbContext db,
        string policyKey,
        DateOnly asOf)
    {
        var overrides = (await LoadAsync(db, policyKey)).Select(row => row.ToOverride()).ToList();
        var counts = overrides.ToDictionary(item => item.Id, _ => 0);

        if (overrides.Count == 0)
        {
            return counts;
        }

        foreach (var row in await HrConditionFacts.LoadAsync(db))
        {
            var facts = HrConditionFacts.Build(row, asOf);

            // يُحتسب للنسخة **الفائزة** فقط لا لكل نسخة تطابق — العدد المعروض يجب
            // أن يجيب «كم موظفاً تحكمهم هذه النسخة فعلاً»، لا «كم يطابق شرطها».
            foreach (var candidate in overrides.Where(item => item.IsActive).OrderBy(item => item.SortOrder).ThenBy(item => item.Id))
            {
                if (HrConditions.Matches(candidate.Conditions, facts, matchWhenEmpty: false))
                {
                    counts[candidate.Id]++;
                    break;
                }
            }
        }

        return counts;
    }
}
