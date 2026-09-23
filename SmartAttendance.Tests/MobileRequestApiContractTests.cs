using Xunit;

namespace SmartAttendance.Tests;

public sealed class MobileRequestApiContractTests
{
    [Fact]
    public void MobileRequestApi_UsesDynamicCatalogAndStructuredSubmission()
    {
        var api = Read("SmartAttendance.Web", "Controllers/Api/MeController.cs");

        Assert.Contains("[HttpGet(\"request-types\")]", api, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"requests/create\")]", api, StringComparison.Ordinal);
        Assert.Contains("RequestTypeId", api, StringComparison.Ordinal);
        Assert.Contains("StartTime", api, StringComparison.Ordinal);
        Assert.Contains("EndTime", api, StringComparison.Ordinal);
        Assert.Contains("IFormFile? Attachment", api, StringComparison.Ordinal);
        Assert.Contains("CompanyLeavePolicyStore.ValidateRequestAsync", api, StringComparison.Ordinal);
        Assert.Contains("EmployeeRequestEligibility.CheckAsync", api, StringComparison.Ordinal);
        Assert.Contains("SelfServiceAccessPolicy.IsAllowedAsync", api, StringComparison.Ordinal);
        Assert.Contains("هذه النسخة من تطبيق ZYNORA قديمة", api, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeClient_UsesCatalogMultipartAndNoLegacyWebViewPermissions()
    {
        var mobileApi = Read("ZynoraHR.Mobile", "MobileApi.cs");
        var page = Read("ZynoraHR.Mobile", "MainPage.xaml.cs");
        var manifest = Read("ZynoraHR.Mobile", "Platforms/Android/AndroidManifest.xml");

        Assert.Contains("api/v1/me/request-types", mobileApi, StringComparison.Ordinal);
        Assert.Contains("api/v1/me/requests/create", mobileApi, StringComparison.Ordinal);
        Assert.Contains("MultipartFormDataContent", mobileApi, StringComparison.Ordinal);
        Assert.Contains("FilePicker.Default.PickAsync", page, StringComparison.Ordinal);
        Assert.Contains("HomeCacheKey", page, StringComparison.Ordinal);
        Assert.Contains("location.Accuracy", page, StringComparison.Ordinal);
        Assert.DoesNotContain("android.permission.CAMERA", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("android.permission.POST_NOTIFICATIONS", manifest, StringComparison.Ordinal);
        Assert.Contains("android.permission.USE_BIOMETRIC", manifest, StringComparison.Ordinal);
    }

    private static string Read(string project, string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;

        var root = Assert.IsType<DirectoryInfo>(directory).FullName;
        return File.ReadAllText(Path.Combine(
            root,
            project,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
