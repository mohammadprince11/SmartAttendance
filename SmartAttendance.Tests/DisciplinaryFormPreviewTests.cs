using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Pages.DisciplinaryRules;
using System.Security.Claims;

namespace SmartAttendance.Tests;

public sealed class DisciplinaryFormPreviewTests
{
    [Theory]
    [InlineData("/uploads/disciplinary-forms/../a4-form_20000101000000000.pdf")]
    [InlineData("/uploads/disciplinary-forms/a4-form_20000101000000000.pdf?x=1")]
    [InlineData("/uploads/disciplinary-forms/a4-form_20000101000000000.pdf\n")]
    [InlineData("/uploads/disciplinary-forms/a4-form_20000101000000000.svg")]
    [InlineData("/uploads/employees/a4-form_20000101000000000.pdf")]
    [InlineData("https://example.test/uploads/disciplinary-forms/a4-form_20000101000000000.pdf")]
    [InlineData("C:\\private\\file.pdf")]
    [InlineData(null)]
    public void InvalidStoredPaths_AreRejected(string? path) =>
        Assert.Null(DisciplinaryFormPreview.Resolve(Path.GetTempPath(), path));

    [Theory]
    [InlineData("pdf", "application/pdf")]
    [InlineData("png", "image/png")]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    public void NewlyCreatedForm_IsResolvedWithoutStaticAssetManifest(string extension, string contentType)
    {
        var root = Path.Combine(Path.GetTempPath(), "zynora-form-preview-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var directory = Path.Combine(root, "uploads", "disciplinary-forms");
            Directory.CreateDirectory(directory);
            var name = "a4-form_20000101000000000." + extension;
            File.WriteAllText(Path.Combine(directory, name), "synthetic fixture only");
            var asset = DisciplinaryFormPreview.Resolve(root, "/uploads/disciplinary-forms/" + name);
            Assert.NotNull(asset);
            Assert.Equal(Path.Combine(directory, name), asset.FullPath);
            Assert.Equal(contentType, asset.ContentType);
            Assert.Null(DisciplinaryFormPreview.Resolve(root, "/uploads/disciplinary-forms/a4-form_20000102000000000.pdf"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnauthenticatedOrDeniedScope_CannotReadFileOrSettings(bool authenticated)
    {
        var scope = new DeniedScope();
        // Null dependencies prove denial happens before any DB/file read.
        var model = new IndexModel(null!, null!, scope)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };
        if (authenticated) model.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var result = await model.OnGetA4FormPreviewAsync();
        if (authenticated) { Assert.IsType<ForbidResult>(result); Assert.Equal(1, scope.Calls); }
        else { Assert.IsType<ChallengeResult>(result); Assert.Equal(0, scope.Calls); }
    }

    private sealed class DeniedScope : ICompanyScopeProvider
    {
        public int Calls { get; private set; }
        public Task<CompanyScope> GetAsync(CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(CompanyScope.DeniedAll()); }
    }
}
