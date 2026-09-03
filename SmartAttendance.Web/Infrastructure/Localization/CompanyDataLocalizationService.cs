using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Localization;

public sealed record CompanyLanguageOption(
    string CultureCode,
    string NativeName,
    string EnglishName,
    string Direction,
    bool IsDefault,
    bool IsRequired);

public sealed record LocalizedFieldValue(
    string CultureCode,
    string FieldName,
    string? Value);

public interface ICompanyDataLocalizationService
{
    Task<IReadOnlyList<CompanyLanguageOption>> GetLanguagesAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task SaveLanguagesAsync(
        int companyId,
        string defaultCultureCode,
        IReadOnlyCollection<string> activeCultureCodes,
        IReadOnlyCollection<string> requiredCultureCodes,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ValidateRequiredValuesAsync(
        int companyId,
        IReadOnlyCollection<string> fieldNames,
        IReadOnlyCollection<LocalizedFieldValue> values,
        CancellationToken cancellationToken = default);

    Task SaveValuesAsync(
        int companyId,
        string entityType,
        int entityId,
        IReadOnlyCollection<LocalizedFieldValue> values,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetValuesAsync(
        int companyId,
        string entityType,
        int entityId,
        string? cultureCode,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Tenant-safe store for dynamic business-data translations. UI labels are handled
/// by <see cref="ILocalizationDictionaryService"/> and never mixed with employee or
/// company data.
/// </summary>
public sealed class CompanyDataLocalizationService : ICompanyDataLocalizationService
{
    private const int MaxFieldsPerWrite = 100;
    private const int MaxValueLength = 4000;
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;
    private readonly ILocalizationDictionaryService _dictionary;

    public CompanyDataLocalizationService(
        ApplicationDbContext db,
        ICompanyScopeProvider companyScope,
        ILocalizationDictionaryService dictionary)
    {
        _db = db;
        _companyScope = companyScope;
        _dictionary = dictionary;
    }

    public async Task<IReadOnlyList<CompanyLanguageOption>> GetLanguagesAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAccessAsync(companyId, cancellationToken);
        return await _db.CompanyLanguages
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.IsActive && !item.IsDeleted)
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.EnglishName)
            .Select(item => new CompanyLanguageOption(
                item.CultureCode,
                item.NativeName,
                item.EnglishName,
                item.Direction,
                item.IsDefault,
                item.IsRequired))
            .ToListAsync(cancellationToken);
    }

    public async Task SaveLanguagesAsync(
        int companyId,
        string defaultCultureCode,
        IReadOnlyCollection<string> activeCultureCodes,
        IReadOnlyCollection<string> requiredCultureCodes,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAccessAsync(companyId, cancellationToken);
        var normalizedDefault = NormalizeCulture(defaultCultureCode);
        var normalizedActive = activeCultureCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(NormalizeCulture)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalizedRequired = requiredCultureCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(NormalizeCulture)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selectionError = CompanyLanguagePolicy.ValidateSelection(
            normalizedDefault,
            normalizedActive);
        if (selectionError is not null)
            throw new InvalidOperationException(selectionError);

        // اللغة الأساسية لا يمكن أن تكون اختيارية.
        if (!normalizedRequired.Contains(
                normalizedDefault,
                StringComparer.OrdinalIgnoreCase))
        {
            normalizedRequired = normalizedRequired
                .Append(normalizedDefault)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var requiredOutsideActive = normalizedRequired.FirstOrDefault(
            code => !normalizedActive.Contains(
                code,
                StringComparer.OrdinalIgnoreCase));

        if (requiredOutsideActive is not null)
        {
            throw new InvalidOperationException(
                $"اللغة المطلوبة {requiredOutsideActive} يجب أن تكون مفعلة أولاً.");
        }

        var catalogLanguages = await _dictionary.GetLanguagesAsync(cancellationToken);
        var catalogByCode = catalogLanguages.ToDictionary(
            item => NormalizeCulture(item.Code),
            StringComparer.OrdinalIgnoreCase);
        var unknown = normalizedActive.FirstOrDefault(code => !catalogByCode.ContainsKey(code));
        if (unknown is not null)
            throw new InvalidOperationException($"اللغة {unknown} غير موجودة في قاموس النظام. أضفها إلى القاموس أولاً.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await _db.CompanyLanguages
            .Where(item => item.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        // Clear the former default first. SQL Server validates the filtered unique
        // index per statement, so switching defaults must be an explicit two-phase
        // operation inside the same transaction.
        foreach (var item in existing.Where(item => item.IsDefault))
            item.IsDefault = false;
        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync(cancellationToken);

        foreach (var item in existing)
        {
            item.IsActive = normalizedActive.Contains(
                item.CultureCode,
                StringComparer.OrdinalIgnoreCase);

            item.IsDefault = item.IsActive &&
                string.Equals(
                    item.CultureCode,
                    normalizedDefault,
                    StringComparison.OrdinalIgnoreCase);

            item.IsRequired = item.IsActive &&
                normalizedRequired.Contains(
                    item.CultureCode,
                    StringComparer.OrdinalIgnoreCase);
            item.IsDeleted = false;
            item.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var cultureCode in normalizedActive)
        {
            var language = catalogByCode[cultureCode];
            var item = existing.FirstOrDefault(existingLanguage =>
                string.Equals(existingLanguage.CultureCode, cultureCode, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                item = new CompanyLanguage
                {
                    CompanyId = companyId,
                    CultureCode = cultureCode,
                    NativeName = language.NativeName,
                    EnglishName = language.EnglishName,
                    Direction = language.Direction,
                    IsActive = true,
                    IsRequired = normalizedRequired.Contains(
                        cultureCode,
                        StringComparer.OrdinalIgnoreCase),
                    IsDefault = string.Equals(
                        cultureCode,
                        normalizedDefault,
                        StringComparison.OrdinalIgnoreCase)
                };
                _db.CompanyLanguages.Add(item);
            }
            else
            {
                item.NativeName = language.NativeName;
                item.EnglishName = language.EnglishName;
                item.Direction = language.Direction;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ValidateRequiredValuesAsync(
        int companyId,
        IReadOnlyCollection<string> fieldNames,
        IReadOnlyCollection<LocalizedFieldValue> values,
        CancellationToken cancellationToken = default)
    {
        var languages = await GetLanguagesAsync(companyId, cancellationToken);
        if (languages.Count == 0)
            return ["يجب تفعيل لغة أساسية واحدة على الأقل من إعدادات لغات بيانات الشركة."];

        var normalizedFields = NormalizeFields(fieldNames);
        var normalizedValues = NormalizeValues(values);
        return CompanyLanguagePolicy.MissingRequiredValues(languages, normalizedFields, normalizedValues);
    }

    public async Task SaveValuesAsync(
        int companyId,
        string entityType,
        int entityId,
        IReadOnlyCollection<LocalizedFieldValue> values,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAccessAsync(companyId, cancellationToken);
        var normalizedEntityType = NormalizeToken(entityType, 80, "نوع الكيان");
        if (entityId <= 0) throw new InvalidOperationException("معرف الكيان غير صحيح.");
        if (values.Count > MaxFieldsPerWrite * 20)
            throw new InvalidOperationException("عدد قيم الترجمة في العملية الواحدة تجاوز الحد المسموح.");

        var languages = await GetLanguagesAsync(companyId, cancellationToken);
        var allowedCultures = languages.Select(item => item.CultureCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = NormalizeValues(values);
        var invalidCulture = normalized.Keys.FirstOrDefault(key => !allowedCultures.Contains(key.CultureCode));
        if (invalidCulture != default)
            throw new InvalidOperationException($"اللغة {invalidCulture.CultureCode} غير مفعلة لهذه الشركة.");

        var fields = normalized.Keys.Select(key => key.FieldName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existing = await _db.LocalizedEntityValues
            .Where(item => item.CompanyId == companyId &&
                           item.EntityType == normalizedEntityType &&
                           item.EntityId == entityId &&
                           fields.Contains(item.FieldName))
            .ToListAsync(cancellationToken);

        foreach (var pair in normalized)
        {
            var row = existing.FirstOrDefault(item =>
                string.Equals(item.CultureCode, pair.Key.CultureCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.FieldName, pair.Key.FieldName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                if (row is not null) _db.LocalizedEntityValues.Remove(row);
                continue;
            }

            if (row is null)
            {
                _db.LocalizedEntityValues.Add(new LocalizedEntityValue
                {
                    CompanyId = companyId,
                    EntityType = normalizedEntityType,
                    EntityId = entityId,
                    FieldName = pair.Key.FieldName,
                    CultureCode = pair.Key.CultureCode,
                    Value = pair.Value,
                    TranslationStatus = "Manual"
                });
            }
            else
            {
                row.Value = pair.Value;
                row.TranslationStatus = "Manual";
                row.IsDeleted = false;
                row.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetValuesAsync(
        int companyId,
        string entityType,
        int entityId,
        string? cultureCode,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAccessAsync(companyId, cancellationToken);
        var languages = await GetLanguagesAsync(companyId, cancellationToken);
        var requested = NormalizeCulture(cultureCode ?? CultureInfo.CurrentUICulture.Name);
        var fallback = languages.FirstOrDefault(item => item.IsDefault)?.CultureCode;
        var candidates = new[] { requested, fallback }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rows = await _db.LocalizedEntityValues
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId &&
                           item.EntityType == entityType &&
                           item.EntityId == entityId &&
                           candidates.Contains(item.CultureCode) &&
                           !item.IsDeleted)
            .ToListAsync(cancellationToken);

        return rows.GroupBy(item => item.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.FirstOrDefault(item =>
                             string.Equals(item.CultureCode, requested, StringComparison.OrdinalIgnoreCase))?.Value
                         ?? group.FirstOrDefault(item =>
                             string.Equals(item.CultureCode, fallback, StringComparison.OrdinalIgnoreCase))?.Value
                         ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task EnsureCompanyAccessAsync(int companyId, CancellationToken cancellationToken)
    {
        if (companyId <= 0) throw new InvalidOperationException("الشركة غير محددة.");
        var scope = await _companyScope.GetAsync(cancellationToken);
        if (!scope.Allows(companyId)) throw new UnauthorizedAccessException("الشركة خارج نطاق المستخدم.");
        if (!await _db.Companies.AsNoTracking().AnyAsync(
                item => item.Id == companyId && !item.IsDeleted,
                cancellationToken))
            throw new InvalidOperationException("الشركة غير موجودة.");
    }

    private static string NormalizeCulture(string cultureCode)
    {
        try { return CultureInfo.GetCultureInfo(cultureCode.Trim()).Name; }
        catch (CultureNotFoundException) { throw new InvalidOperationException("رمز اللغة غير صحيح."); }
    }

    private static string[] NormalizeFields(IEnumerable<string> fields) => fields
        .Select(field => NormalizeToken(field, 80, "اسم الحقل"))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxFieldsPerWrite + 1)
        .ToArray();

    private static Dictionary<(string CultureCode, string FieldName), string> NormalizeValues(
        IEnumerable<LocalizedFieldValue> values)
    {
        var result = new Dictionary<(string CultureCode, string FieldName), string>();
        foreach (var value in values)
        {
            var key = (
                NormalizeCulture(value.CultureCode),
                NormalizeToken(value.FieldName, 80, "اسم الحقل"));
            if (!result.TryAdd(key, (value.Value ?? string.Empty).Trim()))
                throw new InvalidOperationException($"القيمة مكررة للغة {key.Item1} والحقل {key.Item2}.");
            if (result[key].Length > MaxValueLength)
                throw new InvalidOperationException($"قيمة الحقل {key.Item2} تجاوزت {MaxValueLength} حرف.");
        }
        return result;
    }

    private static string NormalizeToken(string value, int maximumLength, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength ||
            normalized.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '-')))
            throw new InvalidOperationException($"{label} غير صحيح.");
        return normalized;
    }
}
