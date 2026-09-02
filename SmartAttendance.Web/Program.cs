using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using SmartAttendance.Web;
using SmartAttendance.Web.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Application.AttendanceImports.Services;
using SmartAttendance.Application.AttendanceProcessing.Services;
using SmartAttendance.Application.AttendanceRecords.Services;
using SmartAttendance.Application.AttendanceReports.Services;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Common.Mapping;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Devices.Services;
using SmartAttendance.Application.EmployeePermissions.Services;
using SmartAttendance.Application.EmployeeShifts.Services;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Holidays.Services;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.MasterDataImports.Services;
using SmartAttendance.Application.Permissions.Services;
using SmartAttendance.Application.Setup.Services;
using SmartAttendance.Application.Shifts.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Repositories;
using SmartAttendance.Infrastructure.Seeding;
using SmartAttendance.Infrastructure.Services;
using SmartAttendance.Web.Infrastructure.Theming;
using SmartAttendance.Web.Infrastructure.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<ProductionOperationsOptions>(
    builder.Configuration.GetSection(ProductionOperationsOptions.SectionName));

builder.Services.AddRazorPages()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (_, factory) =>
            factory.Create(typeof(SharedResource));
    })
    // محرك تقارير واحد يخدم مسارين: الأشخاص (/PeopleReports) والحضور
    // (/AttendanceReports). الصفحة تستنتج الموديول من المسار وتعرض مصادره فقط.
    .AddRazorPagesOptions(options =>
    {
        options.Conventions.AddPageRoute("/PeopleReports/Index", "/AttendanceReports");
        // تقارير الرواتب (نظير «التقارير» بمودل رواتب كيان) — نفس الصفحة بمصادر pay_*.
        options.Conventions.AddPageRoute("/PeopleReports/Index", "/PayrollReports");
    });

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = ZynoraSupportedCultures.All
        .Select(item => item.Culture)
        .ToArray();

    options.DefaultRequestCulture = new RequestCulture(ZynoraSupportedCultures.DefaultCode);
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
    options.FallBackToParentCultures = true;
    options.FallBackToParentUICultures = true;
    options.ApplyCurrentCultureToResponseHeaders = true;
    options.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new QueryStringRequestCultureProvider()
    ];
});

// Branding & Theme Engine runtime (P4): in-memory theme cache + request-scoped
// resolver. No company theme is persisted yet, so this serves the ZYNORA Default.
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IThemeContextService, ThemeContextService>();
builder.Services.AddSingleton<ILocalizationDictionaryService, LocalizationDictionaryService>();
builder.Services.Configure<AzureTranslatorOptions>(
    builder.Configuration.GetSection(AzureTranslatorOptions.SectionName));
builder.Services.AddHttpClient<IAutomaticTextTranslator, AzureAutomaticTextTranslator>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddScoped<ILocalizationAutoTranslationService, LocalizationAutoTranslationService>();
builder.Services.AddScoped<ICompanyDataLocalizationService, CompanyDataLocalizationService>();

// مرفقات الموظفين الحسّاسة: حفظ خارج wwwroot + روابط تنزيل موقّعة + فحص malware
// قبل الكتابة. في التطوير يمكن تعطيل المحرك صراحةً، أما بوابة الإنتاج فتفرضه.
builder.Services.Configure<MalwareScanningOptions>(
    builder.Configuration.GetSection(MalwareScanningOptions.SectionName));
var malwareOptions = builder.Configuration
    .GetSection(MalwareScanningOptions.SectionName)
    .Get<MalwareScanningOptions>() ?? new MalwareScanningOptions();
if (malwareOptions.IsUsable)
    builder.Services.AddSingleton<IFileThreatScanner, ClamAvFileThreatScanner>();
else
    builder.Services.AddSingleton<IFileThreatScanner, DisabledFileThreatScanner>();

builder.Services.AddSingleton<IProtectedFileService, ProtectedFileService>();

// مقاييس الطلبات بالذاكرة (FIX-004 · OBS-006): خطُّ الأساس الذي كان مفقوداً —
// زمن الطلب وP95 ومعدّل الأخطاء لكل مسار. Singleton لأن الحالة مشتركة عبر الطلبات.
builder.Services.AddSingleton<SmartAttendance.Web.Infrastructure.Observability.RequestMetricsSink>();

// إجبار HTTPS مفصول عن «البيئة»: النشر خلف Cloudflare Tunnel (ينهي TLS عند الحافة
// ويمرّر HTTP محلياً) وعلى LAN يحتاج HTTP لبوت‑ستراب شهادة الـCA. لذا نفصل إنفاذ TLS
// في راية مستقلة (افتراضها false) فيبقى الإنتاج بمعالج أخطاء آمن بلا كسر التنفيل/الدخول.
var forceHttps = builder.Configuration.GetValue<bool>("ForceHttps");

// المرحلة 10: الوسيط العكسي (Cloudflare Tunnel) صريح بالإعدادات. بلا تفعيل ووسطاء
// موثوقين لا تُقرأ ترويسات X-Forwarded إطلاقاً — فلا يستطيع أي عميل على الإنترنت
// انتحال البروتوكول أو المضيف.
var reverseProxyOptions = builder.Configuration
    .GetSection(ReverseProxyOptions.SectionName)
    .Get<ReverseProxyOptions>() ?? new ReverseProxyOptions();

builder.Services.Configure<ReverseProxyOptions>(
    builder.Configuration.GetSection(ReverseProxyOptions.SectionName));

// أمان كوكي الجلسة — يُحسم قبل تسجيل المصادقة، ويرفض الإقلاع بالإنتاج بلا إثبات TLS.
var cookieSecurity = CookieSecurityPolicy.Evaluate(
    forceHttps,
    reverseProxyOptions.Enabled,
    builder.Environment.IsProduction(),
    builder.Configuration.GetValue<bool>(CookieSecurityPolicy.AllowInsecureCookiesKey));

if (cookieSecurity == CookieSecurityDecision.RefuseToStart)
{
    throw new InvalidOperationException(CookieSecurityPolicy.BuildRefusalMessage());
}

if (reverseProxyOptions.Enabled)
{
    builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto |
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost |
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor;

        // الافتراضات تثق بـlocalhost فقط؛ نمسحها ونعتمد المصرَّح به بالإعدادات.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        options.ForwardLimit = 1;

        foreach (var proxy in reverseProxyOptions.KnownProxies)
        {
            if (System.Net.IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }

        foreach (var network in reverseProxyOptions.KnownNetworks)
        {
            if (System.Net.IPNetwork.TryParse(network, out var parsedNetwork))
            {
                options.KnownIPNetworks.Add(parsedNetwork);
            }
        }

        foreach (var host in reverseProxyOptions.AllowedHosts)
        {
            options.AllowedHosts.Add(host);
        }
    });
}

// ═══ حلقة مفاتيح Data Protection ═══
//
// كانت على قرص العملية حصراً — صالح لخادم واحد لا أكثر. بنسختين، كلٌّ تولّد حلقتها:
// كوكي صادر من A يُرفض على B (طرد عشوائي)، و**روابط تنزيل الملفات الموقّعة**
// (/files/download?t=) تصير غير قابلة لفكّ التشفير عبر النسخ. وبكل نشرٍ على حاوية
// جديدة يُطرد الجميع لأن القرص جديد.
//
// المخزن الآن **قاعدة البيانات** حين تتوفّر: هي مشتركة بين كل النسخ أصلاً وتنجو من
// إعادة النشر — بلا Redis ولا تخزين سحابيّ ولا أي بنية جديدة. وبلا سلسلة اتصال
// (تطوير محليّ) يبقى القرص كما كان، فلا ينكسر أي مسار قائم.
//
// `SetApplicationName` ثابت وصريح: بدونه يشتقّه الإطار من اسم مجلد المحتوى، فيختلف
// بين مسار محليّ و`/app` بحاوية ⟹ نفس المفاتيح تُعطي أغلفة مختلفة ولا تُفكّ.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("ZYNORA.HR");

var dataProtectionConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (!string.IsNullOrWhiteSpace(dataProtectionConnectionString))
{
    dataProtection.PersistKeysToDbContext<ApplicationDbContext>();
}
else
{
    var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
    Directory.CreateDirectory(dataProtectionKeysPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/AccessDenied";
        options.Cookie.Name = "ZYNORA.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.Path = "/";
        options.Cookie.SameSite = SameSiteMode.Lax;
        // القرار من `CookieSecurityPolicy` لا من رايةٍ واحدة: الوسيط العكسيّ الموثوق
        // يعني TLS منتهٍ عند الحافة فتُصدَر Secure دائماً، وإنتاجٌ بلا إثبات لا يصل
        // هنا أصلاً (رُفض الإقلاع أعلاه).
        options.Cookie.SecurePolicy = cookieSecurity == CookieSecurityDecision.AlwaysSecure
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // حاجز إعارة الهاتف (دور الموظف فقط): «آخر نشاط» يُختم داخل التذكرة المشفّرة
        // ويُفحص بكل طلب ضد مهلة خمول داينمك (إعدادات الحضور، 0=معطّل) — تجاوزها
        // يُسقط الجلسة سيرفرياً فلا يُخدع بتعطيل جافاسكربت العميل.
        options.Events.OnValidatePrincipal = async context =>
        {
            var role = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            // المرحلة 5: إبطال الجلسات البائتة لكل الأدوار. لا نضرب القاعدة بالأصول
            // الثابتة، والقراءة نفسها بكاش 60 ثانية (AccountSecurityStore).
            if (!PublicPathPolicy.IsStaticAsset(context.HttpContext.Request.Path.Value))
            {
                var username = context.Principal?.Identity?.Name;
                var ticketStamp = context.Principal
                    ?.FindFirst(AccountSecurityStore.SecurityStampClaimType)?.Value;

                AccountSecurityState? accountState = null;

                try
                {
                    accountState = await AccountSecurityStore.GetStateAsync(
                        context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>(),
                        context.HttpContext.RequestServices
                            .GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                        username);
                }
                catch (Exception ex)
                {
                    // تعذّر الوصول للقاعدة لا يطرد الجلسات القائمة (توفّر قبل تشدّد).
                    //
                    // ⚠️ AUTHN-002: هذا **فشلٌ مفتوح** مقصود — تعذّر التحقّق يعني أن
                    // حساباً عُطِّل أو تغيّرت كلمة مروره تبقى جلسته حيّة حتى 8 ساعات.
                    // (ومسار الموبايل يفشل **مغلقاً** بنفس الضابط — سلوكان متناقضان.)
                    // كان يُبتلع صامتاً فلا أثر يُراجَع؛ صار يُسجَّل ليُرى بالسجلّ
                    // ويُقاس تكراره قبل حسم أي السلوكين هو المقصود.
                    // AUTHN-002 — **فشلٌ مغلق** (قرار المالك 2026-08-12: «نقطع الجلسة»):
                    // لا نعرف هل عُطّل الحساب أو تغيّرت كلمته — فنرفض بدل أن نثق.
                    // يوحّد السلوك مع مسار الموبايل. الأثر على التوفّر محدود: كاش
                    // 60 ثانية يمتصّ الأعطال العابرة، والتطبيق يلزمه القاعدة لكل عملٍ أصلاً.
                    context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Security.SessionRevocation")
                        .LogWarning(ex,
                            "تعذّر التحقّق من حالة الحساب — قُطعت الجلسة (فشلٌ مغلق). المسار: {Path}",
                            context.HttpContext.Request.Path);

                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                if (accountState is not null)
                {
                    var decision = SessionSecurityValidator.Evaluate(ticketStamp, role, accountState);

                    if (decision == SessionSecurityDecision.Reject)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(
                            CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }

                    if (decision == SessionSecurityDecision.Refresh &&
                        context.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                    {
                        SessionClaimsRefresher.Apply(identity, accountState);
                        role = accountState.Role;
                        context.ShouldRenew = true;
                    }
                }
            }

            if (!string.Equals(role, "Employee", StringComparison.OrdinalIgnoreCase)) return;

            var isRememberedSession = context.Principal?.HasClaim(
                SmartAttendance.Web.Infrastructure.Security.PortalSessionPolicy.RememberMeClaimType,
                bool.TrueString) == true;

            if (!SmartAttendance.Web.Infrastructure.Security.PortalSessionPolicy
                    .ShouldEnforceSessionTimeouts(isRememberedSession))
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (!context.Properties.Items.TryGetValue(
                    SmartAttendance.Web.Infrastructure.Security.PortalSessionPolicy.LastActivityItem,
                    out var raw) || !long.TryParse(raw, out var ticks))
            {
                // جلسة صادرة قبل الميزة: نختمها الآن ونجدد التذكرة.
                context.Properties.Items[SmartAttendance.Web.Infrastructure.Security.PortalSessionPolicy.LastActivityItem] =
                    now.Ticks.ToString();
                context.ShouldRenew = true;
                return;
            }

            int idleMinutes;
            try
            {
                var cache = context.HttpContext.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                idleMinutes = await Microsoft.Extensions.Caching.Memory.CacheExtensions.GetOrCreateAsync(
                    cache, "portal:idleMinutes", async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                        return await SmartAttendance.Web.Infrastructure.Security.PortalSessionPolicy
                            .GetIdleMinutesAsync(db);
                    });
            }
            catch { return; } // تعذّر قراءة الإعداد لا يقطع الجلسات

            var lastActivity = new DateTime(ticks, DateTimeKind.Utc);
            if (SmartAttendance.Web.Infrastructure.Security.PortalSessionPolicy.ShouldExpire(lastActivity, now, idleMinutes))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            if (SmartAttendance.Web.Infrastructure.Security.PortalSessionPolicy.ShouldRenew(lastActivity, now))
            {
                context.Properties.Items[SmartAttendance.Web.Infrastructure.Security.PortalSessionPolicy.LastActivityItem] =
                    now.Ticks.ToString();
                context.ShouldRenew = true;
            }
        };
    })
    // مصادقة توكن Bearer لواجهة الموبايل (بجانب الكوكيز) — كنترولرات /api/*
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
        SmartAttendance.Web.Infrastructure.Api.ApiTokenAuthHandler>(
        SmartAttendance.Web.Infrastructure.Api.ApiTokenAuthHandler.SchemeName, null);

// سياسة تفويض احتياطية: **كل** نقطة تتطلّب مستخدماً مصادقاً ما لم تُعفَ صراحةً
// بـ[AllowAnonymous]. سببها أن PublicPathPolicy يصنّف /api/ و/push/ و/files/
// كـPublic (لأن مصادقتها بالتوكن داخل الكنترولرات لا بكوكيز الحارس)، فأي
// كنترولر أو MapGet جديد يُنسى فيه [Authorize] كان يصبح مكشوفاً للإنترنت بلا
// أي إنذار. الفشل هنا مغلق لا مفتوح.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SmartAttendance.Web.Infrastructure.Api.ApiTokenAuthHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build();
});

// كنترولرات واجهة الموبايل (REST/JSON) — بجانب Razor Pages
builder.Services.AddControllers();

// حدّ معدّل محاولات الدخول — يخنق رشّ كلمات المرور الذي لا يلمس قفل الحساب
// (القفل لكل حساب؛ الرشّ يجرّب كلمة واحدة على ألف حساب). المحدِّد **عام** بمُقسِّم
// يعفي كل ما ليس مسار دخول، فلا يُخنق استعمال مشروع.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
        context =>
        {
            if (!LoginRateLimitPolicy.AppliesToAttempt(
                    context.Request.Method,
                    context.Request.Path.Value))
            {
                return System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("free");
            }

            return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                LoginRateLimitPolicy.PartitionKey(context.Connection.RemoteIpAddress?.ToString()),
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = LoginRateLimitPolicy.PermitLimit,
                    Window = TimeSpan.FromMinutes(LoginRateLimitPolicy.WindowMinutes),
                    QueueLimit = 0
                });
        });
});

// فحوص الصحّة: المنصّات السحابية توجّه الحركة بمسبار جاهزية. بدونه تتلقّى النسخة
// طلبات قبل اكتمال الهجرات والبذور — أي بأخطر لحظة بدورة حياتها.
//   /health/live  — العملية حيّة (بلا لمس القاعدة، فلا يُعاد تشغيلها لعطل قاعدة عابر).
//   /health/ready — القاعدة مستجيبة فعلاً.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database", tags: new[] { "ready" })
    .AddCheck<SmartAttendance.Web.Infrastructure.Observability.BackupFreshnessHealthCheck>(
        "verified-offsite-backup", tags: new[] { "ready" });
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<IModelMapper, ConventionModelMapper>();

// Repositories
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Services
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IBranchService, BranchService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IShiftService, ShiftService>();
builder.Services.AddScoped<IEmployeeShiftService, EmployeeShiftService>();
builder.Services.AddScoped<IAttendanceRecordService, AttendanceRecordService>();
builder.Services.AddScoped<IAttendanceProcessingService, AttendanceProcessingService>();
builder.Services.AddScoped<IAttendanceReportService, AttendanceReportService>();
builder.Services.AddScoped<IAttendanceAdvancedReportService, AttendanceAdvancedReportService>();
builder.Services.AddScoped<IHolidayService, HolidayService>();
builder.Services.AddScoped<ILeaveRequestService, LeaveRequestService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IEmployeePermissionService, EmployeePermissionService>();
builder.Services.AddScoped<ILoginIdentityService, LoginIdentityService>();
builder.Services.AddScoped<IPermissionAuthorizationService, PermissionAuthorizationService>();
builder.Services.AddScoped<IAttendanceImportService, AttendanceImportService>();
builder.Services.AddScoped<SmartAttendance.Web.Infrastructure.Imports.AttendanceImportStagingStore>();
builder.Services.AddScoped<IMasterDataImportService, MasterDataImportService>();
builder.Services.AddScoped<ISetupService, SetupService>();
builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();
builder.Services.AddScoped<SmartAttendance.Web.Infrastructure.Security.IAccessRoleService, SmartAttendance.Web.Infrastructure.Security.AccessRoleService>();
builder.Services.AddScoped<SmartAttendance.Web.Infrastructure.Security.IEffectiveScopeService, SmartAttendance.Web.Infrastructure.Security.EffectiveScopeService>();
// نطاق شركات الطلب — يُشتقّ من محرك الصلاحيات نفسه (IEffectiveScopeService) فلا
// يتباعد عنه مصدرُ حقيقةٍ ثانٍ. Scoped لأن نتيجته تُكاش بعمر الطلب.
builder.Services.AddScoped<SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider, SmartAttendance.Web.Infrastructure.Security.CompanyScopeProvider>();

// قناة الإشعارات الخارجية (SMTP): تُفعَّل عمداً عبر قسم "Smtp" (Enabled=true). حين
// تكون معطّلة يُحقَن مرسِل No-Op ولا تعمل خدمة التسليم الخلفية (بلا استهلاك).
builder.Services.Configure<SmartAttendance.Web.Infrastructure.Notifications.SmtpOptions>(
    builder.Configuration.GetSection(SmartAttendance.Web.Infrastructure.Notifications.SmtpOptions.SectionName));
var smtpOptions = builder.Configuration
    .GetSection(SmartAttendance.Web.Infrastructure.Notifications.SmtpOptions.SectionName)
    .Get<SmartAttendance.Web.Infrastructure.Notifications.SmtpOptions>()
    ?? new SmartAttendance.Web.Infrastructure.Notifications.SmtpOptions();
var smtpEnabled = smtpOptions.IsUsable;
if (smtpEnabled)
{
    builder.Services.AddSingleton<SmartAttendance.Web.Infrastructure.Notifications.IEmailSender,
        SmartAttendance.Web.Infrastructure.Notifications.SmtpEmailSender>();
    builder.Services.AddHostedService<SmartAttendance.Web.Infrastructure.Notifications.NotificationDispatcherService>();
}
else
{
    builder.Services.AddSingleton<SmartAttendance.Web.Infrastructure.Notifications.IEmailSender,
        SmartAttendance.Web.Infrastructure.Notifications.NoOpEmailSender>();
}

// قناة Web-Push (VAPID): تُفعَّل عمداً عبر قسم "WebPush" (المفاتيح موجودة). معطّلة ⟹
// مرسِل No-Op فلا دفع، ونقطة vapid-key تعيد enabled=false فيتوقف العميل عن الاشتراك.
builder.Services.Configure<SmartAttendance.Web.Infrastructure.Notifications.VapidOptions>(
    builder.Configuration.GetSection(SmartAttendance.Web.Infrastructure.Notifications.VapidOptions.SectionName));
var pushEnabled = builder.Configuration
    .GetSection(SmartAttendance.Web.Infrastructure.Notifications.VapidOptions.SectionName)
    .Get<SmartAttendance.Web.Infrastructure.Notifications.VapidOptions>()?.IsUsable ?? false;
if (pushEnabled)
    builder.Services.AddSingleton<SmartAttendance.Web.Infrastructure.Notifications.IWebPushSender,
        SmartAttendance.Web.Infrastructure.Notifications.WebPushSender>();
else
    builder.Services.AddSingleton<SmartAttendance.Web.Infrastructure.Notifications.IWebPushSender,
        SmartAttendance.Web.Infrastructure.Notifications.NoOpWebPushSender>();

// مولّد مركز الإشعارات (كرون يومي): يقرأ قواعد المركز المفعّلة ويُطلق فعلياً عبر
// صندوق داخل النظام + Web-Push. يعمل دائماً (لا يحتاج SMTP) ويمنع التكرار بجدول أحداث.
builder.Services.AddHostedService<SmartAttendance.Web.Infrastructure.Notifications.NotificationRuleGeneratorService>();

// Webhooks: صندوق صادر durable يعمل حتى إن لم توجد اشتراكات؛ الأسرار محمية
// بـData Protection، والتحويلات ممنوعة كي لا تتجاوز سياسة عنوان الوجهة.
builder.Services.Configure<SmartAttendance.Web.Infrastructure.Integrations.WebhookDispatcherOptions>(
    builder.Configuration.GetSection(
        SmartAttendance.Web.Infrastructure.Integrations.WebhookDispatcherOptions.SectionName));
builder.Services.AddHttpClient("ZynoraWebhooks", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
});
builder.Services.AddHostedService<SmartAttendance.Web.Infrastructure.Integrations.WebhookDispatcherService>();
builder.Services.AddHostedService<SmartAttendance.Web.Infrastructure.Integrations.DevicePunchProcessorService>();
builder.Services.AddHostedService<SmartAttendance.Web.Infrastructure.Reports.ReportScheduleDispatcherService>();
builder.Services.AddHostedService<SmartAttendance.Web.Infrastructure.Hrms.ApprovalSlaDispatcherService>();

// كلمة مرور شهادة HTTPS لم تعد بالمستودع: مصدرها متغيّر البيئة وحده. نفشل بوضوح
// عند الحاجة إليها وغيابها بدل رسالة ربط غامضة من Kestrel أو تشغيل بلا TLS بصمت.
var certificatePath = builder.Configuration["Kestrel:Certificates:Default:Path"];

if (!string.IsNullOrWhiteSpace(certificatePath))
{
    var configuredUrls = new List<string?> { builder.Configuration["urls"] };
    configuredUrls.AddRange(builder.Configuration
        .GetSection("Kestrel:Endpoints")
        .GetChildren()
        .Select(endpoint => endpoint["Url"]));

    var certificateFullPath = Path.IsPathRooted(certificatePath)
        ? certificatePath
        : Path.Combine(builder.Environment.ContentRootPath, certificatePath);

    var certificateState = CertificateSecretGuard.Evaluate(
        CertificateSecretGuard.HasHttpsEndpoint(configuredUrls),
        File.Exists(certificateFullPath),
        builder.Configuration["Kestrel:Certificates:Default:Password"]);

    if (certificateState == CertificateSecretState.MissingPassword)
    {
        throw new InvalidOperationException(
            CertificateSecretGuard.BuildFailureMessage(certificateFullPath));
    }
}

// لا يكفي أن تكون الاختبارات خضراء كي يكون تشغيل الإنتاج آمناً. هذه البوابة
// تفشل قبل لمس القاعدة إذا لم يوثَّق قبول المالك وRPO/RTO والنسخ الخارجي
// والمراقبة وSMTP وفحص الملفات. يمكن تعطيلها صراحةً فقط بقرار طوارئ موثّق.
var productionOperations = builder.Configuration
    .GetSection(ProductionOperationsOptions.SectionName)
    .Get<ProductionOperationsOptions>() ?? new ProductionOperationsOptions();
var readinessFailures = ProductionReadinessGuard.Validate(
    builder.Environment.EnvironmentName,
    productionOperations,
    smtpOptions,
    malwareOptions);
if (readinessFailures.Count > 0)
{
    throw new InvalidOperationException(
        ProductionReadinessGuard.BuildFailureMessage(readinessFailures));
}

var app = builder.Build();

// حارس فصل البيئات — **قبل المهاجر لا بعده.** تشغيلٌ غير إنتاجي يشير لقاعدة
// الإنتاج يعني هجرةً تلقائية وبياناتٍ تجريبية على بيانات حقيقية. يُرفض الإقلاع
// صراحةً بدل تحذيرٍ بسجلٍّ لا يقرأه أحد.
if (SmartAttendance.Web.Infrastructure.Hrms.EnvironmentDatabaseGuard.Validate(
        app.Environment.EnvironmentName,
        builder.Configuration.GetConnectionString("DefaultConnection")) is { } environmentRefusal)
{
    throw new InvalidOperationException(environmentRefusal);
}

// هجرات المخطط المحكومة للجداول القديمة (SQL خام) تعمل صراحةً مرة واحدة عند
// الإقلاع — لا بكل طلب — وأي فشل يظهر فوراً بدل عطل صامت لاحق.
using (var migrationScope = app.Services.CreateScope())
{
    var migrationDb = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // These legacy tables pre-date the controlled migrator. Ensure their base shape
    // at startup so the SalaryItemId migration also covers a clean database.
    await SmartAttendance.Web.Infrastructure.Hrms.SalaryItemStore.EnsureAsync(migrationDb);
    await SmartAttendance.Web.Infrastructure.Hrms.EmployeeAllowanceSchema.EnsureAsync(migrationDb);
    await SmartAttendance.Web.Infrastructure.Hrms.PayrollTransactionStore.EnsureAsync(migrationDb);
    // الجداول القديمة الأساسية يجب أن توجد قبل الهجرات التي تضيف لها علاقات
    // (مثل ApprovalRequestWatchers -> SelfServiceRequests).
    await SmartAttendance.Web.Infrastructure.Hrms.HrmsDatabase.EnsureCreatedAsync(migrationDb);
    await SmartAttendance.Web.Infrastructure.Security.LoginDatabase.EnsureCreatedAsync(migrationDb);
    await SmartAttendance.Web.Infrastructure.Hrms.SqlSchemaMigrator.ApplyAsync(migrationDb);

    // مخطط توكنات الـAPI يُضمَن هنا مرّة واحدة عند الإقلاع — لا بمسار التحقّق الساخن.
    // كان ValidateAsync يفحص/ينشئ الجدول (DDL) بكل طلب Bearer؛ نقلُه للإقلاع يجعل
    // التحقّق بحثاً مفهرساً محدوداً (بذرة فريدة على TokenHash).
    await SmartAttendance.Web.Infrastructure.Api.ApiTokenStore.EnsureAsync(migrationDb);
}

await DefaultShiftSeeder.SeedAsync(app.Services);
await PeoplePermissionSeeder.SeedAsync(app.Services);

// ترويسات الوسيط تُعالَج أولاً: كل ما بعدها (تحويل HTTPS، الكوكيز الآمنة، أصل
// WebAuthn) يرى بروتوكولاً ومضيفاً مطبَّعَين من وسيط موثوق فقط.
if (reverseProxyOptions.Enabled)
{
    app.UseForwardedHeaders();
}

app.UseRequestLocalization();
app.UseMiddleware<DynamicDictionaryCultureMiddleware>();

// معالج الأخطاء يعمل في الإنتاج بصرف النظر عن TLS: يمنع تسريب صفحة الاستثناء
// المطوِّرة (stack trace) للمستخدم النهائي.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// إنفاذ TLS (HSTS + تحويل HTTPS) مفصول عن البيئة: يبقى مطفأً خلف Cloudflare Tunnel
// وعلى LAN (بوت‑ستراب شهادة الـCA يحتاج HTTP). يُفعَّل فقط عند ForceHttps=true.
if (forceHttps)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// ترويسات الأمان + سياسة المحتوى (CSP) — مصدرها SecurityHeaderPolicy النقي.
// مجمّع مخالفات CSP: **أداة قياس** لترحيل #8، مطفأة افتراضيّاً. مطفأةً لا تُضاف
// نقطة نهاية ولا تتغيّر ترويسة واحدة — سلوك الإنتاج كما هو حرفيّاً. مُشعَلةً تصل
// المخالفات لمجمّع بالذاكرة فنرحّل بقياسٍ لا بتخمينٍ على 181 صفحة.
var cspCollector = app.Configuration.GetValue<bool>("Security:CspReportCollector");
var cspReportPath = cspCollector ? CspReportEndpoint.ReportPath : null;
var securityHeaders = SecurityHeaderPolicy.BuildStaticHeaders(cspReportPath);
// راية معطّلة افتراضيّاً: السلوك الحيّ يبقى 'unsafe-inline' حتى يكتمل ترحيل الوسوم
// المضمّنة (nonce) وتُفعَّل في بيئة يمكن اختبارها بصريّاً. تفعيلها بلا ترحيل يكسر
// كل معالِجات onclick المضمّنة عمداً — فهذا هو غرض CSP الصارمة.
var strictCsp = app.Configuration.GetValue<bool>("Security:StrictCsp");

app.Use(async (context, next) =>
{
    foreach (var header in securityHeaders)
    {
        context.Response.Headers[header.Key] = header.Value;
    }

    if (strictCsp)
    {
        var nonce = SecurityHeaderPolicy.NewNonce();
        context.SetCspNonce(nonce);
        context.Response.Headers["Content-Security-Policy"] =
            SecurityHeaderPolicy.BuildContentSecurityPolicy(nonce);
    }

    await next();
});

app.UseRouting();

// قياس الطلبات بعد UseRouting كي يتوفّر قالب المسار المُطابَق، وقبل المصادقة/التحديد
// كي يلتقط ردود 401/429 أيضاً — فيقيس الطلب كاملاً بلا استثناء (FIX-004).
app.UseMiddleware<SmartAttendance.Web.Infrastructure.Observability.RequestMetricsMiddleware>();

// بعد UseForwardedHeaders (أعلاه) فيكون عنوان الطالب مُطبَّعاً من وسيطٍ موثوق —
// وقبل المصادقة فلا تُستهلك دورات تجزئة كلمة المرور على طلبٍ سيُرفض أصلاً.
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<RoleSecurityMiddleware>();

app.UseAuthorization();

// الأصول الساكنة تُخدَم كنقاط نهاية بـ.NET 10، فسياسة التفويض الاحتياطية تشملها
// وتردّ 401 على CSS وJS وعامل خدمة الـPWA — أي أن صفحة الدخول نفسها تفقد تنسيقها
// وتطبيق الموظف ينكسر. إعفاؤها صريح: الحماية على البيانات لا على ملفات الواجهة.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

app.MapControllers();

if (cspCollector)
{
    app.MapCspReportCollector();
}

// المسباران عامّان بلا مصادقة: المنصّة تستدعيهما قبل وجود أي جلسة، ولا يكشفان
// تفاصيل — `live` يردّ بلا أي فحص، و`ready` يردّ نجاحاً/فشلاً بلا رسالة استثناء.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

// تحميل تطبيق أندرويد الملفوف (APK) — نقطة عامة بنوع MIME الصحيح ليثبّته الموظف مباشرة.
app.MapGet("/app.apk", (IWebHostEnvironment env) =>
{
    var apk = Path.Combine(env.WebRootPath, "downloads", "ZynoraPortal.apk");
    return File.Exists(apk)
        ? Results.File(apk, "application/vnd.android.package-archive", "ZynoraPortal.apk")
        : Results.NotFound();
}).AllowAnonymous();

app.Run();

