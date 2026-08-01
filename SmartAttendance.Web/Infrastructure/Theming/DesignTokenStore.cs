using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// تخزين مقاسات الواجهة المختارة وتصريفها إلى متغيّرات CSS.
///
/// يُخزَّن **المخالف للافتراضي فقط**: توكنٌ بقيمته الافتراضية لا يُكتب صفّه. فلو
/// غيّرنا افتراضاً بالكود لاحقاً سرى التغيير على من لم يخصّصه، ولم يتجمّد على قيمةٍ
/// نسخناها يوماً بالقاعدة.
/// </summary>
public static class DesignTokenStore
{
    /// <summary>
    /// كاش العملية: التخطيط يقرأ هذه القيم **بكل طلب لكل صفحة**، فقراءة القاعدة
    /// كل مرّة تعني استعلاماً زائداً بكل نقرة بالنظام. يُبطَل عند الحفظ.
    /// </summary>
    private static IReadOnlyDictionary<string, double>? _cache;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void Invalidate() => _cache = null;

    public static async Task<IReadOnlyDictionary<string, double>> LoadAsync(ApplicationDbContext db)
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        await Gate.WaitAsync();
        try
        {
            if (_cache is { } inner)
            {
                return inner;
            }

            var rows = await HrmsDatabase.QueryAsync(
                db,
                "SELECT TokenKey, TokenValue FROM DesignTokens;",
                command => { },
                reader => (
                    Key: HrmsDatabase.GetString(reader, "TokenKey"),
                    Value: HrmsDatabase.GetString(reader, "TokenValue")));

            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                // قيمةٌ تالفة بالقاعدة تُتجاهَل ولا تُسقط الصفحة: مقاسٌ واحد فاسد
                // لا يجوز أن يعطّل النظام كلّه.
                if (DesignTokenCatalog.Find(row.Key) is { } token
                    && double.TryParse(row.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    map[token.Key] = DesignTokenCatalog.Clamp(token, parsed);
                }
            }

            _cache = map;
            return map;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>القيمة السارية لتوكن: المخصَّصة إن وُجدت، وإلا الافتراضية.</summary>
    public static double Effective(IReadOnlyDictionary<string, double> saved, DesignTokenCatalog.Token token) =>
        saved.TryGetValue(token.Key, out var value) ? value : token.Default;

    /// <summary>
    /// يصرّف المخالف للافتراضي إلى كتلة <c>:root</c>. يرجع فارغاً إن لم يُخصَّص
    /// شيء — فيبقى الـHTML مطابقاً تماماً لما قبل الميزة.
    /// </summary>
    public static string CompileCss(IReadOnlyDictionary<string, double> saved)
    {
        var declarations = new List<string>();
        foreach (var token in DesignTokenCatalog.All)
        {
            if (saved.TryGetValue(token.Key, out var value)
                && Math.Abs(value - token.Default) > 0.0001)
            {
                declarations.Add($"--zy-{token.Key}:{DesignTokenCatalog.Format(token, value)}");
            }
        }

        return declarations.Count == 0 ? string.Empty : ":root{" + string.Join(';', declarations) + "}";
    }

    public static async Task SaveAsync(
        ApplicationDbContext db,
        IReadOnlyDictionary<string, double> values,
        string? actor)
    {
        foreach (var (key, raw) in values)
        {
            if (DesignTokenCatalog.Find(key) is not { } token)
            {
                continue;
            }

            var clamped = DesignTokenCatalog.Clamp(token, raw);

            // العودة للافتراضي تُحذف لا تُكتب — انظر تعليق الصنف.
            if (Math.Abs(clamped - token.Default) <= 0.0001)
            {
                await HrmsDatabase.ExecuteAsync(
                    db,
                    "DELETE FROM DesignTokens WHERE TokenKey = @Key;",
                    command => HrmsDatabase.AddParameter(command, "@Key", token.Key));
                continue;
            }

            // MERGE بدل INSERT/UPDATE: الحفظ يتكرّر بكل ضغطة «اعتمد»، فيجب أن
            // تكون الكتابة idempotent لا أن تكدّس صفوفاً أو تنفجر بالمفتاح.
            await HrmsDatabase.ExecuteAsync(
                db,
                """
MERGE DesignTokens AS target
USING (SELECT @Key AS TokenKey) AS source ON target.TokenKey = source.TokenKey
WHEN MATCHED THEN UPDATE SET TokenValue = @Value, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @Actor
WHEN NOT MATCHED THEN INSERT (TokenKey, TokenValue, UpdatedAt, UpdatedBy)
     VALUES (@Key, @Value, SYSUTCDATETIME(), @Actor);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Key", token.Key);
                    HrmsDatabase.AddParameter(
                        command,
                        "@Value",
                        clamped.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    HrmsDatabase.AddParameter(command, "@Actor", (object?)actor ?? DBNull.Value);
                });
        }

        Invalidate();
    }

    /// <summary>يمسح كل تخصيص فيعود النظام لمقاساته الافتراضية.</summary>
    public static async Task ResetAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(db, "DELETE FROM DesignTokens;");
        Invalidate();
    }
}
