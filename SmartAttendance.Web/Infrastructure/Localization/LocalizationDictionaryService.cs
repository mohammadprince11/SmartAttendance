using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Localization;

public sealed record DictionaryLanguage(
    string Code,
    string NativeName,
    string EnglishName,
    string Direction,
    bool IsDefault = false);

public sealed record DictionaryEntryRow(
    string CultureCode,
    string NativeName,
    string EnglishName,
    string Direction,
    string Key,
    string Translation);

public sealed record DictionaryImportResult(
    string CultureCode,
    int Imported,
    int Empty,
    bool IsNewLanguage);

public interface ILocalizationDictionaryService
{
    Task<IReadOnlyList<DictionaryLanguage>> GetLanguagesAsync(CancellationToken cancellationToken = default);
    Task<DictionaryLanguage?> FindLanguageAsync(string? code, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetCatalogAsync(string? culture, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DictionaryEntryRow>> GetRowsAsync(CancellationToken cancellationToken = default);
    Task SaveTranslationAsync(string culture, string key, string translation, CancellationToken cancellationToken = default);
    Task<DictionaryImportResult> ImportAsync(Stream stream, string fileName, bool replace, CancellationToken cancellationToken = default);
    Task DeleteLanguageAsync(string culture, CancellationToken cancellationToken = default);
}

public sealed class LocalizationDictionaryService : ILocalizationDictionaryService
{
    private const int MaxUploadBytes = 8 * 1024 * 1024;
    private const int MaxRows = 100_000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _statePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DictionaryState? _state;

    public LocalizationDictionaryService(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _statePath = configuration["LocalizationDictionary:Path"]
            ?? Path.Combine(environment.ContentRootPath, "App_Data", "localization-dictionary.json");
    }

    public async Task<IReadOnlyList<DictionaryLanguage>> GetLanguagesAsync(CancellationToken cancellationToken = default)
    {
        var state = await ReadStateAsync(cancellationToken);
        return state.Languages
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.EnglishName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<DictionaryLanguage?> FindLanguageAsync(string? code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var state = await ReadStateAsync(cancellationToken);
        return state.Languages.FirstOrDefault(item =>
            string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyDictionary<string, string>> GetCatalogAsync(
        string? culture,
        CancellationToken cancellationToken = default)
    {
        var language = await FindLanguageAsync(culture, cancellationToken);
        if (language is null) return new Dictionary<string, string>(StringComparer.Ordinal);

        var catalog = LoadCompiledCatalog(language.Code);
        var state = await ReadStateAsync(cancellationToken);
        if (state.Translations.TryGetValue(language.Code, out var overrides))
        {
            foreach (var pair in overrides)
                catalog[pair.Key] = pair.Value;
        }

        if (language.IsDefault)
        {
            foreach (var key in GetSourceKeys(state)) catalog.TryAdd(key, key);
        }

        return catalog;
    }

    public async Task<IReadOnlyList<DictionaryEntryRow>> GetRowsAsync(CancellationToken cancellationToken = default)
    {
        var state = await ReadStateAsync(cancellationToken);
        var keys = GetSourceKeys(state);
        var rows = new List<DictionaryEntryRow>(keys.Count * state.Languages.Count);

        foreach (var language in state.Languages)
        {
            var catalog = await GetCatalogAsync(language.Code, cancellationToken);
            foreach (var key in keys)
            {
                rows.Add(new DictionaryEntryRow(
                    language.Code,
                    language.NativeName,
                    language.EnglishName,
                    language.Direction,
                    key,
                    language.IsDefault ? key : catalog.GetValueOrDefault(key, string.Empty)));
            }
        }

        return rows;
    }

    public async Task SaveTranslationAsync(
        string culture,
        string key,
        string translation,
        CancellationToken cancellationToken = default)
    {
        key = NormalizeCell(key, 4_000, "Key");
        translation = NormalizeCell(translation, 12_000, "Translation", allowEmpty: true);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateUnsafeAsync(cancellationToken);
            var language = FindLanguage(state, culture);
            if (language.IsDefault)
                throw new InvalidOperationException("لا يمكن تعديل مفاتيح العربية لأنها لغة المصدر المحمية.");

            if (!GetSourceKeys(state).Contains(key, StringComparer.Ordinal))
                throw new InvalidOperationException("المفتاح غير موجود في قاموس النظام.");

            if (!state.Translations.TryGetValue(language.Code, out var values))
                state.Translations[language.Code] = values = new Dictionary<string, string>(StringComparer.Ordinal);
            values[key] = translation;
            await PersistUnsafeAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DictionaryImportResult> ImportAsync(
        Stream stream,
        string fileName,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        if (!stream.CanSeek) throw new InvalidOperationException("تعذر قراءة الملف المرفوع.");
        if (stream.Length <= 0 || stream.Length > MaxUploadBytes)
            throw new InvalidOperationException("حجم الملف يجب أن يكون بين 1 بايت و8 ميغابايت.");

        var rows = SpreadsheetReader.Read(stream, fileName);
        if (rows.Count < 2) throw new InvalidOperationException("ملف القاموس لا يحتوي صفوف بيانات.");
        if (rows.Count > MaxRows) throw new InvalidOperationException("ملف القاموس تجاوز الحد الأعلى للصفوف.");

        var headers = rows[0]
            .Select((value, index) => new { Name = value.Trim(), Index = index })
            .Where(item => item.Name.Length > 0)
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var required = new[] { "CultureCode", "NativeName", "EnglishName", "Direction", "Key", "Translation" };
        var missing = required.Where(header => !headers.ContainsKey(header)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"أعمدة الملف ناقصة: {string.Join(", ", missing)}");

        string Cell(string[] row, string name) => headers[name] < row.Length ? row[headers[name]].Trim() : string.Empty;
        var parsed = rows.Skip(1)
            .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .Select(row => new DictionaryEntryRow(
                Cell(row, "CultureCode"),
                Cell(row, "NativeName"),
                Cell(row, "EnglishName"),
                Cell(row, "Direction"),
                Cell(row, "Key"),
                Cell(row, "Translation")))
            .ToArray();
        if (parsed.Length == 0) throw new InvalidOperationException("ملف القاموس لا يحتوي صفوفاً قابلة للاستيراد.");

        var codes = parsed.Select(item => item.CultureCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Length != 1)
            throw new InvalidOperationException("يجب أن يحتوي ملف الاستيراد لغة واحدة فقط في كل عملية.");

        var code = NormalizeCultureCode(codes[0]);
        if (string.Equals(code, ZynoraSupportedCultures.DefaultCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("لا يمكن استيراد تعديلات فوق لغة المصدر العربية.");

        var nativeName = SingleMetadata(parsed.Select(item => item.NativeName), "NativeName", 120);
        var englishName = SingleMetadata(parsed.Select(item => item.EnglishName), "EnglishName", 120);
        var direction = SingleMetadata(parsed.Select(item => item.Direction), "Direction", 3).ToLowerInvariant();
        if (direction is not ("rtl" or "ltr"))
            throw new InvalidOperationException("Direction يجب أن يكون rtl أو ltr.");

        var duplicateKey = parsed.GroupBy(item => item.Key.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Key.Length > 0 && group.Count() > 1);
        if (duplicateKey is not null)
            throw new InvalidOperationException($"المفتاح مكرر داخل الملف: {duplicateKey.Key}");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateUnsafeAsync(cancellationToken);
            var sourceKeys = GetSourceKeys(state);
            var unknown = parsed.Select(item => item.Key.Trim())
                .Where(key => key.Length > 0 && !sourceKeys.Contains(key, StringComparer.Ordinal))
                .Take(5)
                .ToArray();
            if (unknown.Length > 0)
                throw new InvalidOperationException($"يتضمن الملف مفاتيح غير معروفة: {string.Join("، ", unknown)}");

            var existing = state.Languages.FirstOrDefault(item =>
                string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
            var isNew = existing is null;
            var language = new DictionaryLanguage(code, nativeName, englishName, direction, false);
            if (existing is null) state.Languages.Add(language);
            else state.Languages[state.Languages.IndexOf(existing)] = language;

            if (replace || !state.Translations.TryGetValue(code, out var translations))
                state.Translations[code] = translations = new Dictionary<string, string>(StringComparer.Ordinal);

            var imported = 0;
            var empty = 0;
            foreach (var item in parsed)
            {
                var key = NormalizeCell(item.Key, 4_000, "Key", allowEmpty: true);
                if (key.Length == 0) continue;
                var value = NormalizeCell(item.Translation, 12_000, "Translation", allowEmpty: true);
                if (value.Length == 0) empty++;
                translations[key] = value;
                imported++;
            }

            await PersistUnsafeAsync(state, cancellationToken);
            return new DictionaryImportResult(code, imported, empty, isNew);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteLanguageAsync(string culture, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateUnsafeAsync(cancellationToken);
            var language = FindLanguage(state, culture);
            if (language.IsDefault)
                throw new InvalidOperationException("لا يمكن حذف العربية لأنها لغة المصدر واللغة الاحتياطية للنظام.");
            state.Languages.Remove(language);
            state.Translations.Remove(language.Code);
            await PersistUnsafeAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DictionaryState> ReadStateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await LoadStateUnsafeAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<DictionaryState> LoadStateUnsafeAsync(CancellationToken cancellationToken)
    {
        if (_state is not null) return _state;
        if (File.Exists(_statePath))
        {
            await using var stream = File.OpenRead(_statePath);
            _state = await JsonSerializer.DeserializeAsync<DictionaryState>(stream, JsonOptions, cancellationToken)
                ?? CreateInitialState();
        }
        else
        {
            _state = CreateInitialState();
        }

        NormalizeState(_state);
        return _state;
    }

    private async Task PersistUnsafeAsync(DictionaryState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var temporaryPath = _statePath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        File.Move(temporaryPath, _statePath, overwrite: true);
        _state = state;
    }

    private static DictionaryState CreateInitialState() => new()
    {
        Languages = ZynoraSupportedCultures.All
            .Select(item => new DictionaryLanguage(
                item.Code,
                item.NativeName,
                item.EnglishName,
                item.IsRightToLeft ? "rtl" : "ltr",
                string.Equals(item.Code, ZynoraSupportedCultures.DefaultCode, StringComparison.OrdinalIgnoreCase)))
            .ToList()
    };

    private static void NormalizeState(DictionaryState state)
    {
        state.Languages ??= [];
        state.Translations = new Dictionary<string, Dictionary<string, string>>(
            state.Translations ?? new Dictionary<string, Dictionary<string, string>>(),
            StringComparer.OrdinalIgnoreCase);
        if (!state.Languages.Any(item => item.IsDefault))
            throw new InvalidOperationException("ملف القاموس لا يحتوي لغة مصدر افتراضية.");
    }

    private static DictionaryLanguage FindLanguage(DictionaryState state, string culture) =>
        state.Languages.FirstOrDefault(item => string.Equals(item.Code, culture, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("اللغة المطلوبة غير موجودة.");

    private static SortedSet<string> GetSourceKeys(DictionaryState state)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in LoadCompiledCatalog("en-US").Keys) keys.Add(key);
        foreach (var language in state.Translations.Values)
            foreach (var key in language.Keys) keys.Add(key);
        return keys;
    }

    private static Dictionary<string, string> LoadCompiledCatalog(string culture)
    {
        var manager = new ResourceManager("SmartAttendance.Web.Resources.SharedResource", typeof(SharedResource).Assembly);
        var resourceSet = manager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false);
        return resourceSet?.Cast<DictionaryEntry>()
            .Where(item => item.Key is string && item.Value is string)
            .ToDictionary(item => (string)item.Key, item => (string)item.Value!, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static string NormalizeCultureCode(string value)
    {
        value = NormalizeCell(value, 20, "CultureCode");
        try { return CultureInfo.GetCultureInfo(value).Name; }
        catch (CultureNotFoundException) { throw new InvalidOperationException("CultureCode غير صالح، مثال: en-US أو tr-TR."); }
    }

    private static string SingleMetadata(IEnumerable<string> values, string name, int maxLength)
    {
        var distinct = values.Select(value => NormalizeCell(value, maxLength, name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinct.Length != 1)
            throw new InvalidOperationException($"يجب أن تكون قيمة {name} واحدة ومتطابقة في جميع الصفوف.");
        return distinct[0];
    }

    private static string NormalizeCell(string? value, int maxLength, string name, bool allowEmpty = false)
    {
        value = (value ?? string.Empty).Trim();
        if (!allowEmpty && value.Length == 0) throw new InvalidOperationException($"{name} لا يمكن أن يكون فارغاً.");
        if (value.Length > maxLength) throw new InvalidOperationException($"{name} تجاوز الحد الأعلى المسموح.");
        return value;
    }

    private sealed class DictionaryState
    {
        public List<DictionaryLanguage> Languages { get; set; } = [];
        public Dictionary<string, Dictionary<string, string>> Translations { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
