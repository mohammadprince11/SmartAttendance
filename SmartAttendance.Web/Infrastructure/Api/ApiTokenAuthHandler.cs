using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Api;

/// <summary>
/// معالج مصادقة توكن الموبايل (scheme «ApiToken»): يقرأ ترويسة
/// <c>Authorization: Bearer &lt;token&gt;</c>، يتحقق منه عبر <see cref="ApiTokenStore"/>،
/// ويبني <see cref="ClaimsPrincipal"/> بنفس أنواع المطالبات المستخدمة بالكوكيز
/// (EmployeeId/SystemUserId/Role/Name) فتعمل السياسات و<c>User</c> كالمعتاد.
/// </summary>
public sealed class ApiTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiToken";

    private readonly ApplicationDbContext _db;

    public ApiTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApplicationDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        var identity = await ApiTokenStore.ValidateAsync(_db, token);
        if (identity == null)
            return AuthenticateResult.Fail("توكن غير صالح أو منتهٍ.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.SystemUserId.ToString()),
            new(ClaimTypes.Name, identity.Username),
            new(ClaimTypes.Role, identity.Role),
            new("DisplayName", identity.DisplayName ?? string.Empty),
            new("EmployeeId", identity.EmployeeId?.ToString() ?? string.Empty),
            new("SystemUserId", identity.SystemUserId.ToString())
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
