using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartAttendance.Application.Announcements.Models;

public sealed record StudioField(string Key, string Label, string Type = "text", bool Required = true);
public sealed record StudioText(string Title, string Body);
public sealed class StudioTemplate
{
    public string Key { get; set; } = "custom";
    public string Name { get; set; } = "قالب مخصص";
    public List<StudioField> Fields { get; set; } = [];
    public Dictionary<string, StudioText> Languages { get; set; } = new();
    public List<Guid> DesignIds { get; set; } = [];
}
public sealed record StudioPresentation(Guid DesignId, string Fit, string Position, string TextPlacement, string? AssetKey = null);
public sealed record StudioRendered(string LanguageCode, string Title, string Body);

/// <summary>One renderer for the live preview and persisted publication. Templates contain text, never HTML.</summary>
public static class AnnouncementStudio
{
    public static readonly string[] BuiltinAssets = ["welcome", "promotion", "newborn", "marriage", "holiday", "farewell", "custom", "condolence", "circular"];
    public static bool ValidLanguage(string code) => Regex.IsMatch(code, "^[a-z]{2,3}(-[A-Za-z0-9]{2,8}){0,2}$");
    public static string? Validate(StudioTemplate template)
    {
        if (template.Key == null || template.Name == null || template.Fields == null || template.Languages == null || template.DesignIds == null ||
            template.Fields.Any(f => f == null || f.Key == null || f.Label == null) || template.Languages.Any(l => l.Value == null || l.Value.Title == null || l.Value.Body == null))
            return "بيانات القالب غير مكتملة.";
        if (!Regex.IsMatch(template.Key, "^[a-z][a-z0-9-]{0,79}$") || string.IsNullOrWhiteSpace(template.Name) || template.Name.Length > 150)
            return "يرجى إدخال اسم ورمز صحيحين للقالب.";
        if (template.Fields.Count > 16 || template.Fields.Select(f => f.Key).Distinct().Count() != template.Fields.Count)
            return "حقول القالب مكررة أو أكثر من 16 حقلاً.";
        foreach (var f in template.Fields)
            if (!Regex.IsMatch(f.Key, "^[a-z][a-zA-Z0-9]{0,39}$") || string.IsNullOrWhiteSpace(f.Label) || f.Label.Length > 100 || f.Type is not ("text" or "date" or "month" or "number"))
                return "نوع أو اسم حقل القالب غير صالح.";
        if (template.Languages.Count is < 1 or > 12 || template.DesignIds.Count > 30) return "أضف لغة واحدة على الأقل، وبحد أقصى 12 لغة و30 تصميماً.";
        if (template.Languages.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != template.Languages.Count)
            return "رمز اللغة مكرر.";
        foreach (var (lang, text) in template.Languages)
        {
            if (!ValidLanguage(lang) || string.IsNullOrWhiteSpace(text.Title) || text.Title.Length > 250 || string.IsNullOrWhiteSpace(text.Body) || text.Body.Length > 10000)
                return "يرجى إكمال عنوان ونص كل لغة ضمن الحدود المسموحة.";
            var tokens = Regex.Matches(text.Title + text.Body, "\\{([^{}]+)\\}").Select(m => m.Groups[1].Value);
            if (tokens.Any(t => t != "years" && !template.Fields.Any(f => f.Key == t))) return "النص يحتوي متغيراً غير معرّف ضمن حقول القالب.";
            if (tokens.Contains("years") && !template.Fields.Any(f => f.Key == "joiningDate" && f.Type == "date"))
                return "متغير years يحتاج حقل joiningDate من نوع تاريخ.";
        }
        return null;
    }

    public static List<StudioRendered> Render(StudioTemplate template, IReadOnlyDictionary<string, string> values, DateOnly today)
    {
        var error = Validate(template);
        if (error != null) throw new ArgumentException(error);
        var normalized = new Dictionary<string, string>();
        foreach (var field in template.Fields)
        {
            var value = values.GetValueOrDefault(field.Key)?.Trim() ?? "";
            if (field.Required && value.Length == 0) throw new ArgumentException($"الحقل مطلوب: {field.Label}");
            if (value.Length > 500) throw new ArgumentException($"قيمة الحقل طويلة: {field.Label}");
            if (value.Length > 0 && field.Type == "date" && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new ArgumentException($"تاريخ غير صالح: {field.Label}");
            if (value.Length > 0 && field.Type == "month" && !DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new ArgumentException($"شهر غير صالح: {field.Label}");
            if (value.Length > 0 && field.Type == "number" && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                throw new ArgumentException($"رقم غير صالح: {field.Label}");
            normalized[field.Key] = value;
        }
        if (DateOnly.TryParse(normalized.GetValueOrDefault("startDate"), out var start) && DateOnly.TryParse(normalized.GetValueOrDefault("endDate"), out var end) && end < start)
            throw new ArgumentException("تاريخ النهاية يجب ألا يسبق البداية.");
        var years = "";
        if (DateOnly.TryParse(normalized.GetValueOrDefault("joiningDate"), out var joining))
        {
            if (joining > today) throw new ArgumentException("تاريخ المباشرة لا يجوز أن يكون مستقبلياً لحساب ذكرى العمل.");
            years = (today.Year - joining.Year - (today < joining.AddYears(today.Year - joining.Year) ? 1 : 0)).ToString(CultureInfo.InvariantCulture);
        }
        normalized["years"] = years;
        string Replace(string text) => Regex.Replace(text, "\\{([^{}]+)\\}", m => normalized.GetValueOrDefault(m.Groups[1].Value) ?? "");
        return template.Languages.Select(x => new StudioRendered(x.Key, Replace(x.Value.Title), Replace(x.Value.Body)))
            .Select(x => x.Title.Length <= 250 ? x : throw new ArgumentException("عنوان الإعلان الناتج أكثر من 250 حرفاً."))
            .ToList();
    }

    public static IReadOnlyList<StudioTemplate> Defaults()
    {
        StudioField Person() => new("person", "اسم الموظف");
        StudioTemplate Make(string key, string name, List<StudioField> fields, string arTitle, string arBody, string enTitle, string enBody) => new()
        {
            Key = key, Name = name, Fields = fields,
            Languages = new() { ["ar"] = new(arTitle, arBody), ["en"] = new(enTitle, enBody) }
        };
        return [
            Make("birthday", "عيد ميلاد", [Person(), new("date", "تاريخ الميلاد", "date")], "عيد ميلاد سعيد {person}!", "نتمنى لك عيد ميلاد سعيداً بتاريخ {date} وسنة مليئة بالنجاح والسعادة.", "Happy birthday, {person}!", "Wishing you a happy birthday on {date} and a wonderful year ahead."),
            Make("newborn-boy", "مولود جديد", [Person(), new("child", "اسم المولود"), new("date", "تاريخ الولادة", "date", false)], "مبارك المولود يا {person}!", "نبارك لك قدوم مولودك {child}. تاريخ الولادة: {date}.", "Congratulations, {person}!", "Congratulations on the arrival of your baby boy {child}. Birth date: {date}."),
            Make("newborn-girl", "مولودة جديدة", [Person(), new("child", "اسم المولودة"), new("date", "تاريخ الولادة", "date", false)], "مبارك المولودة يا {person}!", "نبارك لك قدوم مولودتك {child}. تاريخ الولادة: {date}.", "Congratulations, {person}!", "Congratulations on the arrival of your baby girl {child}. Birth date: {date}."),
            Make("marriage", "زواج موظف", [Person(), new("date", "تاريخ الزواج", "date")], "زواج مبارك {person}!", "نبارك لك الزواج بتاريخ {date} ونتمنى لك حياة سعيدة.", "Congratulations on your marriage, {person}!", "Wishing you a happy life together. Wedding date: {date}."),
            Make("condolence", "تعزية", [Person(), new("relation", "صلة المتوفى"), new("date", "تاريخ الوفاة", "date", false)], "خالص التعازي إلى {person}", "نتقدم إليك بخالص التعازي بوفاة {relation}. تاريخ الوفاة: {date}.", "Our condolences, {person}", "Our deepest condolences on the loss of your {relation}. Date: {date}."),
            Make("employee-of-month", "موظف الشهر", [Person(), new("month", "الشهر والسنة", "month")], "موظف الشهر: {person}", "نبارك تكريمك موظف الشهر {month} تقديراً لجهودك المميزة.", "Employee of the month: {person}", "Congratulations on being our employee of the month for {month}. Thank you for your outstanding work."),
            Make("welcome", "الترحيب بموظف جديد", [Person(), new("position", "المنصب"), new("date", "تاريخ المباشرة", "date")], "أهلاً بك {person}!", "يسرنا انضمامك بمنصب {position} اعتباراً من {date}. نتمنى لك بداية موفقة.", "Welcome aboard, {person}!", "We welcome you as {position}, starting {date}. Wishing you a successful journey with us."),
            Make("farewell", "وداع موظف", [Person(), new("date", "آخر يوم عمل", "date")], "نتمنى لك التوفيق {person}!", "نشكرك على عطائك. آخر يوم عمل: {date}. نتمنى لك النجاح في مسيرتك القادمة.", "Best wishes, {person}!", "Thank you for your contribution. Last working day: {date}. We wish you success in your next chapter."),
            Make("holiday", "إعلان عطلة", [new("occasion", "اسم العطلة"), new("startDate", "من تاريخ", "date"), new("endDate", "إلى تاريخ", "date")], "إعلان عطلة: {occasion}", "تكون مكاتبنا مغلقة من {startDate} إلى {endDate} بمناسبة {occasion}.", "Holiday announcement: {occasion}", "Our offices will be closed from {startDate} through {endDate} for {occasion}."),
            Make("anniversary", "ذكرى عمل", [Person(), new("joiningDate", "تاريخ المباشرة", "date")], "ذكرى عمل سعيدة {person}!", "نحتفل بمرور {years} سنة منذ انضمامك بتاريخ {joiningDate}. شكراً لعطائك.", "Happy work anniversary, {person}!", "Celebrating {years} years since you joined us on {joiningDate}. Thank you for your contribution."),
            Make("retirement", "تقاعد موظف", [Person(), new("date", "تاريخ التقاعد", "date")], "تقاعد سعيد {person}!", "نشكرك على سنوات العطاء ونتمنى لك تقاعداً سعيداً اعتباراً من {date}.", "Happy retirement, {person}!", "Thank you for your years of dedication. Wishing you a happy retirement starting {date}.")
        ];
    }
}
