using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Payroll impact for an incomplete attendance day (DayAttendance.Status = Incomplete).
/// The configured percentage is applied to the employee's daily basic salary per incomplete day.
/// Zero disables the deduction.
/// </summary>
public static class MissingPunchPayrollPolicy
{
    public const string PercentKey = "Payroll.MissingPunchPenaltyPercentOfDailyBasic";

    public static async Task<decimal> LoadPercentAsync(ApplicationDbContext db, int? companyId)
    {
        var raw = companyId is > 0
            ? await HrSettingsStore.GetCompanyAsync(db, companyId.Value, PercentKey, "0")
            : await HrSettingsStore.GetAsync(db, PercentKey, "0");
        return ParsePercent(raw);
    }

    public static decimal ParsePercent(string? raw) =>
        decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? NormalizePercent(value)
            : 0m;
    public static decimal NormalizePercent(decimal value) => Math.Clamp(value, 0m, 100m);

    public static async Task SavePercentAsync(ApplicationDbContext db, int companyId, decimal percent)
    {
        var normalized = NormalizePercent(percent);
        await HrSettingsStore.SetCompanyAsync(db, companyId, PercentKey,
            normalized.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static decimal Calculate(decimal dailyBasic, int incompleteDays, decimal percent)
    {
        if (dailyBasic <= 0m || incompleteDays <= 0 || percent <= 0m) return 0m;
        return Math.Round(dailyBasic * incompleteDays * NormalizePercent(percent) / 100m, 2);
    }
}
