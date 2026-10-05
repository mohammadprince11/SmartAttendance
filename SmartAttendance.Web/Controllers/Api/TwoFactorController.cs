using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Api;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Controllers.Api;

[ApiController]
[Route("api/v1/auth/2fa")]
[Route("api/auth/2fa")]
[Authorize(AuthenticationSchemes = ApiTokenAuthHandler.SchemeName)]
public sealed class TwoFactorController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _protector;

    public TwoFactorController(
        ApplicationDbContext db,
        IMemoryCache cache,
        IDataProtectionProvider dataProtection)
    {
        _db = db;
        _cache = cache;
        _protector = dataProtection.CreateProtector("ZYNORA.Auth.Totp.v1");
    }

    public sealed record SetupRequest(string CurrentPassword);
    public sealed record EnableRequest(string Code);
    public sealed record DisableRequest(
        string CurrentPassword,
        string? Code,
        string? RecoveryCode);

    public sealed record RegenerateRequest(
        string CurrentPassword,
        string? Code,
        string? RecoveryCode);

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var user = await CurrentLoginUserAsync();
        if (user is null) return Unauthorized();

        var state = await AppLoginTwoFactorStore.GetAsync(
            _db,
            user.Id,
            cancellationToken);

        return Ok(new
        {
            enabled = state.IsEnabled,
            setupInProgress = !string.IsNullOrWhiteSpace(state.PendingSecretProtected),
            recoveryCodesRemaining = state.RecoveryCodeHashes.Count
        });
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup(
        [FromBody] SetupRequest body,
        CancellationToken cancellationToken)
    {
        var user = await CurrentLoginUserAsync();
        if (user is null) return Unauthorized();

        if (!ValidCurrentPassword(user, body?.CurrentPassword))
            return BadRequest(new { message = "كلمة المرور الحالية غير صحيحة." });

        var state = await AppLoginTwoFactorStore.GetAsync(
            _db,
            user.Id,
            cancellationToken);

        if (state.IsEnabled)
            return Conflict(new
            {
                message = "المصادقة الثنائية مفعلة بالفعل. عطّلها أولاً لإعادة الإعداد."
            });

        var secret = TotpSecurity.GenerateSecret();
        await AppLoginTwoFactorStore.BeginSetupAsync(
            _db,
            user.Id,
            _protector.Protect(secret),
            cancellationToken);

        return Ok(new
        {
            secret,
            otpAuthUri = TotpSecurity.BuildOtpAuthUri(
                "ZYNORA HR",
                user.Username,
                secret),
            digits = 6,
            periodSeconds = 30
        });
    }
    [HttpPost("enable")]
    public async Task<IActionResult> Enable(
        [FromBody] EnableRequest body,
        CancellationToken cancellationToken)
    {
        var user = await CurrentLoginUserAsync();
        if (user is null) return Unauthorized();

        var state = await AppLoginTwoFactorStore.GetAsync(
            _db,
            user.Id,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(state.PendingSecretProtected))
            return BadRequest(new { message = "ابدأ إعداد المصادقة الثنائية أولاً." });

        string secret;
        try
        {
            secret = _protector.Unprotect(state.PendingSecretProtected);
        }
        catch
        {
            return StatusCode(500, new { message = "تعذر قراءة إعداد المصادقة الثنائية." });
        }

        if (!TotpSecurity.ValidateCode(secret, body?.Code))
            return BadRequest(new { message = "رمز التحقق غير صحيح أو منتهي." });

        var recoveryCodes = TotpSecurity.GenerateRecoveryCodes();
        var hashes = recoveryCodes.Select(TotpSecurity.HashRecoveryCode).ToArray();
        await AppLoginTwoFactorStore.EnablePendingAsync(
            _db,
            user.Id,
            hashes,
            cancellationToken);

        await AccountSecurityStore.BumpStampAsync(
            _db,
            _cache,
            user.Id,
            "Two-factor authentication enabled",
            user.Username);

        return Ok(new
        {
            message = "تم تفعيل المصادقة الثنائية.",
            recoveryCodes,
            requiresLogin = true
        });
    }

    [HttpPost("disable")]
    public async Task<IActionResult> Disable(
        [FromBody] DisableRequest body,
        CancellationToken cancellationToken)
    {
        var user = await CurrentLoginUserAsync();
        if (user is null) return Unauthorized();

        if (!ValidCurrentPassword(user, body?.CurrentPassword))
            return BadRequest(new { message = "كلمة المرور الحالية غير صحيحة." });

        if (!await VerifySecondFactorAsync(
                user.Id,
                body?.Code,
                body?.RecoveryCode,
                consumeRecovery: true,
                cancellationToken))
            return BadRequest(new { message = "رمز المصادقة الثنائية غير صحيح." });
        await AppLoginTwoFactorStore.DisableAsync(
            _db,
            user.Id,
            cancellationToken);

        await AccountSecurityStore.BumpStampAsync(
            _db,
            _cache,
            user.Id,
            "Two-factor authentication disabled",
            user.Username);

        return Ok(new
        {
            message = "تم تعطيل المصادقة الثنائية.",
            requiresLogin = true
        });
    }

    [HttpPost("recovery-codes")]
    public async Task<IActionResult> RegenerateRecoveryCodes(
        [FromBody] RegenerateRequest body,
        CancellationToken cancellationToken)
    {
        var user = await CurrentLoginUserAsync();
        if (user is null) return Unauthorized();

        if (!ValidCurrentPassword(user, body?.CurrentPassword))
            return BadRequest(new { message = "كلمة المرور الحالية غير صحيحة." });

        if (!await VerifySecondFactorAsync(
                user.Id,
                body?.Code,
                body?.RecoveryCode,
                consumeRecovery: true,
                cancellationToken))
            return BadRequest(new { message = "رمز المصادقة الثنائية غير صحيح." });
        var recoveryCodes = TotpSecurity.GenerateRecoveryCodes();
        await AppLoginTwoFactorStore.ReplaceRecoveryCodesAsync(
            _db,
            user.Id,
            recoveryCodes.Select(TotpSecurity.HashRecoveryCode).ToArray(),
            cancellationToken);

        await AccountSecurityStore.BumpStampAsync(
            _db,
            _cache,
            user.Id,
            "Two-factor recovery codes regenerated",
            user.Username);

        return Ok(new
        {
            message = "تم إنشاء رموز استرداد جديدة.",
            recoveryCodes,
            requiresLogin = true
        });
    }

    private async Task<bool> VerifySecondFactorAsync(
        int loginUserId,
        string? code,
        string? recoveryCode,
        bool consumeRecovery,
        CancellationToken cancellationToken)
    {
        var state = await AppLoginTwoFactorStore.GetAsync(
            _db,
            loginUserId,
            cancellationToken);

        if (!state.IsEnabled || string.IsNullOrWhiteSpace(state.ActiveSecretProtected))
            return false;
        try
        {
            var secret = _protector.Unprotect(state.ActiveSecretProtected);
            if (TotpSecurity.ValidateCode(secret, code))
                return true;
        }
        catch
        {
            return false;
        }

        return consumeRecovery &&
               !string.IsNullOrWhiteSpace(recoveryCode) &&
               await AppLoginTwoFactorStore.ConsumeRecoveryCodeAsync(
                   _db,
                   loginUserId,
                   recoveryCode,
                   cancellationToken);
    }

    private async Task<LoginDatabase.LoginUser?> CurrentLoginUserAsync()
    {
        var username = User.Identity?.Name;
        return string.IsNullOrWhiteSpace(username)
            ? null
            : await LoginDatabase.GetByUsernameAsync(
                _db,
                TenantContext.GetTenantId(User) ?? 0,
                username.Trim());
    }

    private static bool ValidCurrentPassword(
        LoginDatabase.LoginUser user,
        string? currentPassword) =>
        !string.IsNullOrWhiteSpace(currentPassword) &&
        SimplePasswordHasher.Verify(
            currentPassword,
            user.PasswordSalt,
            user.PasswordHash);
}
