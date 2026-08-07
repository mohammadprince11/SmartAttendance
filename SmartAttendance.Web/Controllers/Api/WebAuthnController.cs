using System.Security.Claims;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authentication;
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
    private readonly SmartAttendance.Application.Common.Security.ILoginIdentityService _loginIdentityService;
    private readonly ReverseProxyOptions _reverseProxy;

    public WebAuthnController(
        ApplicationDbContext db,
        IMemoryCache cache,
        SmartAttendance.Application.Common.Security.ILoginIdentityService loginIdentityService,
        Microsoft.Extensions.Options.IOptions<ReverseProxyOptions> reverseProxyOptions)
    {
        _db = db;
        _cache = cache;
        _loginIdentityService = loginIdentityService;
        _reverseProxy = reverseProxyOptions.Value;
    }

    /// <summary>
    /// نسخة Fido2 مضبوطة على نطاق الطلب الحالي: تعمل على النطاق الحي
    /// (portal.zynorahr.com عبر HTTPS) وعلى localhost أثناء التطوير بلا إعداد يدوي.
    /// خلف نفق Cloudflare يصل الطلب للـKestrel بـHTTP بينما أصل المتصفح HTTPS —
    /// فنحترم X-Forwarded-Proto وإلا فشل تحقق الـOrigin بكل عمليات WebAuthn.
    /// </summary>
    private Fido2 CreateFido2()
    {
        // المرحلة 10: لا نقرأ X-Forwarded-Proto الخام هنا — ForwardedHeadersMiddleware
        // يطبّعه لوسطاء موثوقين فقط ثم نبني الأصل من القيم المطبَّعة، مع قائمة
        // مضيفات بيضاء اختيارية بالإعدادات. أصل غير موثوق ⟹ رفض صريح لا تخمين.
        var origin = ForwardedOriginResolver.Resolve(
            Request.Scheme,
            Request.Host.Value,
            _reverseProxy.AllowedHosts);

        if (origin is null)
        {
            throw new InvalidOperationException(
                "تعذّر اشتقاق أصل موثوق لطلب WebAuthn (المضيف خارج القائمة المسموح بها).");
        }

        return new Fido2(new Fido2Configuration
        {
            ServerDomain = ForwardedOriginResolver.HostNameOnly(Request.Host.Value),
            ServerName = "Zynora HR",
            Origins = new HashSet<string> { origin }
        });
    }

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
        catch (Exception ex)
        {
            // نُظهر السبب الفعلي بدل «فشل حفظ المفتاح» العامة — التشخيص أهم من الإخفاء هنا.
            return BadRequest(new { message = $"خطأ أثناء حفظ المفتاح: {ex.Message}" });
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
        catch (Exception ex)
        {
            return BadRequest(new { message = $"خطأ أثناء التأكيد: {ex.Message}" });
        }
    }

    // ===== الدخول للبوابة بالمفتاح (بلا كلمة مرور) =====

    /// <summary>
    /// خيارات دخول بلا اسم مستخدم: قائمة مفاتيح مسموحة فارغة ⟹ الجهاز يعرض
    /// passkeys المكتشفة (المسجّلة لنطاقنا) ويعيد userHandle يدلّنا على الموظف.
    /// </summary>
    [HttpPost("login/options")]
    [AllowAnonymous]
    public IActionResult LoginOptions()
    {
        var options = CreateFido2().GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = Array.Empty<PublicKeyCredentialDescriptor>(),
            UserVerification = UserVerificationRequirement.Required
        });

        var key = Guid.NewGuid().ToString("N");
        _cache.Set($"webauthn:login:{key}", options, ChallengeLifetime);
        return Ok(new { key, options });
    }

    /// <summary>
    /// تحقق الدخول: توقيع صحيح بمفتاح «نشط» (معتمد من HR) ⟹ جلسة كوكي بنفس
    /// claims الدخول بكلمة المرور لحساب الموظف المرتبط. المفاتيح المعلّقة/الملغاة
    /// لا تدخل، وكلمة المرور تبقى بديلاً دائماً.
    /// </summary>
    [HttpPost("login/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginVerify([FromBody] AssertVerifyRequest request)
    {
        var cacheKey = $"webauthn:login:{request.Key}";
        if (!_cache.TryGetValue(cacheKey, out AssertionOptions? options) || options is null)
            return BadRequest(new { message = "انتهت صلاحية جلسة الدخول — أعد المحاولة." });
        _cache.Remove(cacheKey);

        var credentialId = WebAuthnCredentialStore.ToBase64Url(request.Assertion.RawId);
        var credential = await WebAuthnCredentialStore.FindByCredentialIdAsync(_db, credentialId);
        if (credential is null || !WebAuthnCredentialStore.IsUsable(credential.Status))
            return BadRequest(new { message = "المفتاح غير معروف أو غير معتمد بعد — سجّل الدخول بكلمة المرور." });

        try
        {
            var result = await CreateFido2().MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = request.Assertion,
                OriginalOptions = options,
                StoredPublicKey = Convert.FromBase64String(credential.PublicKey),
                StoredSignatureCounter = (uint)Math.Max(0, credential.SignCount),
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                    Task.FromResult(
                        System.Text.Encoding.UTF8.GetString(args.UserHandle) == credential.EmployeeId.ToString())
            });

            await WebAuthnCredentialStore.UpdateSignCountAsync(_db, credential.Id, result.SignCount);
        }
        catch (Fido2VerificationException ex)
        {
            return BadRequest(new { message = $"فشل التحقق: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"خطأ أثناء التحقق: {ex.Message}" });
        }

        // حساب الدخول المرتبط بالموظف (نفضّل حساب دور «موظف»).
        var username = await HrmsDatabase.ScalarAsync<string>(
            _db,
            """
SELECT TOP 1 Username FROM AppLoginUsers
WHERE EmployeeId = @Emp AND IsActive = 1
ORDER BY CASE WHEN Role = 'Employee' THEN 0 ELSE 1 END, Id;
""",
            c => HrmsDatabase.AddParameter(c, "@Emp", credential.EmployeeId));
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest(new { message = "لا يوجد حساب دخول نشط مرتبط بهذا الموظف." });

        var user = await LoginDatabase.GetByUsernameAsync(_db, username);
        if (user is null || !user.IsActive)
            return BadRequest(new { message = "الحساب غير متاح." });
        if (user.IsLockedOut(DateTime.UtcNow))
            return BadRequest(new { message = "الحساب مقفل مؤقتاً — حاول لاحقاً أو استخدم كلمة المرور." });

        var displayName = string.IsNullOrWhiteSpace(user.EmployeeName) ? user.Username : user.EmployeeName;
        int? systemUserId;
        try
        {
            systemUserId = await _loginIdentityService.EnsureSystemUserAsync(
                new SmartAttendance.Application.Common.Security.LoginIdentityRequest
                {
                    EmployeeId = user.EmployeeId,
                    UserName = user.Username,
                    DisplayName = displayName,
                    CompatibilityRole = user.Role,
                    IsActive = user.IsActive
                },
                HttpContext.RequestAborted);
        }
        catch
        {
            return BadRequest(new { message = "تعذر إكمال تسجيل الدخول حالياً — استخدم كلمة المرور." });
        }
        if (systemUserId is not > 0)
            return BadRequest(new { message = "تعذر إكمال تسجيل الدخول حالياً — استخدم كلمة المرور." });

        // ختم الأمان **إلزامي** بكل تذكرة. بدونه يعيد SessionSecurityValidator
        // القرار Refresh لا Reject، فتُختم جلسة الـpasskey تلقائياً بالختم الجديد
        // بعد أي تدوير — أي أن تغيير كلمة المرور أو سحب الصلاحية لا يُسقطها،
        // ويبقى مهاجمٌ يملك جلسة passkey داخلاً بعد أن تغيّر الضحية كلمة مرورها.
        var securityStamp = !string.IsNullOrWhiteSpace(user.SecurityStamp)
            ? user.SecurityStamp
            : await AccountSecurityStore.EnsureStampAsync(_db, user.Id);

        // نفس claims مسار كلمة المرور (Login.cshtml.cs) — جلسة قياسية 8 ساعات.
        var issuedUtc = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("DisplayName", displayName),
            new("EmployeeId", user.EmployeeId?.ToString() ?? string.Empty),
            new("SystemUserId", systemUserId.Value.ToString()),
            new("SessionIssuedUtc", issuedUtc.ToString("O")),
            new(AccountSecurityStore.SecurityStampClaimType, securityStamp)
        };

        await LoginDatabase.RecordSuccessfulLoginAsync(
            _db, user, HttpContext.Connection.RemoteIpAddress?.ToString());

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                IsPersistent = false,
                IssuedUtc = issuedUtc,
                ExpiresUtc = issuedUtc.AddHours(8),
                AllowRefresh = true
            });

        var isEmployee = user.Role.Equals("Employee", StringComparison.OrdinalIgnoreCase);
        return Ok(new { redirect = isEmployee ? "/EmployeePortal" : "/" });
    }
}
