using System.Data.Common;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// حارس فصل البيئات — يمنع أن يلمس تشغيلُ تطويرٍ قاعدةَ الإنتاج.
///
/// <para><b>لماذا وُجد:</b> كان <c>appsettings.json</c> و<c>appsettings.Development.json</c>
/// يشيران لنفس القاعدة حرفياً (<c>Database=SmartAttendance</c>)، فـ<c>dotnet run</c>
/// بالتطوير يكتب على الإنتاج — و<see cref="SqlSchemaMigrator"/> يعمل عند كل إقلاع
/// ⟹ <b>هجرة تلقائية من التطوير للإنتاج</b>. وهذا ليس خطراً نظرياً: وقع فعلاً
/// بجلسة 2026-08-08 (صفٌّ للفحص الحيّ كُتب بقاعدة الإنتاج ثم حُذف).</para>
///
/// <para><b>مغلق الفشل:</b> يرمي عند الإقلاع لا يسجّل تحذيراً — تحذيرٌ بسجلّ
/// إقلاعٍ لا يقرأه أحد ليس حاجزاً. والقرار يُتّخذ <b>قبل</b> تشغيل المهاجر.</para>
/// </summary>
public static class EnvironmentDatabaseGuard
{
    /// <summary>اسم قاعدة الإنتاج — القاعدة الوحيدة التي لا يجوز أن يمسّها تشغيل غير إنتاجي.</summary>
    public const string ProductionDatabaseName = "SmartAttendance";

    /// <summary>
    /// يستخرج اسم القاعدة من نصّ الاتصال. يعيد <c>null</c> إن تعذّر التحليل —
    /// والمتّصل يعامل ذلك كحالة شكّ لا كحالة أمان.
    /// </summary>
    public static string? DatabaseName(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

            foreach (var key in new[] { "Database", "Initial Catalog" })
            {
                if (builder.TryGetValue(key, out var value) &&
                    value?.ToString() is { Length: > 0 } name)
                {
                    return name;
                }
            }
        }
        catch (ArgumentException)
        {
            // نصّ اتصال تالف — لا نخمّن.
        }

        return null;
    }

    /// <summary>
    /// رسالة الرفض، أو <c>null</c> إن كان التشغيل مسموحاً.
    ///
    /// <para>القاعدة: بيئةٌ غير إنتاجية + قاعدةُ الإنتاج = رفض. وما عدا ذلك يمرّ —
    /// الإنتاج على قاعدة الإنتاج طبيعيّ، والتطوير على قاعدة أخرى هو المطلوب.</para>
    /// </summary>
    public static string? Validate(string? environmentName, string? connectionString)
    {
        var isProduction = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);
        if (isProduction) return null;

        var database = DatabaseName(connectionString);
        if (database is null) return null;   // لا نمنع تشغيلاً بسبب نصّ لم نفهمه

        if (!string.Equals(database, ProductionDatabaseName, StringComparison.OrdinalIgnoreCase))
            return null;

        return $"""
            رُفض الإقلاع: البيئة «{environmentName ?? "غير محدَّدة"}» تشير لقاعدة الإنتاج «{database}».
            تشغيلٌ غير إنتاجي على قاعدة الإنتاج يعني أن هجرات المخطط والبيانات التجريبية
            تُكتب على بيانات حقيقية. صحّح ConnectionStrings:DefaultConnection ببيئتك
            (مثلاً {ProductionDatabaseName}_Dev)، أو شغّل بـASPNETCORE_ENVIRONMENT=Production
            إن كان هذا خادم الإنتاج فعلاً.
            """;
    }
}
