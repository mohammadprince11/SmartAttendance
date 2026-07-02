using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Shifts.Services;
using SmartAttendance.Application.Shifts.ViewModels;

namespace SmartAttendance.Web.Pages.Shifts;

public class IndexModel : PageModel
{
    private readonly IShiftService _shiftService;

    public IndexModel(IShiftService shiftService)
    {
        _shiftService = shiftService;
    }

    public IEnumerable<ShiftListViewModel> Shifts { get; set; } = new List<ShiftListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        Shifts = await _shiftService.GetAllAsync(SearchTerm);
    }
}
