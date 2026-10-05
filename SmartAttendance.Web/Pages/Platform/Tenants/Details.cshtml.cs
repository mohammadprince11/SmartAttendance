using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Platform;

namespace SmartAttendance.Web.Pages.Platform.Tenants;

[Authorize(Policy = PlatformAuthenticationDefaults.Policy)]
public sealed class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public DetailsModel(ApplicationDbContext db) => _db = db;

    public PlatformPortalStore.TenantSummary Tenant { get; private set; } = null!;
    public PlatformPortalStore.TenantAdminAccount? TenantAdmin { get; private set; }
    public List<PlatformPortalStore.AuditEvent> AuditEvents { get; private set; } = [];
    public List<PlatformPortalStore.SubscriptionInvoice> Invoices { get; private set; } = [];
    public IReadOnlyDictionary<string, string> Modules => PlatformPortalStore.ModuleCatalog;
    public IReadOnlyDictionary<string, string> PaymentMethods => PlatformPortalStore.PaymentMethodCatalog;
    public IReadOnlyDictionary<string, string> Currencies => PlatformPortalStore.CurrencyCatalog;
    public IReadOnlyList<PlatformPlanCatalog.Plan> Plans => PlatformPlanCatalog.Plans;
    public bool HasCustomPlan => PlatformPlanCatalog.Find(Tenant.PlanCode) is null;
    public int? DaysUntilExpiry => Tenant.ExpiresAtUtc.HasValue
        ? (int)Math.Ceiling((Tenant.ExpiresAtUtc.Value.Date - DateTime.UtcNow.Date).TotalDays)
        : null;
    public string PaidTotalsText
    {
        get
        {
            var totals = Invoices
                .Where(invoice => invoice.Status == "Paid")
                .GroupBy(invoice => invoice.Currency, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Sum(invoice => invoice.Amount):N2} {group.Key}")
                .ToArray();
            return totals.Length == 0 ? "0.00" : string.Join(" • ", totals);
        }
    }
    public int EmployeeUsagePercent => UsagePercent(Tenant.EmployeeCount, Tenant.MaxEmployees);
    public int DeviceUsagePercent => UsagePercent(Tenant.DeviceCount, Tenant.MaxDevices);
    public int CompanyUsagePercent => UsagePercent(Tenant.CompanyCount, Tenant.MaxCompanies);

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public string[] EnabledModules { get; set; } = [];

    [BindProperty]
    public RenewalInputModel Renewal { get; set; } = new();

    [BindProperty]
    public DomainInputModel Domain { get; set; } = new();

    public CustomerProfileInputModel CustomerProfile { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? LicenseMessage { get; set; }

    [TempData]
    public string? LicenseError { get; set; }

    public sealed class InputModel
    {
        public int TenantId { get; set; }
        public string VersionToken { get; set; } = string.Empty;

        // jQuery length rules count selected options, not the selected code's characters.
        // Regex validates the actual string on both client and server.
        [Required(ErrorMessage = "اختر خطة الترخيص."), RegularExpression(@"^[\s\S]{2,60}$", ErrorMessage = "اسم الخطة يجب أن يكون بين حرفين و60 حرفاً.")]
        public string PlanCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "اختر حالة الترخيص.")]
        public string LicenseStatus { get; set; } = "Active";

        [DataType(DataType.Date)]
        public DateTime StartsAt { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ExpiresAt { get; set; }

        [DataType(DataType.Date)]
        public DateTime? GraceEndsAt { get; set; }

        [Range(1, 1000, ErrorMessage = "حد الشركات يجب أن يكون بين 1 و1000.")] public int MaxCompanies { get; set; }
        [Range(1, 1000000, ErrorMessage = "حد الموظفين يجب أن يكون بين 1 و1000000.")] public int MaxEmployees { get; set; }
        [Range(0, 100000, ErrorMessage = "حد الأجهزة يجب أن يكون بين 0 و100000.")] public int MaxDevices { get; set; }
    }

    public sealed class CustomerProfileInputModel
    {
        public int TenantId { get; set; }

        [Required, StringLength(200, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(240)]
        public string? LegalName { get; set; }

        [Required, StringLength(160, MinimumLength = 2)]
        public string ContactName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(254)]
        public string ContactEmail { get; set; } = string.Empty;

        [Required, Phone, StringLength(40, MinimumLength = 5)]
        public string ContactPhone { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 2)]
        public string Country { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Address { get; set; }

        [StringLength(80)]
        public string? TaxNumber { get; set; }
    }

    public sealed class RenewalInputModel
    {
        public int TenantId { get; set; }
        public string VersionToken { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public int Months { get; set; } = 12;
        public int GraceDays { get; set; } = 7;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "IQD";
        public string PaymentMethod { get; set; } = "BankTransfer";
        public string? PaymentReference { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class DomainInputModel
    {
        public int TenantId { get; set; }

        [StringLength(63)]
        public string? PortalSubdomain { get; set; }

        [StringLength(253)]
        public string? CustomDomain { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadTenantAsync(id)) return NotFound();

        var tenant = Tenant;
        Input = NewLicenseInput(tenant);
        EnabledModules = tenant.EnabledModules.ToArray();
        Renewal = NewRenewalInput(tenant);
        CustomerProfile = NewCustomerProfileInput(tenant);
        Domain = NewDomainInput(tenant);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveDomainAsync()
    {
        var subdomain = Domain.PortalSubdomain?.Trim().ToLowerInvariant();
        var customDomain = Domain.CustomDomain?.Trim().ToLowerInvariant();
        var subdomainValid = string.IsNullOrWhiteSpace(subdomain) ||
                             Regex.IsMatch(subdomain, "^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$");
        var domainValid = string.IsNullOrWhiteSpace(customDomain) ||
                          Regex.IsMatch(customDomain, "^(?=.{4,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$");

        if (!subdomainValid || !domainValid)
        {
            ErrorMessage = "تحقق من صيغة النطاق الفرعي والنطاق المخصص. أدخل الاسم فقط بدون http أو مسار.";
            return RedirectToPage(new { id = Domain.TenantId });
        }

        var updated = await PlatformPortalStore.UpdateTenantDomainAsync(
            _db,
            new PlatformPortalStore.UpdateTenantDomainCommand(Domain.TenantId, subdomain, customDomain),
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!updated)
        {
            ErrorMessage = "تعذر حفظ النطاق. قد يكون مستخدماً لمنظومة أخرى أو أن المنظومة مؤرشفة.";
        }
        else
        {
            Message = string.IsNullOrWhiteSpace(subdomain) && string.IsNullOrWhiteSpace(customDomain)
                ? "تم إلغاء ربط النطاقات."
                : "تم حفظ النطاق بحالة بانتظار التحقق من DNS وSSL عند النشر.";
        }

        return RedirectToPage(new { id = Domain.TenantId });
    }

    public async Task<IActionResult> OnPostSaveCustomerProfileAsync(CustomerProfileInputModel customerProfile)
    {
        CustomerProfile = customerProfile;
        ModelState.Clear();
        if (!TryValidateModel(CustomerProfile, nameof(CustomerProfile)))
        {
            if (!await LoadTenantAsync(CustomerProfile.TenantId)) return NotFound();
            Input = NewLicenseInput(Tenant);
            EnabledModules = Tenant.EnabledModules.ToArray();
            Renewal = NewRenewalInput(Tenant);
            Domain = NewDomainInput(Tenant);
            return Page();
        }

        var updated = await PlatformPortalStore.UpdateTenantProfileAsync(
            _db,
            new PlatformPortalStore.UpdateTenantProfileCommand(
                CustomerProfile.TenantId,
                CustomerProfile.Name,
                CustomerProfile.LegalName,
                CustomerProfile.ContactName,
                CustomerProfile.ContactEmail,
                CustomerProfile.ContactPhone,
                CustomerProfile.Country,
                CustomerProfile.Address,
                CustomerProfile.TaxNumber),
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        Message = updated ? "تم تحديث بيانات العميل." : "تعذر تحديث بيانات العميل.";
        return RedirectToPage(new { id = CustomerProfile.TenantId });
    }

    public async Task<IActionResult> OnPostSaveLicenseAsync()
    {
        PlatformFormValidation.RemoveOtherForms(ModelState, nameof(Renewal), nameof(Domain), nameof(CustomerProfile));

        if (!TryDecodeVersion(Input.VersionToken, out var version))
            ModelState.AddModelError("Input.VersionToken", "بيانات نسخة الترخيص غير صالحة. حدّث الصفحة ثم أعد المحاولة.");
        if (!PlatformLicensePolicy.AllowedStatuses.Contains(Input.LicenseStatus, StringComparer.Ordinal))
            ModelState.AddModelError("Input.LicenseStatus", "اختر حالة ترخيص صالحة.");
        if (Input.ExpiresAt.HasValue && Input.ExpiresAt.Value.Date < Input.StartsAt.Date)
            ModelState.AddModelError("Input.ExpiresAt", "تاريخ الانتهاء يجب ألا يسبق تاريخ البداية.");
        if (Input.GraceEndsAt.HasValue && (!Input.ExpiresAt.HasValue || Input.GraceEndsAt.Value.Date < Input.ExpiresAt.Value.Date))
            ModelState.AddModelError("Input.GraceEndsAt", "نهاية فترة السماح يجب أن تكون بتاريخ الانتهاء أو بعده، مع تحديد تاريخ الانتهاء.");
        if (EnabledModules.Length == 0 || EnabledModules.Any(module => !Modules.ContainsKey(module)))
            ModelState.AddModelError(nameof(EnabledModules), "اختر مودلاً واحداً على الأقل من المودلات المتاحة.");

        if (!ModelState.IsValid)
        {
            if (!await LoadTenantAsync(Input.TenantId)) return NotFound();
            Renewal = NewRenewalInput(Tenant);
            Domain = NewDomainInput(Tenant);
            CustomerProfile = NewCustomerProfileInput(Tenant);
            return Page();
        }

        var updated = await PlatformPortalStore.UpdateLicenseAsync(
            _db,
            new PlatformPortalStore.UpdateLicenseCommand(
                Input.TenantId,
                Input.PlanCode,
                Input.LicenseStatus,
                DateTime.SpecifyKind(Input.StartsAt.Date, DateTimeKind.Utc),
                Input.ExpiresAt.HasValue ? DateTime.SpecifyKind(Input.ExpiresAt.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) : null,
                Input.GraceEndsAt.HasValue ? DateTime.SpecifyKind(Input.GraceEndsAt.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) : null,
                Input.MaxCompanies,
                Input.MaxEmployees,
                Input.MaxDevices,
                EnabledModules,
                version),
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!updated)
        {
            LicenseError = "لم يُحفظ التعديل لأن اللايسنس تغير من جلسة أخرى أو تعذر تحديثه. راجع القيم ثم أعد المحاولة.";
        }
        else
        {
            LicenseMessage = "تم حفظ تغييرات اللايسنس بنجاح.";
        }

        return RedirectToPage(null, null, new { id = Input.TenantId }, "license-management");
    }

    public async Task<IActionResult> OnPostSetActiveAsync(int id, string? reason, bool confirmed)
    {
        // لا نثق بحالة مرسلة من المتصفح؛ نقرأ الحالة الحالية من القاعدة ونقلبها.
        // هذا يفصل زر التشغيل عن ModelState الخاص بنموذج اللايسنس الموجود في الصفحة.
        var tenant = await PlatformPortalStore.GetTenantAsync(_db, id);
        if (tenant is null) return NotFound();
        if (tenant.IsDeleted)
        {
            ErrorMessage = "المنظومة مؤرشفة. استعدها أولاً قبل تغيير حالة التشغيل.";
            return RedirectToPage(new { id });
        }

        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (!confirmed || normalizedReason.Length is < 5 or > 500)
        {
            ErrorMessage = "اكتب سبباً واضحاً من 5 إلى 500 حرف، ثم فعّل مربع التأكيد.";
            return RedirectToPage(new { id });
        }

        var activate = !tenant.IsActive;
        var updated = await PlatformPortalStore.SetTenantActiveAsync(
            _db,
            id,
            activate,
            normalizedReason,
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        Message = !updated
            ? "تعذر تغيير حالة المنظومة. أعد تحميل الصفحة وحاول مرة أخرى."
            : activate
                ? "تم تفعيل المنظومة."
                : "تم إيقاف المنظومة وإسقاط وصولها.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRenewSubscriptionAsync()
    {
        var validVersion = TryDecodeVersion(Renewal.VersionToken, out var version);
        var validKey = Guid.TryParse(Renewal.IdempotencyKey, out var idempotencyKey);
        var currency = Renewal.Currency?.Trim() ?? string.Empty;
        var paymentMethod = Renewal.PaymentMethod?.Trim() ?? string.Empty;
        var valid = validVersion && validKey &&
                    Renewal.TenantId > 0 &&
                    Renewal.Months is >= 1 and <= 60 &&
                    Renewal.GraceDays is >= 0 and <= 90 &&
                    Renewal.Amount is >= 0 and <= 9999999999999999.99m &&
                    Currencies.ContainsKey(currency) &&
                    PaymentMethods.ContainsKey(paymentMethod) &&
                    (Renewal.PaymentReference?.Trim().Length ?? 0) <= 120 &&
                    (Renewal.Notes?.Trim().Length ?? 0) <= 500;

        if (!valid)
        {
            ErrorMessage = "تحقق من مدة التجديد والمبلغ والعملة وطريقة الدفع قبل الحفظ.";
            return RedirectToPage(new { id = Renewal.TenantId });
        }

        var result = await PlatformPortalStore.RecordPaidRenewalAsync(
            _db,
            new PlatformPortalStore.RecordRenewalCommand(
                Renewal.TenantId,
                version,
                idempotencyKey,
                Renewal.Months,
                Renewal.GraceDays,
                Renewal.Amount,
                currency,
                paymentMethod,
                Renewal.PaymentReference,
                Renewal.Notes),
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!result.Success)
        {
            ErrorMessage = "لم يُسجل التجديد لأن بيانات اللايسنس تغيرت من جلسة أخرى. أعد تحميل الصفحة ثم حاول مجدداً.";
        }
        else if (result.WasDuplicate)
        {
            Message = "طلب التجديد مسجل مسبقاً، ولم تُكرر مدة الاشتراك أو الفاتورة.";
        }
        else
        {
            Message = "تم تجديد الاشتراك وتسجيل الفاتورة المسددة بنجاح.";
        }

        return RedirectToPage(new { id = Renewal.TenantId });
    }

    public async Task<IActionResult> OnPostVoidInvoiceAsync(
        int id,
        long invoiceId,
        string? reason,
        bool confirmed)
    {
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (!confirmed || normalizedReason.Length is < 5 or > 500)
        {
            ErrorMessage = "اكتب سبب إلغاء واضحاً من 5 إلى 500 حرف، ثم فعّل مربع التأكيد.";
            return RedirectToPage(new { id });
        }

        var updated = await PlatformPortalStore.VoidSubscriptionInvoiceAsync(
            _db,
            id,
            invoiceId,
            normalizedReason,
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        Message = updated
            ? "تم إلغاء الفاتورة مالياً مع الاحتفاظ بها في السجل. لم تتغير مدة اللايسنس تلقائياً."
            : "تعذر إلغاء الفاتورة؛ قد تكون ملغاة مسبقاً أو لا تتبع هذه المنظومة.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSetArchivedAsync(
        int id,
        string? reason,
        bool confirmed)
    {
        var tenant = await PlatformPortalStore.GetTenantAsync(_db, id);
        if (tenant is null) return NotFound();

        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (!confirmed || normalizedReason.Length is < 5 or > 500)
        {
            ErrorMessage = "اكتب سبباً واضحاً من 5 إلى 500 حرف، ثم فعّل مربع التأكيد.";
            return RedirectToPage(new { id });
        }

        var archive = !tenant.IsDeleted;
        var updated = await PlatformPortalStore.SetTenantArchivedAsync(
            _db,
            id,
            archive,
            normalizedReason,
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        Message = !updated
            ? "تعذر تغيير حالة الأرشفة. أعد تحميل الصفحة وحاول مجدداً."
            : archive
                ? "تمت أرشفة المنظومة وإيقاف وصولها مع الاحتفاظ بجميع بياناتها وكودها."
                : "تمت استعادة المنظومة بحالة موقوفة. راجع اللايسنس ثم فعّلها يدوياً.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetExportInvoicesAsync(int id)
    {
        var tenant = await PlatformPortalStore.GetTenantAsync(_db, id);
        if (tenant is null) return NotFound();

        var invoices = await PlatformPortalStore.ListSubscriptionInvoicesAsync(_db, id, 500);
        var csv = new StringBuilder("\uFEFFرقم الفاتورة,الحالة,بداية الفترة,نهاية الفترة,الأشهر,السماح,المبلغ,العملة,طريقة التحصيل,المرجع,تاريخ التسجيل,المسجل بواسطة,سبب الإلغاء\r\n");
        foreach (var invoice in invoices)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(invoice.InvoiceNumber), Csv(invoice.Status), Csv(invoice.PeriodStartsAtUtc.ToString("yyyy-MM-dd")),
                Csv(invoice.PeriodEndsAtUtc.ToString("yyyy-MM-dd")), invoice.Months.ToString(), invoice.GraceDays.ToString(),
                invoice.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), Csv(invoice.Currency),
                Csv(PaymentMethodLabel(invoice.PaymentMethod)), Csv(invoice.PaymentReference),
                Csv(invoice.CreatedAtUtc.ToString("O")), Csv(invoice.CreatedBy), Csv(invoice.VoidReason)
            }));
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"tenant-{tenant.Code}-invoices.csv");
    }

    public async Task<IActionResult> OnGetExportAuditAsync(int id)
    {
        var tenant = await PlatformPortalStore.GetTenantAsync(_db, id);
        if (tenant is null) return NotFound();

        var events = await PlatformPortalStore.ListTenantAuditAsync(_db, tenant.Id, tenant.Code, 200);
        var csv = new StringBuilder("\uFEFFالتاريخ,الإجراء,المنفذ,التفاصيل,عنوان IP\r\n");
        foreach (var item in events)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(item.CreatedAtUtc.ToString("O")), Csv(AuditActionLabel(item.ActionCode)),
                Csv(item.ActorUsername), Csv(AuditDetailsLabel(item)), Csv(item.IpAddress)
            }));
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"tenant-{tenant.Code}-audit.csv");
    }

    public async Task<IActionResult> OnPostResetTenantAdminPasswordAsync(
        int id,
        int adminId,
        string? temporaryPassword,
        string? confirmPassword)
    {
        var password = temporaryPassword ?? string.Empty;
        if (password.Length is < 12 or > 200 || !string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "كلمة المرور المؤقتة يجب أن تكون من 12 إلى 200 حرف ومتطابقة في الحقلين.";
            return RedirectToPage(new { id });
        }

        var updated = await PlatformPortalStore.ResetTenantAdminPasswordAsync(
            _db,
            id,
            adminId,
            password,
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        Message = updated
            ? "تم تعيين كلمة مرور مؤقتة وإبطال الجلسات وإلزام المدير بتغييرها عند الدخول."
            : "تعذر العثور على حساب مدير تابع لهذه المنظومة.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUnlockTenantAdminAsync(int id, int adminId)
    {
        var updated = await PlatformPortalStore.UnlockTenantAdminAsync(
            _db,
            id,
            adminId,
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        Message = updated ? "تم فك قفل حساب المدير." : "تعذر العثور على حساب المدير.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostToggleTenantAdminAsync(int id, int adminId)
    {
        var admin = await PlatformPortalStore.GetTenantAdminAsync(_db, id);
        if (admin is null || admin.Id != adminId) return NotFound();

        var activate = !admin.IsActive;
        var updated = await PlatformPortalStore.SetTenantAdminActiveAsync(
            _db,
            id,
            adminId,
            activate,
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        Message = !updated
            ? "تعذر تغيير حالة حساب المدير."
            : activate ? "تم تفعيل حساب المدير." : "تم تعطيل حساب المدير وإبطال جلساته.";
        return RedirectToPage(new { id });
    }

    public string AuditActionLabel(string actionCode) => actionCode switch
    {
        "TenantCreated" => "إنشاء المنظومة",
        "LicenseUpdated" => "تحديث اللايسنس",
        "TenantActivated" => "تفعيل المنظومة",
        "TenantSuspended" => "إيقاف المنظومة",
        "SubscriptionRenewed" => "تجديد الاشتراك",
        "TenantProfileUpdated" => "تحديث بيانات العميل",
        "TenantAdminPasswordReset" => "إعادة تعيين كلمة مرور المدير",
        "TenantAdminUnlocked" => "فك قفل حساب المدير",
        "TenantAdminActivated" => "تفعيل حساب المدير",
        "TenantAdminDeactivated" => "تعطيل حساب المدير",
        "TenantDomainsUpdated" => "تحديث نطاقات المنظومة",
        "SubscriptionInvoiceVoided" => "إلغاء فاتورة اشتراك",
        "TenantArchived" => "أرشفة المنظومة",
        "TenantRestored" => "استعادة المنظومة",
        _ => actionCode
    };

    public string AuditDetailsLabel(PlatformPortalStore.AuditEvent audit)
    {
        var details = audit.Details?.Trim() ?? string.Empty;
        if (details.Length == 0) return "لا توجد تفاصيل إضافية.";
        if (Regex.IsMatch(details, "[\\u0600-\\u06FF]")) return details;

        return audit.ActionCode switch
        {
            "TenantCreated" =>
                $"تم إنشاء المنظومة وحساب المدير الأول. الخطة المسجلة وقت الإنشاء: {PlanLabel(LegacyValue(details, "Plan"))}.",
            "LicenseUpdated" =>
                $"تم تحديث الترخيص. الخطة: {PlanLabel(LegacyValue(details, "Plan"))}، الحالة: {LicenseStatusLabel(LegacyValue(details, "Status"))}.",
            "TenantProfileUpdated" => "تم تحديث بيانات العميل وبيانات الاتصال الخاصة بالمنظومة.",
            "TenantDomainsUpdated" =>
                $"تم تحديث نطاقات المنظومة. النطاق الفرعي: {LegacyValue(details, "Subdomain")}، النطاق المخصص: {LegacyValue(details, "CustomDomain")}، حالة التحقق: بانتظار التحقق.",
            "SubscriptionRenewed" => FormatLegacyRenewal(details),
            "SubscriptionInvoiceVoided" => FormatLegacyInvoiceVoid(details),
            "TenantArchived" => $"تمت أرشفة المنظومة وإيقاف الدخول إليها. السبب: {LegacyReason(details)}",
            "TenantRestored" => $"تمت استعادة المنظومة بحالة موقوفة. السبب: {LegacyReason(details)}",
            "TenantAdminPasswordReset" => "تمت إعادة تعيين كلمة مرور مدير المنظومة، وإنهاء جلساته النشطة، وإلزامه بتغييرها عند الدخول.",
            "TenantAdminUnlocked" => "تم فك قفل حساب مدير المنظومة وإنهاء جلساته النشطة.",
            "TenantAdminActivated" => "تم تفعيل حساب مدير المنظومة وإنهاء جلساته النشطة.",
            "TenantAdminDeactivated" => "تم تعطيل حساب مدير المنظومة وإنهاء جلساته النشطة.",
            _ => $"تم تنفيذ الإجراء الإداري. التفاصيل الأصلية: {details}"
        };
    }

    private static string PlanLabel(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == "-") return "غير محددة";
        if (string.Equals(code, "Custom", StringComparison.OrdinalIgnoreCase)) return "مخصصة";
        return PlatformPlanCatalog.Find(code)?.Name ?? code;
    }

    private static string LicenseStatusLabel(string? status) => status switch
    {
        "Active" => "فعال",
        "Trial" => "تجريبي",
        "Suspended" => "موقوف",
        _ => string.IsNullOrWhiteSpace(status) ? "غير محددة" : status
    };

    private static string LegacyValue(string details, string key)
    {
        var match = Regex.Match(details, $@"(?:^|[.;]\s*){Regex.Escape(key)}=([^;]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim().TrimEnd('.') : "غير محدد";
    }

    private static string LegacyReason(string details)
    {
        var match = Regex.Match(details, @"Reason:\s*(.+?)(?:\s+License dates|$)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim().TrimEnd('.') : "غير محدد";
    }

    private static string FormatLegacyRenewal(string details)
    {
        var match = Regex.Match(
            details,
            @"Subscription renewed for\s+(\d+)\s+month\(s\)\.\s*Invoice=([^;]+);\s*Amount=(.+)$",
            RegexOptions.IgnoreCase);
        return match.Success
            ? $"تم تجديد الاشتراك لمدة {match.Groups[1].Value} شهر. رقم الفاتورة: {match.Groups[2].Value.Trim()}، المبلغ: {match.Groups[3].Value.Trim()}."
            : "تم تجديد الاشتراك وتسجيل الدفعة الخارجية.";
    }

    private static string FormatLegacyInvoiceVoid(string details)
    {
        var match = Regex.Match(details, @"Invoice\s+(.+?)\s+voided\.\s*Reason:\s*(.+?)\s+License dates", RegexOptions.IgnoreCase);
        return match.Success
            ? $"تم إلغاء الفاتورة {match.Groups[1].Value.Trim()}. السبب: {match.Groups[2].Value.Trim()}. لم تتغير تواريخ الترخيص تلقائياً."
            : "تم إلغاء فاتورة الاشتراك، ولم تتغير تواريخ الترخيص تلقائياً.";
    }

    public string PaymentMethodLabel(string paymentMethod) =>
        PaymentMethods.TryGetValue(paymentMethod, out var label) ? label : paymentMethod;

    private async Task<bool> LoadTenantAsync(int id)
    {
        var tenant = await PlatformPortalStore.GetTenantAsync(_db, id);
        if (tenant is null) return false;

        Tenant = tenant;
        AuditEvents = await PlatformPortalStore.ListTenantAuditAsync(_db, tenant.Id, tenant.Code);
        Invoices = await PlatformPortalStore.ListSubscriptionInvoicesAsync(_db, tenant.Id);
        TenantAdmin = await PlatformPortalStore.GetTenantAdminAsync(_db, tenant.Id);
        return true;
    }

    private static InputModel NewLicenseInput(PlatformPortalStore.TenantSummary tenant) => new()
    {
        TenantId = tenant.Id,
        VersionToken = tenant.VersionToken,
        PlanCode = tenant.PlanCode,
        LicenseStatus = tenant.LicenseStatus,
        StartsAt = tenant.StartsAtUtc.Date,
        ExpiresAt = tenant.ExpiresAtUtc?.Date,
        GraceEndsAt = tenant.GraceEndsAtUtc?.Date,
        MaxCompanies = tenant.MaxCompanies,
        MaxEmployees = tenant.MaxEmployees,
        MaxDevices = tenant.MaxDevices
    };

    private static CustomerProfileInputModel NewCustomerProfileInput(PlatformPortalStore.TenantSummary tenant) => new()
    {
        TenantId = tenant.Id,
        Name = tenant.Name,
        LegalName = tenant.LegalName,
        ContactName = tenant.ContactName ?? string.Empty,
        ContactEmail = tenant.ContactEmail ?? string.Empty,
        ContactPhone = tenant.ContactPhone ?? string.Empty,
        Country = tenant.Country ?? string.Empty,
        Address = tenant.Address,
        TaxNumber = tenant.TaxNumber
    };

    private static RenewalInputModel NewRenewalInput(PlatformPortalStore.TenantSummary tenant) => new()
    {
        TenantId = tenant.Id,
        VersionToken = tenant.VersionToken,
        IdempotencyKey = Guid.NewGuid().ToString("D"),
        Months = 12,
        GraceDays = 7,
        Currency = "IQD",
        PaymentMethod = "BankTransfer"
    };

    private static DomainInputModel NewDomainInput(PlatformPortalStore.TenantSummary tenant) => new()
    {
        TenantId = tenant.Id,
        PortalSubdomain = tenant.PortalSubdomain,
        CustomDomain = tenant.CustomDomain
    };

    public string DomainStatusLabel(string status) => status switch
    {
        "Pending" => "بانتظار التحقق",
        "Verified" => "تم التحقق",
        "Failed" => "فشل التحقق",
        _ => "غير مهيأ"
    };

    private static int UsagePercent(int used, int limit) =>
        limit <= 0 ? (used > 0 ? 100 : 0) : Math.Clamp((int)Math.Ceiling(used * 100d / limit), 0, 999);

    private static string Csv(string? value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static bool TryDecodeVersion(string? token, out byte[] version)
    {
        try
        {
            version = Convert.FromBase64String(token ?? string.Empty);
            return version.Length == 8;
        }
        catch (FormatException)
        {
            version = [];
            return false;
        }
    }
}
