using System.Security.Claims;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Controllers.Api;

/// <summary>
/// نقاط WebAuthn/Passkeys (بصمة/وجه الجهاز): تسجيل مفتاح من بوابة الموظف (بحالة
/// «معلّق» حتى يعتمده HR)، وتأكيد بيولوجي لحظة البصم الأونلاين. التحقق كله بالخادم
/// (Fido2NetLib): التحدّي يُصدَر هنا ويُخزَّن بذاكرة الخادم دقائق معدودة — لا ثقة
/// بأي شيء يرسله العميل سوى توقيعٍ يُطابق التحدّي والمفتاح العام المخزَّن.
/// </summary>
[ApiController]
[Route("api/webauthn")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class WebAuthnController : ControllerBase
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public WebAuthnController(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>
    /// نسخة Fido2 مضبوطة على نطاق الطلب الحالي: تعمل على النطاق الحي
    /// (portal.zynorahr.com عبر HTTPS) وعلى localhost أثناء التطوير بلا إعداد يدوي.
    /// </summary>
    private Fido2 CreateFido2() => new(new Fido2Configuration
    {
        ServerDomain = Request.Host.Host,
        ServerName = "Zynora HR",
        Origins = new HashSet<string> { $"{Request.Scheme}://{Request.Host}" }
    });

    private async Task<int> ResolveEmployeeIdAsync()
    {
        var claim = User.FindFirstValue("EmployeeId");
        if (int.TryParse(claim, out var id) && id > 0) return id;

        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username)) return 0;
        return await HrmsDatabase.ScalarAsync<int>(
            _db,
            "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @U AND IsActive = 1",
            c => HrmsDatabase.AddParameter(c, "@U", username));
    }

    // ===== التسجيل (من درج إعدادات بوابة الموظف) =====

    /// <summary>خيارات إنشاء مفتاح جديد: تحدّي + هوية مستخدم + استثناء المفاتيح المسجّلة.</summary>
    [HttpPost("register/options")]
    public async Task<IActionResult> RegisterOptions()
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            return BadRequest(new { message = "الحساب غير مرتبط بموظف." });

        var employeeName = await HrmsDatabase.ScalarAsync<string>(
            _db, "SELECT FullName FROM Employees WHERE Id = @Id",
            c => HrmsDatabase.AddParameter(c, "@Id", employeeId)) ?? $"موظف {employeeId}";

        var existing = await WebAuthnCredentialStore.ListForEmployeeAsync(_db, employeeId);

        var options = CreateFido2().RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                // معرّف المستخدم = رقم الموظف (يعود بالـuserHandle عند الدخول بالمفتاح)
                Id = System.Text.Encoding.UTF8.GetBytes(employeeId.ToString()),
                Name = User.Identity?.Name ?? employeeId.ToString(),
                DisplayName = employeeName
            },
            ExcludeCredentials = existing
                .Select(c => new PublicKeyCredentialDescriptor(
                    WebAuthnCredentialStore.FromBase64Url(c.CredentialId)))
                .ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                // بيولوجيا الجهاز نفسه (بصمة/وجه) لا مفاتيح USB خارجية
                AuthenticatorAttachment = AuthenticatorAttachment.Platform,
                UserVerification = UserVerificationRequirement.Required,
                // مفتاح مكتشَف ليصلح للدخول بلا اسم مستخدم
                ResidentKey = ResidentKeyRequirement.Preferred
            },
            AttestationPreference = AttestationConveyancePreference.None
        });

        var key = Guid.NewGuid().ToString("N");
        _cache.Set($"webauthn:reg:{key}:{employeeId}", options, ChallengeLifetime);
        return Ok(new { key, options });
    }

    public sealed class RegisterCompleteRequest
    {
        public string Key { get; set; } = string.Empty;
        public string? Label { get; set; }
        public AuthenticatorAttestationRawResponse Attestation { get; set; } = default!;
    }

    /// <summary>إتمام التسجيل: تحقق التوقيع/التحدّي ثم حفظ المفتاح بحالة «معلّق» لاعتماد HR.</summary>
    [HttpPost("register/complete")]
    public async Task<IActionResult> RegisterComplete([FromBody] RegisterCompleteRequest request)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            return BadRequest(new { message = "الحساب غير مرتبط بموظف." });

        var cacheKey = $"webauthn:reg:{request.Key}:{employeeId}";
        if (!_cache.TryGetValue(cacheKey, out CredentialCreateOptions? options) || options is null)
            return BadRequest(new { message = "انتهت صلاحية جلسة التسجيل — أعد المحاولة." });
        _cache.Remove(cacheKey);

        try
        {
            var credential = await CreateFido2().MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = request.Attestation,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = async (args, _) =>
                    await WebAuthnCredentialStore.FindByCredentialIdAsync(
                        _db, WebAuthnCredentialStore.ToBase64Url(args.CredentialId)) is null
            });

            var (ok, message) = await WebAuthnCredentialStore.RegisterAsync(
                _db,
                employeeId,
                WebAuthnCredentialStore.ToBase64Url(credential.Id),
                Convert.ToBase64String(credential.PublicKey),
                credential.SignCount,
                string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
                credential.AaGuid.ToString());

            return ok ? Ok(new { message }) : BadRequest(new { message });
        }
        catch (Fido2VerificationException ex)
        {
            return BadRequest(new { message = $"فشل التحقق من المفتاح: {ex.Message}" });
        }
    }

    // ===== التأكيد البيولوجي لحظة البصم =====

    /// <summary>خيارات التأكيد: تحدٍّ مقيّد بالمفتاح النشط المعتمد لهذا الموظف فقط.</summary>
    [HttpPost("punch/options")]
    public async Task<IActionResult> PunchOptions()
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            return BadRequest(new { message = "الحساب غير مرتبط بموظف." });

        var active = await WebAuthnCredentialStore.GetActiveForEmployeeAsync(_db, employeeId);
        if (active is null)
            return BadRequest(new { message = "لا يوجد مفتاح بصمة/وجه معتمد لهذا الحساب." });

        var options = CreateFido2().GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = new[]
            {
                new PublicKeyCredentialDescriptor(
                    WebAuthnCredentialStore.FromBase64Url(active.CredentialId))
            },
            UserVerification = UserVerificationRequirement.Required
        });

        var key = Guid.NewGuid().ToString("N");
        _cache.Set($"webauthn:punch:{key}:{employeeId}", options, ChallengeLifetime);
        return Ok(new { key, options });
    }

    public sealed class AssertVerifyRequest
    {
        public string Key { get; set; } = string.Empty;
        public AuthenticatorAssertionRawResponse Assertion { get; set; } = default!;
    }

    /// <summary>
    /// تحقق التأكيد: توقيع صحيح بالمفتاح النشط ⟹ توكن إثبات أحادي الاستهلاك
    /// (دقيقتان) يُرفَق بنموذج البصمة فيقبله الخادم.
    /// </summary>
    [HttpPost("punch/verify")]
    public async Task<IActionResult> PunchVerify([FromBody] AssertVerifyRequest request)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            return BadRequest(new { message = "الحساب غير مرتبط بموظف." });

        var cacheKey = $"webauthn:punch:{request.Key}:{employeeId}";
        if (!_cache.TryGetValue(cacheKey, out AssertionOptions? options) || options is null)
            return BadRequest(new { message = "انتهت صلاحية جلسة التأكيد — أعد المحاولة." });
        _cache.Remove(cacheKey);

        var active = await WebAuthnCredentialStore.GetActiveForEmployeeAsync(_db, employeeId);
        if (active is null)
            return BadRequest(new { message = "لا يوجد مفتاح معتمد." });

        try
        {
            var result = await CreateFido2().MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = request.Assertion,
                OriginalOptions = options,
                StoredPublicKey = Convert.FromBase64String(active.PublicKey),
                StoredSignatureCounter = (uint)Math.Max(0, active.SignCount),
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                    Task.FromResult(
                        System.Text.Encoding.UTF8.GetString(args.UserHandle) == employeeId.ToString())
            });

            await WebAuthnCredentialStore.UpdateSignCountAsync(_db, active.Id, result.SignCount);
            return Ok(new { token = WebAuthnProofStore.Issue(employeeId) });
        }
        catch (Fido2VerificationException ex)
        {
            return BadRequest(new { message = $"فشل التأكيد البيولوجي: {ex.Message}" });
        }
    }
}
