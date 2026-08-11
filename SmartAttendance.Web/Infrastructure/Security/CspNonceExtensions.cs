namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// يحمل nonce طلبِ الـCSP ويكشفه للعروض. العرض يضع <c>nonce="@Context.GetCspNonce()"</c>
/// على كل <c>&lt;script&gt;</c>/<c>&lt;style&gt;</c> مضمّن ليعمل تحت السياسة الصارمة.
/// عند تعطيل الراية يعود فارغاً فتُهمَل الخاصّية بلا ضرر.
/// </summary>
public static class CspNonceExtensions
{
    private const string ItemKey = "CspNonce";

    public static void SetCspNonce(this HttpContext context, string nonce) =>
        context.Items[ItemKey] = nonce;

    public static string? GetCspNonce(this HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
}
