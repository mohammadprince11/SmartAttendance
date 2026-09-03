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
    bool IsDefault = false,
    bool IsHidden = false);

public sealed record DictionaryEntryRow(
    string CultureCode,
    string NativeName,
    string EnglishName,
    string Direction,
    string Key,
    string Translation,
    bool RequiresReview = false);

public sealed record DictionaryImportResult(
    string CultureCode,
    int Imported,
    int Empty,
    bool IsNewLanguage);

public interface ILocalizationDictionaryService
{
    Task<IReadOnlyList<DictionaryLanguage>> GetLanguagesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DictionaryLanguage>> GetAllLanguagesAsync(CancellationToken cancellationToken = default);
    Task<DictionaryLanguage?> FindLanguageAsync(string? code, CancellationToken cancellationToken = default);

    Task AddLanguageAsync(
        string cultureCode,
        string nativeName,
        string englishName,
        string direction,
        CancellationToken cancellationToken = default);

    Task SetLanguageHiddenAsync(
        string culture,
        bool hidden,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetCatalogAsync(string? culture, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DictionaryEntryRow>> GetRowsAsync(CancellationToken cancellationToken = default);
    Task SaveTranslationAsync(string culture, string key, string translation, CancellationToken cancellationToken = default);
    Task<int> SaveTranslationsAsync(
        string culture,
        IReadOnlyDictionary<string, string> translations,
        bool machineGenerated,
        CancellationToken cancellationToken = default);
    Task<DictionaryImportResult> ImportAsync(Stream stream, string fileName, bool replace, CancellationToken cancellationToken = default);
    Task DeleteLanguageAsync(string culture, CancellationToken cancellationToken = default);
}

public sealed class LocalizationDictionaryService : ILocalizationDictionaryService
{
    private const int MaxUploadBytes = 8 * 1024 * 1024;
    private const int MaxRows = 100_000;
    private static readonly IReadOnlyDictionary<string, string[]> ImportHeaderAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["CultureCode"] = ["رمز اللغة", "CultureCode"],
            ["NativeName"] = ["اسم اللغة", "NativeName"],
            ["EnglishName"] = ["الاسم بالإنجليزية", "EnglishName"],
            ["Direction"] = ["الاتجاه", "Direction"],
            ["Key"] = ["النص العربي / المفتاح", "المفتاح", "Key"],
            ["Translation"] = ["الترجمة", "Translation"]
        };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _statePath;
    private readonly Lazy<IReadOnlyCollection<string>> _scannedSourceKeys;
    private readonly bool _includeScannedSourceKeys;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DictionaryState? _state;

    public LocalizationDictionaryService(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _statePath = configuration["LocalizationDictionary:Path"]
            ?? Path.Combine(environment.ContentRootPath, "App_Data", "localization-dictionary.json");
        var publishedSourceCatalogPath = configuration["LocalizationDictionary:SourceCatalogPath"]
            ?? Path.Combine(environment.ContentRootPath, "localization-source-keys.json");
        _scannedSourceKeys = new Lazy<IReadOnlyCollection<string>>(
            () => LoadPublishedSourceCatalog(environment.ContentRootPath, publishedSourceCatalogPath),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _includeScannedSourceKeys = !string.Equals(
            configuration["LocalizationDictionary:IncludeScannedSourceKeys"],
            "false",
            StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<DictionaryLanguage>> GetLanguagesAsync(
        CancellationToken cancellationToken = default)
    {
        var languages = await GetAllLanguagesAsync(cancellationToken);

        return languages
            .Where(item => !item.IsHidden)
            .ToArray();
    }

    public async Task<IReadOnlyList<DictionaryLanguage>> GetAllLanguagesAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await ReadStateAsync(cancellationToken);

        return state.Languages
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.IsHidden)
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
            state.MachineTranslatedKeys.TryGetValue(language.Code, out var machineTranslatedKeys);
            foreach (var key in keys)
            {
                rows.Add(new DictionaryEntryRow(
                    language.Code,
                    language.NativeName,
                    language.EnglishName,
                    language.Direction,
                    key,
                    language.IsDefault ? key : catalog.GetValueOrDefault(key, string.Empty),
                    !language.IsDefault && machineTranslatedKeys?.Contains(key) == true));
            }
        }

        return rows;
    }

    public async Task SaveTranslationAsync(
        string culture,
        string key,
        string translation,
        CancellationToken cancellationToken = default) =>
        _ = await SaveTranslationsAsync(
            culture,
            new Dictionary<string, string>(StringComparer.Ordinal) { [key] = translation },
            machineGenerated: false,
            cancellationToken);

    public async Task<int> SaveTranslationsAsync(
        string culture,
        IReadOnlyDictionary<string, string> translations,
        bool machineGenerated,
        CancellationToken cancellationToken = default)
    {
        if (translations.Count == 0) return 0;
        if (translations.Count > 1_000)
            throw new InvalidOperationException("لا يمكن حفظ أكثر من 1000 ترجمة في العملية الواحدة.");

        var normalized = translations
            .Select(pair => new KeyValuePair<string, string>(
                NormalizeKey(pair.Key),
                NormalizeCell(pair.Value, 12_000, "Translation", allowEmpty: true)))
            .ToArray();

        var duplicate = normalized
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"المفتاح مكرر داخل عملية الحفظ: {duplicate.Key}");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateUnsafeAsync(cancellationToken);
            var language = FindLanguage(state, culture);
            if (language.IsDefault)
                throw new InvalidOperationException("لا يمكن تعديل مفاتيح العربية لأنها لغة المصدر المحمية.");

            var sourceKeys = GetSourceKeys(state);
            var unknown = normalized
                .Select(pair => pair.Key)
                .FirstOrDefault(key => !sourceKeys.Contains(key));
            if (unknown is not null)
                throw new InvalidOperationException($"المفتاح غير موجود في قاموس النظام: {unknown}");

            if (!state.Translations.TryGetValue(language.Code, out var values))
                state.Translations[language.Code] = values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!state.MachineTranslatedKeys.TryGetValue(language.Code, out var machineTranslatedKeys))
                state.MachineTranslatedKeys[language.Code] = machineTranslatedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (key, translation) in normalized)
            {
                if (translation.Length == 0)
                {
                    values.Remove(key);
                    machineTranslatedKeys.Remove(key);
                    continue;
                }

                values[key] = translation;
                if (machineGenerated) machineTranslatedKeys.Add(key);
                else machineTranslatedKeys.Remove(key);
            }

            await PersistUnsafeAsync(state, cancellationToken);
            return normalized.Length;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddLanguageAsync(
        string cultureCode,
        string nativeName,
        string englishName,
        string direction,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeCultureCode(cultureCode);

        if (string.Equals(
                code,
                ZynoraSupportedCultures.DefaultCode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "لغة المصدر العربية موجودة مسبقاً ولا يمكن إضافتها مرة أخرى.");
        }

        nativeName = NormalizeCell(
            nativeName,
            120,
            "NativeName");

        englishName = NormalizeCell(
            englishName,
            120,
            "EnglishName");

        direction = NormalizeCell(
                direction,
                3,
                "Direction")
            .ToLowerInvariant();

        if (direction is not ("rtl" or "ltr"))
        {
            throw new InvalidOperationException(
                "Direction يجب أن يكون rtl أو ltr.");
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await LoadStateUnsafeAsync(
                cancellationToken);

            if (state.Languages.Any(item =>
                    string.Equals(
                        item.Code,
                        code,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"اللغة {code} موجودة مسبقاً.");
            }

            // اللغة الجديدة تبدأ مخفية حتى تتم مراجعة القاموس.
            state.Languages.Add(
                new DictionaryLanguage(
                    code,
                    nativeName,
                    englishName,
                    direction,
                    false,
                    true));

            state.Translations.TryAdd(
                code,
                new Dictionary<string, string>(
                    StringComparer.Ordinal));

            state.MachineTranslatedKeys.TryAdd(
                code,
                new HashSet<string>(
                    StringComparer.Ordinal));

            await PersistUnsafeAsync(
                state,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetLanguageHiddenAsync(
        string culture,
        bool hidden,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var state = await LoadStateUnsafeAsync(
                cancellationToken);

            var language = FindLanguage(
                state,
                culture);

            if (language.IsDefault)
            {
                throw new InvalidOperationException(
                    "لا يمكن إخفاء لغة المصدر العربية.");
            }

            var index = state.Languages.IndexOf(language);

            state.Languages[index] =
                language with
                {
                    IsHidden = hidden
                };

            await PersistUnsafeAsync(
                state,
                cancellationToken);
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

        var rows = SpreadsheetReader.Read(stream, fileName, trimCells: false);
        if (rows.Count < 2) throw new InvalidOperationException("ملف القاموس لا يحتوي صفوف بيانات.");
        if (rows.Count > MaxRows) throw new InvalidOperationException("ملف القاموس تجاوز الحد الأعلى للصفوف.");

        var sourceHeaders = rows[0]
            .Select((value, index) => new { Name = value.Trim(), Index = index })
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonicalName, aliases) in ImportHeaderAliases)
        {
            var alias = aliases.FirstOrDefault(sourceHeaders.ContainsKey);
            if (alias is not null) headers[canonicalName] = sourceHeaders[alias];
        }

        var missing = ImportHeaderAliases.Keys.Where(header => !headers.ContainsKey(header)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"أعمدة الملف ناقصة: {string.Join("، ", missing.Select(header => ImportHeaderAliases[header][0]))}");

        string RawCell(string[] row, string name) => headers[name] < row.Length ? row[headers[name]] : string.Empty;
        string Cell(string[] row, string name) => RawCell(row, name).Trim();
        var parsed = rows.Skip(1)
            .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .Select(row => new DictionaryEntryRow(
                Cell(row, "CultureCode"),
                Cell(row, "NativeName"),
                Cell(row, "EnglishName"),
                Cell(row, "Direction"),
                RawCell(row, "Key"),
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

        var duplicateKey = parsed
            .Select(item => NormalizeKey(item.Key, allowEmpty: true))
            .Where(key => key.Length > 0)
            .GroupBy(key => key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateKey is not null)
            throw new InvalidOperationException($"المفتاح مكرر داخل الملف: {duplicateKey.Key}");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateUnsafeAsync(cancellationToken);
            var sourceKeys = GetSourceKeys(state);
            var unknown = parsed
                .Select(item => NormalizeKey(item.Key, allowEmpty: true))
                .Where(key => key.Length > 0 && !sourceKeys.Contains(key, StringComparer.Ordinal))
                .Take(5)
                .ToArray();
            if (unknown.Length > 0)
                throw new InvalidOperationException($"يتضمن الملف مفاتيح غير معروفة: {string.Join("، ", unknown)}");

            var existing = state.Languages.FirstOrDefault(item =>
                string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
            var isNew = existing is null;
            var language = new DictionaryLanguage(
                code,
                nativeName,
                englishName,
                direction,
                false,
                existing?.IsHidden ?? false);
            if (existing is null) state.Languages.Add(language);
            else state.Languages[state.Languages.IndexOf(existing)] = language;

            if (replace || !state.Translations.TryGetValue(code, out var translations))
                state.Translations[code] = translations = new Dictionary<string, string>(StringComparer.Ordinal);
            if (replace || !state.MachineTranslatedKeys.TryGetValue(code, out var machineTranslatedKeys))
                state.MachineTranslatedKeys[code] = machineTranslatedKeys = new HashSet<string>(StringComparer.Ordinal);

            var imported = 0;
            var empty = 0;
            foreach (var item in parsed)
            {
                var key = NormalizeKey(item.Key, allowEmpty: true);
                if (key.Length == 0) continue;
                var value = NormalizeCell(item.Translation, 12_000, "Translation", allowEmpty: true);
                if (value.Length == 0)
                {
                    empty++;
                    translations.Remove(key);
                    machineTranslatedKeys.Remove(key);
                    imported++;
                    continue;
                }

                translations[key] = value;
                machineTranslatedKeys.Remove(key);
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
            state.MachineTranslatedKeys.Remove(language.Code);
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
        state.MachineTranslatedKeys = new Dictionary<string, HashSet<string>>(
            (state.MachineTranslatedKeys ?? new Dictionary<string, HashSet<string>>())
                .ToDictionary(
                    pair => pair.Key,
                    pair => new HashSet<string>(pair.Value ?? [], StringComparer.Ordinal),
                    StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        if (!state.Languages.Any(item => item.IsDefault))
            throw new InvalidOperationException("ملف القاموس لا يحتوي لغة مصدر افتراضية.");
    }

    private static DictionaryLanguage FindLanguage(DictionaryState state, string culture) =>
        state.Languages.FirstOrDefault(item => string.Equals(item.Code, culture, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("اللغة المطلوبة غير موجودة.");

    private static IReadOnlyCollection<string> LoadPublishedSourceCatalog(
        string contentRootPath,
        string publishedSourceCatalogPath)
    {
        // Published applications do not contain the complete Razor/C# source tree.
        // The publish pipeline therefore ships a deterministic catalog generated
        // from the source checkout. Development falls back to live source scanning.
        if (!File.Exists(publishedSourceCatalogPath))
            return LocalizationSourceTextScanner.Scan(contentRootPath);

        try
        {
            using var stream = File.OpenRead(publishedSourceCatalogPath);
            var values = JsonSerializer.Deserialize<string[]>(stream) ?? [];
            var keys = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > 4_000)
                    continue;

                keys.Add(value);
            }

            if (keys.Count == 0)
                throw new InvalidOperationException(
                    "كتالوج مفاتيح الترجمة المنشور فارغ. أعد نشر النظام من المصدر.");

            return keys;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "تعذر قراءة كتالوج مفاتيح الترجمة المنشور. أعد نشر النظام من المصدر.",
                exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "تعذر فتح كتالوج مفاتيح الترجمة المنشور.",
                exception);
        }
    }
    private SortedSet<string> GetSourceKeys(DictionaryState state)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in LoadCompiledCatalog("en-US").Keys) keys.Add(key);

        if (_includeScannedSourceKeys)
        {
            foreach (var key in _scannedSourceKeys.Value) keys.Add(key);
        }

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

    private static string NormalizeKey(string? value, bool allowEmpty = false)
    {
        value ??= string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            if (allowEmpty) return string.Empty;
            throw new InvalidOperationException("Key لا يمكن أن يكون فارغاً.");
        }

        if (value.Length > 4_000)
            throw new InvalidOperationException("Key تجاوز الحد الأعلى المسموح.");

        return value;
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
        public Dictionary<string, HashSet<string>> MachineTranslatedKeys { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
