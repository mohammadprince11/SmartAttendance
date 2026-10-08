using SmartAttendance.Web.Pages.DisciplinaryRules;

namespace SmartAttendance.Tests;

public sealed class DisciplinaryPreviewLayoutTests
{
    [Fact]
    public void TextBlockOffset_UsesLogicalPropertyAcceptedByStyleHydrator()
    {
        var model = new IndexModel(null!, null!, null!);
        var block = new FormTextBlock { XPercent = 8, YPercent = 13, WidthPercent = 84, FontSize = 20 };
        var style = model.BuildTextBlockStyle(block);
        Assert.Contains("inset-inline-start:8%", style);
        Assert.Contains("top:13%", style);
        Assert.Contains("width:84%", style);
        Assert.DoesNotContain("right:", style);
    }

    [Fact]
    public void MainBodyOffset_UsesLogicalPropertyWithoutChangingSavedCoordinates()
    {
        var model = new IndexModel(null!, null!, null!);
        Assert.Contains("inset-inline-start:8%", model.BuildMainBodyStyle());
        Assert.Equal(8, model.Settings.MainBodyXPercent);
    }
}
