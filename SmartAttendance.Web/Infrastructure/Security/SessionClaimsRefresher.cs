using System.Security.Claims;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// المرحلة 5 — تحديث آمن لمطالبات تذكرة قائمة: يستبدل الدور والختم بقيم الحساب
/// الحالية. لا يمنح شيئاً لم يقرّره الخادم: القيم مصدرها قراءة الحساب من القاعدة
/// لا من العميل.
/// </summary>
public static class SessionClaimsRefresher
{
    public static void Apply(ClaimsIdentity identity, AccountSecurityState account)
    {
        Replace(identity, ClaimTypes.Role, account.Role);
        Replace(
            identity,
            AccountSecurityStore.SecurityStampClaimType,
            account.SecurityStamp ?? string.Empty);
    }

    private static void Replace(ClaimsIdentity identity, string claimType, string value)
    {
        foreach (var existing in identity.FindAll(claimType).ToList())
        {
            identity.RemoveClaim(existing);
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            identity.AddClaim(new Claim(claimType, value));
        }
    }
}
