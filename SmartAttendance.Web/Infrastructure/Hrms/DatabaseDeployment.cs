using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Api;
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
        await LoginDatabase.EnsureCreatedAsync(db);
        await ShiftTypeStore.EnsureAsync(db);

        // Versioned, auditable migrations. People AI foundation is registered
        // here and recorded in dbo.__SchemaMigrations.
        await SqlSchemaMigrator.ApplyAsync(db);
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
    public static Task VerifyProductionSchemaAsync(
        ApplicationDbContext db) =>
        PeopleAiSchema.VerifyAsync(db);
}
