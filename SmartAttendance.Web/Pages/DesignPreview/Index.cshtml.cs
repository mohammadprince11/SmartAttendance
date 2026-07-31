using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.DesignPreview;

/// <summary>
/// صفحة تجريبية للهوية الجديدة (/DesignPreview — للأدمن): تعرض تخطيط «لوحة
/// البيانات» المعتمد بأرقام حية + معرض مكونات Design System v1 (zy-*) — حتى
/// يُحكم على النتيجة داخل النظام الفعلي قبل تعميمها صفحة-صفحة. لا تغيّر شيئاً.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public int ActiveEmployees { get; set; }
    public int PendingBiometricKeys { get; set; }
    public int ActiveShiftTypes { get; set; }
    public int RosterCellsThisMonth { get; set; }

    public async Task OnGetAsync()
    {
        try { ActiveEmployees = await _dbContext.Employees.CountAsync(e => e.IsActive); } catch { }
        try
        {
            PendingBiometricKeys =
                (await WebAuthnCredentialStore.ListAllAsync(_dbContext, WebAuthnCredentialStore.StatusPending)).Count;
        }
        catch { }
        try
        {
            ActiveShiftTypes = (await ShiftTypeStore.ListAsync(_dbContext))
                .Count(s => s.IsActive && s.AvailableInRoster);
        }
        catch { }
        try
        {
            var t = DateTime.Today;
            RosterCellsThisMonth = await HrmsDatabase.ScalarAsync<int>(
                _dbContext,
                "SELECT COUNT(1) FROM RosterCells WHERE WorkDate >= @F AND WorkDate < @T",
                c =>
                {
                    HrmsDatabase.AddParameter(c, "@F", new DateOnly(t.Year, t.Month, 1));
                    HrmsDatabase.AddParameter(c, "@T", new DateOnly(t.Year, t.Month, 1).AddMonths(1));
                });
        }
        catch { }
    }
}
