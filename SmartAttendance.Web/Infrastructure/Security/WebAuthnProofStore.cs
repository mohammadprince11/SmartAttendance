using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// إثباتات بيولوجية قصيرة العمر تُستهلك مرة واحدة: بعد نجاح تحقق WebAuthn لحظة
/// البصم يُصدَر توكن عشوائي يربط الموظف بلحظة التحقق، ويقدَّم مع نموذج البصمة
/// فيُستهلك (يُحذف) عند القبول — لا يصلح لإعادة الاستعمال ولا لموظف آخر ولا بعد
/// انقضاء النافذة. خزن بالذاكرة (خادم واحد) والمنطق الزمني نقي قابل للاختبار.
/// </summary>
public static class WebAuthnProofStore
{
    /// <summary>نافذة صلاحية الإثبات: التحقق البيولوجي يجب أن يسبق البصمة بلحظات.</summary>
    public static readonly TimeSpan ProofWindow = TimeSpan.FromMinutes(2);

    private static readonly ConcurrentDictionary<string, (int EmployeeId, DateTime IssuedUtc)> Proofs = new();

    /// <summary>منطق نقي: هل الإثبات صالح لهذا الموظف بهذه اللحظة؟</summary>
    public static bool IsValid(
        (int EmployeeId, DateTime IssuedUtc) proof, int employeeId, DateTime nowUtc, TimeSpan window) =>
        proof.EmployeeId == employeeId
        && nowUtc >= proof.IssuedUtc
        && nowUtc - proof.IssuedUtc <= window;

    /// <summary>إصدار توكن إثبات لموظف تحقق بيولوجياً للتو.</summary>
    public static string Issue(int employeeId)
    {
        Cleanup(DateTime.UtcNow);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Proofs[token] = (employeeId, DateTime.UtcNow);
        return token;
    }

    /// <summary>استهلاك التوكن (مرة واحدة): يرجع صحيحاً فقط إن كان صالحاً لهذا الموظف الآن.</summary>
    public static bool Consume(string? token, int employeeId)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (!Proofs.TryRemove(token, out var proof)) return false;
        return IsValid(proof, employeeId, DateTime.UtcNow, ProofWindow);
    }

    private static void Cleanup(DateTime nowUtc)
    {
        foreach (var pair in Proofs)
        {
            if (nowUtc - pair.Value.IssuedUtc > ProofWindow)
                Proofs.TryRemove(pair.Key, out _);
        }
    }
}
