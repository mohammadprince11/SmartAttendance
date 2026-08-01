using System.Security.Cryptography;
using System.Text;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// التحقق من أصالة وثيقة صادرة (منشئ الوثائق — م٤) — منطق نقيّ.
///
/// ⭐ **أعلى قيمة مفردة بالمنشئ كلّه**: الوثيقة تحمل رمزاً يفحصه **طرفٌ ثالث**
/// (بنك · سفارة · جهة حكومية) فيتحقّق من صحّتها. تحوّل شهادة الراتب من ورقة
/// تُزوَّر بـWord إلى مستند قابل للتحقّق.
///
/// <b>ثلاثة مبادئ يقوم عليها هذا الملف:</b>
/// <list type="number">
/// <item><b>الرمز وحده لا يكفي.</b> فاحصٌ يحمل الرمز يجب أن يُثبت أنه يحمل الورقة
/// أيضاً — برقم وطني أو PIN. وإلا صار تخمين الرموز كشفاً لبيانات الموظفين.</item>
/// <item><b>الجواب أدنى ما يكفي.</b> «صحيحة · صدرت بتاريخ كذا · لفلان» — لا نصّ
/// الوثيقة ولا راتبها. التحقق إثباتُ أصالةٍ لا نافذةُ بيانات.</item>
/// <item><b>لا تمييز بين أسباب الفشل أمام الفاحص.</b> رمزٌ خطأ وعاملٌ خطأ يعطيان
/// نفس الجواب، وإلا صارت الصفحة أداةَ تخمينٍ تكشف أي الرموز موجودة.</item>
/// </list>
/// </summary>
public static class DocumentVerificationPolicy
{
    public const string FactorNone = "None";
    public const string FactorNationalId = "NationalId";
    public const string FactorPin = "Pin";

    public static string FactorLabel(string? factor) => factor switch
    {
        FactorNationalId => "الرقم الوطني",
        FactorPin => "رمز PIN",
        _ => "بلا عامل تحقق"
    };

    /// <summary>نتيجة التحقق كما تُعرض. التمييز داخليٌّ للسجلّ لا للفاحص.</summary>
    public enum Result
    {
        Valid,
        NotFound,
        FactorMismatch,
        Expired,
        Revoked
    }

    public static bool IsSuccess(Result result) => result == Result.Valid;

    /// <summary>
    /// الرسالة المعروضة للفاحص. <b>NotFound وFactorMismatch يشتركان بنفس النصّ</b>
    /// عمداً — التمييز بينهما يجعل الصفحة أداة تخمين.
    /// </summary>
    public static string Message(Result result) => result switch
    {
        Result.Valid => "الوثيقة صحيحة وصادرة عن النظام.",
        Result.Expired => "هذه الوثيقة صحيحة لكن انتهت مدة صلاحية التحقق منها.",
        Result.Revoked => "هذه الوثيقة سُحبت من قِبل الجهة المُصدِرة.",
        _ => "لا تطابق — تأكّد من رمز التحقق وعامل التحقق."
    };

    /// <summary>
    /// حروف الرمز: بلا <c>0/O/1/I/L</c> — الرمز يُنسخ من ورقة مطبوعة بالعين،
    /// والمحارف المتشابهة تُنتج شكاوى «الرمز لا يعمل» وهو صحيح.
    /// </summary>
    private const string TokenAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>رمز تحقق عشوائي بمجموعات رباعية (<c>ABCD-EFGH-JKMN</c>) لسهولة النسخ.</summary>
    public static string NewToken(int groups = 3)
    {
        var parts = new List<string>(groups);

        for (var group = 0; group < groups; group++)
        {
            var builder = new StringBuilder(4);
            for (var i = 0; i < 4; i++)
            {
                builder.Append(TokenAlphabet[RandomNumberGenerator.GetInt32(TokenAlphabet.Length)]);
            }
            parts.Add(builder.ToString());
        }

        return string.Join('-', parts);
    }

    /// <summary>PIN رقمي من ستّ خانات — يُعطى لحامل الوثيقة ولا يُخزَّن خاماً.</summary>
    public static string NewPin() => RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();

    /// <summary>يطبّع الرمز المدخل: الشرطات والمسافات وحالة الأحرف كلها تُتجاهل.</summary>
    public static string NormalizeToken(string? token) =>
        new string((token ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>
    /// تجزئة الـPIN بملح مشتقّ من الرمز: نفس الـPIN بوثيقتين يعطي جزءاً مختلفاً،
    /// فلا يكشف جدولُ التجزئة أن وثيقتين تشتركان بالرمز السرّي.
    /// </summary>
    public static string HashPin(string pin, string token)
    {
        var material = $"{NormalizeToken(token)}:{pin.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    public static bool PinMatches(string? pin, string token, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        // مقارنة ثابتة الزمن: مقارنة نصّية عادية تسرّب طول البادئة المطابقة.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(HashPin(pin, token)),
            Encoding.UTF8.GetBytes(storedHash));
    }

    /// <summary>
    /// الرقم الوطني يُقارَن **مطبَّعاً** (بلا فواصل ولا مسافات): الفاحص ينسخه من
    /// هوية ورقية بتنسيقات مختلفة.
    /// </summary>
    public static bool NationalIdMatches(string? entered, string? stored) =>
        !string.IsNullOrWhiteSpace(entered)
        && !string.IsNullOrWhiteSpace(stored)
        && NormalizeToken(entered) == NormalizeToken(stored);

    /// <summary>تاريخ انتهاء التحقق. مدّة غير موجبة ⟹ بلا انتهاء.</summary>
    public static DateOnly? ValidUntil(DateOnly issuedOn, int? validDays) =>
        validDays is { } days && days > 0 ? issuedOn.AddDays(days) : null;

    /// <summary>
    /// الحكم النهائي. الترتيب مقصود: **السحب يسبق الانتهاء**، فوثيقة سُحبت وانتهت
    /// يجب أن تُعلَن مسحوبةً — السحب واقعة إدارية والانتهاء مرور زمن.
    /// </summary>
    public static Result Evaluate(
        bool found,
        bool factorOk,
        DateTime? revokedAt,
        DateOnly? validUntil,
        DateOnly asOf)
    {
        if (!found || !factorOk)
        {
            // نفس النتيجة لكليهما بالعرض؛ التمييز هنا للسجلّ الداخلي.
            return found ? Result.FactorMismatch : Result.NotFound;
        }

        if (revokedAt is not null)
        {
            return Result.Revoked;
        }

        if (validUntil is { } until && asOf > until)
        {
            return Result.Expired;
        }

        return Result.Valid;
    }
}
