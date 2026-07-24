using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Api;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Controllers.Api;

/// <summary>
/// مصادقة واجهة الموبايل: تسجيل الدخول يُصدر توكن Bearer (يعيد استخدام تحقّق
/// الاعتماد نفسه المستخدم في صفحة الدخول)، والخروج يُلغيه.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);

    private readonly ApplicationDbContext _db;
    private readonly ILoginIdentityService _identity;

    public AuthController(ApplicationDbContext db, ILoginIdentityService identity)
    {
        _db = db;
        _identity = identity;
    }

    public sealed record LoginRequest(string Username, string Password);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
            return BadRequest(new { message = "اسم المستخدم وكلمة المرور مطلوبة." });

        await LoginDatabase.EnsureCreatedAsync(_db);
        var utcNow = DateTime.UtcNow;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var user = await LoginDatabase.GetByUsernameAsync(_db, body.Username.Trim());

        // رسالة موحّدة عند أي فشل (لا نكشف السبب)
        const string generic = "بيانات الدخول غير صحيحة أو الحساب غير متاح.";

        if (user is null || !user.IsActive || user.IsLockedOut(utcNow))
        {
            SimplePasswordHasher.PerformDummyVerification(body.Password);
            return Unauthorized(new { message = generic });
        }

        if (!SimplePasswordHasher.Verify(body.Password, user.PasswordSalt, user.PasswordHash))
        {
            await LoginDatabase.RecordFailedLoginAsync(_db, user, ip, utcNow);
            return Unauthorized(new { message = generic });
        }

        var displayName = string.IsNullOrWhiteSpace(user.EmployeeName) ? user.Username : user.EmployeeName;

        int? systemUserId;
        try
        {
            systemUserId = await _identity.EnsureSystemUserAsync(new LoginIdentityRequest
            {
                EmployeeId = user.EmployeeId,
                UserName = user.Username,
                DisplayName = displayName,
                CompatibilityRole = user.Role,
                IsActive = user.IsActive
            }, HttpContext.RequestAborted);
        }
        catch
        {
            return StatusCode(500, new { message = "تعذّر إكمال تسجيل الدخول حالياً." });
        }

        if (systemUserId is not > 0)
            return StatusCode(500, new { message = "تعذّر إكمال تسجيل الدخول حالياً." });

        await LoginDatabase.RecordSuccessfulLoginAsync(_db, user, ip);

        var token = await ApiTokenStore.IssueAsync(_db, new ApiTokenStore.TokenIdentity
        {
            SystemUserId = systemUserId.Value,
            EmployeeId = user.EmployeeId,
            Username = user.Username,
            Role = user.Role,
            DisplayName = displayName
        }, TokenLifetime);

        return Ok(new
        {
            token,
            expiresInDays = (int)TokenLifetime.TotalDays,
            user = new
            {
                username = user.Username,
                displayName,
                role = user.Role,
                employeeId = user.EmployeeId
            }
        });
    }

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = ApiTokenAuthHandler.SchemeName)]
    public async Task<IActionResult> Logout()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            await ApiTokenStore.RevokeAsync(_db, header["Bearer ".Length..].Trim());
        return Ok(new { message = "تم تسجيل الخروج." });
    }
}
