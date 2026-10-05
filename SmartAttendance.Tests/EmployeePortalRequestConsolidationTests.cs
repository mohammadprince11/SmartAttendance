using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeePortalRequestConsolidationTests
{
    private static string WebRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SmartAttendance.Web"));

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { WebRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void RequestsPane_UsesUnifiedDynamicEntry_NotLegacyFiveButtonStudio()
    {
        var razor = Read("Pages", "EmployeePortal", "Index.cshtml");

        Assert.Contains("data-open-reqsheet", razor);
        Assert.Contains("id=\"nxex-leave-modal\"", razor);
        Assert.Contains("data-leave-panel", razor);
        Assert.Contains("data-punch-block", razor);
        Assert.DoesNotContain("data-request-type-grid", razor);
    }

    [Fact]
    public void DynamicSubmission_UsesStableEffectCodeForAuthorization()
    {
        var pageModel = Read("Pages", "EmployeePortal", "Index.cshtml.cs");

        Assert.Contains("ActionForEffectCode(RequestTypeEffectCatalog.EffectiveCode(typeDef))", pageModel);
        Assert.DoesNotContain("IsAllowedAsync(_dbContext, HttpContext, \"LeaveRequest\")", pageModel);
        Assert.Contains("typeDef is null", pageModel);
    }

    [Fact]
    public void ModernRequestFlow_KeepsTimeGateAndDeepLinkSupport()
    {
        var pageModel = Read("Pages", "EmployeePortal", "Index.cshtml.cs");
        var script = Read("wwwroot", "js", "nxex-bottom-nav.js");

        Assert.Contains("OnGetTimeGateAsync", pageModel);
        Assert.Contains("PunchDaySummary", pageModel);
        Assert.Contains("searchParams.set('handler', 'TimeGate')", script);
        Assert.Contains("get('open') === 'leave'", script);
    }

    [Fact]
    public void TimeGate_ExposesSequentialPunchesAndScheduledHours()
    {
        var pageModel = Read("Pages", "EmployeePortal", "Index.cshtml.cs");
        var policy = Read("Infrastructure", "Hrms", "CompanyLeavePolicyStore.cs");
        var script = Read("wwwroot", "js", "nxex-bottom-nav.js");

        Assert.Contains("Array.Empty<PunchTypingEngine.TypedPunch>()", pageModel);
        Assert.Contains("punchCount = ordered.Length", pageModel);
        Assert.Contains("shiftName = workday.ShiftName", pageModel);
        Assert.Contains("scheduledStart = workday.ScheduledStart", pageModel);
        Assert.Contains("scheduledEnd = workday.ScheduledEnd", pageModel);
        Assert.Contains("scheduledHours = workday.ScheduledHours", pageModel);
        Assert.Contains("actualAttendanceMinutes", pageModel);
        Assert.Contains("missingPunch = summary.IsIncomplete", pageModel);
        Assert.Contains("GetWorkdaySnapshotAsync", policy);
        Assert.Contains("ShiftName", policy);
        Assert.Contains("ScheduledStart", policy);
        Assert.Contains("ScheduledEnd", policy);
        Assert.Contains("'Punch ' + (punch.index || idx + 1)", script);
        Assert.Contains("fact('المناوبة'", script);
        Assert.Contains("fact('الجدول'", script);
        Assert.Contains("fact('ساعات الدوام'", script);
        Assert.Contains("fact('الحضور الفعلي'", script);
        Assert.Contains("fact('حالة البصمة'", script);
        Assert.Contains("بداية الطلب", script);
        Assert.Contains("نهاية الطلب", script);
        Assert.Contains("المدة المطلوبة", script);
        Assert.Contains("/EmployeePortal/MissingPunch?date=", script);
    }

    [Fact]
    public void MissingPunchCorrection_CanPrefillDateFromAttendanceGate()
    {
        var script = Read("wwwroot", "js", "nxex-missing-punch.js");
        Assert.Contains("new URLSearchParams(location.search).get('date')", script);
        Assert.Contains("dateEl.value = presetDate", script);
        Assert.Contains("dateEl.dispatchEvent(new Event('change'", script);
    }

    [Fact]
    public void DynamicApprovalRouting_PrefersStoredRequestTypeIdentity()
    {
        var engine = Read("Infrastructure", "Hrms", "ApprovalWorkflowEngine.cs");

        Assert.Contains("requestInfo?.RequestTypeId is > 0", engine);
        Assert.Contains("catalogTypes.FirstOrDefault(t => t.Id == requestInfo.RequestTypeId.Value)", engine);
        Assert.Contains("ResolveRequestTypeKeyFromEffectCode(RequestTypeEffectCatalog.EffectiveCode(catalogType))", engine);
    }

    [Fact]
    public void TimedRequests_SupportCrossMidnightWindow()
    {
        var pageModel = Read("Pages", "EmployeePortal", "Index.cshtml.cs");

        Assert.Contains("etTs <= stTs", pageModel);
        Assert.Contains("to = from.Value.AddDays(1);", pageModel);
        var handlerStart = pageModel.IndexOf("OnPostCreateLeaveAsync(", StringComparison.Ordinal);
        var crossMidnight = pageModel.IndexOf("to = from.Value.AddDays(1);", handlerStart, StringComparison.Ordinal);
        var punchGate = pageModel.IndexOf("FindIncompletePunchDayAsync(", handlerStart, StringComparison.Ordinal);
        Assert.True(
            handlerStart >= 0 && crossMidnight > handlerStart && punchGate > crossMidnight,
            "Cross-midnight must expand the effective range before the CreateLeave server punch gate runs.");
    }

    [Fact]
    public void EmployeePortal_NewSession_IgnoresHiddenTimestampFromPreviousSession()
    {
        var layout = Read("Pages", "Shared", "_EmployeePortalLayout.cshtml");

        Assert.Contains("SessionIssuedUtc", layout);
        Assert.Contains("sessionIssuedMs", layout);
        Assert.Contains("t < sessionIssuedMs", layout);
        Assert.Contains("localStorage.removeItem(\"zyHiddenAt\")", layout);
    }

    [Fact]
    public void FullEmployeeProfile_LoadsOnProfileTab_AndRendersMobileSections()
    {
        var model = Read("Pages", "EmployeePortal", "Index.cshtml.cs");
        var razor = Read("Pages", "EmployeePortal", "Index.cshtml");
        var css = Read("wwwroot", "css", "zynora-employee-experience.css");

        Assert.Contains("FullProfile = await LoadFullProfileAsync(employeeId);", model);
        Assert.Contains("EmployeeIdentityDocuments", model);
        Assert.Contains("EmployeeDependents", model);
        Assert.Contains("EmployeeFinancialInfos", model);
        Assert.Contains("EmployeeContracts", model);
        Assert.Contains("EmployeeFileRecords", model);
        Assert.Contains("EmployeeDocuments", model);

        Assert.Contains("البيانات الشخصية والهوية", razor);
        Assert.Contains("الوظيفة والعقد", razor);
        Assert.Contains("التواصل والعائلة", razor);
        Assert.Contains("المالية والبنك", razor);
        Assert.Contains("التعليم والخبرة", razor);
        Assert.Contains("المهارات واللغات والملخص", razor);
        Assert.Contains("nxex-profile-section", css);
        Assert.Contains("Full profile — exact mobile widths", css);
    }

    [Fact]
    public void ModernRequestTypeSummary_UsesPolicyMetadataAndConditionalFields()
    {
        var razor = Read("Pages", "EmployeePortal", "Index.cshtml");
        var pageModel = Read("Pages", "EmployeePortal", "Index.cshtml.cs");
        var script = Read("wwwroot", "js", "nxex-bottom-nav.js");
        var css = Read("wwwroot", "css", "zynora-employee-experience.css");

        Assert.Contains("data-type-summary", razor);
        Assert.Contains("data-max-per-request", razor);
        Assert.Contains("data-eligibility-days", razor);
        Assert.Contains("AttachmentRequiredOverride ?? t.AttachmentRequired", razor);
        Assert.Contains("data-reason-field hidden", razor);
        Assert.Contains("data-attachment-field hidden", razor);
        Assert.Contains("renderTypeSummary(form, opt)", script);
        Assert.Contains("attachmentInput.required = attachmentRequired", script);
        Assert.Contains("reasonInput.required = reasonRequired", script);
        Assert.DoesNotContain("typeDef is { AttachmentRequired: true }", pageModel);
        Assert.Contains("CompanyLeavePolicyStore.ValidateRequestAsync", pageModel);
        Assert.Contains("hasAttachment: attachment is { Length: > 0 }", pageModel);
        Assert.Contains("@media(max-width:430px)", css);
        Assert.Contains("@media(max-width:390px)", css);
        Assert.Contains("@media(max-width:360px)", css);
    }

    [Fact]
    public void OvertimeIntelligence_UsesAttendanceApprovalAndPayrollSources()
    {
        var pageModel = Read("Pages", "EmployeePortal", "Index.cshtml.cs");
        var razor = Read("Pages", "EmployeePortal", "Index.cshtml");
        var css = Read("wwwroot", "css", "zynora-employee-experience.css");

        Assert.Contains("PopulateOvertimeIntelligenceAsync", pageModel);
        Assert.Contains("BulkRequestStore.ResolveEffect(type).Kind == BulkRequestStore.EffectKind.Overtime", pageModel);
        Assert.Contains("AttendanceRequestPolicy.Duration", pageModel);
        Assert.Contains("ActualAttendanceOvertimeHours", pageModel);
        Assert.Contains("ApprovedOvertimeHours", pageModel);
        Assert.Contains("PayrollEligible", pageModel);
        Assert.Contains("PayrollPosted", pageModel);
        Assert.Contains("AND TxType=N'Overtime'", pageModel);
        Assert.Contains("Source LIKE N'Approval:%'", pageModel);
        Assert.Contains("PayrollTransactionStore.DefaultRateFactor", pageModel);
        Assert.Contains("Requested OT", razor);
        Assert.Contains("Actual Attendance OT", razor);
        Assert.Contains("Approved OT", razor);
        Assert.Contains("Payroll Eligible", razor);
        Assert.Contains("Not Posted", razor);
        Assert.Contains("nxex-overtime-intelligence", css);
    }

    [Fact]
    public void RequestImpactPreview_UsesCompanyLeavePolicyAsSourceOfTruth()
    {
        var pageModel = Read("Pages", "EmployeePortal", "Index.cshtml.cs");
        var razor = Read("Pages", "EmployeePortal", "Index.cshtml");
        var script = Read("wwwroot", "js", "nxex-bottom-nav.js");
        var css = Read("wwwroot", "css", "zynora-employee-experience.css");

        Assert.Contains("OnGetRequestImpactAsync", pageModel);
        Assert.Contains("CompanyLeavePolicyStore.ValidateRequestAsync", pageModel);
        Assert.Contains("requiresBalance = true", pageModel);
        Assert.Contains("currentRemaining = validation.Entitlement - validation.Reserved", pageModel);
        Assert.Contains("requested = validation.Requested", pageModel);
        Assert.Contains("remainingAfter = validation.RemainingAfter", pageModel);
        Assert.Contains("toDate = fromDate.AddDays(1)", pageModel);
        Assert.Contains("data-request-impact", razor);
        Assert.Contains("data-impact-current", razor);
        Assert.Contains("data-impact-requested", razor);
        Assert.Contains("data-impact-after", razor);
        Assert.Contains("endpoint.searchParams.set('handler', 'RequestImpact')", script);
        Assert.Contains("data.requiresBalance !== true", script);
        Assert.Contains("queueRequestImpact", script);
        Assert.Contains("nxex-request-impact-grid", css);
    }

    [Fact]
    public void LeaveBalanceCard_SeparatesApprovedFromPendingReserved()
    {
        var policy = Read("Infrastructure", "Hrms", "CompanyLeavePolicyStore.cs");
        var api = Read("Controllers", "Api", "MeController.cs");
        var razor = Read("Pages", "EmployeePortal", "Index.cshtml");
        var css = Read("wwwroot", "css", "zynora-employee-experience.css");

        Assert.Contains("decimal Approved", policy);
        Assert.Contains("decimal PendingReserved", policy);
        Assert.Contains("row.Status.Equals(\"Approved\"", policy);
        Assert.Contains("row.Status.Equals(\"Pending\"", policy);
        Assert.Contains("approved = b.Approved", api);
        Assert.Contains("pendingReserved = b.PendingReserved", api);
        Assert.Contains("الاستحقاق", razor);
        Assert.Contains("المعتمد", razor);
        Assert.Contains("قيد الاعتماد", razor);
        Assert.Contains("b.PendingReserved", razor);
        Assert.Contains("nxex-balance-breakdown-grid", css);
    }

    [Fact]
    public void RequestLifecycle_UsesFrozenApprovalFlowAndRendersExpandableTimeline()
    {
        var engine = Read("Infrastructure", "Hrms", "ApprovalWorkflowEngine.cs");
        var pageModel = Read("Pages", "EmployeePortal", "Index.cshtml.cs");
        var razor = Read("Pages", "EmployeePortal", "Index.cshtml");
        var css = Read("wwwroot", "css", "zynora-employee-experience.css");

        Assert.Contains("GetFlowsAsync", engine);
        Assert.Contains("ActionAt = HrmsDatabase.GetDateTime(reader, \"ActionAt\")", engine);
        Assert.Contains("Note = HrmsDatabase.GetString(reader, \"Note\")", engine);
        Assert.Contains("ApprovalWorkflowEngine.GetFlowsAsync", pageModel);
        Assert.Contains("ApprovalFlow { get; set; }", pageModel);
        Assert.Contains("class=\"nxex-approval-details\"", razor);
        Assert.Contains("row.ApprovalFlow.CurrentSteps", razor);
        Assert.Contains("step.ActionAt.Value.ToString", razor);
        Assert.Contains("step.Note", razor);
        Assert.Contains("nxex-approval-step", css);
    }

    [Fact]
    public void NotificationDeepLinks_LoadAndFocusExactRequest()
    {
        var employeeModel = Read("Pages", "EmployeePortal", "Index.cshtml.cs");
        var employeeRazor = Read("Pages", "EmployeePortal", "Index.cshtml");
        var approvalsModel = Read("Pages", "Approvals", "Index.cshtml.cs");
        var approvalsRazor = Read("Pages", "Approvals", "Index.cshtml");

        Assert.Contains("int? requestId)", employeeModel);
        Assert.Contains("RequestId = requestId is > 0 ? requestId : null;", employeeModel);
        Assert.Contains("if (Requests.Any(request => request.Id == RequestId.Value))", employeeModel);
        Assert.Contains("Tab = \"requests\";", employeeModel);
        Assert.Contains("LoadRequestsAsync(employeeId, RequestId)", employeeModel);
        Assert.Contains("WHERE r.EmployeeId = @EmployeeId", employeeModel);
        Assert.Contains("CASE WHEN @RequestId IS NOT NULL AND r.Id = @RequestId THEN 0 ELSE 1 END", employeeModel);
        Assert.Contains("id=\"employee-request-@row.Id\"", employeeRazor);
        Assert.Contains("Model.RequestId == row.Id", employeeRazor);
        Assert.Contains("scrollIntoView", employeeRazor);

        Assert.Contains("[BindProperty(SupportsGet = true)] public int? RequestId", approvalsModel);
        Assert.Contains("WHERE {scopeFilter}", approvalsModel);
        Assert.Contains("(@RequestId IS NOT NULL AND r.Id = @RequestId)", approvalsModel);
        Assert.Contains("HrmsDatabase.AddParameter(command, \"@RequestId\"", approvalsModel);
        Assert.Contains("FirstOrDefault(x => x.Id == Model.RequestId)", approvalsRazor);
        Assert.Contains("id=\"row_@request.Id\"", approvalsRazor);
        Assert.Contains("id=\"side_@request.Id\"", approvalsRazor);
    }
}
