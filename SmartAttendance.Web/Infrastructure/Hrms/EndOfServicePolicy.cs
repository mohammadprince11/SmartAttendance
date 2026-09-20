using System.Globalization;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Company-scoped end-of-service calculation policy.
/// Auto calculation is intentionally disabled by default because eligibility depends on
/// the termination context. When enabled, the configured weeks-per-year rate is applied
/// to the configured monthly basis amount using a 30-day month.
/// </summary>
public static class EndOfServicePolicy
{
    public const string AutoEnabledKey = "Payroll.EndOfService.AutoCalculationEnabled";
    public const string WeeksPerYearKey = "Payroll.EndOfService.WeeksPerYear";

    public sealed record Policy(bool AutoCalculationEnabled, decimal WeeksPerYear)
    {
        public static Policy Default { get; } = new(false, 2m);

        public Policy Normalized() => this with
        {
            WeeksPerYear = Math.Clamp(WeeksPerYear, 0m, 52m)
        };
    }

    public static async Task<Policy> LoadAsync(ApplicationDbContext db, int companyId)
    {
        if (companyId <= 0) return Policy.Default;

        var enabled = await HrSettingsStore.GetCompanyAsync(
            db, companyId, AutoEnabledKey, "False");
        var weeksRaw = await HrSettingsStore.GetCompanyAsync(
            db, companyId, WeeksPerYearKey, "2");

        if (!decimal.TryParse(
                weeksRaw,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var weeks))
            weeks = 2m;

        return new Policy(
            bool.TryParse(enabled, out var on) && on,
            weeks).Normalized();
    }

    public static decimal Compute(
        decimal yearsOfService,
        decimal monthlyBasis,
        decimal weeksPerYear,
        decimal multiplier = 1m)
    {
        if (yearsOfService <= 0m || monthlyBasis <= 0m || weeksPerYear <= 0m || multiplier <= 0m)
            return 0m;

        var dailyBasis = monthlyBasis / 30m;
        var daysPerYear = weeksPerYear * 7m;
        return decimal.Round(
            yearsOfService * daysPerYear * dailyBasis * multiplier,
            2,
            MidpointRounding.AwayFromZero);
    }
}
