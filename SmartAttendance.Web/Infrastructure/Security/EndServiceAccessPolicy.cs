namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// Approved timing contract, independent of employee/payroll state and delivery success.
/// Persistence and all authentication adapters must use this contract before enabling scheduling.
/// </summary>
public static class EndServiceAccessPolicy
{
    public static bool IsFarewellOnlyRoute(string path, string method) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(path, EndServiceAccessStore.FarewellPath, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(path, "/Account/Logout", StringComparison.OrdinalIgnoreCase));

    public sealed record Timing(DateTimeOffset NotificationEligibleAtUtc, DateTimeOffset AccessEndsAtUtc,
        bool CanNotifyInPortal, bool Immediate);

    private static TimeZoneInfo BaghdadZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Arabic Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Baghdad"); }
    }

    public static Timing Plan(DateOnly lastWorkingDate, bool immediate, DateTimeOffset now)
    {
        if (lastWorkingDate == DateOnly.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(lastWorkingDate), "A next-day cutoff must be representable.");
        var utcNow = now.ToUniversalTime();
        DateTimeOffset Midnight(DateOnly date) => new(TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), BaghdadZone()), TimeSpan.Zero);
        var start = Midnight(lastWorkingDate);
        var cutoff = Midnight(lastWorkingDate.AddDays(1));
        // Past dates never grant a fresh day of access. Immediate closure never waits for a message.
        var accessEnds = immediate || cutoff <= utcNow ? utcNow : cutoff;
        var notifyAt = immediate ? utcNow : start > utcNow ? start : utcNow;
        return new Timing(notifyAt, accessEnds, !immediate && notifyAt < accessEnds, immediate);
    }

    // Evaluate the absolute deadline on every request, even when the record is cached.
    // Equality is denied: at 00:00 of the next Baghdad day there is no remaining grace.
    public static bool AllowsAccess(bool accountActive, DateTimeOffset? accessEndsAtUtc, DateTimeOffset now) =>
        accountActive && (!accessEndsAtUtc.HasValue || now.ToUniversalTime() < accessEndsAtUtc.Value.ToUniversalTime());

    public static bool CanNotifyInPortal(Timing timing, DateTimeOffset now) =>
        timing.CanNotifyInPortal && now >= timing.NotificationEligibleAtUtc && now < timing.AccessEndsAtUtc;
}
