using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Pages.HrSettings;

public class SelfServiceSettingsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public SelfServiceSettingsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty] public bool AllowWallPosts { get; set; }
    [BindProperty] public bool DisableAnonymousSuggestions { get; set; } = true;
    [BindProperty] public string EmployeeFeelingAudience { get; set; } = "AllEmployees";
    [BindProperty] public bool AllowManagersTeamAccess { get; set; } = true;
    [BindProperty] public string ManagerAccessLevel { get; set; } = "LastUnit";
    [BindProperty] public bool AccessEvaluations { get; set; } = true;
    [BindProperty] public bool AccessAttendance { get; set; } = true;
    [BindProperty] public bool AccessRequests { get; set; } = true;
    [BindProperty] public bool AccessEmployees { get; set; } = true;
    [BindProperty] public bool AccessReports { get; set; } = true;
    [BindProperty] public bool AccessCards { get; set; } = true;
    [BindProperty] public bool RequireAttachmentOnInfoChange { get; set; }
    [BindProperty] public bool RequireBankAttachment { get; set; }
    [BindProperty] public bool AllowDirectManagerTransfer { get; set; }
    [BindProperty] public bool AllowMobileScreenshots { get; set; } = true;
    [BindProperty] public bool ShowDailyStatus { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HrSettingsStore.SetAsync(_db, "SelfService.AllowWallPosts", AllowWallPosts.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.DisableAnonymousSuggestions", DisableAnonymousSuggestions.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.EmployeeFeelingAudience", EmployeeFeelingAudience);
        await HrSettingsStore.SetAsync(_db, "SelfService.AllowManagersTeamAccess", AllowManagersTeamAccess.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.ManagerAccessLevel", ManagerAccessLevel);
        await HrSettingsStore.SetAsync(_db, "SelfService.AccessEvaluations", AccessEvaluations.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.AccessAttendance", AccessAttendance.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.AccessRequests", AccessRequests.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.AccessEmployees", AccessEmployees.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.AccessReports", AccessReports.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.AccessCards", AccessCards.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.RequireAttachmentOnInfoChange", RequireAttachmentOnInfoChange.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.RequireBankAttachment", RequireBankAttachment.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.AllowDirectManagerTransfer", AllowDirectManagerTransfer.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.AllowMobileScreenshots", AllowMobileScreenshots.ToString());
        await HrSettingsStore.SetAsync(_db, "SelfService.ShowDailyStatus", ShowDailyStatus.ToString());

        TempData["SuccessMessage"] = "تم حفظ إعدادات الخدمة الذاتية.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        AllowWallPosts = await GetBool("SelfService.AllowWallPosts", false);
        DisableAnonymousSuggestions = await GetBool("SelfService.DisableAnonymousSuggestions", true);
        EmployeeFeelingAudience = await HrSettingsStore.GetAsync(_db, "SelfService.EmployeeFeelingAudience", "AllEmployees");
        AllowManagersTeamAccess = await GetBool("SelfService.AllowManagersTeamAccess", true);
        ManagerAccessLevel = await HrSettingsStore.GetAsync(_db, "SelfService.ManagerAccessLevel", "LastUnit");
        AccessEvaluations = await GetBool("SelfService.AccessEvaluations", true);
        AccessAttendance = await GetBool("SelfService.AccessAttendance", true);
        AccessRequests = await GetBool("SelfService.AccessRequests", true);
        AccessEmployees = await GetBool("SelfService.AccessEmployees", true);
        AccessReports = await GetBool("SelfService.AccessReports", true);
        AccessCards = await GetBool("SelfService.AccessCards", true);
        RequireAttachmentOnInfoChange = await GetBool("SelfService.RequireAttachmentOnInfoChange", false);
        RequireBankAttachment = await GetBool("SelfService.RequireBankAttachment", false);
        AllowDirectManagerTransfer = await GetBool("SelfService.AllowDirectManagerTransfer", false);
        AllowMobileScreenshots = await GetBool("SelfService.AllowMobileScreenshots", true);
        ShowDailyStatus = await GetBool("SelfService.ShowDailyStatus", false);
    }

    private async Task<bool> GetBool(string key, bool fallback)
    {
        return bool.TryParse(await HrSettingsStore.GetAsync(_db, key, fallback.ToString()), out var value) ? value : fallback;
    }
}
