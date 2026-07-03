using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.AttendanceProcessing.Services;
using SmartAttendance.Application.AttendanceProcessing.ViewModels;

namespace SmartAttendance.Web.Pages.AttendanceProcessing;

public class IndexModel : PageModel
{
    private readonly IAttendanceProcessingService _attendanceProcessingService;

    public IndexModel(IAttendanceProcessingService attendanceProcessingService)
    {
        _attendanceProcessingService = attendanceProcessingService;
    }

    public IEnumerable<AttendanceProcessingResultViewModel> Records { get; set; } = new List<AttendanceProcessingResultViewModel>();

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MaxRows { get; set; } = 500;

    public int TotalResults { get; set; }

    public bool IsLimited { get; set; }

    public async Task OnGetAsync()
    {
        FromDate ??= DateOnly.FromDateTime(DateTime.Today);
        ToDate ??= FromDate;

        if (ToDate < FromDate)
            ToDate = FromDate;

        MaxRows = NormalizeMaxRows(MaxRows);

        var processedRecords = await _attendanceProcessingService.GetProcessedRecordsAsync(
            FromDate,
            ToDate,
            SearchTerm);

        var materialized = processedRecords.ToList();

        TotalResults = materialized.Count;
        IsLimited = TotalResults > MaxRows;

        Records = materialized
            .Take(MaxRows)
            .ToList();
    }

    private static int NormalizeMaxRows(int value)
    {
        return value switch
        {
            100 => 100,
            250 => 250,
            500 => 500,
            1000 => 1000,
            _ => 500
        };
    }
}
