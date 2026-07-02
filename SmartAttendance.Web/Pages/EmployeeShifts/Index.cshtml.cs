using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.EmployeeShifts.Services;
using SmartAttendance.Application.EmployeeShifts.ViewModels;

namespace SmartAttendance.Web.Pages.EmployeeShifts;

public class IndexModel : PageModel
{
    private readonly IEmployeeShiftService _employeeShiftService;

    public IndexModel(IEmployeeShiftService employeeShiftService)
    {
        _employeeShiftService = employeeShiftService;
    }

    public IEnumerable<EmployeeShiftListViewModel> EmployeeShifts { get; set; } = new List<EmployeeShiftListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        EmployeeShifts = await _employeeShiftService.GetAllAsync(SearchTerm);
    }
}
