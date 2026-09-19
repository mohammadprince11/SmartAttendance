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
}
