namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class PollLifecycle
{
    public const int PrivacyMinimumResponses = 5;
    public static bool ValidDates(DateTime? start, DateTime? end) => !start.HasValue || !end.HasValue || end.Value.Date >= start.Value.Date;
    public static bool CanShowResults(bool confidential, int responses) => !confidential || responses >= PrivacyMinimumResponses;
    public static string Status(bool published, DateTime? start, DateTime? end, DateTime today)
        => !published ? "مسودة" : start?.Date > today.Date ? "مجدول" : end?.Date < today.Date ? "مغلق" : "نشط";
}
