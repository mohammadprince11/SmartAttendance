using System.Text.Json;
using System.Text.RegularExpressions;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>Company-specific configuration stored in the existing settings table. No request-time DDL.</summary>
public sealed record NotificationRuleSettings
{
    public bool IsEnabled { get; init; }
    public string Audience { get; init; } = NotificationRoutingPolicy.Supervisors;
    public int DaysBefore { get; init; }
    public string SupervisorName { get; init; } = "";
    public int[] GroupMembers { get; init; } = [];
    public bool InApp { get; init; } = true;
    public bool Email { get; init; }
    public string TitleTemplate { get; init; } = "";
    public string BodyTemplate { get; init; } = "";
    public DateTime EnabledSinceUtc { get; init; }
    public string WelcomeBasis { get; init; } = "CreatedAt";

    public static string Key(int companyId, int ruleId) => HrSettingsStore.CompanyKey(companyId, $"Notifications.Rule.{ruleId}");
    public static string ReferenceName(string name) => name switch
    {
        "إنشاء صلاحية الوثيقة" => "انتهاء صلاحية الوثيقة",
        "رفض الموظفين" => "رضا الموظفين",
        _ => name
    };
    public static NotificationRuleSettings FromLegacy(NotificationRuleRow row) => new()
    {
        // Never activate satisfaction from the legacy, semantically different rejection flag.
        IsEnabled = row.Name is not ("رفض الموظفين" or "إنشاء صلاحية الوثيقة") && row.IsEnabled, Audience = row.Audience, DaysBefore = row.DaysBefore,
        SupervisorName = row.SupervisorName
    };

    public static async Task<Dictionary<int, NotificationRuleSettings>> LoadCompanyAsync(ApplicationDbContext db, int companyId)
    {
        var prefix = HrSettingsStore.CompanyKey(companyId, "Notifications.Rule.");
        var rows = await HrmsDatabase.QueryAsync(db,
            "SELECT SettingKey, SettingValue FROM ZynoraHrSettings WHERE SettingKey LIKE @Prefix;",
            command => HrmsDatabase.AddParameter(command, "@Prefix", prefix + "%"),
            reader => (Key: HrmsDatabase.GetString(reader, "SettingKey"), Json: HrmsDatabase.GetString(reader, "SettingValue")));
        var result = new Dictionary<int, NotificationRuleSettings>();
        foreach (var row in rows)
        {
            if (!int.TryParse(row.Key[prefix.Length..], out var id)) continue;
            try
            {
                var settings = JsonSerializer.Deserialize<NotificationRuleSettings>(row.Json);
                result[id] = settings is not null && Valid(settings) ? settings : new();
            }
            catch (JsonException) { result[id] = new(); } // invalid configuration is disabled, never broadcast
        }
        return result;
    }

    // The caller holds the generator distributed lock when updating a card; saves and generation cannot race.
    public static Task SaveAsync(ApplicationDbContext db, int companyId, int ruleId, NotificationRuleSettings settings) =>
        HrSettingsStore.SetAsync(db, Key(companyId, ruleId), JsonSerializer.Serialize(settings));

    public static bool Valid(NotificationRuleSettings settings) =>
        settings.GroupMembers is not null && settings.SupervisorName is not null && settings.TitleTemplate is not null && settings.BodyTemplate is not null
        && NotificationRoutingPolicy.ValidAudience(settings.Audience) && settings.DaysBefore is >= 0 and <= 366
        && settings.SupervisorName.Length <= 150 && settings.GroupMembers.Length <= 200
        && settings.GroupMembers.All(id => id > 0) && settings.GroupMembers.Distinct().Count() == settings.GroupMembers.Length
        && (settings.Audience is not (NotificationRoutingPolicy.Group or NotificationRoutingPolicy.Specific) || settings.GroupMembers.Length > 0)
        && NotificationRuleCatalog.WelcomeBases.Any(b => b.Value == settings.WelcomeBasis)
        && (settings.InApp || settings.Email) && !settings.TitleTemplate.Contains('\n') && !settings.TitleTemplate.Contains('\r')
        && NotificationTemplate.Valid(settings.TitleTemplate, 200)
        && NotificationTemplate.Valid(settings.BodyTemplate, 4000);
}

/// <summary>Plain-text templates: allowlisted variables only, no HTML execution or sensitive HR fields.</summary>
public static class NotificationTemplate
{
    public static IReadOnlyList<string> Variables { get; } = ["EmployeeName", "RuleName", "Date", "Message"];
    private static readonly Regex Token = new(@"\{\{([^{}]+)\}\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static bool Valid(string? value, int maximum)
    {
        if (value is null || value.Length > maximum) return false;
        var remaining = Token.Replace(value, match => Variables.Contains(match.Groups[1].Value) ? "" : "{");
        return !remaining.Contains('{') && !remaining.Contains('}');
    }
    public static string Render(string template, string fallback, string employeeName, string ruleName, DateOnly date, string message)
    {
        if (string.IsNullOrWhiteSpace(template)) return fallback;
        if (!Valid(template, 4000)) throw new ArgumentException("Invalid notification template.", nameof(template));
        return Token.Replace(template, match => match.Groups[1].Value switch
        {
            "EmployeeName" => employeeName, "RuleName" => ruleName,
            "Date" => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "Message" => message,
            _ => ""
        });
    }
}
