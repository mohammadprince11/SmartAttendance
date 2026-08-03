using System.Globalization;
using System.Text;

namespace SmartAttendance.Web.Infrastructure.Imports;

/// <summary>
/// قراءة مبلغ مالي من خليّة إكسل كتبها إنسان — دالة نقية بلا قاعدة بيانات.
///
/// الخلية الواقعية تأتي بفواصل آلاف، أو مسافة غير فاصلة يُدرجها إكسل، أو أرقام
/// عربية-هندية، أو عملة ملحقة («٥٠٠٬٠٠٠ د.ع»). كلها يجب أن تُقرأ رقماً واحداً.
///
/// ⚠️ الفاصلة العشرية تُقرأ بثقافة ثابتة (<see cref="CultureInfo.InvariantCulture"/>)
/// عمداً: لو تُركت لثقافة الجهاز لاختلف تفسير <c>1.5</c> بين خادمٍ وآخر — وهذا
/// فرقٌ بالراتب لا بالعرض.
/// </summary>
public static class ImportAmountParser
{
    /// <summary>الفاصلة العشرية العربية (U+066B).</summary>
    private const char ArabicDecimalSeparator = '٫';

    /// <summary>فواصل الآلاف المقبولة: لاتينية · عربية (U+066C) · فاصلة عربية.</summary>
    private const string ThousandsSeparators = ",٬،";

    public static bool TryParse(string? value, out decimal result)
    {
        result = 0m;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            // الأرقام العربية-الهندية (٠-٩) والفارسية (۰-۹) تُحوَّل لنظيرها اللاتيني.
            if (character is >= '٠' and <= '٩')
            {
                builder.Append((char)('0' + (character - '٠')));
                continue;
            }

            if (character is >= '۰' and <= '۹')
            {
                builder.Append((char)('0' + (character - '۰')));
                continue;
            }

            if (char.IsDigit(character) || character is '.' or '-')
            {
                builder.Append(character);
                continue;
            }

            // تُحوَّل هنا لا بعد الحلقة: لو تُركت لقاعدة «أي محرف آخر يُسقط»
            // لصار ١٫٥ يُقرأ ١٥ — خطأ صامت بعشرة أضعاف.
            if (character == ArabicDecimalSeparator)
            {
                builder.Append('.');
                continue;
            }

            if (ThousandsSeparators.Contains(character) ||
                char.IsWhiteSpace(character))
            {
                continue;
            }

            // رموز العملة وأي حرف آخر تُسقط، والحكم النهائي على الناتج المجمَّع.
        }

        return decimal.TryParse(
            builder.ToString(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out result);
    }
}
