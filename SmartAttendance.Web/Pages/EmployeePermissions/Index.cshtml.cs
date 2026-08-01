using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.EmployeePermissions.Services;
using SmartAttendance.Application.EmployeePermissions.ViewModels;
using SmartAttendance.Application.Permissions.Services;

namespace SmartAttendance.Web.Pages.EmployeePermissions;

public class IndexModel : PageModel
{
    private readonly IEmployeePermissionService _employeePermissionService;
    private readonly IPermissionService _permissionService;

    public IndexModel(
        IEmployeePermissionService employeePermissionService,
        IPermissionService permissionService)
    {
        _employeePermissionService = employeePermissionService;
        _permissionService = permissionService;
    }

    /// <summary>رمز الموظف المحسوم واسمه — لتعبئة المنتقي ابتداءً.</summary>
    public string? SelectedEmployeeCode { get; set; }

    public string? SelectedEmployeeName { get; set; }

    public EmployeePermissionAssignmentViewModel? Assignment { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? EmployeeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty]
    public List<int> SelectedPermissionIds { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadPageAsync();
    }

    public async Task<IActionResult> OnPostAsync(int employeeId)
    {
        var saved = await _employeePermissionService.SaveAssignmentsAsync(employeeId, SelectedPermissionIds);

        if (!saved)
        {
            EmployeeId = employeeId;
            ErrorMessage = "Employee not found or permissions could not be saved.";
            await LoadPageAsync();
            return Page();
        }

        TempData["SuccessMessage"] = "Employee permissions saved successfully.";

        return RedirectToPage("./Index", new { EmployeeId = employeeId, SearchTerm });
    }

    public async Task<IActionResult> OnPostSeedAsync()
    {
        var added = await _permissionService.SeedDefaultPermissionsAsync();

        TempData["SuccessMessage"] = added == 0
            ? "Default permissions already exist."
            : $"{added} default permissions added successfully.";

        return RedirectToPage("./Index", new { EmployeeId, SearchTerm });
    }

    private async Task LoadPageAsync()
    {
        // سابقاً كانت الصفحة تُسقط الاختيار على **أول** موظف بالقائمة حين لا يُحدَّد
        // أحد. مع منتقٍ لا يعرض قائمة، ذلك يعني تحرير صلاحيات موظفٍ لم يخترْه أحد —
        // فالاختيار صار صريحاً، وبلا اختيارٍ تُعرض دعوةٌ للاختيار.
        if (!EmployeeId.HasValue || EmployeeId.Value <= 0)
        {
            return;
        }

        Assignment = await _employeePermissionService.GetAssignmentAsync(EmployeeId.Value);
        SelectedEmployeeCode = Assignment?.EmployeeNo;
        SelectedEmployeeName = Assignment?.FullName;
    }
}
