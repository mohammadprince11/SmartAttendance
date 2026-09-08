using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Localization;

public sealed record EmployeeBusinessDisplay(
    int EmployeeId,
    string FullName,
    string BranchName,
    string DepartmentName,
    string? Position);

/// <summary>
/// يختار بيانات الشركة المترجمة حسب لغة واجهة المستخدم الحالية.
/// هذا يخص بيانات الأعمال فقط (اسم الموظف/الشركة/الموقع/القسم/المنصب)،
/// وليس قاموس نصوص الواجهة.
/// </summary>
public static class EmployeeBusinessDataDisplayLocalizer
{
    public static async Task LocalizeCompaniesAsync(
        ApplicationDbContext db,
        IEnumerable<CompanyListViewModel> companies,
        CancellationToken cancellationToken = default)
    {
        var items = companies.ToList();
        if (items.Count == 0) return;

        var names = await ResolveNamesAsync(
            db,
            "Company",
            items.Select(item => new DisplayNameTarget(
                item.Id,
                item.Id,
                item.Name)),
            cancellationToken);

        foreach (var item in items)
        {
            if (names.TryGetValue((item.Id, item.Id), out var name))
            {
                item.Name = name;
            }
        }
    }

    public static async Task<IReadOnlyDictionary<int, string>> GetCompanyNamesAsync(
        ApplicationDbContext db,
        IEnumerable<(int Id, string Fallback)> companies,
        CancellationToken cancellationToken = default)
    {
        var items = companies
            .Select(item => new DisplayNameTarget(
                item.Id,
                item.Id,
                item.Fallback))
            .ToList();

        var names = await ResolveNamesAsync(
            db,
            "Company",
            items,
            cancellationToken);

        return items.ToDictionary(
            item => item.EntityId,
            item => names.TryGetValue(
                    (item.CompanyId, item.EntityId),
                    out var value)
                ? value
                : item.Fallback);
    }

    public static async Task<IReadOnlyDictionary<int, EmployeeBusinessDisplay>>
        GetEmployeeBusinessDataAsync(
            ApplicationDbContext db,
            IEnumerable<int> employeeIds,
            CancellationToken cancellationToken = default)
    {
        var ids = employeeIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<int, EmployeeBusinessDisplay>();
        }

        var items = await db.Employees
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new EmployeeListViewModel
            {
                Id = item.Id,
                EmployeeNo = item.EmployeeNo,
                FullName = item.FullName,
                CompanyId = item.CompanyId ?? item.Branch.CompanyId,
                BranchId = item.BranchId,
                DepartmentId = item.DepartmentId,
                PositionId = item.PositionId,
                Position = item.Position,
                BranchName = item.Branch.Name,
                DepartmentName = item.Department.Name
            })
            .ToListAsync(cancellationToken);

        await LocalizeEmployeeListAsync(
            db,
            items,
            cancellationToken);

        return items.ToDictionary(
            item => item.Id,
            item => new EmployeeBusinessDisplay(
                item.Id,
                item.FullName,
                item.BranchName,
                item.DepartmentName,
                item.Position));
    }

    public static async Task<
        IReadOnlyDictionary<(int CompanyId, int EntityId), string>>
        GetEntityNamesAsync(
            ApplicationDbContext db,
            string entityType,
            IEnumerable<(
                int CompanyId,
                int EntityId,
                string Fallback)> source,
            CancellationToken cancellationToken = default)
    {
        var items = source
            .Where(item =>
                item.CompanyId > 0 &&
                item.EntityId > 0)
            .Select(item => new DisplayNameTarget(
                item.CompanyId,
                item.EntityId,
                item.Fallback))
            .ToList();

        return await ResolveNamesAsync(
            db,
            entityType,
            items,
            cancellationToken);
    }
    public static async Task LocalizeBranchesAsync(
        ApplicationDbContext db,
        IEnumerable<BranchListViewModel> branches,
        CancellationToken cancellationToken = default)
    {
        var items = branches.ToList();
        if (items.Count == 0) return;

        var names = await ResolveNamesAsync(
            db,
            "Branch",
            items.Select(item => new DisplayNameTarget(
                item.CompanyId,
                item.Id,
                item.Name)),
            cancellationToken);

        foreach (var item in items)
        {
            if (names.TryGetValue((item.CompanyId, item.Id), out var name))
            {
                item.Name = name;
            }
        }
    }

    public static async Task LocalizeDepartmentsAsync(
        ApplicationDbContext db,
        IEnumerable<DepartmentListViewModel> departments,
        CancellationToken cancellationToken = default)
    {
        var items = departments.ToList();
        if (items.Count == 0) return;

        var names = await ResolveNamesAsync(
            db,
            "Department",
            items.Select(item => new DisplayNameTarget(
                item.CompanyId,
                item.Id,
                item.Name)),
            cancellationToken);

        foreach (var item in items)
        {
            if (names.TryGetValue((item.CompanyId, item.Id), out var name))
            {
                item.Name = name;
            }
        }
    }

    public static async Task LocalizePositionsAsync(
        ApplicationDbContext db,
        IEnumerable<PositionOptionViewModel> positions,
        CancellationToken cancellationToken = default)
    {
        var items = positions.ToList();
        if (items.Count == 0) return;

        var names = await ResolveNamesAsync(
            db,
            "Position",
            items.Select(item => new DisplayNameTarget(
                item.CompanyId,
                item.Id,
                item.Name)),
            cancellationToken);

        foreach (var item in items)
        {
            if (names.TryGetValue((item.CompanyId, item.Id), out var name))
            {
                item.Name = name;
            }
        }
    }

    public static async Task LocalizeEmployeeListAsync(
        ApplicationDbContext db,
        IEnumerable<EmployeeListViewModel> employees,
        CancellationToken cancellationToken = default)
    {
        var items = employees.ToList();
        if (items.Count == 0) return;

        var requestedCulture = CultureInfo.CurrentUICulture.Name;

        var companyIds = items
            .Select(item => item.CompanyId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var languageRows = await db.CompanyLanguages
            .AsNoTracking()
            .Where(item =>
                companyIds.Contains(item.CompanyId) &&
                item.IsActive &&
                !item.IsDeleted)
            .Select(item => new
            {
                item.CompanyId,
                item.CultureCode,
                item.IsDefault
            })
            .ToListAsync(cancellationToken);

        var selectedCultures = ResolveCultures(
            companyIds,
            languageRows.Select(item => new LanguageRow(
                item.CompanyId,
                NormalizeCulture(item.CultureCode),
                item.IsDefault)),
            requestedCulture);

        var candidateCultures = selectedCultures.Values
            .SelectMany(value => new[]
            {
                value.PreferredCulture,
                value.DefaultCulture
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var employeeIds = items.Select(item => item.Id).Distinct().ToArray();
        var branchIds = items.Select(item => item.BranchId).Distinct().ToArray();
        var departmentIds = items.Select(item => item.DepartmentId).Distinct().ToArray();
        var positionIds = items
            .Where(item => item.PositionId.HasValue)
            .Select(item => item.PositionId!.Value)
            .Distinct()
            .ToArray();

        var rows = await db.LocalizedEntityValues
            .AsNoTracking()
            .Where(item =>
                companyIds.Contains(item.CompanyId) &&
                candidateCultures.Contains(item.CultureCode) &&
                !item.IsDeleted &&
                (
                    (item.EntityType == "Employee" &&
                     employeeIds.Contains(item.EntityId)) ||
                    (item.EntityType == "Branch" &&
                     branchIds.Contains(item.EntityId)) ||
                    (item.EntityType == "Department" &&
                     departmentIds.Contains(item.EntityId)) ||
                    (item.EntityType == "Position" &&
                     positionIds.Contains(item.EntityId))
                ))
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            var fullName = Pick(
                rows,
                selectedCultures,
                item.CompanyId,
                "Employee",
                item.Id,
                "FullName");

            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName = ComposeName(
                    Pick(rows, selectedCultures, item.CompanyId, "Employee", item.Id, "FirstName"),
                    Pick(rows, selectedCultures, item.CompanyId, "Employee", item.Id, "SecondName"),
                    Pick(rows, selectedCultures, item.CompanyId, "Employee", item.Id, "ThirdName"),
                    Pick(rows, selectedCultures, item.CompanyId, "Employee", item.Id, "LastName"));
            }

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                item.FullName = fullName;
            }

            item.BranchName =
                Pick(
                    rows,
                    selectedCultures,
                    item.CompanyId,
                    "Branch",
                    item.BranchId,
                    "Name")
                ?? item.BranchName;

            item.DepartmentName =
                Pick(
                    rows,
                    selectedCultures,
                    item.CompanyId,
                    "Department",
                    item.DepartmentId,
                    "Name")
                ?? item.DepartmentName;

            if (item.PositionId.HasValue)
            {
                item.Position =
                    Pick(
                        rows,
                        selectedCultures,
                        item.CompanyId,
                        "Position",
                        item.PositionId.Value,
                        "Name")
                    ?? item.Position;
            }
        }
    }

    private static async Task<Dictionary<(int CompanyId, int EntityId), string>>
        ResolveNamesAsync(
            ApplicationDbContext db,
            string entityType,
            IEnumerable<DisplayNameTarget> source,
            CancellationToken cancellationToken)
    {
        var items = source.ToList();

        if (items.Count == 0)
        {
            return new Dictionary<(int CompanyId, int EntityId), string>();
        }

        var companyIds = items
            .Select(item => item.CompanyId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var languageRows = await db.CompanyLanguages
            .AsNoTracking()
            .Where(item =>
                companyIds.Contains(item.CompanyId) &&
                item.IsActive &&
                !item.IsDeleted)
            .Select(item => new
            {
                item.CompanyId,
                item.CultureCode,
                item.IsDefault
            })
            .ToListAsync(cancellationToken);

        var selectedCultures = ResolveCultures(
            companyIds,
            languageRows.Select(item => new LanguageRow(
                item.CompanyId,
                NormalizeCulture(item.CultureCode),
                item.IsDefault)),
            CultureInfo.CurrentUICulture.Name);

        var candidateCultures = selectedCultures.Values
            .SelectMany(value => new[]
            {
                value.PreferredCulture,
                value.DefaultCulture
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var entityIds = items
            .Select(item => item.EntityId)
            .Distinct()
            .ToArray();

        var rows = await db.LocalizedEntityValues
            .AsNoTracking()
            .Where(item =>
                companyIds.Contains(item.CompanyId) &&
                item.EntityType == entityType &&
                entityIds.Contains(item.EntityId) &&
                item.FieldName == "Name" &&
                candidateCultures.Contains(item.CultureCode) &&
                !item.IsDeleted)
            .ToListAsync(cancellationToken);

        var result =
            new Dictionary<(int CompanyId, int EntityId), string>();

        foreach (var item in items)
        {
            var value = Pick(
                rows,
                selectedCultures,
                item.CompanyId,
                entityType,
                item.EntityId,
                "Name");

            result[(item.CompanyId, item.EntityId)] =
                string.IsNullOrWhiteSpace(value)
                    ? item.Fallback
                    : value;
        }

        return result;
    }

    private static Dictionary<int, CultureSelection> ResolveCultures(
        IReadOnlyCollection<int> companyIds,
        IEnumerable<LanguageRow> languageSource,
        string? requestedCulture)
    {
        var languages = languageSource.ToList();
        var requested = NormalizeCulture(requestedCulture);
        var requestedLanguage = GetLanguage(requested);

        var result = new Dictionary<int, CultureSelection>();

        foreach (var companyId in companyIds)
        {
            var companyLanguages = languages
                .Where(item => item.CompanyId == companyId)
                .ToList();

            var fallback = companyLanguages
                .FirstOrDefault(item => item.IsDefault)
                ?.CultureCode;

            var exact = companyLanguages
                .FirstOrDefault(item =>
                    item.CultureCode.Equals(
                        requested,
                        StringComparison.OrdinalIgnoreCase))
                ?.CultureCode;

            var family = companyLanguages
                .FirstOrDefault(item =>
                    GetLanguage(item.CultureCode).Equals(
                        requestedLanguage,
                        StringComparison.OrdinalIgnoreCase))
                ?.CultureCode;

            result[companyId] = new CultureSelection(
                exact ?? family ?? fallback,
                fallback);
        }

        return result;
    }

    private static string? Pick(
        IReadOnlyList<SmartAttendance.Domain.Entities.LocalizedEntityValue> rows,
        IReadOnlyDictionary<int, CultureSelection> cultures,
        int companyId,
        string entityType,
        int entityId,
        string fieldName)
    {
        if (!cultures.TryGetValue(companyId, out var selection))
        {
            return null;
        }

        string? Find(string? culture) =>
            string.IsNullOrWhiteSpace(culture)
                ? null
                : rows.FirstOrDefault(item =>
                    item.CompanyId == companyId &&
                    item.EntityType == entityType &&
                    item.EntityId == entityId &&
                    item.FieldName.Equals(
                        fieldName,
                        StringComparison.OrdinalIgnoreCase) &&
                    item.CultureCode.Equals(
                        culture,
                        StringComparison.OrdinalIgnoreCase))
                    ?.Value;

        return Find(selection.PreferredCulture) ??
               Find(selection.DefaultCulture);
    }

    private static string NormalizeCulture(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
        {
            return string.Empty;
        }

        try
        {
            return CultureInfo.GetCultureInfo(cultureCode.Trim()).Name;
        }
        catch (CultureNotFoundException)
        {
            return cultureCode.Trim();
        }
    }

    private static string GetLanguage(string? cultureCode)
    {
        var normalized = NormalizeCulture(cultureCode);
        var separator = normalized.IndexOf('-');

        return separator > 0
            ? normalized[..separator]
            : normalized;
    }

    private static string? ComposeName(params string?[] parts)
    {
        var value = string.Join(
            " ",
            parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim()));

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private sealed record DisplayNameTarget(
        int CompanyId,
        int EntityId,
        string Fallback);

    private sealed record LanguageRow(
        int CompanyId,
        string CultureCode,
        bool IsDefault);

    private sealed record CultureSelection(
        string? PreferredCulture,
        string? DefaultCulture);
}