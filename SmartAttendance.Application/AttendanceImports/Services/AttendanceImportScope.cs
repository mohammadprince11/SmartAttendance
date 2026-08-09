namespace SmartAttendance.Application.AttendanceImports.Services;

/// <summary>
/// Transport-neutral company authorization supplied to the attendance importer.
/// One import is always bound to exactly one company because employee numbers are
/// not globally unique.
/// </summary>
public sealed record AttendanceImportScope
{
    public bool IsUnrestricted { get; init; }

    public IReadOnlyCollection<int> AllowedCompanyIds { get; init; } = Array.Empty<int>();

    public int SelectedCompanyId { get; init; }

    public bool AllowsSelectedCompany() =>
        SelectedCompanyId > 0 &&
        (IsUnrestricted || AllowedCompanyIds.Contains(SelectedCompanyId));

    public static AttendanceImportScope Unrestricted(int selectedCompanyId) => new()
    {
        IsUnrestricted = true,
        SelectedCompanyId = selectedCompanyId
    };

    public static AttendanceImportScope Restricted(
        IEnumerable<int> allowedCompanyIds,
        int selectedCompanyId) => new()
    {
        AllowedCompanyIds = allowedCompanyIds.Where(id => id > 0).Distinct().ToArray(),
        SelectedCompanyId = selectedCompanyId
    };
}
