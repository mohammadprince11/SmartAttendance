using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.AttendanceRecords.Services;
using SmartAttendance.Application.AttendanceRecords.ViewModels;

namespace SmartAttendance.Web.Pages.AttendanceRecords;

public class DeleteModel : PageModel
{
    private readonly IAttendanceRecordService _attendanceRecordService;

    public DeleteModel(IAttendanceRecordService attendanceRecordService)
    {
        _attendanceRecordService = attendanceRecordService;
    }

    public AttendanceRecordDetailsViewModel AttendanceRecord { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var attendanceRecord = await _attendanceRecordService.GetByIdAsync(id);

        if (attendanceRecord == null)
            return NotFound();

        AttendanceRecord = attendanceRecord;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _attendanceRecordService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Attendance record not found or could not be deleted.";

            var attendanceRecord = await _attendanceRecordService.GetByIdAsync(id);
            if (attendanceRecord != null)
                AttendanceRecord = attendanceRecord;

            return Page();
        }

        TempData["SuccessMessage"] = "Attendance record deleted successfully.";

        return RedirectToPage("./Index");
    }
}
