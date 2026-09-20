using SmartAttendance.Application.PeopleAi;

namespace SmartAttendance.Web.Infrastructure.PeopleAi;

public sealed record DocumentFormatCapability(
    string Extension,
    bool CanUpload,
    bool CanStore,
    bool CanPreview,
    bool CanExtractText);

public sealed record DocumentProcessingDecision(
    DocumentFormatCapability Format,
    string DocumentType,
    bool HasStructuredExtractor,
    bool ShouldQueueAutomaticExtraction,
    string ProcessingMode,
    string UserMessage);

public static class DocumentProcessingContract
{
    private static readonly IReadOnlyDictionary<string, DocumentFormatCapability>
        Formats = new Dictionary<string, DocumentFormatCapability>(
            StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = new(".png", true, true, true, true),
            [".jpg"] = new(".jpg", true, true, true, true),
            [".jpeg"] = new(".jpeg", true, true, true, true),
            [".webp"] = new(".webp", true, true, true, true),
            [".pdf"] = new(".pdf", true, true, true, true),
            [".doc"] = new(".doc", true, true, false, true),
            [".docx"] = new(".docx", true, true, false, true),
            [".xls"] = new(".xls", true, true, false, true),
            [".xlsx"] = new(".xlsx", true, true, false, true)
        };
    private static readonly HashSet<string> StructuredDocumentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            PeopleAiDocumentTypes.NationalId,
            PeopleAiDocumentTypes.Passport,
            PeopleAiDocumentTypes.Cv
        };

    public static IReadOnlyCollection<string> UploadExtensions =>
        Formats.Values
            .Where(x => x.CanUpload)
            .Select(x => x.Extension)
            .ToArray();

    public static DocumentProcessingDecision Resolve(
        string? extension,
        string? declaredDocumentType)
    {
        var normalizedExtension = NormalizeExtension(extension);
        var format = Formats.TryGetValue(normalizedExtension, out var found)
            ? found
            : new DocumentFormatCapability(
                normalizedExtension,
                false,
                false,
                false,
                false);

        var documentType = string.IsNullOrWhiteSpace(declaredDocumentType)
            ? PeopleAiDocumentTypes.Unknown
            : declaredDocumentType.Trim();

        var hasStructuredExtractor =
            StructuredDocumentTypes.Contains(documentType) &&
            format.CanExtractText;

        var shouldQueue =
            format.CanUpload &&
            format.CanStore &&
            format.CanExtractText;

        var mode = !shouldQueue
            ? "StorageOnly"
            : hasStructuredExtractor
                ? "AutomaticStructuredExtraction"
                : "AutomaticTextExtraction";
        var message = mode switch
        {
            "StorageOnly" =>
                "سيُحفظ الملف بأمان للمراجعة فقط. الاستخراج التلقائي غير مدعوم لهذا التنسيق حالياً.",
            "AutomaticStructuredExtraction" =>
                "هذا التنسيق والنوع يدعمان استخراج النص تلقائياً (OCR عند الحاجة) واستخراج الحقول مع مراجعة بشرية.",
            _ =>
                "يدعم هذا التنسيق استخراج النص تلقائياً (OCR عند الحاجة)، لكن لا يوجد extractor منظم لهذا النوع؛ ستبقى المراجعة البشرية هي المرجع."
        };

        return new DocumentProcessingDecision(
            format,
            documentType,
            hasStructuredExtractor,
            shouldQueue,
            mode,
            message);
    }

    public static bool HasStructuredExtractorDefinition(
        string? documentType) =>
        !string.IsNullOrWhiteSpace(documentType) &&
        StructuredDocumentTypes.Contains(documentType.Trim());

    public static bool SupportsAutomaticExtraction(string? extension) =>
        Resolve(extension, PeopleAiDocumentTypes.Unknown)
            .ShouldQueueAutomaticExtraction;

    public static string ResolveOcrLanguageProfile(
        IReadOnlyList<string>? enabledLanguages,
        string? declaredDocumentType,
        string fallbackLanguage)
    {
        var languages = (enabledLanguages ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x is "ar" or "en")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (languages.Count == 0)
        {
            languages.Add(
                string.IsNullOrWhiteSpace(fallbackLanguage)
                    ? "ar"
                    : fallbackLanguage.Trim().ToLowerInvariant());
        }
        if (string.Equals(
                declaredDocumentType,
                PeopleAiDocumentTypes.Passport,
                StringComparison.OrdinalIgnoreCase))
        {
            // TD3 MRZ is ICAO Latin text. English OCR is a semantic
            // requirement, not an optional company display-language choice.
            return "en";
        }

        if (string.Equals(
                declaredDocumentType,
                PeopleAiDocumentTypes.NationalId,
                StringComparison.OrdinalIgnoreCase))
        {
            // Iraqi National ID extraction depends on Arabic field labels.
            return "ar";
        }

        if (languages.Contains("ar", StringComparer.OrdinalIgnoreCase) &&
            languages.Contains("en", StringComparer.OrdinalIgnoreCase))
        {
            return "ar,en";
        }

        return languages[0];
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var value = extension.Trim();
        return value.StartsWith('.')
            ? value.ToLowerInvariant()
            : "." + value.ToLowerInvariant();
    }
}
