namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// باني النماذج العام — الطبقة البنيوية الرابعة والأخيرة (منطق نقيّ).
///
/// ⭐ <b>لماذا واحد لا اثنان:</b> الفحص الحيّ أثبت أن كيان يستعمل **نفس البنية**
/// (<c>lstGroups[i].lstGroupFields[j]</c>) لباني الطلبات المخصصة **ولباني
/// الاستبيانات**. فبناء بانيين عندنا كان سيكرّر المخزن والتحقق والواجهة مرّتين
/// لفجوتين هما في الحقيقة فجوة واحدة.
///
/// وأثره العملي: نوع طلب جديد أو استبيان جديد يصير **إعداداً بالواجهة** بدل
/// صفحة Razor + جدول + Store + معالج + نشر.
/// </summary>
public static class FormBuilder
{
    // ── أنواع النماذج ──────────────────────────────────────────────────────────

    public const string FormTypeRequest = "Request";
    public const string FormTypeSurvey = "Survey";
    public const string FormTypeExitInterview = "ExitInterview";
    public const string FormTypeTraining = "Training";

    public static readonly IReadOnlyList<(string Key, string Label)> FormTypes = new[]
    {
        (FormTypeRequest, "طلب مخصص"),
        (FormTypeSurvey, "استبيان عام"),
        (FormTypeExitInterview, "مقابلة نهاية خدمة"),
        (FormTypeTraining, "استبيان تدريب")
    };

    public static string FormTypeLabel(string? key) =>
        FormTypes.FirstOrDefault(entry => entry.Key == key).Label ?? "نموذج";

    /// <summary>سؤال التقييم لا معنى له بطلب — الأنواع تُرشَّح بنوع النموذج.</summary>
    public static bool IsSurveyLike(string? formType) =>
        formType is FormTypeSurvey or FormTypeExitInterview or FormTypeTraining;

    // ── أنواع الحقول ───────────────────────────────────────────────────────────

    public const string ControlText = "Text";
    public const string ControlTextArea = "TextArea";
    public const string ControlNumber = "Number";
    public const string ControlDate = "Date";
    public const string ControlTime = "Time";
    public const string ControlDateRange = "DateRange";
    public const string ControlTimeRange = "TimeRange";
    public const string ControlYesNo = "YesNoNa";
    public const string ControlSelect = "Select";
    public const string ControlMultiSelect = "MultiSelect";
    public const string ControlFile = "File";
    public const string ControlRating = "Rating";

    public sealed record ControlType(string Key, string Label, bool NeedsOptions, bool NeedsScale, bool SurveyOnly);

    /// <summary>
    /// الأنواع الاثنا عشر = أحد عشر بباني الطلبات + «تقييم» بالاستبيانات.
    /// دُمجت بقائمة واحدة لأن الباني واحد، وتُرشَّح بنوع النموذج عند العرض.
    /// </summary>
    public static readonly IReadOnlyList<ControlType> ControlTypes = new List<ControlType>
    {
        new(ControlText,        "نص",                         false, false, false),
        new(ControlTextArea,    "منطقة نص",                   false, false, false),
        new(ControlNumber,      "رقم",                        false, false, false),
        new(ControlDate,        "تاريخ",                      false, false, false),
        new(ControlTime,        "وقت",                        false, false, false),
        new(ControlDateRange,   "من – إلى تاريخ",             false, false, false),
        new(ControlTimeRange,   "من – إلى وقت",               false, false, false),
        new(ControlYesNo,       "نعم / لا / بلا إجابة",       false, false, false),
        new(ControlSelect,      "قائمة منسدلة",               true,  false, false),
        new(ControlMultiSelect, "قائمة متعددة الاختيار",      true,  false, false),
        new(ControlFile,        "أرفق ملف",                   false, false, false),
        new(ControlRating,      "تقييم",                      false, true,  true)
    };

    public static ControlType? FindControl(string? key) =>
        ControlTypes.FirstOrDefault(type => type.Key == key);

    public static string ControlLabel(string? key) => FindControl(key)?.Label ?? "حقل";

    /// <summary>الأنواع المتاحة لنوع نموذج بعينه — «تقييم» للاستبيانات وحدها.</summary>
    public static IReadOnlyList<ControlType> ControlsFor(string? formType) =>
        ControlTypes.Where(type => !type.SurveyOnly || IsSurveyLike(formType)).ToList();

    /// <summary>سلالم التقييم المسموحة (نفس كيان: 3–6).</summary>
    public static readonly IReadOnlyList<int> Scales = new[] { 3, 4, 5, 6 };

    /// <summary>
    /// سلّم غير مسموح ⟹ خمسة. و**السلّم الزوجي يُجبر على الانحياز والفرديّ يسمح
    /// بالحياد** — قرارٌ منهجي، ولذلك يُعرض للمستخدم لا يُفرض عليه.
    /// </summary>
    public static int NormalizeScale(int? scale) =>
        scale is { } value && Scales.Contains(value) ? value : 5;

    public static bool ScaleAllowsNeutral(int scale) => scale % 2 == 1;

    // ── التحقق من تعريف الحقل ──────────────────────────────────────────────────

    /// <summary>
    /// سبب رفض تعريف حقل، أو <c>null</c> إن كان سليماً.
    ///
    /// ⚠️ **منسدلة بلا خيارات هي أخطر تعريف ناقص**: تُحفظ وتُعرض فارغةً فلا يستطيع
    /// أحد الإجابة، ولا رسالة خطأ — نموذجٌ معطَّل بصمت.
    /// </summary>
    public static string? ValidateField(string? label, string? controlType, string? options, int? scale)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "نصّ الحقل إلزامي.";
        }

        var control = FindControl(controlType);
        if (control is null)
        {
            return "نوع الحقل غير معروف.";
        }

        if (control.NeedsOptions && ParseOptions(options).Count == 0)
        {
            return "القائمة المنسدلة تحتاج خياراً واحداً على الأقل.";
        }

        if (control.NeedsScale && scale is { } value && !Scales.Contains(value))
        {
            return "سلّم التقييم يجب أن يكون بين 3 و6.";
        }

        return null;
    }

    /// <summary>خيارات المنسدلة: سطرٌ لكل خيار، مع طيّ التكرار والفراغ.</summary>
    public static List<string> ParseOptions(string? raw) =>
        (raw ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(option => option.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // ── التحقق من الإجابة ──────────────────────────────────────────────────────

    /// <summary>
    /// سبب رفض إجابة، أو <c>null</c>. يُستعمل عند تعبئة النموذج لاحقاً.
    ///
    /// الحقل غير الإلزامي الفارغ **يمرّ** ولا يُفحص نوعه: قيمةٌ فارغة ليست قيمةً
    /// خاطئة، وفحصها يرفض نموذجاً سليماً.
    /// </summary>
    public static string? ValidateAnswer(
        string label, string? controlType, bool isRequired, string? options, string? answer)
    {
        var empty = string.IsNullOrWhiteSpace(answer);

        if (isRequired && empty)
        {
            return $"«{label}» إلزامي.";
        }

        if (empty)
        {
            return null;
        }

        var value = answer!.Trim();

        return controlType switch
        {
            ControlNumber when !decimal.TryParse(value, out _) => $"«{label}» يجب أن يكون رقماً.",
            ControlDate when !DateOnly.TryParse(value, out _) => $"«{label}» يجب أن يكون تاريخاً.",
            ControlTime when !TimeOnly.TryParse(value, out _) => $"«{label}» يجب أن يكون وقتاً.",
            ControlYesNo when value is not ("نعم" or "لا" or "بلا إجابة") => $"«{label}» يقبل: نعم · لا · بلا إجابة.",
            // القيمة خارج الخيارات المعرَّفة ⟹ إمّا تلاعب بالنموذج أو خيارٌ حُذف
            // بعد الإجابة؛ وكلاهما يستحقّ الرفض لا القبول الصامت.
            ControlSelect when !ParseOptions(options).Contains(value, StringComparer.OrdinalIgnoreCase) =>
                $"«{label}» يحمل قيمة خارج الخيارات المعرَّفة.",
            ControlMultiSelect when SplitMulti(value).Except(ParseOptions(options), StringComparer.OrdinalIgnoreCase).Any() =>
                $"«{label}» يحمل قيمة خارج الخيارات المعرَّفة.",
            _ => null
        };
    }

    public static List<string> SplitMulti(string? raw) =>
        (raw ?? string.Empty)
            .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .ToList();

    /// <summary>
    /// يفحص نموذجاً كاملاً ويُرجع **كل** الأخطاء لا أوّلها: مستخدمٌ يُصحّح خطأً
    /// ليُفاجأ بآخر يتخلّى عن النموذج.
    /// </summary>
    public static List<string> ValidateSubmission(
        IEnumerable<(string Label, string? ControlType, bool IsRequired, string? Options, string? Answer)> fields) =>
        fields
            .Select(field => ValidateAnswer(field.Label, field.ControlType, field.IsRequired, field.Options, field.Answer))
            .Where(error => error is not null)
            .Select(error => error!)
            .ToList();
}
