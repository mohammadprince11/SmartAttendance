using System.Text.Json;
using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Tests;

public sealed class FreeAnnouncementTests
{
    [Fact] public void UsesConfiguredLanguagesAndPlacesPrimaryFirst()
    {
        var texts = new[] { new FreeAnnouncementText { LanguageCode="ar-IQ", Title="إعلان افتراضي", Body="نص تجريبي" }, new FreeAnnouncementText { LanguageCode="fr-FR", Title=" Test ", Body=" Body " }, new FreeAnnouncementText {LanguageCode="en-US"} };
        var result=FreeAnnouncement.Normalize(texts,"fr-FR",["ar-IQ","en-US","fr-FR"]);
        Assert.Equal(2,result.Count);Assert.Equal("fr-FR",result[0].LanguageCode);Assert.Equal("Test",result[0].Title);
        Assert.Throws<ArgumentException>(()=>FreeAnnouncement.Normalize(texts,"de-DE",["ar-IQ","fr-FR"]));
        Assert.Throws<ArgumentException>(()=>FreeAnnouncement.Normalize(texts,"ar-IQ",["ar-IQ"]));
    }
    [Fact] public void RequiresCompleteTranslationsAndPrimaryText()
    {
        Assert.Throws<ArgumentException>(()=>FreeAnnouncement.Normalize([],"en-US",["en-US"]));
        Assert.Throws<ArgumentException>(()=>FreeAnnouncement.Normalize([new(){LanguageCode="en-US",Title="Only title"}],"en-US",["en-US"]));
        Assert.Throws<ArgumentException>(()=>FreeAnnouncement.Normalize([new(){LanguageCode="en-US",Title=new string('x',251),Body="Body"}],"en-US",["en-US"]));
        Assert.Throws<ArgumentException>(()=>FreeAnnouncement.Normalize([new(){LanguageCode="en-US",Title="Test",Body=new string('x',20001)}],"en-US",["en-US"]));
        Assert.Throws<ArgumentException>(()=>FreeAnnouncement.Normalize([new(){LanguageCode="en-US",Title="Test",Body="Body"},new(){LanguageCode="EN-us",Title="Test",Body="Body"}],"en-US",["en-US"]));
    }
    [Fact] public void RecipientUsesMatchingTranslationThenConfiguredPrimary()
    {
        var select=typeof(SmartAttendance.Infrastructure.Services.AnnouncementService).GetMethod("SelectContent",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        var snapshot=JsonSerializer.Serialize(new StudioPresentation(Guid.Empty,"contain","center","above",PrimaryLanguage:"fr-FR"));
        var items=new[] {new AnnouncementContent {LanguageCode="ar-IQ",Title="Arabic",PresentationJson=snapshot},new AnnouncementContent {LanguageCode="fr-FR",Title="French",PresentationJson=snapshot},new AnnouncementContent {LanguageCode="en-US",Title="English",PresentationJson=snapshot}};
        AnnouncementContent? Pick(string code)=>(AnnouncementContent?)select.Invoke(null,[items,code]);
        Assert.Equal("English",Pick("en-GB")?.Title);Assert.Equal("Arabic",Pick("ar")?.Title);Assert.Equal("French",Pick("de-DE")?.Title);
    }
    [Fact] public void AttachmentRejectsMismatchedCompanyIdAndUnsafeData()
    {
        var validate=typeof(SmartAttendance.Infrastructure.Services.AnnouncementService).GetMethod("ValidateCreateRequest",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        var id=Guid.NewGuid();var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jBv0AAAAASUVORK5CYII=");
        string? Check(int company,Guid snapshotId,byte[] bytes)=>(string?)validate.Invoke(null,[new AnnouncementCreateRequest {LanguageCode="en-US",Title="Test",Body="Body",CompanyIds=[7],ImageUpload=new(id,company,"Test","image/png",bytes),PresentationJson=JsonSerializer.Serialize(new StudioPresentation(snapshotId,"contain","center","above",PrimaryLanguage:"en-US"))}]);
        Assert.Null(Check(7,id,png));Assert.NotNull(Check(8,id,png));Assert.NotNull(Check(7,Guid.NewGuid(),png));Assert.NotNull(Check(7,id,"<svg>bad</svg>"u8.ToArray()));
    }
}
