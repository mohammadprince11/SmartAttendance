using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using SmartAttendance.Web.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Application.AttendanceImports.Services;
using SmartAttendance.Application.AttendanceProcessing.Services;
using SmartAttendance.Application.AttendanceRecords.Mappings;
using SmartAttendance.Application.AttendanceRecords.Services;
using SmartAttendance.Application.AttendanceReports.Services;
using SmartAttendance.Application.Branches.Mappings;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Companies.Mappings;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Departments.Mappings;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Devices.Mappings;
using SmartAttendance.Application.Devices.Services;
using SmartAttendance.Application.EmployeePermissions.Services;
using SmartAttendance.Application.EmployeeShifts.Mappings;
using SmartAttendance.Application.EmployeeShifts.Services;
using SmartAttendance.Application.Employees.Mappings;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Holidays.Mappings;
using SmartAttendance.Application.Holidays.Services;
using SmartAttendance.Application.LeaveRequests.Mappings;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.MasterDataImports.Services;
using SmartAttendance.Application.Permissions.Mappings;
using SmartAttendance.Application.Permissions.Services;
using SmartAttendance.Application.Setup.Services;
using SmartAttendance.Application.Shifts.Mappings;
using SmartAttendance.Application.Shifts.Services;
using SmartAttendance.Application.SystemUsers.Mappings;
using SmartAttendance.Application.SystemUsers.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Repositories;
using SmartAttendance.Infrastructure.Seeding;
using SmartAttendance.Infrastructure.Services;
using SmartAttendance.Web.Infrastructure.Theming;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages()
    // محرك تقارير واحد يخدم مسارين: الأشخاص (/PeopleReports) والحضور
    // (/AttendanceReports). الصفحة تستنتج الموديول من المسار وتعرض مصادره فقط.
    .AddRazorPagesOptions(options =>
        options.Conventions.AddPageRoute("/PeopleReports/Index", "/AttendanceReports"));

// Branding & Theme Engine runtime (P4): in-memory theme cache + request-scoped
// resolver. No company theme is persisted yet, so this serves the ZYNORA Default.
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IThemeContextService, ThemeContextService>();

// إجبار HTTPS مفصول عن «البيئة»: النشر خلف Cloudflare Tunnel (ينهي TLS عند الحافة
// ويمرّر HTTP محلياً) وعلى LAN يحتاج HTTP لبوت‑ستراب شهادة الـCA. لذا نفصل إنفاذ TLS
// في راية مستقلة (افتراضها false) فيبقى الإنتاج بمعالج أخطاء آمن بلا كسر التنفيل/الدخول.
var forceHttps = builder.Configuration.GetValue<bool>("ForceHttps");

// Persist data-protection keys so auth cookies survive app restarts
// (otherwise every restart regenerates the keys and logs everyone out).
var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

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
        // SameAsRequest يسمح بالدخول عبر HTTP (التنفيل/LAN)؛ Always فقط عند إجبار HTTPS.
        options.Cookie.SecurePolicy = forceHttps
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    // مصادقة توكن Bearer لواجهة الموبايل (بجانب الكوكيز) — كنترولرات /api/*
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
        SmartAttendance.Web.Infrastructure.Api.ApiTokenAuthHandler>(
        SmartAttendance.Web.Infrastructure.Api.ApiTokenAuthHandler.SchemeName, null);

// كنترولرات واجهة الموبايل (REST/JSON) — بجانب Razor Pages
builder.Services.AddControllers();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// AutoMapper
builder.Services.AddAutoMapper(cfg =>
{
    cfg.AddProfile<CompanyProfile>();
    cfg.AddProfile<BranchProfile>();
    cfg.AddProfile<DepartmentProfile>();
    cfg.AddProfile<EmployeeProfile>();
    cfg.AddProfile<DeviceProfile>();
    cfg.AddProfile<ShiftProfile>();
    cfg.AddProfile<EmployeeShiftProfile>();
    cfg.AddProfile<AttendanceRecordProfile>();
    cfg.AddProfile<HolidayProfile>();
    cfg.AddProfile<LeaveRequestProfile>();
    cfg.AddProfile<SystemUserProfile>();
    cfg.AddProfile<PermissionProfile>();
});

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
builder.Services.AddScoped<ISystemUserService, SystemUserService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IEmployeePermissionService, EmployeePermissionService>();
builder.Services.AddScoped<ILoginIdentityService, LoginIdentityService>();
builder.Services.AddScoped<IPermissionAuthorizationService, PermissionAuthorizationService>();
builder.Services.AddScoped<IAttendanceImportService, AttendanceImportService>();
builder.Services.AddScoped<IMasterDataImportService, MasterDataImportService>();
builder.Services.AddScoped<ISetupService, SetupService>();
builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();
builder.Services.AddScoped<SmartAttendance.Web.Infrastructure.Security.IAccessRoleService, SmartAttendance.Web.Infrastructure.Security.AccessRoleService>();
builder.Services.AddScoped<SmartAttendance.Web.Infrastructure.Security.IEffectiveScopeService, SmartAttendance.Web.Infrastructure.Security.EffectiveScopeService>();

// قناة الإشعارات الخارجية (SMTP): تُفعَّل عمداً عبر قسم "Smtp" (Enabled=true). حين
// تكون معطّلة يُحقَن مرسِل No-Op ولا تعمل خدمة التسليم الخلفية (بلا استهلاك).
builder.Services.Configure<SmartAttendance.Web.Infrastructure.Notifications.SmtpOptions>(
    builder.Configuration.GetSection(SmartAttendance.Web.Infrastructure.Notifications.SmtpOptions.SectionName));
var smtpEnabled = builder.Configuration
    .GetSection(SmartAttendance.Web.Infrastructure.Notifications.SmtpOptions.SectionName)
    .Get<SmartAttendance.Web.Infrastructure.Notifications.SmtpOptions>()?.IsUsable ?? false;
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

var app = builder.Build();

await DefaultShiftSeeder.SeedAsync(app.Services);
await PeoplePermissionSeeder.SeedAsync(app.Services);

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

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    // SAMEORIGIN لا DENY: تسمح لبوابة الموظف بتضمين صفحات طلباتها بمكانها (iframe)
    // مع منع أي موقع خارجي من تأطيرنا (حماية من clickjacking).
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // geolocation=(self): يسمح لبصمة الموقع (geofence) من نطاقنا فقط، ويمنع الكاميرا/المايك.
    context.Response.Headers["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=(self)";

    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<RoleSecurityMiddleware>();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapControllers();

// تحميل تطبيق أندرويد الملفوف (APK) — نقطة عامة بنوع MIME الصحيح ليثبّته الموظف مباشرة.
app.MapGet("/app.apk", (IWebHostEnvironment env) =>
{
    var apk = Path.Combine(env.WebRootPath, "downloads", "ZynoraPortal.apk");
    return File.Exists(apk)
        ? Results.File(apk, "application/vnd.android.package-archive", "ZynoraPortal.apk")
        : Results.NotFound();
}).AllowAnonymous();

app.Run();

