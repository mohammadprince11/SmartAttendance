using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Imports;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// استيراد «بريد الموظفين فقط»: لصق عمودَين من Excel (رقم الموظف، البريد) ⟵ معاينة
/// (سيُحدَّث/مطابق أصلاً/رقم غير موجود/بريد غير صالح) ⟵ تطبيق يحدّث عمود Email وحده.
/// وُجد لفتح قناة SMTP: الاستيراد الشامل يعيد كتابة الاسم/التعيين فلا يصلح لهذا الغرض.
/// </summary>
public class EmailImportModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public EmailImportModel(ApplicationDbContext dbContext) => _dbContext = dbContext;

    [BindProperty]
    public string? PastedData { get; set; }

    public sealed record PreviewRow(string EmployeeNo, string EmployeeName, string CurrentEmail, string NewEmail, string Status);

    public List<PreviewRow> PreviewRows { get; } = new();
    public List<EmployeeEmailImportParser.Entry> RejectedRows { get; private set; } = new();
    public int UpdateCount { get; private set; }
    public int SameCount { get; private set; }
    public int NotFoundCount { get; private set; }
    public bool ShowPreview { get; private set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        await BuildPreviewAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync()
    {
        var updates = await BuildPreviewAsync(collectUpdates: true);
        if (updates.Count == 0)
        {
            ErrorMessage ??= "لا صفوف قابلة للتحديث.";
            return Page();
        }

        foreach (var (employeeId, email) in updates)
        {
            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                "UPDATE Employees SET Email = @Email WHERE Id = @Id;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Email", email);
                    HrmsDatabase.AddParameter(command, "@Id", employeeId);
                });
        }

        Message = $"تم تحديث بريد {updates.Count} موظفاً.";
        PastedData = null;
        PreviewRows.Clear();
        RejectedRows = new List<EmployeeEmailImportParser.Entry>();
        ShowPreview = false;
        return Page();
    }

    /// <summary>يحلّل الملصق ويبني المعاينة؛ عند <paramref name="collectUpdates"/> يعيد (Id، بريد) للتطبيق.</summary>
    private async Task<List<(int EmployeeId, string Email)>> BuildPreviewAsync(bool collectUpdates = false)
    {
        var updates = new List<(int, string)>();
        var (valid, rejected) = EmployeeEmailImportParser.Parse(PastedData);
        RejectedRows = rejected;

        if (valid.Count == 0 && rejected.Count == 0)
        {
            ErrorMessage = "الصق بيانات بعمودَين: رقم الموظف ثم البريد الإلكتروني.";
            return updates;
        }

        var employees = await HrmsDatabase.QueryAsync(
            _dbContext,
            "SELECT Id, EmployeeNo, FullName, ISNULL(Email, N'') AS Email FROM Employees;",
            command => { },
            reader => new
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                Email = HrmsDatabase.GetString(reader, "Email")
            });
        var byNo = employees
            .GroupBy(e => e.EmployeeNo.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in valid)
        {
            if (!byNo.TryGetValue(entry.EmployeeNo, out var employee))
            {
                NotFoundCount++;
                PreviewRows.Add(new PreviewRow(entry.EmployeeNo, "—", "—", entry.Email, "NotFound"));
                continue;
            }

            if (string.Equals(employee.Email, entry.Email, StringComparison.OrdinalIgnoreCase))
            {
                SameCount++;
                PreviewRows.Add(new PreviewRow(entry.EmployeeNo, employee.FullName, employee.Email, entry.Email, "Same"));
                continue;
            }

            UpdateCount++;
            PreviewRows.Add(new PreviewRow(entry.EmployeeNo, employee.FullName, employee.Email, entry.Email, "Update"));
            if (collectUpdates) updates.Add((employee.Id, entry.Email));
        }

        ShowPreview = true;
        return updates;
    }
}
