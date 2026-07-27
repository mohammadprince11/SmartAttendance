using System.Net.Mail;

namespace SmartAttendance.Web.Infrastructure.Imports;

/// <summary>
/// محلّل استيراد «بريد الموظفين فقط»: نص ملصوق من Excel (أو CSV) بعمودَين —
/// رقم الموظف ثم البريد الإلكتروني. بديل رشيق عن الاستيراد الشامل الذي يتطلب
/// الأعمدة الكاملة ويعيد كتابة الاسم/التعيين، فهنا لا يُمسّ إلا عمود Email.
/// منطق نقي قابل للاختبار؛ صف العناوين (إن وُجد) يُتجاوز تلقائياً، والمكرر
/// لنفس رقم الموظف: الأخير يفوز.
/// </summary>
public static class EmployeeEmailImportParser
{
    /// <summary>صف واحد محلَّل. <see cref="Error"/> null = صالح.</summary>
    public sealed record Entry(int LineNo, string EmployeeNo, string Email, string? Error);

    /// <summary>
    /// يحلّل النص الملصوق: سطر لكل موظف، الفاصل تبويب (لصق Excel) أو فاصلة/فاصلة منقوطة.
    /// يعيد الصفوف الصالحة (بعد حسم التكرار: الأخير يفوز) والمرفوضة معاً.
    /// </summary>
    public static (List<Entry> Valid, List<Entry> Rejected) Parse(string? pasted)
    {
        var valid = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var rejected = new List<Entry>();
        if (string.IsNullOrWhiteSpace(pasted)) return (new List<Entry>(), rejected);

        var lines = pasted.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;

            var cells = line.Split('\t', ',', ';')
                .Select(c => c.Trim().Trim('"'))
                .Where(c => c.Length > 0)
                .ToArray();
            if (cells.Length == 0) continue;

            // صف العناوين: لا بريد صالح فيه وأول ظهوره فقط
            if (i == 0 && IsHeaderRow(cells)) continue;

            if (cells.Length < 2)
            {
                rejected.Add(new Entry(i + 1, cells[0], string.Empty, "السطر ناقص — مطلوب عمودان: رقم الموظف والبريد."));
                continue;
            }

            var employeeNo = cells[0];
            var email = cells[1];

            if (!IsValidEmail(email))
            {
                rejected.Add(new Entry(i + 1, employeeNo, email, "صيغة البريد غير صالحة."));
                continue;
            }

            valid[employeeNo] = new Entry(i + 1, employeeNo, email, null);
        }

        return (valid.Values.OrderBy(e => e.LineNo).ToList(), rejected);
    }

    /// <summary>بريد صالح؟ عبر MailAddress مع رفض ما يحوي فراغات أو بلا نطاق منقّط.</summary>
    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Contains(' ')) return false;
        if (!MailAddress.TryCreate(email, out var parsed)) return false;
        // MailAddress يقبل نطاقاً بلا نقطة (user@host) — نرفضه لأنه شبه مؤكد خطأ إدخال
        var at = parsed.Address.LastIndexOf('@');
        return at > 0 && parsed.Address.IndexOf('.', at) > at + 1;
    }

    /// <summary>أسماء أعمدة معروفة (بلا فراغات) — مطابقة تامة لتفادي فخّ substring مثل not-an-email.</summary>
    private static readonly HashSet<string> HeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "e-mail", "mail", "البريد", "بريد", "البريدالإلكتروني", "البريدالالكتروني",
        "employeeno", "employeenumber", "رقمالموظف", "الرقم", "رقم"
    };

    private static bool IsHeaderRow(string[] cells) =>
        !cells.Any(IsValidEmail)
        && cells.Any(c => HeaderNames.Contains(c.Replace(" ", string.Empty)));
}
