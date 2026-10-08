using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Ui;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>Reference-backed choices; only the selected legacy value is retained.</summary>
public static class HrLookupOptions
{
    public sealed record Option(string Value, string Label);

    public static async Task<List<Option>> NationalitiesAsync(ApplicationDbContext db, string? current = null) =>
        BuildNationalities(await HrLookups.LoadAsync(db, "nationalities", activeOnly: true), current);

    public static List<Option> BuildNationalities(IEnumerable<HrLookups.LookupItem> items, string? current = null)
    {
        var options = items.Where(x => x.IsActive).Select(x =>
        {
            var known = ZynoraEmployeeLookups.PrimaryNationalities.FirstOrDefault(n =>
                n.Label.Equals(x.ArabicName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                n.Value.Equals(x.ArabicName.Trim(), StringComparison.OrdinalIgnoreCase));
            // Preserve established English values; new custom nationalities use Arabic names.
            return new Option(known?.Value ?? x.ArabicName.Trim(), x.ArabicName.Trim());
        }).Where(x => x.Value.Length > 0).DistinctBy(x => x.Value, StringComparer.OrdinalIgnoreCase).ToList();
        return PreserveCurrent(options, current);
    }

    public static List<Option> PreserveCurrent(IEnumerable<Option> options, string? current)
    {
        var result = options.ToList();
        if (!string.IsNullOrWhiteSpace(current) && !result.Any(x => x.Value.Equals(current, StringComparison.OrdinalIgnoreCase)))
            result.Add(new Option(current, current));
        return result;
    }

    public static string? PreserveNationality(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return ZynoraEmployeeLookups.PrimaryNationalities.FirstOrDefault(x =>
            x.Value.Equals(trimmed, StringComparison.OrdinalIgnoreCase))?.Value ?? trimmed;
    }

    public static IReadOnlyDictionary<string, string> ConditionCategories { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["religion"] = "religions", ["nationality"] = "nationalities",
            ["contracttype"] = "contracttypes", ["worktype"] = "worktypes",
            ["jobgrade"] = "grades", ["sponsor"] = "sponsors"
        };

    public static List<Option> BuildConditionOptions(string category, IEnumerable<HrLookups.LookupItem> items)
    {
        var active = items.Where(x => x.IsActive).ToList();
        var options = active.Select(x => new Option(x.ArabicName, x.ArabicName)).ToList();
        if (category == "nationalities")
            options.AddRange(BuildNationalities(active).Where(x => !options.Any(o => o.Value == x.Value))
                .Select(x => new Option(x.Value, $"{x.Label} ({x.Value})")));
        return options.DistinctBy(x => x.Value, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
