using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeePortal;

/// <summary>
/// «الدخول والبصم بالبصمة/الوجه» (درج إعدادات بوابة الموظف): تسجيل Passkey جديد
/// (navigator.credentials.create — البيولوجيا تبقى بالجهاز، يُخزَّن مفتاح عام فقط)
/// وعرض حالة مفاتيح الموظف. المفتاح لا يعمل قبل اعتماد الموارد البشرية، وتسجيل
/// جديد يُزيح القديم ويحتاج اعتماداً جديداً (مفتاح نشط واحد).
/// </summary>
public class BiometricModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public BiometricModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<WebAuthnCredentialStore.Credential> Credentials { get; private set; } = new();
    public bool HasActive => Credentials.Any(c => c.Status == WebAuthnCredentialStore.StatusActive);
    public bool HasPending => Credentials.Any(c => c.Status == WebAuthnCredentialStore.StatusPending);
    public bool IsLinkedToEmployee { get; private set; }

    public async Task OnGetAsync()
    {
        var employeeId = await ResolveEmployeeIdAsync();
        IsLinkedToEmployee = employeeId > 0;
        if (IsLinkedToEmployee)
            Credentials = await WebAuthnCredentialStore.ListForEmployeeAsync(_dbContext, employeeId);
    }

    private async Task<int> ResolveEmployeeIdAsync()
    {
        var claim = User.FindFirstValue("EmployeeId");
        if (int.TryParse(claim, out var id) && id > 0) return id;

        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username)) return 0;
        return await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @U AND IsActive = 1",
            c => HrmsDatabase.AddParameter(c, "@U", username));
    }
}
