using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Pages.HrSettings;

public class NoticePeriodModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public NoticePeriodModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty] public int NoticeValue { get; set; }
    [BindProperty] public string NoticeUnit { get; set; } = "Day";
    [BindProperty] public string NoticeBasis { get; set; } = "TerminationDate";
    [BindProperty] public bool ExcludeOfficialHolidays { get; set; }
    [BindProperty] public string NoAttendanceException { get; set; } = "None";
    [BindProperty] public string RegularAttendanceException { get; set; } = "None";
    [BindProperty] public string ScheduledAttendanceException { get; set; } = "None";
    [BindProperty] public string SalaryAction { get; set; } = "LastWorkingDay";
    [BindProperty] public string EndOfServiceAction { get; set; } = "LastWorkingDay";
    [BindProperty] public string LeaveAllowanceAction { get; set; } = "LastWorkingDay";
    [BindProperty] public string TicketAction { get; set; } = "LastWorkingDay";

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HrSettingsStore.SetAsync(_db, "Notice.Value", Math.Max(0, NoticeValue).ToString());
        await HrSettingsStore.SetAsync(_db, "Notice.Unit", NoticeUnit);
        await HrSettingsStore.SetAsync(_db, "Notice.Basis", NoticeBasis);
        await HrSettingsStore.SetAsync(_db, "Notice.ExcludeOfficialHolidays", ExcludeOfficialHolidays.ToString());
        await HrSettingsStore.SetAsync(_db, "Notice.NoAttendanceException", NoAttendanceException);
        await HrSettingsStore.SetAsync(_db, "Notice.RegularAttendanceException", RegularAttendanceException);
        await HrSettingsStore.SetAsync(_db, "Notice.ScheduledAttendanceException", ScheduledAttendanceException);
        await HrSettingsStore.SetAsync(_db, "Notice.SalaryAction", SalaryAction);
        await HrSettingsStore.SetAsync(_db, "Notice.EndOfServiceAction", EndOfServiceAction);
        await HrSettingsStore.SetAsync(_db, "Notice.LeaveAllowanceAction", LeaveAllowanceAction);
        await HrSettingsStore.SetAsync(_db, "Notice.TicketAction", TicketAction);

        TempData["SuccessMessage"] = "تم حفظ إعدادات فترة الإنذار.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        NoticeValue = int.TryParse(await HrSettingsStore.GetAsync(_db, "Notice.Value", "0"), out var value) ? value : 0;
        NoticeUnit = await HrSettingsStore.GetAsync(_db, "Notice.Unit", "Day");
        NoticeBasis = await HrSettingsStore.GetAsync(_db, "Notice.Basis", "TerminationDate");
        ExcludeOfficialHolidays = bool.TryParse(await HrSettingsStore.GetAsync(_db, "Notice.ExcludeOfficialHolidays", "False"), out var exclude) && exclude;
        NoAttendanceException = await HrSettingsStore.GetAsync(_db, "Notice.NoAttendanceException", "None");
        RegularAttendanceException = await HrSettingsStore.GetAsync(_db, "Notice.RegularAttendanceException", "None");
        ScheduledAttendanceException = await HrSettingsStore.GetAsync(_db, "Notice.ScheduledAttendanceException", "None");
        SalaryAction = await HrSettingsStore.GetAsync(_db, "Notice.SalaryAction", "LastWorkingDay");
        EndOfServiceAction = await HrSettingsStore.GetAsync(_db, "Notice.EndOfServiceAction", "LastWorkingDay");
        LeaveAllowanceAction = await HrSettingsStore.GetAsync(_db, "Notice.LeaveAllowanceAction", "LastWorkingDay");
        TicketAction = await HrSettingsStore.GetAsync(_db, "Notice.TicketAction", "LastWorkingDay");
    }
}
