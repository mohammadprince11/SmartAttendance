using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Holidays.Services;
using SmartAttendance.Application.Holidays.ViewModels;

namespace SmartAttendance.Web.Pages.Holidays;

public class IndexModel : PageModel
{
    private readonly IHolidayService _holidayService;

    public IndexModel(IHolidayService holidayService)
    {
        _holidayService = holidayService;
    }

    public IEnumerable<HolidayListViewModel> Holidays { get; set; } = new List<HolidayListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        Holidays = await _holidayService.GetAllAsync(SearchTerm);
    }
}
