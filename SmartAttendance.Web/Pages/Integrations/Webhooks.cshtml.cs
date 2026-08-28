using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Integrations;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Integrations;

public sealed class WebhooksModel : PageModel
{
    private static readonly Regex EventName = new("^[a-z][a-z0-9_.-]{1,99}$", RegexOptions.Compiled);
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;
    private readonly IDataProtector _protector;

    public WebhooksModel(
        ApplicationDbContext db, ICompanyScopeProvider companyScope,
        IDataProtectionProvider dataProtection)
    {
        _db = db;
        _companyScope = companyScope;
        _protector = dataProtection.CreateProtector("ZYNORA.Webhooks.Secret.v1");
    }

    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }
    public List<CompanyOption> Companies { get; private set; } = [];
    public List<WebhookStore.Subscription> Subscriptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var scope = await _companyScope.GetAsync();
        await LoadCompaniesAsync(scope);
        if (CompanyId is > 0 && !scope.Allows(CompanyId.Value)) return Forbid();
        if (CompanyId is null && Companies.Count == 1) CompanyId = Companies[0].Id;
        if (CompanyId is > 0)
            Subscriptions = await WebhookStore.ListSubscriptionsAsync(_db, scope, CompanyId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(
        int companyId, int id, string name, string endpointUrl, string eventsCsv,
        bool isActive = false, bool rotateSecret = false)
    {
        var scope = await _companyScope.GetAsync();
        if (!scope.Allows(companyId)) return Forbid();
        if (string.IsNullOrWhiteSpace(name) ||
            !Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint) ||
            !WebhookEndpointPolicy.IsAllowed(endpoint))
        {
            TempData["ErrorMessage"] = "الاسم وعنوان HTTPS عام صحيح مطلوبان.";
            return RedirectToPage(new { CompanyId = companyId });
        }

        var events = NormalizeEvents(eventsCsv);
        if (events is null)
        {
            TempData["ErrorMessage"] = "أسماء الأحداث غير صحيحة. استخدم * أو أسماء مثل employee.updated مفصولة بفواصل.";
            return RedirectToPage(new { CompanyId = companyId });
        }

        string protectedSecret;
        if (id == 0 || rotateSecret)
        {
            var rawSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            protectedSecret = _protector.Protect(rawSecret);
            TempData["WebhookSecret"] = rawSecret;
        }
        else
        {
            protectedSecret = string.Empty;
        }

        await WebhookStore.SaveSubscriptionAsync(
            _db, scope, companyId, id, name, endpoint, protectedSecret, events, isActive);
        TempData["SuccessMessage"] = id > 0 ? "تم تحديث الاشتراك." : "تم إنشاء الاشتراك.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    public async Task<IActionResult> OnPostDisableAsync(int companyId, int id)
    {
        var scope = await _companyScope.GetAsync();
        if (!scope.Allows(companyId)) return Forbid();
        await WebhookStore.DeleteSubscriptionAsync(_db, scope, companyId, id);
        TempData["SuccessMessage"] = "تم تعطيل الاشتراك مع إبقاء سجل التسليم للتدقيق.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    public async Task<IActionResult> OnPostTestAsync(int companyId)
    {
        var scope = await _companyScope.GetAsync();
        if (!scope.Allows(companyId)) return Forbid();
        var idempotency = $"test:{companyId}:{Guid.NewGuid():N}";
        await WebhookStore.EnqueueAsync(_db, companyId, "integration.test",
            new { eventType = "integration.test", companyId, occurredAt = DateTimeOffset.UtcNow },
            idempotency);
        TempData["SuccessMessage"] = "أضيف حدث الاختبار إلى صندوق الإرسال.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    private static string? NormalizeEvents(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "*") return "*";
        var events = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        return events.Length is > 0 and <= 50 && events.All(item => EventName.IsMatch(item))
            ? string.Join(',', events)
            : null;
    }

    private async Task LoadCompaniesAsync(CompanyScope scope)
    {
        var query = _db.Companies.AsNoTracking().Where(company => company.IsActive && !company.IsDeleted);
        if (!scope.IsUnrestricted)
        {
            var allowed = scope.AllowedCompanyIds.ToArray();
            query = query.Where(company => allowed.Contains(company.Id));
        }
        Companies = await query.OrderBy(company => company.Name)
            .Select(company => new CompanyOption(company.Id, company.Name)).ToListAsync();
    }

    public sealed record CompanyOption(int Id, string Name);
}
