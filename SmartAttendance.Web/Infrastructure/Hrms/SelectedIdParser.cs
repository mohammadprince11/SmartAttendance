namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// Parses checkbox/form identifiers without throwing on empty or malformed values.
/// Route and form identifiers remain untrusted; callers must still enforce ownership
/// and company/employee scope when using the returned identifiers.
/// </summary>
public static class SelectedIdParser
{
    public static List<int> Parse(IEnumerable<string?> values) => values
        .Select(value => int.TryParse(value, out var id) && id > 0 ? id : (int?)null)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .Distinct()
        .ToList();
}
