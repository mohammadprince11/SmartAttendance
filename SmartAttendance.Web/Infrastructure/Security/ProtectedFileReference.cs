namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// مرجع ملف محمي: بدل إضافة عمود `ProtectedKey` لكل جدول مرفقات (مستندات، مالية،
/// سجلات، تأديبية، قروض، مرفقات طلبات) نُبقي عمود المسار القائم ونكتب فيه
/// <c>protected:{مفتاح التخزين}</c>. فلا هجرة مخطط لكل موديول، ويبقى تمييز الصفوف
/// الجديدة عن التاريخية (التي تبدأ بـ<c>/uploads/</c>) صريحاً بالقراءة.
///
/// الحمولة الموقّعة (employeeId|key) تُمرَّر لنقطة التنزيل مشفَّرة بـData Protection،
/// فلا يستطيع العميل تبديل الموظف أو المفتاح. **التوقيع ليس تخويلاً**: النقطة تفحص
/// صلاحية الموظف المستهدف بعد فكّ الحمولة — التوقيع يمنع العبث لا أكثر.
/// </summary>
public static class ProtectedFileReference
{
    public const string Prefix = "protected:";

    public static string ToStoredPath(string storageKey) => Prefix + storageKey;

    public static bool IsProtected(string? storedPath) =>
        !string.IsNullOrWhiteSpace(storedPath) &&
        storedPath.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>يستخرج مفتاح التخزين من قيمة العمود، أو null إن كان الصفّ تاريخياً.</summary>
    public static string? ExtractKey(string? storedPath) =>
        IsProtected(storedPath) ? storedPath![Prefix.Length..] : null;

    public static string EncodePayload(int employeeId, string storageKey) =>
        $"{employeeId}|{storageKey}";

    public static bool TryDecodePayload(string? payload, out int employeeId, out string storageKey)
    {
        employeeId = 0;
        storageKey = string.Empty;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var separator = payload.IndexOf('|');

        if (separator <= 0 || separator == payload.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(payload[..separator], out employeeId) || employeeId <= 0)
        {
            return false;
        }

        storageKey = payload[(separator + 1)..];
        return storageKey.Length > 0;
    }
}
