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
}
