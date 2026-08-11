namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// تهريب قيمة نصّية لتوضَع داخل <b>حرفيّة سلسلة JS بعلامات مفردة</b> ثمّ داخل
/// خاصّية HTML (مثل <c>onclick="fn('...')"</c>).
///
/// <b>لماذا لا يكفي <c>Replace("'", "\\'")</c>:</b> الخاصّية بعلامات مزدوجة،
/// فقيمة فيها <c>"</c> أو <c>&lt;</c> تكسر الخاصّية ⟹ XSS. الحل طبقتان:
/// <list type="number">
///   <item>هذه الدالة تُهرّب طبقة JS فقط: <c>\</c> و<c>'</c> وفواصل الأسطر.</item>
///   <item>ثمّ يُستدعى الناتج <b>بلا</b> <c>Html.Raw</c>، فيُرمّز Razor طبقة الخاصّية
///   (<c>" &amp; &lt; &gt; '</c>)؛ ومحلّل HTML يفكّها داخل قيمة الخاصّية فيسلّم JS
///   سلسلةً سليمةً لا تعيد الدخول لسياق HTML أبداً.</item>
/// </list>
/// النتيجة: يستحيل الخروج من الخاصّية أو من سلسلة JS مهما كان المُدخَل.
/// </summary>
public static class ScriptSafe
{
    /// <summary>
    /// يُرجِع القيمة مُهرّبةً لطبقة سلسلة JS. لا يُغلَّف بعلامات — يضعها القالب.
    /// استعملها <b>بلا</b> <c>Html.Raw</c> ليُكمِل Razor ترميز الخاصّية.
    /// </summary>
    public static string Js(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\'':
                    sb.Append("\\'");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\u2028':
                    sb.Append("\\u2028");
                    break;
                case '\u2029':
                    sb.Append("\\u2029");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }
}
