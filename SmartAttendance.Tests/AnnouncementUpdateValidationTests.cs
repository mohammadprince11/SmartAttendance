using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Infrastructure.Services;

namespace SmartAttendance.Tests;
public sealed class AnnouncementUpdateValidationTests
{
    private static string? Validate(AnnouncementUpdateRequest request) => (string?)typeof(AnnouncementService)
        .GetMethod("ValidateUpdate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[request]);
    private static AnnouncementUpdateRequest Request() => new() { Id=1,Revision=Convert.ToBase64String(new byte[8]),Translations=[new(){LanguageCode="en-US",Title="Test",Body="Body"}] };
    [Fact] public void RequiresCompleteBoundedTextsAndConcurrencyToken()
    {
        Assert.Null(Validate(Request()));
        var r=Request();r.Id=0;Assert.NotNull(Validate(r));
        r=Request();r.Revision="invalid";Assert.NotNull(Validate(r));
        r=Request();r.Revision="";Assert.NotNull(Validate(r));
        r=Request();r.Translations[0].Body=" ";Assert.NotNull(Validate(r));
        r=Request();r.Translations[0].Title=new string('x',251);Assert.NotNull(Validate(r));
        r=Request();r.Translations[0].Body=new string('x',20001);Assert.NotNull(Validate(r));
        r=Request();r.Translations.Add(new(){LanguageCode="EN-us",Title="Test",Body="Body"});Assert.NotNull(Validate(r));
    }
}
