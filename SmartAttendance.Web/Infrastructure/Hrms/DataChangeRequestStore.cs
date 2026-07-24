using System.Data.Common;
using System.Text;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// طلب «تعديل البيانات» (نمط ZenHR/كيان): الموظف يقترح تعديلاً على حقول ملفه الشخصية،
/// فيُنشأ طلب خدمة ذاتية (RequestType = <see cref="RequestTypeLabel"/>) يمرّ عبر
/// محرك الموافقات القائم — وتُخزَّن التعديلات المقترَحة (حقل/قيمة قديمة/قيمة جديدة) في
/// جدول فرعي. عند الاعتماد النهائي فقط تُطبَّق على جدول Employees مع أثر تدقيق.
/// خلافاً للتعديل المباشر بشاشة «ملفي» — هنا لا يتغيّر شيء قبل موافقة اللجنة.
/// </summary>
public static class DataChangeRequestStore
{
    /// <summary>قيمة RequestType المخزَّنة بالطلب — بها تُميَّز طلبات تعديل البيانات.</summary>
    public const string RequestTypeLabel = "تعديل البيانات";

    /// <summary>
    /// حقل قابل للتعديل: مفتاح ثابت + عمود بجدول Employees + تسمية + نوع مدخل.
    /// Kind ∈ text|email|tel|date|select|photo — تحدّد كيف تُرندره صفحة تعديل البيانات.
    /// OptionsKey لحقول القائمة: marital|country|nationality|religion (تُحلّ من قوائم النظام).
    /// </summary>
    public sealed record Field(string Key, string Column, string Label, string Kind = "text", string? OptionsKey = null);

    /// <summary>
    /// كتالوج الحقول الشخصية القابلة للطلب. تُرشَّح فعلياً بأعمدة Employees الموجودة
    /// (<see cref="ListEditableAsync"/>) فلا يظهر حقل لعمود غير موجود، ويُحمى التطبيق
    /// كذلك بـ COL_LENGTH لكل عمود. القوائم من قوائم النظام (لا إدخال حر).
    /// </summary>
    public static readonly IReadOnlyList<Field> Catalog = new List<Field>
    {
        new("Phone",         "Phone",         "رقم الهاتف",        "tel"),
        new("Email",         "Email",         "البريد الإلكتروني", "email"),
        new("NationalId",    "NationalId",    "رقم الهوية",        "text"),
        new("BirthDate",     "BirthDate",     "تاريخ الميلاد",     "date"),
        new("MaritalStatus", "MaritalStatus", "الحالة الاجتماعية", "select", "marital"),
        new("Nationality",   "Nationality",   "الجنسية",           "select", "nationality"),
        new("Country",       "Country",       "بلد الإقامة",       "select", "country"),
        new("Religion",      "Religion",      "الديانة",           "select", "religion"),
        new("PhotoPath",     "PhotoPath",     "الصورة الشخصية",    "photo"),
    };

    /// <summary>خيار قائمة منسدلة (قيمة مخزَّنة + تسمية معروضة).</summary>
    public sealed record Option(string Value, string Label);

    /// <summary>قوائم النظام الثابتة (تطابق شاشة تعديل الموظف بالإدارة) — الديانة تُحمَّل من HrLookups.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<Option>> StaticOptions =
        new Dictionary<string, IReadOnlyList<Option>>
        {
            ["marital"] = new[]
            {
                new Option("Single", "أعزب"), new Option("Married", "متزوج"),
                new Option("Divorced", "مطلق"), new Option("Widowed", "أرمل"),
                new Option("Separated", "منفصل")
            },
            ["country"] = new[]
            {
                new Option("Iraq","العراق"), new Option("Syria","سوريا"), new Option("Jordan","الأردن"),
                new Option("Lebanon","لبنان"), new Option("Saudi Arabia","السعودية"),
                new Option("United Arab Emirates","الإمارات"), new Option("Qatar","قطر"),
                new Option("Kuwait","الكويت"), new Option("Bahrain","البحرين"), new Option("Oman","عمان"),
                new Option("Egypt","مصر"), new Option("Turkey","تركيا"), new Option("Iran","إيران"),
                new Option("India","الهند"), new Option("Pakistan","باكستان"),
                new Option("Bangladesh","بنغلاديش"), new Option("Philippines","الفلبين"),
                new Option("Nepal","نيبال"), new Option("Other","أخرى")
            },
            ["nationality"] = new[]
            {
                new Option("Iraqi","عراقي"), new Option("Syrian","سوري"), new Option("Jordanian","أردني"),
                new Option("Lebanese","لبناني"), new Option("Saudi","سعودي"), new Option("Emirati","إماراتي"),
                new Option("Qatari","قطري"), new Option("Kuwaiti","كويتي"), new Option("Bahraini","بحريني"),
                new Option("Omani","عماني"), new Option("Egyptian","مصري"), new Option("Turkish","تركي"),
                new Option("Iranian","إيراني"), new Option("Indian","هندي"), new Option("Pakistani","باكستاني"),
                new Option("Bangladeshi","بنغلاديشي"), new Option("Filipino","فلبيني"),
                new Option("Nepali","نيبالي"), new Option("Other","أخرى")
            }
        };

    /// <summary>خيارات حقل قائمة — ثابتة أو من قوائم النظام (الديانة). فارغة لغير القوائم.</summary>
    public static async Task<List<Option>> OptionsAsync(ApplicationDbContext db, string? optionsKey)
    {
        if (string.IsNullOrWhiteSpace(optionsKey)) return new();
        if (optionsKey == "religion")
        {
            var vals = await HrLookups.ValuesAsync(db, "religions");
            return vals.Select(v => new Option(v, v)).ToList();
        }
        return StaticOptions.TryGetValue(optionsKey, out var opts) ? opts.ToList() : new();
    }

    public sealed class ProposedField
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Column { get; set; } = string.Empty;
        public string Kind { get; set; } = "text";
        public string? OptionsKey { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    public static async Task EnsureAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(db, """
IF OBJECT_ID('DataChangeRequestFields','U') IS NULL
BEGIN
    CREATE TABLE DataChangeRequestFields(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RequestId int NOT NULL,
        FieldKey nvarchar(60) NOT NULL,
        FieldLabel nvarchar(120) NOT NULL,
        ColumnName nvarchar(60) NOT NULL,
        OldValue nvarchar(400) NULL,
        NewValue nvarchar(400) NULL,
        Applied bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_DataChangeRequestFields_Request ON DataChangeRequestFields(RequestId);
END;
""");
    }

    /// <summary>الحقول القابلة للتعديل فعلياً = كتالوج ∩ أعمدة Employees الموجودة، مع القيمة الحالية.</summary>
    public static async Task<List<ProposedField>> ListEditableAsync(ApplicationDbContext db, int employeeId)
    {
        // أعمدة الكتالوج الموجودة فعلاً بجدول Employees.
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkSql = new StringBuilder("SELECT ");
        checkSql.Append(string.Join(", ", Catalog.Select((f, i) =>
            $"CASE WHEN COL_LENGTH('Employees','{f.Column}') IS NULL THEN 0 ELSE 1 END AS c{i}")));
        var flags = await HrmsDatabase.QueryAsync(db, checkSql.ToString(), null, r =>
        {
            for (var i = 0; i < Catalog.Count; i++)
                if (HrmsDatabase.GetInt(r, $"c{i}") == 1) existing.Add(Catalog[i].Column);
            return 0;
        });
        _ = flags;

        var usable = Catalog.Where(f => existing.Contains(f.Column)).ToList();
        if (usable.Count == 0) return new();

        // القيم الحالية للموظف (للأعمدة الموجودة فقط).
        var selectCols = string.Join(", ", usable.Select(f => $"CONVERT(nvarchar(400), [{f.Column}]) AS [{f.Column}]"));
        var current = (await HrmsDatabase.QueryAsync(db,
            $"SELECT TOP 1 {selectCols} FROM Employees WHERE Id=@Id",
            cmd => HrmsDatabase.AddParameter(cmd, "@Id", employeeId),
            r =>
            {
                var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in usable) map[f.Column] = HrmsDatabase.GetString(r, f.Column);
                return map;
            })).FirstOrDefault() ?? new(StringComparer.OrdinalIgnoreCase);

        return usable.Select(f => new ProposedField
        {
            Key = f.Key,
            Label = f.Label,
            Column = f.Column,
            Kind = f.Kind,
            OptionsKey = f.OptionsKey,
            OldValue = current.TryGetValue(f.Column, out var v) ? v : null
        }).ToList();
    }

    /// <summary>يخزّن التعديلات المقترَحة لطلب. يتجاهل الحقول غير الموجودة بالكتالوج أو التي بلا تغيير.</summary>
    public static async Task<int> SaveFieldsAsync(ApplicationDbContext db, int requestId, IEnumerable<ProposedField> fields)
    {
        await EnsureAsync(db);
        var byKey = Catalog.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        var saved = 0;
        foreach (var f in fields)
        {
            if (!byKey.TryGetValue(f.Key, out var def)) continue;
            var newVal = f.NewValue?.Trim();
            var oldVal = f.OldValue?.Trim();
            // بلا تغيير فعلي → لا يُخزَّن.
            if (string.Equals(newVal ?? "", oldVal ?? "", StringComparison.Ordinal)) continue;

            await HrmsDatabase.ExecuteAsync(db,
                """
INSERT INTO DataChangeRequestFields(RequestId, FieldKey, FieldLabel, ColumnName, OldValue, NewValue)
VALUES(@r, @k, @l, @c, @o, @n);
""",
                cmd =>
                {
                    HrmsDatabase.AddParameter(cmd, "@r", requestId);
                    HrmsDatabase.AddParameter(cmd, "@k", def.Key);
                    HrmsDatabase.AddParameter(cmd, "@l", def.Label);
                    HrmsDatabase.AddParameter(cmd, "@c", def.Column);
                    HrmsDatabase.AddParameter(cmd, "@o", (object?)oldVal ?? DBNull.Value);
                    HrmsDatabase.AddParameter(cmd, "@n", (object?)newVal ?? DBNull.Value);
                });
            saved++;
        }
        return saved;
    }

    /// <summary>طلب تعديل بيانات معلّق للموظف مع حقوله المقترَحة (لإدارته: تعديل/حذف).</summary>
    public sealed class PendingRequest
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = "Pending";
        public string CurrentStep { get; set; } = string.Empty;
        public List<ProposedField> Fields { get; set; } = new();
    }

    /// <summary>طلبات تعديل البيانات المعلّقة (قيد المراجعة) لموظف — لعرضها مع أزرار تعديل/حذف.</summary>
    public static async Task<List<PendingRequest>> ListPendingForEmployeeAsync(ApplicationDbContext db, int employeeId)
    {
        await EnsureAsync(db);
        var reqs = await HrmsDatabase.QueryAsync(db,
            """
SELECT Id, CreatedAt, ISNULL(Status,'Pending') AS Status, ISNULL(CurrentStep,'') AS CurrentStep
FROM SelfServiceRequests
WHERE EmployeeId=@e AND RequestType=@t AND Status='Pending'
ORDER BY Id DESC;
""",
            cmd => { HrmsDatabase.AddParameter(cmd, "@e", employeeId); HrmsDatabase.AddParameter(cmd, "@t", RequestTypeLabel); },
            r => new PendingRequest
            {
                Id = HrmsDatabase.GetInt(r, "Id"),
                CreatedAt = HrmsDatabase.GetDateTime(r, "CreatedAt") ?? default,
                Status = HrmsDatabase.GetString(r, "Status"),
                CurrentStep = HrmsDatabase.GetString(r, "CurrentStep")
            });

        foreach (var req in reqs)
            req.Fields = await ListFieldsAsync(db, req.Id);
        return reqs;
    }

    /// <summary>حقول طلب معلّق (للتعبئة المسبقة عند التعديل). فارغة إن لم يكن معلّقاً أو لغير صاحبه.</summary>
    public static async Task<List<ProposedField>> GetEditableRequestFieldsAsync(ApplicationDbContext db, int requestId, int employeeId)
    {
        var owns = await HrmsDatabase.ScalarAsync<int>(db,
            "SELECT COUNT(1) FROM SelfServiceRequests WHERE Id=@r AND EmployeeId=@e AND RequestType=@t AND Status='Pending'",
            cmd => { HrmsDatabase.AddParameter(cmd, "@r", requestId); HrmsDatabase.AddParameter(cmd, "@e", employeeId); HrmsDatabase.AddParameter(cmd, "@t", RequestTypeLabel); });
        return owns > 0 ? await ListFieldsAsync(db, requestId) : new();
    }

    /// <summary>حذف طلب تعديل بيانات معلّق (لصاحبه فقط، قبل الاعتماد). يزيل الحقول والسريان.</summary>
    public static async Task<bool> DeletePendingRequestAsync(ApplicationDbContext db, int requestId, int employeeId)
    {
        var owns = await HrmsDatabase.ScalarAsync<int>(db,
            "SELECT COUNT(1) FROM SelfServiceRequests WHERE Id=@r AND EmployeeId=@e AND RequestType=@t AND Status='Pending'",
            cmd => { HrmsDatabase.AddParameter(cmd, "@r", requestId); HrmsDatabase.AddParameter(cmd, "@e", employeeId); HrmsDatabase.AddParameter(cmd, "@t", RequestTypeLabel); });
        if (owns == 0) return false;

        await HrmsDatabase.ExecuteAsync(db,
            """
DELETE FROM DataChangeRequestFields WHERE RequestId=@r;
IF OBJECT_ID('ApprovalRequestSteps','U') IS NOT NULL DELETE FROM ApprovalRequestSteps WHERE RequestId=@r;
IF OBJECT_ID('ApprovalRequestFlows','U') IS NOT NULL DELETE FROM ApprovalRequestFlows WHERE RequestId=@r;
IF OBJECT_ID('ApprovalHistories','U') IS NOT NULL DELETE FROM ApprovalHistories WHERE RequestId=@r;
DELETE FROM SelfServiceRequests WHERE Id=@r;
""",
            cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId));
        return true;
    }

    public static Task<List<ProposedField>> ListFieldsAsync(ApplicationDbContext db, int requestId)
    {
        return HrmsDatabase.QueryAsync(db,
            "SELECT FieldKey, FieldLabel, ColumnName, OldValue, NewValue FROM DataChangeRequestFields WHERE RequestId=@r ORDER BY Id",
            cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId),
            r => new ProposedField
            {
                Key = HrmsDatabase.GetString(r, "FieldKey"),
                Label = HrmsDatabase.GetString(r, "FieldLabel"),
                Column = HrmsDatabase.GetString(r, "ColumnName"),
                OldValue = HrmsDatabase.GetString(r, "OldValue"),
                NewValue = HrmsDatabase.GetString(r, "NewValue")
            });
    }

    /// <summary>عدد طلبات تعديل البيانات وعدد حقولها — للعرض السريع بالشاشات.</summary>
    public static async Task<Dictionary<int, List<ProposedField>>> ListFieldsForRequestsAsync(
        ApplicationDbContext db, IEnumerable<int> requestIds)
    {
        var ids = requestIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var inClause = string.Join(",", ids.Select((_, i) => $"@r{i}"));
        var rows = await HrmsDatabase.QueryAsync(db,
            $"SELECT RequestId, FieldLabel, OldValue, NewValue FROM DataChangeRequestFields WHERE RequestId IN ({inClause}) ORDER BY RequestId, Id",
            cmd => { for (var i = 0; i < ids.Count; i++) HrmsDatabase.AddParameter(cmd, $"@r{i}", ids[i]); },
            r => new
            {
                RequestId = HrmsDatabase.GetInt(r, "RequestId"),
                Field = new ProposedField
                {
                    Label = HrmsDatabase.GetString(r, "FieldLabel"),
                    OldValue = HrmsDatabase.GetString(r, "OldValue"),
                    NewValue = HrmsDatabase.GetString(r, "NewValue")
                }
            });
        return rows.GroupBy(x => x.RequestId)
                   .ToDictionary(g => g.Key, g => g.Select(x => x.Field).ToList());
    }

    /// <summary>
    /// يُطبّق تعديلات الطلب على جدول Employees إذا كان طلب تعديل بيانات وبحالة اعتماد.
    /// آمن التكرار (Applied=1)، ومحمي COL_LENGTH لكل عمود، ويكتب أثر تدقيق واحد.
    /// يُستدعى بعد الاعتماد النهائي من شاشة الموافقات.
    /// </summary>
    public static async Task<bool> ApplyIfDataChangeAsync(ApplicationDbContext db, int requestId, string actor, string? ip)
    {
        await EnsureAsync(db);

        var isDataChange = await HrmsDatabase.ScalarAsync<int>(db,
            "SELECT CASE WHEN EXISTS(SELECT 1 FROM SelfServiceRequests WHERE Id=@r AND RequestType=@t) THEN 1 ELSE 0 END",
            cmd => { HrmsDatabase.AddParameter(cmd, "@r", requestId); HrmsDatabase.AddParameter(cmd, "@t", RequestTypeLabel); });
        if (isDataChange == 0) return false;

        var employeeId = await HrmsDatabase.ScalarAsync<int>(db,
            "SELECT ISNULL((SELECT EmployeeId FROM SelfServiceRequests WHERE Id=@r), 0)",
            cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId));
        if (employeeId <= 0) return false;

        var fields = await HrmsDatabase.QueryAsync(db,
            "SELECT ColumnName, NewValue FROM DataChangeRequestFields WHERE RequestId=@r AND Applied=0",
            cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId),
            r => new { Column = HrmsDatabase.GetString(r, "ColumnName"), New = HrmsDatabase.GetString(r, "NewValue") });
        if (fields.Count == 0) return false;

        // الأعمدة من كتالوج ثابت (لا حقن) — تُطبَّق كل على حدة بحماية COL_LENGTH.
        var valid = Catalog.Select(f => f.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var applied = fields.Where(f => valid.Contains(f.Column))
                            .Select(f => (Col: f.Column, Val: f.New))
                            .ToList();
        if (applied.Count == 0) return false;

        for (var i = 0; i < applied.Count; i++)
        {
            var (col, val) = applied[i];
            await HrmsDatabase.ExecuteAsync(db,
                $"IF COL_LENGTH('Employees','{col}') IS NOT NULL UPDATE Employees SET [{col}] = @val WHERE Id = @eid;",
                cmd =>
                {
                    HrmsDatabase.AddParameter(cmd, "@val", (object?)val ?? DBNull.Value);
                    HrmsDatabase.AddParameter(cmd, "@eid", employeeId);
                });
        }

        // وسم الحقول كمطبَّقة + أثر تدقيق.
        var summary = string.Join("، ", applied.Select(a => $"{a.Col}={a.Val}"));
        await HrmsDatabase.ExecuteAsync(db,
            """
UPDATE DataChangeRequestFields SET Applied=1 WHERE RequestId=@r;

IF OBJECT_ID('AuditLogs','U') IS NOT NULL
    INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
    VALUES ('Employee', CAST(@eid AS nvarchar(80)), 'Data Change Request Applied', @summary, @actor, @ip);
""",
            cmd =>
            {
                HrmsDatabase.AddParameter(cmd, "@r", requestId);
                HrmsDatabase.AddParameter(cmd, "@eid", employeeId);
                HrmsDatabase.AddParameter(cmd, "@summary", summary);
                HrmsDatabase.AddParameter(cmd, "@actor", actor);
                HrmsDatabase.AddParameter(cmd, "@ip", (object?)ip ?? DBNull.Value);
            });
        return true;
    }
}
