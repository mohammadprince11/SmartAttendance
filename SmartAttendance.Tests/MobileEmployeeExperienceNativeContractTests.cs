using Xunit;

namespace SmartAttendance.Tests;

public sealed class MobileEmployeeExperienceNativeContractTests
{
    [Fact]
    public void EmployeeExperienceApi_ExposesSurveyFeedbackAndDisciplineContracts()
    {
        var api = Read("SmartAttendance.Web", "Controllers/Api/MeController.cs");

        Assert.Contains("[HttpGet(\"surveys\")]", api, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"surveys/{id:int}\")]", api, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"surveys/{id:int}\")]", api, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"polls\")]", api, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"polls/{id:int}/vote\")]", api, StringComparison.Ordinal);
        Assert.Contains("EmployeePolls", api, StringComparison.Ordinal);
        Assert.Contains("EmployeePollOptions", api, StringComparison.Ordinal);
        Assert.Contains("EmployeePollVotes", api, StringComparison.Ordinal);
        Assert.Contains("FormSubmissionStore.AvailableForAsync", api, StringComparison.Ordinal);
        Assert.Contains("FormBuilder.ValidateSubmission", api, StringComparison.Ordinal);
        Assert.Contains("FormSubmissionStore.SubmitAsync", api, StringComparison.Ordinal);

        Assert.Contains("[HttpGet(\"feedback\")]", api, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"feedback\")]", api, StringComparison.Ordinal);
        Assert.Contains("EmployeeFeedbackItems", api, StringComparison.Ordinal);
        Assert.Contains("EmployeeRequestEligibility.CheckAsync", api, StringComparison.Ordinal);

        Assert.Contains("[HttpGet(\"discipline\")]", api, StringComparison.Ordinal);
        Assert.Contains("EmployeeViolationCases", api, StringComparison.Ordinal);
        Assert.Contains("ViolationCaseSchema.EnsureAsync", api, StringComparison.Ordinal);

        Assert.Contains("[HttpGet(\"team\")]", api, StringComparison.Ordinal);
        Assert.Contains("e.DirectManagerId = @ManagerId", api, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"approvals\")]", api, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"approvals/{id:int}/approve\")]", api, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"approvals/{id:int}/reject\")]", api, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"approvals/{id:int}/return\")]", api, StringComparison.Ordinal);
        Assert.Contains("ApprovalWorkflowEngine.CanActAsync", api, StringComparison.Ordinal);
        Assert.Contains("ApprovalScopeAsync", api, StringComparison.Ordinal);
        Assert.Contains("CompanyScope.ForCompanies([companyId.Value])", api, StringComparison.Ordinal);

        var approvalsGetStart = api.IndexOf(
            "[HttpGet(\"approvals\")]",
            StringComparison.Ordinal);
        var approvalsApproveStart = api.IndexOf(
            "[HttpPost(\"approvals/{id:int}/approve\")]",
            approvalsGetStart,
            StringComparison.Ordinal);
        Assert.True(approvalsGetStart >= 0 && approvalsApproveStart > approvalsGetStart);
        var approvalsGetBlock = api[approvalsGetStart..approvalsApproveStart];
        Assert.DoesNotContain(
            "ApprovalWorkflowEngine.StartAsync",
            approvalsGetBlock,
            StringComparison.Ordinal);

        Assert.Contains("body?.ApprovedFieldKeys", api, StringComparison.Ordinal);
        Assert.Contains("ApprovalWorkflowEngine.ApproveAsync", api, StringComparison.Ordinal);
        var workflow = Read("SmartAttendance.Web", "Infrastructure/Hrms/ApprovalWorkflowEngine.cs");
        Assert.Contains("DataChangeRequestStore.SetFieldDecisionsAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("ApplyApprovalEffectsAsync", api, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeNavigation_DoesNotReloadHomeDataOnEveryTabSwitch()
    {
        var page = Read("ZynoraHR.Mobile", "MainPage.xaml.cs");
        var xaml = Read("ZynoraHR.Mobile", "MainPage.xaml");

        Assert.DoesNotContain("OnHomeTab(object? sender, EventArgs e)\n    {\n        ShowHome();\n        await MainScrollView.ScrollToAsync(0, 0, true);\n\n        if (Online() && !_busy)\n            await LoadAsync();", page, StringComparison.Ordinal);
        Assert.DoesNotContain("OnAttendanceTab(object? sender, EventArgs e)\n    {\n        ShowAttendance();\n        await MainScrollView.ScrollToAsync(0, 0, true);\n\n        if (Online() && !_busy)\n            await LoadAsync();", page, StringComparison.Ordinal);
        Assert.DoesNotContain("OnCompensationTab(object? sender, EventArgs e)\n    {\n        ShowCompensation();\n        await MainScrollView.ScrollToAsync(0, 0, true);\n\n        if (Online() && !_busy)\n            await LoadAsync();", page, StringComparison.Ordinal);

        Assert.Contains("public MobileCompensation? Compensation { get; set; }", page, StringComparison.Ordinal);
        Assert.Contains("cache.Compensation", page, StringComparison.Ordinal);
        Assert.Contains("if (!_requestsLoaded && !_busy)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("_sectionScrollPositions", page, StringComparison.Ordinal);
        Assert.Contains("SwitchSectionAsync(\"Compensation\", ShowCompensation)", page, StringComparison.Ordinal);
        Assert.Contains("SwitchSectionAsync(\"Requests\", ShowRequests)", page, StringComparison.Ordinal);
        Assert.Contains("await MainScrollView.ScrollToAsync(0, 0, false);", page, StringComparison.Ordinal);
        Assert.Contains("LoginCard.IsVisible", page, StringComparison.Ordinal);
        Assert.Contains("return base.OnBackButtonPressed();", page, StringComparison.Ordinal);
        Assert.Contains("case \"Login\":", page, StringComparison.Ordinal);
        Assert.Contains("ShowLogin();", page, StringComparison.Ordinal);
        Assert.Contains("AuthenticatedHeaderActions.IsVisible = false;", page, StringComparison.Ordinal);
        Assert.Contains("AuthenticatedHeaderActions.IsVisible = true;", page, StringComparison.Ordinal);
        Assert.Contains("BiometricRetryButton.IsVisible = allowBiometricRetry;", page, StringComparison.Ordinal);
        Assert.Contains("RefreshBiometricRetryVisibilityAsync", page, StringComparison.Ordinal);
        Assert.Contains("var hasSession = await _api.HasSessionAsync();", page, StringComparison.Ordinal);
        Assert.Contains("BiometricRetryButton.IsVisible = hasSession;", page, StringComparison.Ordinal);
        Assert.Contains("ShowLogin(allowBiometricRetry: true);", page, StringComparison.Ordinal);
        Assert.Contains("_activeSection = \"Home\";", page, StringComparison.Ordinal);
        Assert.Contains("OnBiometricRetryClicked", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AuthenticatedHeaderActions\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BiometricRetryButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Clicked=\"OnBiometricRetryClicked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LogoutConfirmOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Clicked=\"OnConfirmLogoutClicked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Clicked=\"OnCancelLogoutClicked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowLogoutConfirmation();", page, StringComparison.Ordinal);
        Assert.Contains("ExecuteLogoutAsync", page, StringComparison.Ordinal);
        Assert.Contains("BuildPendingTwoFactorVerificationUi", page, StringComparison.Ordinal);
        Assert.Contains("state.SetupInProgress", page, StringComparison.Ordinal);
        Assert.Contains("تفعيل المصادقة الثنائية", page, StringComparison.Ordinal);
        Assert.DoesNotContain("var confirmed = await DisplayAlertAsync(\n                \"تسجيل الخروج\"", page, StringComparison.Ordinal);
        Assert.Contains("AttendanceLocationLabel", page, StringComparison.Ordinal);
        Assert.Contains("RefreshLocationPreviewAsync", page, StringComparison.Ordinal);
        Assert.Contains("OnRefreshLocationTapped", page, StringComparison.Ordinal);
        Assert.Contains("RequestFab.IsVisible = true;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestFab.IsVisible = ReferenceEquals(activePanel, RequestsPanel);", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AttendanceLocationLabel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Clicked=\"OnRefreshLocationTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BackgroundColor=\"#24465E\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"تفاصيل آخر احتساب Payroll\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompensationGrossLabel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnSpan=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalTextAlignment=\"Center\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeClient_UsesEmployeeExperienceApisAndNativeHandlers()
    {
        var mobileApi = Read("ZynoraHR.Mobile", "MobileApi.cs");
        var page = Read("ZynoraHR.Mobile", "MainPage.xaml.cs");
        var xaml = Read("ZynoraHR.Mobile", "MainPage.xaml");

        Assert.Contains("api/v1/me/surveys", mobileApi, StringComparison.Ordinal);
        Assert.Contains("public string DisplayName", mobileApi, StringComparison.Ordinal);
        Assert.Contains("UiLocalization.Data(FullName, EnglishName)", mobileApi, StringComparison.Ordinal);
        Assert.Contains("UiLocalization.SystemData", mobileApi, StringComparison.Ordinal);
        Assert.Contains("[JsonPropertyName(\"requestTypeId\")]", mobileApi, StringComparison.Ordinal);
        Assert.Contains("UiLocalization.T(\"إجازة سنوية\")", mobileApi, StringComparison.Ordinal);
        Assert.Contains("UiLocalization.T(\"إجازة مرضية\")", mobileApi, StringComparison.Ordinal);
        Assert.Contains("UiLocalization.T(\"مستخدم {0} من {1}\")", mobileApi, StringComparison.Ordinal);
        Assert.Contains("profile.DisplayName", page, StringComparison.Ordinal);
        Assert.Contains("_currentProfile?.DisplayName", page, StringComparison.Ordinal);
        Assert.Contains("SubmitSurveyAsync", mobileApi, StringComparison.Ordinal);
        Assert.Contains("PollsAsync", mobileApi, StringComparison.Ordinal);
        Assert.Contains("VotePollAsync", mobileApi, StringComparison.Ordinal);
        Assert.Contains("api/v1/me/feedback", mobileApi, StringComparison.Ordinal);
        Assert.Contains("SubmitFeedbackAsync", mobileApi, StringComparison.Ordinal);
        Assert.Contains("api/v1/me/discipline", mobileApi, StringComparison.Ordinal);
        Assert.Contains("TeamAsync", mobileApi, StringComparison.Ordinal);
        Assert.Contains("ApprovalsAsync", mobileApi, StringComparison.Ordinal);
        Assert.Contains("ApproveAsync", mobileApi, StringComparison.Ordinal);
        Assert.Contains("RejectApprovalAsync", mobileApi, StringComparison.Ordinal);
        Assert.Contains("ReturnApprovalAsync", mobileApi, StringComparison.Ordinal);

        Assert.Contains("BuildSurveysServiceAsync", page, StringComparison.Ordinal);
        Assert.Contains("استطلاعات Pulse", page, StringComparison.Ordinal);
        Assert.Contains("BuildSurveyDetailServiceAsync", page, StringComparison.Ordinal);
        Assert.Contains("BuildFeedbackServiceAsync", page, StringComparison.Ordinal);
        Assert.Contains("BuildFeedbackCreateServiceAsync", page, StringComparison.Ordinal);
        Assert.Contains("BuildDisciplineServiceAsync", page, StringComparison.Ordinal);
        Assert.Contains("BuildTeamServiceAsync", page, StringComparison.Ordinal);
        Assert.Contains("BuildApprovalsServiceAsync", page, StringComparison.Ordinal);
        Assert.Contains("حقول تعديل البيانات", page, StringComparison.Ordinal);

        Assert.Contains("Clicked=\"OnSurveysTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Clicked=\"OnFeedbackTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Clicked=\"OnDisciplineTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Clicked=\"OnTeamTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Clicked=\"OnApprovalsTapped\"", xaml, StringComparison.Ordinal);
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
