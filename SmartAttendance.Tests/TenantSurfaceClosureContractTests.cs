namespace SmartAttendance.Tests;

public sealed class TenantSurfaceClosureContractTests
{
    [Fact]
    public void AnnouncementPublishingOptions_DefaultOnAndAllowExplicitOptOut()
    {
        var model = ReadWeb("Pages", "Engagement", "Studio.cshtml.cs");
        Assert.Contains("public bool PublishNow { get; set; } = true;", model);
        Assert.Contains("public bool CommentsEnabled { get; set; } = true;", model);
        foreach (var page in new[] { "Studio.cshtml", "_FreeAnnouncement.cshtml" })
        {
            var view = ReadWeb("Pages", "Engagement", page);
            Assert.Contains("class=\"zys-publish-options\"", view);
            foreach (var option in new[] { "CommentsEnabled", "PublishNow" })
            {
                Assert.Contains($"checked=\"@Model.{option}\"", view);
                Assert.Contains($"type=\"hidden\" name=\"{option}\" value=\"false\"", view);
            }
        }
        var studio = ReadWeb("Pages", "Engagement", "Studio.cshtml");
        var detailsStart = studio.IndexOf("<details>", StringComparison.Ordinal);
        var detailsEnd = studio.IndexOf("</details>", detailsStart, StringComparison.Ordinal);
        Assert.DoesNotContain("CommentsEnabled", studio[detailsStart..detailsEnd]);
    }

    [Fact]
    public void AnnouncementEmployeeValidation_HidesDuplicateInlineMessageButRetainsSaveValidation()
    {
        var styles = ReadWeb("wwwroot", "css", "zynora-announcement-templates.css");
        Assert.Contains("align-items:start;height:auto!important;min-height:42px!important", styles);
        Assert.Contains(".zys #zys-subject .zyep-box>.zy-field-error{display:none!important}", styles);
        var model = ReadWeb("Pages", "Engagement", "Studio.cshtml.cs");
        Assert.Contains("if (subject == null || string.IsNullOrWhiteSpace(subject.FullName)) return await Error(\"اختر موظفاً من الشركة بالاسم أو الكود.\");", model);
        Assert.Contains(".zys #zys-subject>p{margin-block:var(--sp-3);line-height:1.6}", styles);
    }

    [Fact]
    public void AnnouncementCompanySelector_OnlyAppearsForMultipleAuthorizedCompanies()
    {
        foreach (var page in new[] { "Studio.cshtml", "Library.cshtml" })
        {
            var view = ReadWeb("Pages", "Engagement", page);
            Assert.Contains("@if (Model.Companies.Count > 1)", view);
            Assert.Contains("<input type=\"hidden\" name=\"CompanyId\" value=\"@Model.CompanyId\"", view);
        }
        var model = ReadWeb("Pages", "Engagement", "Studio.cshtml.cs");
        Assert.Contains("scope.AllowedCompanyIds.Contains(c.Id)", model);
        Assert.Contains("if (CompanyId == 0) CompanyId = Companies.FirstOrDefault()?.Id ?? 0;", model);
    }
    [Fact]
    public void AnnouncementList_ShowsFourColumnsAndKeepsDetailsInDialog()
    {
        var index = ReadWeb("Pages", "Engagement", "Index.cshtml");
        Assert.Contains("zy-table zy-ann-table", index);
        foreach (var key in new[] { "التسلسل", "فئة الإعلان", "العنوان", "تاريخ الإعلان" })
            Assert.Contains($"<th scope=\"col\">@L(\"{key}\")</th>", index);
        Assert.Contains("@(++announcementSequence)", index);
        Assert.Contains("@L(item.Category)", index);
        Assert.Contains("item.PublishDate ?? item.CreatedAt", index);
        Assert.Contains("data-ann-open=\"zy-ann-@item.Id\"", index);
        Assert.Contains("<tr class=\"zy-ann-row\" tabindex=\"0\"", index);
        Assert.Contains("<span class=\"zy-ann-title\">@item.Title</span>", index);
        Assert.Contains("zy-ann-detail-meta", index);
        Assert.Contains("<td colspan=\"4\" class=\"zy-ann-empty\">", index);
        Assert.Contains("@L(\"لا إعلانات بعد. أنشئ أول إعلان.\")", index);
        Assert.DoesNotContain("لا إعلانات بعد. أنشئ أول إعلان من الاستوديو.", index);
        var styles = ReadWeb("wwwroot", "css", "zynora-announcement-management.css");
        Assert.Contains("tr:has(.zy-ann-empty):hover{background:transparent!important;cursor:default!important}", styles);
        var visual = index.IndexOf("<partial name=\"_ManagedAnnouncementVisual\"", StringComparison.Ordinal);
        var dialog = index.LastIndexOf("<dialog ", visual, StringComparison.Ordinal);
        var close = index.IndexOf("</dialog>", dialog, StringComparison.Ordinal);
        Assert.True(dialog >= 0 && visual > dialog && visual < close);
        var manager = new System.Resources.ResourceManager("SmartAttendance.Web.Resources.SharedResource", typeof(SmartAttendance.Web.Pages.Engagement.StudioModel).Assembly);
        foreach (var key in new[] { "التسلسل", "فئة الإعلان", "تاريخ الإعلان" })
            Assert.False(string.IsNullOrWhiteSpace(manager.GetString(key, System.Globalization.CultureInfo.GetCultureInfo("en-US"))));
    }
    [Fact]
    public void AnnouncementCreationAndLibraryHaveDistinctPagesAndFreeModeIsATemplateChoice()
    {
        var create=ReadWeb("Pages","Engagement","Studio.cshtml");
        var library=ReadWeb("Pages","Engagement","Library.cshtml");
        var index=ReadWeb("Pages","Engagement","Index.cshtml");
        Assert.Contains("<option value=\"__free\">",create);
        Assert.DoesNotContain("id=\"zys-builder\"",create);
        Assert.DoesNotContain("id=\"zys-compose\"",library);
        Assert.Contains("id=\"zys-builder\"",library);
        Assert.DoesNotContain("asp-page=\"Library\"",index);
        Assert.Contains("asp-page=\"/Engagement/Library\"",ReadWeb("Pages","HrSettings","Index.cshtml"));
        Assert.Contains("asp-page=\"/Engagement/Library\"",ReadWeb("Pages","Settings","Index.cshtml"));
        Assert.Contains("zyw-ann-create-footer",index);
        Assert.Contains("data-ann-create",index);
        Assert.Contains("data-ann-open",index);
        Assert.Contains("asp-page-handler=\"AnnouncementUpdate\"",index);
        Assert.Contains("@L(\"إعلان جديد\")",index);
        Assert.DoesNotContain("data-zyw-tab=\"announcements\"",index);
        Assert.DoesNotContain("data-zyw-panel=\"announcements\"",index);
        Assert.DoesNotContain("asp-fragment=\"zys-builder\"",index);
        Assert.Contains("if (!CanEditLibrary) return Forbid();",ReadWeb("Pages","Engagement","Library.cshtml.cs"));
        var libraryGets = typeof(SmartAttendance.Web.Pages.Engagement.LibraryModel).GetMethods().Where(m => m.Name == "OnGetAsync").ToArray();
        Assert.Single(libraryGets);
    }
    [Fact]
    public void AnnouncementPreviewLanguage_IsIndependentOfUiDictionaryTranslation()
    {
        var view = ReadWeb("Pages", "Engagement", "Studio.cshtml");
        Assert.Contains("id=\"zys-preview\" data-zy-no-localize", view);
        Assert.Contains("data-zyconfirm=\"@L(\"تعطيل التصميم؟ الإعلانات القديمة ستحتفظ به.\")\"", ReadWeb("Pages", "Engagement", "Library.cshtml"));
        var manager = new System.Resources.ResourceManager("SmartAttendance.Web.Resources.SharedResource", typeof(SmartAttendance.Web.Pages.Engagement.StudioModel).Assembly);
        var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        foreach (var key in new[] { "اختر موظفاً من الشركة بالاسم أو الكود.", "يظهر تلقائياً بعد اختيار الموظف", "اختر المنصب الجديد", "المنصب الجديد يجب أن يختلف عن المنصب الحالي.", "تعطيل التصميم؟ الإعلانات القديمة ستحتفظ به." })
        {
            var value = manager.GetString(key, english);
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.DoesNotContain(value!, c=>c >= '\u0600' && c <= '\u06ff');
        }
    }
    [Fact]
    public void EmployeeOwnedDocumentAndFormStores_FilterByCompanyBeforeMaterialization()
    {
        var documentRequests = ReadWeb("Infrastructure", "Hrms", "DocumentRequestStore.cs");
        var generatedDocuments = ReadWeb("Infrastructure", "Hrms", "DocumentTemplateStore.cs");
        var submissions = ReadWeb("Infrastructure", "Hrms", "FormSubmissionStore.cs");

        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", documentRequests);
        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", generatedDocuments);
        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", submissions);
        Assert.Contains("CanAccessOwnedRowAsync", submissions);
    }

    [Fact]
    public void DocumentAndFormPages_PropagateTheCurrentCompanyScopeToEverySensitiveAction()
    {
        var requests = ReadWeb("Pages", "Documents", "Requests.cshtml.cs");
        var generate = ReadWeb("Pages", "Documents", "Generate.cshtml.cs");
        var submissions = ReadWeb("Pages", "Forms", "Submissions.cshtml.cs");

        Assert.Contains("ICompanyScopeProvider", requests);
        Assert.Contains("scope: scope", requests);
        Assert.Contains("CanAccessEmployeeAsync", generate);
        Assert.Contains("CanAccessOwnedRowAsync", generate);
        Assert.Contains("scope: scope", submissions);
    }

    [Fact]
    public void EngagementManagement_IsScopedForListsTargetsAndMutations()
    {
        var service = Read("SmartAttendance.Infrastructure", "Services", "AnnouncementService.cs");
        var shared = ReadWeb("Pages", "Engagement", "EngagementPageModel.cs");
        var handlers = ReadWeb("Pages", "Engagement", "Index.Handlers.cs");

        Assert.Contains("AllowedCompanyIds", service);
        Assert.Contains("group.AudienceRules.Any", service);
        Assert.Contains("IsTargetWithinCompanyScopeAsync", shared);
        Assert.Contains("CanManageAnnouncementAsync", handlers);
        Assert.Contains("CanAccessOwnedRowAsync", handlers);
    }

    [Fact]
    public void AnnouncementMutationScope_UsesTheMappedAudienceStringNotAnInteger()
    {
        var shared = ReadWeb("Pages", "Engagement", "EngagementPageModel.cs");
        var mapping = Read("SmartAttendance.Infrastructure", "Persistence", "Configurations", "AnnouncementAudienceRuleConfiguration.cs");
        Assert.Contains("HasConversion<string>()", mapping);
        Assert.Contains("r.AudienceType=@AllAudienceType", shared);
        Assert.Contains("AnnouncementAudienceType.All.ToString()", shared);
        Assert.DoesNotContain("r.AudienceType=1", shared);
        Assert.Contains("AND NOT EXISTS", shared);
        Assert.Contains("CompanyId NOT IN ({companyFilter})", shared);
    }

    [Theory]
    [InlineData("Pages", "CompanyDocuments", "Index.cshtml.cs")]
    [InlineData("Pages", "Documents", "Templates.cshtml.cs")]
    [InlineData("Pages", "Forms", "Index.cshtml.cs")]
    public void GlobalConfigurationSurfaces_AreExplicitlyAdministratorOnly(params string[] parts)
    {
        Assert.Contains("[Authorize(Roles = RoleRouteCatalog.Admin)]", ReadWeb(parts));
    }

    [Fact]
    public void StudioPositions_AreAuthoritativeCompanyScopedAndNeverMutateEmployeePositions()
    {
        var source = ReadWeb("Pages", "Engagement", "Studio.cshtml.cs");
        Assert.Contains("p.CompanyId == CompanyId && p.IsActive && p.Id == NewPositionId", source);
        Assert.Contains("p.Id == e.PositionId && p.CompanyId == CompanyId", source);
        Assert.Contains("SubjectDetails(subjects.Where(e => e.Id == SubjectEmployeeId))", source);
        Assert.Contains("Values[key] = subject.PositionName", source);
        Assert.Contains("Values[\"newPosition\"] = position", source);
        Assert.DoesNotContain("employee.PositionId =", source);
        Assert.DoesNotContain("employee.Position =", source);
    }

    private static string ReadWeb(params string[] parts) =>
        Read(new[] { "SmartAttendance.Web" }.Concat(parts).ToArray());

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
