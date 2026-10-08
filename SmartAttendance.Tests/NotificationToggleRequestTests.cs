using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Pages.HrSettings;

namespace SmartAttendance.Tests;

public sealed class NotificationToggleRequestTests
{
    [Fact]
    public async Task JsonToggle_RejectsInvalidId_BeforeDatabaseAccess()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().Options);
        Assert.IsType<BadRequestResult>(await CreateModel(db).OnPostToggleRuleAsync(0, true));
    }

    [Fact]
    public async Task JsonToggle_RequiresExplicitDesiredState_BeforeDatabaseAccess()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().Options);
        var model = CreateModel(db);
        Assert.IsType<BadRequestResult>(await model.OnPostToggleRuleAsync(1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JsonToggle_RejectsInvalidBinding_BeforeDatabaseAccess(bool desired)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().Options);
        var model = CreateModel(db);
        model.ModelState.AddModelError("isEnabled", "Invalid boolean");
        Assert.IsType<BadRequestResult>(await model.OnPostToggleRuleAsync(1, desired));
    }

    private static NotificationCenterModel CreateModel(ApplicationDbContext db)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Accept = "application/json";
        return new NotificationCenterModel(db, null!)
        {
            PageContext = new PageContext { HttpContext = context }
        };
    }

    [Theory]
    [InlineData(0, "المشرفين", 0, "كل الموظفين")]
    [InlineData(1, "المشرفين", -1, "كل الموظفين")]
    [InlineData(1, "", 0, "كل الموظفين")]
    [InlineData(1, "المشرفين", 0, "")]
    [InlineData(1, "المشرفين", 367, "كل الموظفين")]
    [InlineData(1, "unknown", 0, "كل الموظفين")]
    public async Task JsonDetails_RejectsInvalidValues_BeforeDatabaseAccess(int id, string audience, int days, string items)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().Options);
        var model = CreateModel(db);
        Assert.IsType<BadRequestResult>(await model.OnPostUpdateRuleAsync(id, audience, days, items, null));
    }

    [Fact]
    public async Task JsonDetails_RejectsInvalidBinding_BeforeDatabaseAccess()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().Options);
        var model = CreateModel(db);
        model.ModelState.AddModelError("daysBefore", "Invalid integer");
        Assert.IsType<BadRequestResult>(await model.OnPostUpdateRuleAsync(1, "المشرفين", 0, "كل الموظفين", null));
    }
}
