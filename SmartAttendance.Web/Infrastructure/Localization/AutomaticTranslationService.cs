using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SmartAttendance.Web.Infrastructure.Localization;

public sealed class AzureTranslatorOptions
{
    public const string SectionName = "LocalizationDictionary:AzureTranslator";

    public string Endpoint { get; set; } = "https://api.cognitive.microsofttranslator.com";
    public string SubscriptionKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;

    public bool IsConfigured =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) &&
        endpoint.Scheme == Uri.UriSchemeHttps &&
        !string.IsNullOrWhiteSpace(SubscriptionKey);
}

public interface IAutomaticTextTranslator
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        string targetCulture,
        CancellationToken cancellationToken = default);
}

public sealed class AzureAutomaticTextTranslator : IAutomaticTextTranslator
{
    internal const int MaximumTextsPerRequest = 100;
    internal const int MaximumCharactersPerRequest = 40_000;

    private static readonly Regex ProtectedTokenPattern = new(
        @"https?://[^\s<>]+|<[^>]+>|\{\{[^{}]+\}\}|\{[A-Za-z0-9_.-]+(?:,[^{}]+)?(?::[^{}]+)?\}|%\d*\$?[a-zA-Z]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly AzureTranslatorOptions _options;

    public AzureAutomaticTextTranslator(HttpClient httpClient, IOptions<AzureTranslatorOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        string targetCulture,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("خدمة Azure Translator غير مهيأة بعد.");
        if (texts.Count == 0) return [];
        if (texts.Count > MaximumTextsPerRequest)
            throw new InvalidOperationException($"دفعة Azure الواحدة لا تتجاوز {MaximumTextsPerRequest} نصوص.");
        if (texts.Sum(text => text?.Length ?? 0) > MaximumCharactersPerRequest)
            throw new InvalidOperationException("حجم دفعة الترجمة تجاوز الحد الآمن المسموح.");

        var endpoint = ValidateEndpoint(_options.Endpoint);
        var targetLanguage = ResolveTranslatorLanguage(targetCulture);
        var query = $"api-version=3.0&to={Uri.EscapeDataString(targetLanguage)}";
        if (!string.IsNullOrWhiteSpace(_options.SourceLanguage))
            query += $"&from={Uri.EscapeDataString(_options.SourceLanguage.Trim())}";
        var requestUri = new Uri(endpoint, $"translate?{query}");

        var protectedTexts = texts.Select(ProtectTokens).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(
                protectedTexts.Select(item => new TranslationRequest(item.Text)).ToArray(),
                options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _options.SubscriptionKey.Trim());
        if (!string.IsNullOrWhiteSpace(_options.Region))
            request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", _options.Region.Trim());
        request.Headers.TryAddWithoutValidation("X-ClientTraceId", Guid.NewGuid().ToString());

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"رفض Azure Translator الدفعة برمز HTTP {(int)response.StatusCode}.");

        var payload = await response.Content.ReadFromJsonAsync<TranslationResponse[]>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken);
        if (payload is null || payload.Length != texts.Count)
            throw new InvalidOperationException("أعاد Azure Translator نتيجة غير مكتملة.");

        var result = new string[payload.Length];
        for (var index = 0; index < payload.Length; index++)
        {
            var translated = payload[index].Translations?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(translated))
                throw new InvalidOperationException("أعاد Azure Translator ترجمة فارغة.");
            result[index] = RestoreTokens(translated, protectedTexts[index].Tokens);
        }

        return result;
    }

    public static string ResolveTranslatorLanguage(string culture)
    {
        CultureInfo cultureInfo;
        try { cultureInfo = CultureInfo.GetCultureInfo(culture); }
        catch (CultureNotFoundException)
        {
            throw new InvalidOperationException("رمز اللغة المستهدفة غير صالح.");
        }

        return cultureInfo.Name.StartsWith("ckb", StringComparison.OrdinalIgnoreCase)
            ? "ku"
            : cultureInfo.TwoLetterISOLanguageName;
    }

    private static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("عنوان Azure Translator يجب أن يكون HTTPS صالحاً.");
        }

        return uri;
    }

    private static ProtectedText ProtectTokens(string? source)
    {
        source ??= string.Empty;
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        var text = ProtectedTokenPattern.Replace(source, match =>
        {
            var token = $"__ZYNORA_TOKEN_{index++:0000}__";
            tokens[token] = match.Value;
            return token;
        });
        return new ProtectedText(text, tokens);
    }

    private static string RestoreTokens(string translated, IReadOnlyDictionary<string, string> tokens)
    {
        foreach (var (token, original) in tokens)
        {
            var index = translated.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                throw new InvalidOperationException("تعذر الحفاظ على أحد متغيرات النص أثناء الترجمة الآلية.");
            translated = string.Concat(
                translated.AsSpan(0, index),
                original,
                translated.AsSpan(index + token.Length));
        }

        return translated.Trim();
    }

    private sealed record TranslationRequest([property: JsonPropertyName("Text")] string Text);
    private sealed record TranslationResponse(
        [property: JsonPropertyName("translations")] TranslationValue[]? Translations);
    private sealed record TranslationValue(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("to")] string To);
    private sealed record ProtectedText(string Text, IReadOnlyDictionary<string, string> Tokens);
}

public sealed record AutomaticTranslationResult(
    int Translated,
    int Remaining,
    bool IsComplete,
    string? Warning = null);

public interface ILocalizationAutoTranslationService
{
    bool IsConfigured { get; }
    Task<AutomaticTranslationResult> TranslateMissingAsync(
        string culture,
        int maximumItems,
        CancellationToken cancellationToken = default);
}

public sealed class LocalizationAutoTranslationService : ILocalizationAutoTranslationService
{
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);
    private readonly ILocalizationDictionaryService _dictionary;
    private readonly IAutomaticTextTranslator _translator;

    public LocalizationAutoTranslationService(
        ILocalizationDictionaryService dictionary,
        IAutomaticTextTranslator translator)
    {
        _dictionary = dictionary;
        _translator = translator;
    }

    public bool IsConfigured => _translator.IsConfigured;

    public async Task<AutomaticTranslationResult> TranslateMissingAsync(
        string culture,
        int maximumItems,
        CancellationToken cancellationToken = default)
    {
        if (!await ExecutionGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("توجد عملية ترجمة آلية قيد التنفيذ حالياً.");
        try
        {
            return await TranslateMissingCoreAsync(culture, maximumItems, cancellationToken);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private async Task<AutomaticTranslationResult> TranslateMissingCoreAsync(
        string culture,
        int maximumItems,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("خدمة Azure Translator غير مهيأة بعد.");
        maximumItems = Math.Clamp(maximumItems, 1, 1_000);

        var language = await _dictionary.FindLanguageAsync(culture, cancellationToken)
            ?? throw new InvalidOperationException("اللغة المطلوبة غير موجودة.");
        if (language.IsDefault)
            throw new InvalidOperationException("العربية هي لغة المصدر ولا تحتاج ترجمة آلية.");

        var allMissing = (await _dictionary.GetRowsAsync(cancellationToken))
            .Where(item => string.Equals(item.CultureCode, language.Code, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(item.Translation))
            .ToArray();
        if (allMissing.Length == 0)
            return new AutomaticTranslationResult(0, 0, true);
        var missing = allMissing.Take(maximumItems).ToArray();

        var translated = 0;
        string? warning = null;
        foreach (var batch in BuildBatches(missing))
        {
            try
            {
                var values = await _translator.TranslateAsync(
                    batch.Select(item => item.Key).ToArray(),
                    language.Code,
                    cancellationToken);
                var updates = batch
                    .Select((item, index) => new KeyValuePair<string, string>(item.Key, values[index]))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                translated += await _dictionary.SaveTranslationsAsync(
                    language.Code,
                    updates,
                    machineGenerated: true,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InvalidOperationException exception)
            {
                if (translated == 0) throw;
                warning = exception.Message;
                break;
            }
        }

        var remaining = Math.Max(0, allMissing.Length - translated);
        return new AutomaticTranslationResult(translated, remaining, remaining == 0, warning);
    }

    private static IEnumerable<DictionaryEntryRow[]> BuildBatches(IReadOnlyList<DictionaryEntryRow> rows)
    {
        var batch = new List<DictionaryEntryRow>(AzureAutomaticTextTranslator.MaximumTextsPerRequest);
        var characters = 0;
        foreach (var row in rows)
        {
            if (batch.Count > 0 &&
                (batch.Count == AzureAutomaticTextTranslator.MaximumTextsPerRequest ||
                 characters + row.Key.Length > AzureAutomaticTextTranslator.MaximumCharactersPerRequest))
            {
                yield return batch.ToArray();
                batch.Clear();
                characters = 0;
            }

            batch.Add(row);
            characters += row.Key.Length;
        }

        if (batch.Count > 0) yield return batch.ToArray();
    }
}
