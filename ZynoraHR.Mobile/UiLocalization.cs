using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZynoraHR.Mobile;

public static class UiLocalization
{
    public const string PreferenceKey = "zynora.mobile.ui_language.v1";
    public const string Arabic = "ar";
    public const string English = "en";
    public const string Kurdish = "ckb";

    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<Element, byte> Attached = new();
    private static IReadOnlyDictionary<string, string>? _arabic;
    private static IReadOnlyDictionary<string, string>? _english;
    private static IReadOnlyDictionary<string, string>? _kurdish;
    private static IReadOnlyList<TemplateTranslation>? _arabicTemplates;
    private static IReadOnlyList<TemplateTranslation>? _englishTemplates;
    private static IReadOnlyList<TemplateTranslation>? _kurdishTemplates;
    private static bool _applying;

    private static readonly Regex PlaceholderRegex =
        new(@"\{[^{}]+\}", RegexOptions.Compiled);

    public static string CurrentLanguage =>
        NormalizeLanguage(Preferences.Default.Get(PreferenceKey, Arabic));

    public static bool IsEnglish =>
        string.Equals(CurrentLanguage, English, StringComparison.Ordinal);

    public static bool IsRightToLeft =>
        CurrentLanguage is Arabic or Kurdish;

    public static string CurrentCultureCode => CurrentLanguage switch
    {
        English => "en-US",
        Kurdish => "ckb-IQ",
        _ => "ar-IQ"
    };

    public static FlowDirection CurrentFlowDirection =>
        IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public static void ConfigureCulture()
    {
        var culture = CurrentLanguage switch
        {
            English => new CultureInfo("en-US"),
            Kurdish => new CultureInfo("ckb-IQ"),
            _ => new CultureInfo("ar-IQ")
        };

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static void SetLanguage(string language)
    {
        Preferences.Default.Set(PreferenceKey, NormalizeLanguage(language));
        ConfigureCulture();
    }
    public static void Attach(Element root)
    {
        if (!Attached.TryAdd(root, 0))
            return;

        ApplyElement(root);
        root.PropertyChanged += OnElementPropertyChanged;
        root.ChildAdded += OnChildAdded;

        if (root is IVisualTreeElement visual)
        {
            foreach (var child in visual.GetVisualChildren().OfType<Element>())
                Attach(child);
        }
    }

    public static string Data(string? source, string? english = null)
    {
        var value = source?.Trim() ?? string.Empty;
        var englishValue = english?.Trim() ?? string.Empty;

        if (IsEnglish && !string.IsNullOrWhiteSpace(englishValue))
            return englishValue;

        var translated = T(value);

        if (string.Equals(CurrentLanguage, Kurdish, StringComparison.Ordinal) &&
            string.Equals(translated, value, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(englishValue))
        {
            return englishValue;
        }

        return translated;
    }

    public static string SystemData(string? source)
    {
        var value = source?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var canonical = value.ToLowerInvariant() switch
        {
            "male" or "m" => "ذكر",
            "female" or "f" => "أنثى",
            "iraqi" => "عراقي",
            "iraq" or "irq" => "العراق",
            "single" => "أعزب",
            "married" => "متزوج",
            "divorced" => "مطلق",
            "widowed" => "أرمل",
            "active" => "نشط",
            "inactive" => "غير نشط",
            "resigned" => "مستقيل",
            "terminated" => "منتهي الخدمة",
            "ft" or "fulltime" or "full-time" => "دوام كامل",
            "pt" or "parttime" or "part-time" => "دوام جزئي",
            "present" or "حاضر" => "حاضر",
            "late" or "متأخر" => "متأخر",
            "incomplete" or "بصمة ناقصة" => "بصمة ناقصة",
            "absent" or "غائب" => "غائب",
            "weekend" or "عطلة أسبوعية" => "عطلة أسبوعية",
            "rest" or "يوم راحة" => "يوم راحة",
            "holiday" or "عطلة رسمية" => "عطلة رسمية",
            "leave" or "إجازة" => "إجازة",
            "leaveunpaid" or "إجازة بدون راتب" => "إجازة بدون راتب",
            _ => value
        };

        return T(canonical);
    }

    public static string T(string? source)
    {
        if (string.IsNullOrEmpty(source))
            return source ?? string.Empty;

        var catalog = GetCatalog();
        var normalized = NormalizeNewLines(source);

        if (catalog.TryGetValue(normalized, out var exact))
            return RestoreNewLines(exact);

        var templates = GetTemplates();
        foreach (var template in templates)
        {
            var match = template.Pattern.Match(normalized);
            if (!match.Success)
                continue;

            var translated = template.Target;
            for (var index = 0; index < template.Placeholders.Count; index++)
            {
                translated = translated.Replace(
                    template.Placeholders[index],
                    match.Groups[$"p{index}"].Value,
                    StringComparison.Ordinal);
            }

            return RestoreNewLines(translated);
        }

        return source;
    }

    private static void OnChildAdded(object? sender, ElementEventArgs e)
    {
        if (e.Element is not null)
            Attach(e.Element);
    }

    private static void OnElementPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_applying || sender is not Element element)
            return;

        if (e.PropertyName is nameof(Label.Text) or
            nameof(Button.Text) or
            nameof(Entry.Placeholder) or
            nameof(Editor.Placeholder) or
            nameof(SearchBar.Placeholder) or
            nameof(Picker.Title) or
            nameof(Span.Text) or
            nameof(RadioButton.Content))
        {
            ApplyElement(element);
        }
    }
    private static void ApplyElement(Element element)
    {
        _applying = true;
        try
        {
            switch (element)
            {
                case Label label:
                    SetIfChanged(label.Text, value => label.Text = value);
                    break;
                case Button button:
                    SetIfChanged(button.Text, value => button.Text = value);
                    break;
                case Entry entry:
                    SetIfChanged(entry.Placeholder, value => entry.Placeholder = value);
                    break;
                case Editor editor:
                    SetIfChanged(editor.Placeholder, value => editor.Placeholder = value);
                    break;
                case SearchBar searchBar:
                    SetIfChanged(searchBar.Placeholder, value => searchBar.Placeholder = value);
                    break;
                case Picker picker:
                    SetIfChanged(picker.Title, value => picker.Title = value);
                    break;
                case Span span:
                    SetIfChanged(span.Text, value => span.Text = value);
                    break;
                case RadioButton radio when radio.Content is string text:
                {
                    var translated = T(text);
                    if (!string.Equals(translated, text, StringComparison.Ordinal))
                        radio.Content = translated;
                    break;
                }
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private static void SetIfChanged(string? source, Action<string> setter)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        var translated = T(source);
        if (!string.Equals(source, translated, StringComparison.Ordinal))
            setter(translated);
    }

    private static IReadOnlyDictionary<string, string> GetCatalog()
    {
        EnsureLoaded();
        return CurrentLanguage switch
        {
            Arabic => _arabic!,
            Kurdish => _kurdish!,
            _ => _english!
        };
    }

    private static IReadOnlyList<TemplateTranslation> GetTemplates()
    {
        EnsureLoaded();
        return CurrentLanguage switch
        {
            Arabic => _arabicTemplates!,
            Kurdish => _kurdishTemplates!,
            _ => _englishTemplates!
        };
    }
    private static void EnsureLoaded()
    {
        if (_arabic is not null && _english is not null && _kurdish is not null)
            return;

        lock (Gate)
        {
            if (_arabic is not null && _english is not null && _kurdish is not null)
                return;

            _arabic = LoadCatalog("Localization/ui.ar.json");
            _english = LoadCatalog("Localization/ui.en.json");
            _kurdish = LoadCatalog("Localization/ui.ckb.json");
            _arabicTemplates = BuildTemplates(_arabic);
            _englishTemplates = BuildTemplates(_english);
            _kurdishTemplates = BuildTemplates(_kurdish);
        }
    }

    private static IReadOnlyDictionary<string, string> LoadCatalog(string assetPath)
    {
        Stream stream;
        try
        {
            stream = FileSystem.OpenAppPackageFileAsync(assetPath)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Localization asset '{assetPath}' cannot be opened.",
                ex);
        }

        using (stream)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

            return new Dictionary<string, string>(
                values.ToDictionary(
                    pair => NormalizeNewLines(pair.Key),
                    pair => pair.Value,
                    StringComparer.Ordinal),
                StringComparer.Ordinal);
        }
    }

    private static IReadOnlyList<TemplateTranslation> BuildTemplates(
        IReadOnlyDictionary<string, string> catalog)
    {
        var templates = new List<TemplateTranslation>();

        foreach (var pair in catalog)
        {
            var matches = PlaceholderRegex.Matches(pair.Key);
            if (matches.Count == 0)
                continue;

            var pattern = new System.Text.StringBuilder("^");
            var placeholders = new List<string>();
            var cursor = 0;

            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                pattern.Append(Regex.Escape(pair.Key[cursor..match.Index]));
                pattern.Append($"(?<p{index}>.*?)");
                placeholders.Add(match.Value);
                cursor = match.Index + match.Length;
            }

            pattern.Append(Regex.Escape(pair.Key[cursor..]));
            pattern.Append("$");

            templates.Add(new TemplateTranslation(
                new Regex(
                    pattern.ToString(),
                    RegexOptions.Compiled | RegexOptions.Singleline),
                pair.Value,
                placeholders));
        }

        return templates
            .OrderByDescending(item => item.Pattern.ToString().Length)
            .ToArray();
    }
    private static string NormalizeLanguage(string? language) =>
        language?.Trim().ToLowerInvariant() switch
        {
            English => English,
            Kurdish => Kurdish,
            _ => Arabic
        };

    private static string NormalizeNewLines(string value) =>
        value.Replace("\r\n", "\\n", StringComparison.Ordinal)
             .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string RestoreNewLines(string value) =>
        value.Replace("\\n", Environment.NewLine, StringComparison.Ordinal);

    private sealed record TemplateTranslation(
        Regex Pattern,
        string Target,
        IReadOnlyList<string> Placeholders);
}
