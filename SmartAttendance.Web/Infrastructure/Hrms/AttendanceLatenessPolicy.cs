using System.Globalization;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class AttendanceLatenessPolicy
{
    public const string EnabledKey = "Attendance.LatenessAllowance.Enabled";
    public const string AllowanceMinutesKey = "Attendance.LatenessAllowance.Minutes";
    public const string ExceededAsAbsentKey = "Attendance.LatenessAllowance.ExceededAsAbsent";

    public sealed record Policy(bool Enabled, int AllowanceMinutes, bool ExceededAsAbsent)
    {
        public static Policy Default { get; } = new(false, 120, true);

        public Policy Normalized() => this with
        {
            AllowanceMinutes = Math.Clamp(AllowanceMinutes, 0, 10080)
        };
    }

    public static async Task<Policy> LoadAsync(ApplicationDbContext db, int? companyId)
    {
        Task<string> Get(string key, string fallback) => companyId is > 0
            ? HrSettingsStore.GetCompanyAsync(db, companyId.Value, key, fallback)
            : HrSettingsStore.GetAsync(db, key, fallback);

        var enabled = await Get(EnabledKey, "0");
        var allowanceRaw = await Get(AllowanceMinutesKey, "120");
        var asAbsent = await Get(ExceededAsAbsentKey, "1");

        if (!int.TryParse(allowanceRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var allowance))
            allowance = 120;

        return new Policy(enabled == "1", allowance, asAbsent != "0").Normalized();
    }

    public static async Task SaveAsync(ApplicationDbContext db, int companyId, Policy policy)
    {
        var normalized = policy.Normalized();
        await HrSettingsStore.SetCompanyAsync(db, companyId, EnabledKey, normalized.Enabled ? "1" : "0");
        await HrSettingsStore.SetCompanyAsync(db, companyId, AllowanceMinutesKey,
            normalized.AllowanceMinutes.ToString(CultureInfo.InvariantCulture));
        await HrSettingsStore.SetCompanyAsync(db, companyId, ExceededAsAbsentKey,
            normalized.ExceededAsAbsent ? "1" : "0");
    }

    public static IReadOnlyList<AttendanceLateAllowancePolicy.Evaluation> Evaluate(
        IEnumerable<AttendanceLateAllowancePolicy.LateDay> lateDays,
        Policy policy)
    {
        var normalized = policy.Normalized();
        return normalized.Enabled
            ? AttendanceLateAllowancePolicy.Evaluate(lateDays, normalized.AllowanceMinutes)
            : Array.Empty<AttendanceLateAllowancePolicy.Evaluation>();
    }
}
