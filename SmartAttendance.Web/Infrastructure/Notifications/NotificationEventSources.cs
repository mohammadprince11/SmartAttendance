using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>Committed business rows only; never derive an event from a UI toggle.</summary>
public static class NotificationEventSources
{
    public sealed record Source(string Query, string BackOfficeUrl, bool AllowsDepartedSubject = false, bool CollectiveEvent = false);
    public sealed record Event(int Id, int EmployeeId, DateTime OccurredAt);

    public static Source? Describe(NotificationRuleGenerator.RuleKind kind, string welcomeBasis = "CreatedAt") => kind switch
    {
        NotificationRuleGenerator.RuleKind.Feedback => new(
            "SELECT Id, EmployeeId, CreatedAt AS OccurredAt FROM EmployeeFeedbackItems", "/Engagement"),
        NotificationRuleGenerator.RuleKind.FeedbackReply => new(
            "SELECT Id, EmployeeId, RepliedAt AS OccurredAt FROM EmployeeFeedbackItems WHERE RepliedAt IS NOT NULL AND NULLIF(LTRIM(RTRIM(AdminReply)), N'') IS NOT NULL", "/Engagement"),
        NotificationRuleGenerator.RuleKind.ContractRenewal => new(
            "SELECT Id, EmployeeId, CreatedAt AS OccurredAt FROM EmployeeContractMovements WHERE MovementKind = N'Renew'", "/Contracts/Movements"),
        NotificationRuleGenerator.RuleKind.ContractExtension => new(
            "SELECT Id, EmployeeId, CreatedAt AS OccurredAt FROM EmployeeContractMovements WHERE MovementKind = N'Extend'", "/Contracts/Movements"),
        NotificationRuleGenerator.RuleKind.Welcome when welcomeBasis == "ContractStart" => new(
            $"SELECT e.Id, e.Id AS EmployeeId, {BusinessDateUtc("contract.FromDate")} AS OccurredAt FROM Employees e CROSS APPLY (SELECT TOP (1) FromDate FROM EmployeeContracts c WHERE c.EmployeeId = e.Id AND c.IsDeleted = 0 AND c.IsCurrent = 1 ORDER BY c.FromDate DESC, c.Id DESC) contract WHERE e.IsDeleted = 0", "/Employees"),
        NotificationRuleGenerator.RuleKind.Welcome when welcomeBasis is "JoiningDate" or "HireDate" => new(
            $"SELECT Id, Id AS EmployeeId, {BusinessDateUtc(welcomeBasis)} AS OccurredAt FROM Employees WHERE IsDeleted = 0 AND {welcomeBasis} IS NOT NULL", "/Employees"),
        NotificationRuleGenerator.RuleKind.Welcome when welcomeBasis == "CreatedAt" => new(
            "SELECT Id, Id AS EmployeeId, CreatedAt AS OccurredAt FROM Employees WHERE IsDeleted = 0", "/Employees"),
        NotificationRuleGenerator.RuleKind.Rehire => new(
            // Legacy writers use GETDATE(); convert SQL server-local clock, not a guessed Baghdad offset.
            "SELECT Id, EmployeeId, DATEADD(MINUTE, -DATEPART(TZOFFSET, SYSDATETIMEOFFSET()), CreatedAt) AS OccurredAt FROM EmployeeRehires", "/Employees/Lifecycle"),
        NotificationRuleGenerator.RuleKind.Violation => new(
            "SELECT Id, EmployeeId, CreatedAt AS OccurredAt FROM EmployeeViolationCases WHERE IsDeleted = 0", "/Violations"),
        NotificationRuleGenerator.RuleKind.DisciplinaryAction => new(
            "SELECT Id, EmployeeId, COALESCE(ApprovedAt, LetterIssuedAt) AS OccurredAt FROM EmployeeViolationCases WHERE IsDeleted = 0 AND (ApprovedAt IS NOT NULL OR LetterIssuedAt IS NOT NULL)", "/Violations"),
        NotificationRuleGenerator.RuleKind.Satisfaction => new(
            "SELECT Id, EmployeeId, SubmittedAt AS OccurredAt FROM FormSubmissions WHERE FormType = N'Satisfaction' AND Status <> N'Cancelled'", "/Forms/Submissions"),
        NotificationRuleGenerator.RuleKind.ExitInterview => new(
            "SELECT Id, EmployeeId, SubmittedAt AS OccurredAt FROM FormSubmissions WHERE FormType = N'ExitInterview' AND Status <> N'Cancelled'", "/Forms/Submissions", true),
        NotificationRuleGenerator.RuleKind.Farewell => new("""
SELECT s.EndServiceId AS Id, s.EmployeeId, s.NotificationEligibleAtUtc AS OccurredAt
FROM EndServiceAccessSchedules s
JOIN Employees e ON e.Id = s.EmployeeId AND e.CompanyId = s.CompanyId
WHERE s.CompanyId = @CompanyId AND e.IsActive = 0 AND e.IsDeleted = 0 AND s.Immediate = 0
  AND s.AccessEndsAtUtc > @Now
  AND s.EndServiceId = (SELECT MAX(es.Id) FROM EmployeeEndServices es WHERE es.EmployeeId = e.Id)
""", "/Employees/Lifecycle", true),
        NotificationRuleGenerator.RuleKind.Poll => new("""
SELECT p.Id, e.Id AS EmployeeId, p.PublishDate AS OccurredAt
FROM EmployeePolls p
JOIN Employees e ON e.CompanyId = p.CompanyId
WHERE p.CompanyId = @CompanyId AND p.IsPublished = 1 AND (
    p.TargetType = N'All'
    OR (p.TargetType = N'Employee' AND EXISTS (
        SELECT 1 FROM STRING_SPLIT(p.TargetValue, N',') target WHERE TRY_CONVERT(int, target.value) = e.Id))
    OR (p.TargetType = N'Department' AND TRY_CONVERT(int, p.TargetValue) = e.DepartmentId)
    OR (p.TargetType = N'Branch' AND TRY_CONVERT(int, p.TargetValue) = e.BranchId))
""", "/Engagement", CollectiveEvent: true),
        _ => null
    };

    // Identifiers above are a closed catalog, never client SQL. Dates are Baghdad business dates;
    // their midnight is converted to UTC for comparison with activation and scan timestamps.
    private static string BusinessDateUtc(string column) =>
        $"CONVERT(datetime2, (CONVERT(datetime2, {column}) AT TIME ZONE 'Arabic Standard Time') AT TIME ZONE 'UTC')";

    // Identifiers come exclusively from the closed catalog above, never from requests.
    public static string ScopedQuery(Source source) => $"""
SELECT source.Id, source.EmployeeId, source.OccurredAt
FROM ({source.Query}) source
JOIN Employees employee ON employee.Id = source.EmployeeId
WHERE employee.CompanyId = @CompanyId AND employee.IsDeleted = 0 {(source.AllowsDepartedSubject ? "" : "AND employee.IsActive = 1")}
  AND source.OccurredAt >= @EnabledSince AND source.OccurredAt <= @Now
  AND NOT EXISTS (
      SELECT 1 FROM ZynoraNotificationEvents fired
      WHERE fired.EventKey = CONCAT(N'source:', @Kind, N':', @CompanyId, N':', source.Id, N':', {(source.CollectiveEvent ? "0" : "source.EmployeeId")}, N':',
          DATEDIFF_BIG(DAY, CONVERT(datetime2, '00010101', 112), source.OccurredAt) * CAST(864000000000 AS bigint)
          + DATEDIFF_BIG(NANOSECOND, CONVERT(date, source.OccurredAt), source.OccurredAt) / 100)
  );
""";

    public static string Key(NotificationRuleGenerator.RuleKind kind, int companyId, Event source) =>
        $"source:{kind}:{companyId}:{source.Id}:{(kind == NotificationRuleGenerator.RuleKind.Poll ? 0 : source.EmployeeId)}:{source.OccurredAt.Ticks}";

    public static string ActivationKey(int ruleId) => $"rule-activation:{ruleId}";

    public static async Task<DateTime> EnabledSinceAsync(ApplicationDbContext db, int ruleId)
    {
        // Existing enabled rules establish a baseline, rather than replay historical records.
        // This is data in an existing controlled-migration table, not runtime schema creation.
        return await HrmsDatabase.ScalarAsync<DateTime>(db, """
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Enabled bit;
SELECT @Enabled = IsEnabled FROM ZynoraNotificationRules WITH (UPDLOCK, HOLDLOCK) WHERE Id = @RuleId;
IF NOT EXISTS (SELECT 1 FROM ZynoraNotificationEvents WHERE EventKey = @Key)
    INSERT INTO ZynoraNotificationEvents(EventKey, RuleKind, SubjectEmployeeId)
    VALUES (@Key, N'RuleActivation', 0);
SELECT CreatedAt FROM ZynoraNotificationEvents WHERE EventKey = @Key;
COMMIT TRANSACTION;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Key", ActivationKey(ruleId));
            HrmsDatabase.AddParameter(command, "@RuleId", ruleId);
        });
    }
}
