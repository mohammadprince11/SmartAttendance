using System.Text.Json;
using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Web.Pages.Engagement;
using SmartAttendance.Web.Pages.EmployeePortal;

namespace SmartAttendance.Tests;

public sealed class AnnouncementStudioTests
{
    [Fact] public void DefaultsAreCompleteAndDistinct()
    {
        var all = AnnouncementStudio.Defaults();
        Assert.Equal(11, all.Count);
        Assert.Equal(all.Count, all.Select(x => x.Key).Distinct().Count());
        foreach(var t in all) { Assert.Null(AnnouncementStudio.Validate(t)); Assert.Contains("ar",t.Languages.Keys); Assert.Contains("en",t.Languages.Keys); }
    }
    [Theory]
    [InlineData("date", "2026-02-30")]
    [InlineData("date", "1970-01-01T00:00:45.943Z")]
    [InlineData("month", "2026-13")]
    [InlineData("number", "NaN")]
    public void InvalidTypedValuesRejected(string type,string value)
    {
        var t = Custom(new("event", "المناسبة", type));
        Assert.Throws<ArgumentException>(()=>AnnouncementStudio.Render(t,new Dictionary<string,string>{{"event",value}},new(2026,10,9)));
    }
    [Fact] public void RequiredAndOptionalFieldsAreEnforced()
    {
        Assert.Throws<ArgumentException>(()=>AnnouncementStudio.Render(Custom(new("event","التاريخ","date")),new Dictionary<string,string>(),new(2026,10,9)));
        Assert.Single(AnnouncementStudio.Render(Custom(new("event","التاريخ","date",false)),new Dictionary<string,string>(),new(2026,10,9)));
    }
    [Fact] public void DateRangeCannotBeReversed()
    {
        var t=AnnouncementStudio.Defaults().Single(t=>t.Key=="holiday");
        Assert.Throws<ArgumentException>(()=>AnnouncementStudio.Render(t,new Dictionary<string,string>{{"occasion","Test"},{"startDate","2026-10-10"},{"endDate","2026-10-09"}},new(2026,10,9)));
    }
    [Theory][InlineData("2023-10-09",3)][InlineData("2023-10-10",2)][InlineData("2024-02-29",2)]
    public void AnniversaryUsesCompletedYears(string joining,int years)
    {
        var t=AnnouncementStudio.Defaults().Single(t=>t.Key=="anniversary");
        var result=AnnouncementStudio.Render(t,new Dictionary<string,string>{{"person","Example Employee"},{"joiningDate",joining}},new(2026,10,9));
        Assert.Contains($"{years} years", result.Single(r=>r.LanguageCode=="en").Body);
    }
    [Fact] public void LanguageAndFieldSchemasAreDynamic()
    {
        var t=Custom(new("event","حدث"));t.Languages["ckb-IQ"]=new("{event}","{event}");t.Languages["fr-FR"]=new("{event}","{event}");
        Assert.Null(AnnouncementStudio.Validate(t));
        Assert.Equal(3,AnnouncementStudio.Render(t,new Dictionary<string,string>{{"event","Example"}},new(2026,10,9)).Count);
    }
    [Fact] public void UnknownTokensDuplicateFieldsAndNullSchemaAreRejected()
    {
        var t=Custom(new("event","حدث"));t.Languages["ar"]=new("{unknown}","Body");Assert.NotNull(AnnouncementStudio.Validate(t));
        t=Custom(new("event","حدث"));t.Fields.Add(t.Fields[0]);Assert.NotNull(AnnouncementStudio.Validate(t));
        t.Fields=null!;Assert.NotNull(AnnouncementStudio.Validate(t));
    }
    [Theory][InlineData("../../ar")][InlineData("<script>")][InlineData("")]
    public void InvalidLanguageCodesRejected(string code)=>Assert.False(AnnouncementStudio.ValidLanguage(code));
    [Fact] public void RenderingDoesNotRecursivelyInterpretUserInput()
    {
        var result=AnnouncementStudio.Render(Custom(new("event","حدث")),new Dictionary<string,string>{{"event","<script>{event}</script>"}},new(2026,10,9));
        Assert.Equal("<script>{event}</script>",result[0].Title);
    }
    [Fact] public void StoredSnapshotsSurviveTemplateChanges()
    {
        var t=Custom(new("event","حدث"));var original=AnnouncementStudio.Render(t,new Dictionary<string,string>{{"event","Example"}},new(2026,10,9));
        t.Languages["ar"]=new("Changed","Changed"); Assert.Equal("Example",original[0].Title);
        var p=new StudioPresentation(Guid.NewGuid(),"cover","top","below");Assert.Equal(p,JsonSerializer.Deserialize<StudioPresentation>(JsonSerializer.Serialize(p)));
    }
    [Theory][InlineData("<svg>test</svg>")][InlineData("<html>test</html>")][InlineData("fake.jpg")]
    public void ActiveOrFakeImageUploadsRejected(string content)=>Assert.Null(StudioModel.DetectImage(System.Text.Encoding.UTF8.GetBytes(content)));
    [Fact] public void ValidPngAccepted()
    {
        var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jBv0AAAAASUVORK5CYII=");
        Assert.Equal("image/png",StudioModel.DetectImage(png));
        png[16]=255;Assert.Null(StudioModel.DetectImage(png));
    }
    [Fact] public void RecipientImageRequiresMatchingNonEmptySnapshotId()
    {
        var id=Guid.NewGuid();var json=JsonSerializer.Serialize(new StudioPresentation(id,"contain","center","above"));
        Assert.True(AnnouncementDesignModel.Matches(json,id));Assert.False(AnnouncementDesignModel.Matches(json,Guid.NewGuid()));Assert.False(AnnouncementDesignModel.Matches("broken",id));Assert.False(AnnouncementDesignModel.Matches(null,id));Assert.False(AnnouncementDesignModel.Matches(json,Guid.Empty));
    }
    [Fact] public void BuiltinTemplatesCanUseNewDictionaryLanguagesWithoutEditingSource()
    {
        var t=AnnouncementStudio.Defaults().First();var source=t.Languages["ar"];
        var catalog=new Dictionary<string,string>{{source.Title,"Joyeux anniversaire {person}!"},{source.Body,"Date : {date}."}};
        Assert.True(SmartAttendance.Web.Infrastructure.Localization.AnnouncementTemplateDictionary.Apply(t,"fr-FR",catalog));
        Assert.Contains("fr-FR",AnnouncementStudio.Render(t,new Dictionary<string,string>{{"person","Synthetic"},{"date","2026-10-09"}},new(2026,10,9)).Select(x=>x.LanguageCode));
        var invalid=AnnouncementStudio.Defaults().First();catalog[source.Body]="Unknown {badField}";
        Assert.False(SmartAttendance.Web.Infrastructure.Localization.AnnouncementTemplateDictionary.Apply(invalid,"fr-FR",catalog));
        Assert.False(invalid.Languages.ContainsKey("fr-FR"));
    }
    private static StudioTemplate Custom(StudioField field)=>new(){Key="example",Name="Example",Fields=[field],Languages=new(){["ar"]=new("{event}","{event}")}};
}
