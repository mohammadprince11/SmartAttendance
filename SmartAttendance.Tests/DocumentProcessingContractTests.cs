using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Web.Infrastructure.PeopleAi;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class DocumentProcessingContractTests
{
    [Theory]
    [InlineData(".pdf", PeopleAiDocumentTypes.NationalId)]
    [InlineData("PDF", PeopleAiDocumentTypes.Passport)]
    [InlineData(".pdf", PeopleAiDocumentTypes.Cv)]
    public void Pdf_IsQueuedForAutomaticStructuredExtraction(
        string extension,
        string documentType)
    {
        var decision = DocumentProcessingContract.Resolve(
            extension,
            documentType);

        Assert.True(decision.Format.CanExtractText);
        Assert.True(decision.ShouldQueueAutomaticExtraction);
        Assert.True(decision.HasStructuredExtractor);
        Assert.Equal(
            "AutomaticStructuredExtraction",
            decision.ProcessingMode);
    }

    [Theory]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    public void OpenXmlFormats_AreQueuedForAutomaticStructuredExtraction(
        string extension)
    {
        var decision = DocumentProcessingContract.Resolve(
            extension,
            PeopleAiDocumentTypes.Cv);

        Assert.True(decision.Format.CanExtractText);
        Assert.True(decision.ShouldQueueAutomaticExtraction);
        Assert.True(decision.HasStructuredExtractor);
        Assert.Equal(
            "AutomaticStructuredExtraction",
            decision.ProcessingMode);
    }

    [Theory]
    [InlineData(".doc")]
    [InlineData(".xls")]
    public void LegacyOfficeFormats_AreQueuedForAutomaticStructuredExtraction(
        string extension)
    {
        var decision = DocumentProcessingContract.Resolve(
            extension,
            PeopleAiDocumentTypes.Cv);

        Assert.True(decision.Format.CanUpload);
        Assert.True(decision.Format.CanStore);
        Assert.True(decision.Format.CanExtractText);
        Assert.True(decision.ShouldQueueAutomaticExtraction);
        Assert.True(decision.HasStructuredExtractor);
        Assert.Equal(
            "AutomaticStructuredExtraction",
            decision.ProcessingMode);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".webp")]
    [InlineData(".pdf")]
    public void BrowserSafeFormats_SupportProtectedPreview(
        string extension)
    {
        var decision = DocumentProcessingContract.Resolve(
            extension,
            PeopleAiDocumentTypes.Unknown);

        Assert.True(decision.Format.CanPreview);
    }

    [Theory]
    [InlineData(".doc")]
    [InlineData(".docx")]
    [InlineData(".xls")]
    [InlineData(".xlsx")]
    public void OfficeFormats_DoNotUseInlinePreview(string extension)
    {
        var decision = DocumentProcessingContract.Resolve(
            extension,
            PeopleAiDocumentTypes.Unknown);

        Assert.False(decision.Format.CanPreview);
    }

    [Fact]
    public void Pdf_CustomType_StillQueuesTextExtraction()
    {
        var decision = DocumentProcessingContract.Resolve(
            ".pdf",
            "CustomCertificate");

        Assert.True(decision.ShouldQueueAutomaticExtraction);
        Assert.False(decision.HasStructuredExtractor);
        Assert.Equal(
            "AutomaticTextExtraction",
            decision.ProcessingMode);
    }

    [Fact]
    public void NationalId_UsesArabicAndEnglishForVisualAndMrzSides()
    {
        var profile = DocumentProcessingContract.ResolveOcrLanguageProfile(
            ["ar", "en"],
            PeopleAiDocumentTypes.NationalId,
            "ar");

        Assert.Equal("ar,en", profile);
    }

    [Fact]
    public void Passport_UsesEnglishForTd3Mrz()
    {
        var profile = DocumentProcessingContract.ResolveOcrLanguageProfile(
            ["ar", "en"],
            PeopleAiDocumentTypes.Passport,
            "ar");

        Assert.Equal("en", profile);
    }
}
