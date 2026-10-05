namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>Pure calculations used by the company leave-policy engine.</summary>
public static class LeavePolicyMath
{
    public const string UnitDays = "Days";
    public const string UnitHours = "Hours";
    public const string AccrualFullUpfront = "FullUpfront";
    public const string AccrualMonthly = "Monthly";
    public const string AccrualDaily = "Daily";

    public static string NormalizeUnit(string? value) =>
        string.Equals(value?.Trim(), UnitHours, StringComparison.OrdinalIgnoreCase) ? UnitHours : UnitDays;

    public static string NormalizeAccrual(string? value) => value?.Trim() switch
    {
        AccrualMonthly => AccrualMonthly,
        AccrualDaily => AccrualDaily,
        _ => AccrualFullUpfront
    };

    public static decimal RequestAmount(
        DateOnly fromDate, DateOnly toDate, TimeSpan? startTime, TimeSpan? endTime,
        string? unit, decimal dailyHours, int targetYear)
    {
        dailyHours = dailyHours > 0m ? dailyHours : 8m;
        var normalizedUnit = NormalizeUnit(unit);

        if (startTime.HasValue && endTime.HasValue)
        {
            if (fromDate.Year != targetYear) return 0m;
            var duration = endTime.Value > startTime.Value
                ? endTime.Value - startTime.Value
                : TimeSpan.FromDays(1) - startTime.Value + endTime.Value;
            if (duration <= TimeSpan.Zero) return 0m;
            var hours = (decimal)duration.TotalHours;
            var amount = normalizedUnit == UnitHours ? hours : hours / dailyHours;
            return Math.Round(amount, 4, MidpointRounding.AwayFromZero);
        }

        var yearStart = new DateOnly(targetYear, 1, 1);
        var yearEnd = new DateOnly(targetYear, 12, 31);
        var start = fromDate > yearStart ? fromDate : yearStart;
        var end = toDate < yearEnd ? toDate : yearEnd;
        if (end < start) return 0m;
        var days = end.DayNumber - start.DayNumber + 1;
        return normalizedUnit == UnitHours ? days * dailyHours : days;
    }

    public static decimal AccruedEntitlement(
        decimal annualEntitlement, string? accrualMethod, bool prorateOnHire,
        DateOnly hireDate, DateOnly asOfDate)
    {
        if (annualEntitlement <= 0m) return 0m;
        var yearStart = new DateOnly(asOfDate.Year, 1, 1);
        var yearEnd = new DateOnly(asOfDate.Year, 12, 31);
        var effectiveStart = prorateOnHire && hireDate > yearStart ? hireDate : yearStart;
        if (effectiveStart > yearEnd || asOfDate < effectiveStart) return 0m;
        var cappedAsOf = asOfDate > yearEnd ? yearEnd : asOfDate;

        var method = NormalizeAccrual(accrualMethod);
        if (method == AccrualFullUpfront)
        {
            if (!prorateOnHire || effectiveStart == yearStart) return annualEntitlement;
            var eligibleDays = yearEnd.DayNumber - effectiveStart.DayNumber + 1;
            var daysInYear = yearEnd.DayNumber - yearStart.DayNumber + 1;
            return Math.Round(
                annualEntitlement * eligibleDays / daysInYear,
                4, MidpointRounding.AwayFromZero);
        }

        if (method == AccrualMonthly)
        {
            var firstMonth = effectiveStart.Month;
            var lastMonth = cappedAsOf.Month;
            if (lastMonth < firstMonth) return 0m;
            var months = lastMonth - firstMonth + 1;
            var accrued = Math.Round(
                annualEntitlement * months / 12m,
                4, MidpointRounding.AwayFromZero);
            return Math.Min(annualEntitlement, accrued);
        }

        var accruedDays = cappedAsOf.DayNumber - effectiveStart.DayNumber + 1;
        var denominator = yearEnd.DayNumber - yearStart.DayNumber + 1;
        var dailyAccrued = Math.Round(
            annualEntitlement * accruedDays / denominator,
            4, MidpointRounding.AwayFromZero);
        return Math.Min(annualEntitlement, dailyAccrued);
    }
}
