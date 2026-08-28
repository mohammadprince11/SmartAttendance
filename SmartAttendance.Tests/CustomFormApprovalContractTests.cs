namespace SmartAttendance.Tests;

public sealed class CustomFormApprovalContractTests
{
    [Fact]
    public void CustomFormSubmission_IsLinkedSnapshottedAndShownInsideApprovals()
    {
        var root = RepoRoot();
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "FormSubmissionStore.cs");
        var form = Read(root, "SmartAttendance.Web", "Pages", "EmployeePortal", "FormFill.cshtml.cs");
        var approvals = Read(root, "SmartAttendance.Web", "Pages", "Approvals", "Index.cshtml") +
                        Read(root, "SmartAttendance.Web", "Pages", "Approvals", "Index.cshtml.cs");
        var engine = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "ApprovalWorkflowEngine.cs");

        Assert.Contains("20260826-24-form-submission-approval-link", migration);
        Assert.Contains("UX_FormSubmissions_Request", migration);
        Assert.Contains("UX_FormSubmissions_ClientRequestToken", migration);
        Assert.Contains("INSERT INTO SelfServiceRequests", store);
        Assert.Contains("ApprovalWorkflowEngine.StartAsync", store);
        Assert.Contains("RequestId IS NULL", store); // direct review cannot bypass linked workflow
        Assert.Contains("WITH(UPDLOCK,HOLDLOCK)", store); // retries cannot duplicate the request
        Assert.Contains("LoadAnswersForRequestsAsync", approvals);
        Assert.Contains("حقول الطلب المخصص", approvals);
        Assert.Contains("submitted.Workflow?.Ok", form);
        Assert.Contains("UPDATE FormSubmissions", engine);
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
