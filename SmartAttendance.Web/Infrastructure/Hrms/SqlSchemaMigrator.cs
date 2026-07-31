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
