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

builder.Services.AddRazorPages();

// Branding & Theme Engine runtime (P4): in-memory theme cache + request-scoped
// resolver. No company theme is persisted yet, so this serves the ZYNORA Default.
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IThemeContextService, ThemeContextService>();

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
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
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

var app = builder.Build();

await DefaultShiftSeeder.SeedAsync(app.Services);
await PeoplePermissionSeeder.SeedAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=()";

    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<RoleSecurityMiddleware>();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

