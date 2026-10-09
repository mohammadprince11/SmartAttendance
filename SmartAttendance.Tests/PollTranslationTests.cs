using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public class PollTranslationTests
{
    private static readonly string[] Languages = ["ar-IQ", "en-US", "ckb-IQ"];
    private static PollTextTranslation English() => new() { LanguageCode = "en-US", Title = "Lunch preference", Question = "Choose a meal", Options = ["Rice", "Salad"] };
    [Fact]
    public void CompactFieldTranslationsKeepCollectionContractAndGuardIncompleteContent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "SmartAttendance.Web", "Pages"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var root = directory!.FullName;
        var view = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web/Pages/Engagement/Index.cshtml"));
        var partial = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web/Pages/Engagement/_PollFieldLanguages.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web/wwwroot/js/zynora-poll-management.js"));
        var handler = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web/Pages/Engagement/Index.Handlers.cs"));
        Assert.Contains("type=\"hidden\" id=\"poll-primary-language\" value=\"@Model.PollPrimaryLanguage\"", view);
        Assert.DoesNotContain("<select id=\"poll-primary-language\"", view);
        Assert.DoesNotContain("name=\"Poll.PrimaryLanguage\"", view);
        Assert.Contains("PollTranslations.Normalize(PollPrimaryLanguage,", handler);
        Assert.DoesNotContain("Poll.PrimaryLanguage", handler);
        Assert.Contains("primary.dataset.primaryName", script);
        Assert.DoesNotContain("primary.selectedOptions", script);
        Assert.DoesNotContain("primary.options", script);
        Assert.Contains("data-poll-language-toggle aria-expanded=\"false\"", view);
        Assert.DoesNotContain("data-poll-language-add", view);
        Assert.Contains("Poll.Translations.Index", view);
        Assert.Contains("Poll.Translations[@index].@Model.Field", partial);
        Assert.Contains("data-poll-field-languages hidden", partial);
        Assert.Contains("remove.closest('.zy-poll-option').remove()", script);
        Assert.Contains("if (missing)", script);
        Assert.Contains("inputs.forEach(input => input.disabled = !active.has", script);
    }
    [Fact]
    public void ResolvesEntireContentByUserLanguageAndFallsBackToOriginal()
    {
        var json = PollTranslations.Normalize("ar-IQ", [English()], Languages, 2);
        var translated = PollTranslations.Resolve(json, "en-US", 2)!;
        Assert.Equal("Lunch preference", translated.Title);
        Assert.Equal("Choose a meal", translated.Question);
        Assert.Equal(new[] { "Rice", "Salad" }, translated.Options);
        Assert.NotNull(PollTranslations.Resolve(json, "en", 2));
        Assert.Null(PollTranslations.Resolve(json, "ar-IQ", 2));
        Assert.Null(PollTranslations.Resolve(json, "ckb-IQ", 2));
        Assert.Null(PollTranslations.Resolve(null, "en-US", 2));
        Assert.Null(PollTranslations.Resolve("invalid", "en-US", 2));
        Assert.Null(PollTranslations.Resolve(json, "en-US", 3));
    }
    [Fact]
    public void RejectsIncompleteDuplicateAndUnsupportedTranslations()
    {
        Assert.Throws<ArgumentException>(() => PollTranslations.Normalize("xx", [], Languages, 2));
        Assert.Throws<ArgumentException>(() => PollTranslations.Normalize("ar-IQ", [English(), English()], Languages, 2));
        Assert.Throws<ArgumentException>(() => PollTranslations.Normalize("en-US", [English()], Languages, 2));
        var invalid = English(); invalid.Options = ["Rice"];
        Assert.Throws<ArgumentException>(() => PollTranslations.Normalize("ar-IQ", [invalid], Languages, 2));
        invalid = English(); invalid.Question = "";
        Assert.Throws<ArgumentException>(() => PollTranslations.Normalize("ar-IQ", [invalid], Languages, 2));
    }
    [Fact]
    public void ReadingTranslationDoesNotMutateContentOrOptionIdentity()
    {
        var original = English();
        var json = PollTranslations.Normalize("ar-IQ", [original], Languages, 2);
        var a = PollTranslations.Resolve(json, "en-US", 2)!;
        var b = PollTranslations.Resolve(json, "en-US", 2)!;
        a.Options[0] = "Changed";
        Assert.Equal("Rice", b.Options[0]);
        Assert.Equal("Rice", original.Options[0]);
    }
}
