namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class AttendanceLateAllowancePolicy
{
    public readonly record struct LateDay(DateOnly Date, int LateMinutes);

    public readonly record struct Evaluation(
        DateOnly Date,
        int LateMinutes,
        int CoveredMinutes,
        int ExcessMinutes,
        int AllowanceRemaining,
        bool IsViolationDay);

    public static IReadOnlyList<Evaluation> Evaluate(
        IEnumerable<LateDay> days,
        int allowanceMinutes)
    {
        ArgumentNullException.ThrowIfNull(days);

        var remaining = Math.Max(0, allowanceMinutes);
        var orderedDays = days
            .Where(day => day.LateMinutes > 0)
            .GroupBy(day => day.Date)
            .Select(group => new LateDay(
                group.Key,
                group.Sum(day => day.LateMinutes)))
            .OrderBy(day => day.Date)
            .ToList();
        var result = new List<Evaluation>(orderedDays.Count);

        foreach (var day in orderedDays)
        {
            var covered = Math.Min(remaining, day.LateMinutes);
            var excess = Math.Max(0, day.LateMinutes - covered);
            remaining -= covered;

            result.Add(new Evaluation(
                day.Date,
                day.LateMinutes,
                covered,
                excess,
                remaining,
                excess > 0));
        }

        return result;
    }
}
