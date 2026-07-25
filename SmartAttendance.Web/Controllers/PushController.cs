using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Web.Controllers;

/// <summary>
/// دورة حياة اشتراكات Web-Push لبوابة الموظف (مصادقة كوكي). يسلّم مفتاح VAPID العام
/// للعميل ليشترك، يخزّن/يحذف الاشتراك مربوطاً بالموظف الحالي، ويرسل دفعة اختبار.
/// خارج مسار /api/ عمداً (ذاك مقيّد بتوكن الموبايل، والبوابة كوكي).
/// </summary>
[ApiController]
[Route("push")]
[Authorize]
public sealed class PushController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly VapidOptions _vapid;
    private readonly IWebPushSender _push;

    public PushController(ApplicationDbContext db, IOptions<VapidOptions> vapid, IWebPushSender push)
    {
        _db = db;
        _vapid = vapid.Value;
        _push = push;
    }

    public sealed record SubscribeRequest(string Endpoint, string P256dh, string Auth);

    /// <summary>المفتاح العام لتهيئة PushManager.subscribe بالعميل. فارغ = القناة معطّلة.</summary>
    [HttpGet("vapid-key")]
    public IActionResult VapidKey() =>
        Ok(new { enabled = _vapid.IsUsable, publicKey = _vapid.IsUsable ? _vapid.PublicKey : null });

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0) return BadRequest(new { message = "الحساب غير مرتبط بموظف." });
        if (string.IsNullOrWhiteSpace(req.Endpoint) || string.IsNullOrWhiteSpace(req.P256dh) || string.IsNullOrWhiteSpace(req.Auth))
            return BadRequest(new { message = "بيانات الاشتراك ناقصة." });

        await PushSubscriptionStore.SaveAsync(_db, employeeId,
            new PushSubscriptionStore.Subscription(req.Endpoint, req.P256dh, req.Auth));
        return Ok(new { ok = true });
    }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] SubscribeRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.Endpoint))
            await PushSubscriptionStore.DeleteAsync(_db, req.Endpoint);
        return Ok(new { ok = true });
    }

    /// <summary>يرسل دفعة اختبار لأجهزة الموظف الحالي — للتحقق من الإعداد حياً.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test()
    {
        if (!_push.IsEnabled) return BadRequest(new { message = "قناة الدفع غير مفعّلة (VAPID غير مُعدّ)." });
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0) return BadRequest(new { message = "الحساب غير مرتبط بموظف." });

        var delivered = await _push.SendToEmployeeAsync(_db, employeeId,
            new PushPayload("إشعار تجريبي", "وصلك هذا الإشعار — قناة الدفع تعمل ✅", "/EmployeePortal"));
        return Ok(new { delivered });
    }

    private async Task<int> ResolveEmployeeIdAsync()
    {
        if (int.TryParse(User.FindFirstValue("EmployeeId"), out var claimId) && claimId > 0)
            return claimId;

        var username = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username)) return 0;

        return await HrmsDatabase.ScalarAsync<int>(
            _db,
            "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @Username AND IsActive = 1",
            command => HrmsDatabase.AddParameter(command, "@Username", username));
    }
}
