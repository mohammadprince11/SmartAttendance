using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Api;
using SmartAttendance.Web.Infrastructure.Platform;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Canonical schema deployment sequence for ZYNORA.
/// The web host may call this in non-production environments; production
/// deployment should invoke it explicitly through the database migrator tool.
/// </summary>
public static class DatabaseDeployment
{
    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        // Legacy base shapes that pre-date the controlled migration ledger.
        await SalaryItemStore.EnsureAsync(db);
        await EmployeeAllowanceSchema.EnsureAsync(db);
        await PayrollTransactionStore.EnsureAsync(db);
        await EmployeeUpdateSchema.EnsureAsync(db);

        await HrmsDatabase.EnsureCreatedAsync(db);

        // قواعد البيانات القائمة يجب أن تستلم TenantId قبل أن تتحقق طبقة الدخول
        // من مخططها. أما القاعدة الجديدة فلا تملك AppLoginUsers بعد، فننشئه أولاً
        // بالشكل الحديث ثم تمرّ كل الهجرات المحكومة عليه بصورة idempotent.
        var loginTableExists = await HrmsDatabase.ScalarAsync<int>(
            db,
            "SELECT CASE WHEN OBJECT_ID('dbo.AppLoginUsers', 'U') IS NULL THEN 0 ELSE 1 END;") == 1;

        if (loginTableExists)
        {
            await SqlSchemaMigrator.ApplyAsync(db);
        }

        await LoginDatabase.EnsureCreatedAsync(db);
        await ShiftTypeStore.EnsureAsync(db);

        // Versioned, auditable migrations. People AI foundation is registered
        // here and recorded in dbo.__SchemaMigrations.
        if (!loginTableExists)
        {
            await SqlSchemaMigrator.ApplyAsync(db);
        }

        // إنشاء أول مالك للمنصة عملية بيانات فقط، وتعمل حصراً عند خلو الجدول
        // ووجود قيم محمية بمتغيرات البيئة. لا كلمة مرور افتراضية بالمصدر.
        await PlatformPortalStore.VerifySchemaAsync(db);
        await PlatformPortalStore.EnsureBootstrapOwnerAsync(db);

        // Verification only: PeopleAiSchema.VerifyAsync never performs DDL.
        await PeopleAiSchema.VerifyAsync(db);

        // Data defaults/backfill happen only after the schema is verified.
        await PeopleAiSettingsStore.EnsureDefaultsAsync(db);
        await PeopleIdentityBootstrap.EnsureLegacyIdentityRowsAsync(db);

        // Existing API token schema remains part of the explicit deployment
        // sequence rather than a hot-path operation.
        await ApiTokenStore.EnsureAsync(db);
    }

    /// <summary>
    /// Production startup verification. It intentionally performs no DDL.
    /// </summary>
    public static async Task VerifyProductionSchemaAsync(
        ApplicationDbContext db)
    {
        await PeopleAiSchema.VerifyAsync(db);
        await PlatformPortalStore.VerifySchemaAsync(db);
    }
}
