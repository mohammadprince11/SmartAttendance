using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Branches.Mappings;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Companies.Mappings;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Departments.Mappings;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Repositories;
using SmartAttendance.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// AutoMapper
builder.Services.AddAutoMapper(cfg =>
{
    cfg.AddProfile<CompanyProfile>();
    cfg.AddProfile<BranchProfile>();
    cfg.AddProfile<DepartmentProfile>();
});

// Repositories
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Services
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IBranchService, BranchService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();