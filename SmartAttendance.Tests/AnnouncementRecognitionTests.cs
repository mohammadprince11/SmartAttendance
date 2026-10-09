using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Web.Pages.Engagement;

namespace SmartAttendance.Tests;

public class AnnouncementRecognitionTests
{
    private sealed class RecognitionModel() : EngagementPageModel(null!, null!)
    {
        public static string Resolve(string? category, string? title) => ResolveAnnouncementTemplateKey(category, title);
        public void Load(string category, string title) => Announcements =
            [new() { Id = 1, Category = category, Title = title, TemplateKey = Resolve(category, title) }];
    }

    [Theory]
    [InlineData("ترقية موظف", "مبارك الترقية!")]
    [InlineData("ترقية موظف", "Congratulations on your promotion!")]
    [InlineData(" ترقية موظف ", "عنوان مخصص")]
    [InlineData("تهنئة", "مبارك الترقية!")]
    public void PromotionAppearsInRecognitionWithoutChangingAnnouncement(string category, string title)
    {
        var model = new RecognitionModel();
        model.Load(category, title);
        var item = Assert.Single(model.RecognitionAnnouncements);
        Assert.Equal("promotion", item.TemplateKey);
        Assert.Same(model.Announcements[0], item);
        Assert.Equal(title, item.Title);
    }

    [Fact]
    public void StudioPromotionCategoryIsRecognized()
    {
        var template = AnnouncementStudio.Defaults().Single(t => t.Key == "promotion");
        Assert.Equal("promotion", RecognitionModel.Resolve(template.Name, template.Languages["en"].Title));
    }

    [Theory]
    [InlineData("عام", "إعلان عام", "custom")]
    [InlineData("زواج موظف", "زواج مبارك!", "custom")]
    [InlineData("إعلان عطلة", "عطلة", "holiday")]
    [InlineData("تهنئة", "مبارك المولود!", "newborn")]
    public void OtherCategoriesKeepExistingClassification(string category, string title, string expected)
    {
        Assert.Equal(expected, RecognitionModel.Resolve(category, title));
    }
}
