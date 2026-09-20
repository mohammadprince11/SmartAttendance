using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

public class LeavePolicyTruthSourceTests
{
    private static string PortalSource()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "EmployeePortal", "Index.cshtml.cs"));
    }

    [Fact]
    public void EssRequestSubmission_UsesCompanyLeavePolicyEngine()
    {
        var source = PortalSource();

        Assert.Contains("CompanyLeavePolicyStore.ValidateRequestAsync", source);
        Assert.DoesNotContain("typeLabel == \"إجازة سنوية\"", source);
    }

    [Fact]
    public void EssRequestSubmission_PersistsRequestTypeIdentity()
    {
        var source = PortalSource();

        Assert.Contains("(EmployeeId, RequestTypeId, RequestType, CreatedAt", source);
        Assert.Contains("@RequestTypeId, @RequestType", source);
        Assert.Contains("typeDef?.Id", source);
    }
}
