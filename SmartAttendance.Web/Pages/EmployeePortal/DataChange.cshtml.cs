using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeePortal;

/// <summary>
/// صفحة مستقلة: طلب تعديل البيانات الشخصية + الصورة. يقترح الموظف تعديلاً على حقوله
/// (قوائم النظام لا إدخال حر، والميلاد بكلندر النظام)، فيُنشأ طلب خدمة ذاتية يمرّ عبر
/// لجنة الموافقة — ولا يُطبَّق على الملف إلا بعد الاعتماد النهائي.
/// </summary>
public class DataChangeModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    private static readonly HashSet<string> AllowedPhotoExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    public DataChangeModel(ApplicationDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    [TempData]
    public string? StatusMessage { get; set; }

    public List<DataChangeRequestStore.ProposedField> Fields { get; private set; } = new();
    public Dictionary<string, List<DataChangeRequestStore.Option>> Options { get; private set; } = new();
    public string CurrentPhotoPath { get; private set; } = string.Empty;

    /// <summary>طلبات تعديل معلّقة للموظف (لعرضها مع أزرار تعديل/حذف).</summary>
    public List<DataChangeRequestStore.PendingRequest> PendingRequests { get; private set; } = new();

    /// <summary>معرّف طلب قيد التعديل (0 = طلب جديد). عند &gt;0 يُستبدَل الطلب القديم عند الإرسال.</summary>
    public int EditId { get; private set; }

    /// <summary>قيم مقترَحة لتعبئة النموذج مسبقاً عند التعديل (مفتاح الحقل ⟶ القيمة الجديدة).</summary>
    public Dictionary<string, string?> Prefill { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IActionResult> OnGetAsync(int? edit)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");

        await LoadAsync(employeeId);

        // وضع التعديل: عبّئ النموذج بقيم الطلب المعلّق (يُستبدَل عند الإرسال).
        if (edit is int editId && editId > 0)
        {
            var editable = await DataChangeRequestStore.GetEditableRequestFieldsAsync(_dbContext, editId, employeeId);
            if (editable.Count > 0)
            {
                EditId = editId;
                foreach (var f in editable) Prefill[f.Key] = f.NewValue;
            }
        }
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");

        var ok = await DataChangeRequestStore.DeletePendingRequestAsync(_dbContext, id, employeeId);
        StatusMessage = ok ? "تم حذف طلب التعديل المعلّق." : "تعذّر الحذف (الطلب غير موجود أو تمّ البتّ فيه).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage();
        }

        await DataChangeRequestStore.EnsureAsync(_dbContext);
        var editable = await DataChangeRequestStore.ListEditableAsync(_dbContext, employeeId);

        var proposed = new List<DataChangeRequestStore.ProposedField>();
        foreach (var f in editable)
        {
            if (f.Kind == "photo") continue; // الصورة تُعالَج بملف مستقل أدناه.
            var raw = Request.Form[$"new_{f.Key}"].ToString();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var val = raw.Trim();

            // حقول القائمة: القيمة يجب أن تكون ضمن قوائم النظام (لا إدخال حر).
            if (f.Kind == "select")
            {
                var opts = await DataChangeRequestStore.OptionsAsync(_dbContext, f.OptionsKey);
                if (!opts.Any(o => string.Equals(o.Value, val, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }
            proposed.Add(new DataChangeRequestStore.ProposedField { Key = f.Key, OldValue = f.OldValue, NewValue = val });
        }

        // الصورة (اختيارية): تُحفظ فوراً على القرص، والمسار يُقترَح كحقل — يُطبَّق عند الاعتماد.
        var photo = Request.Form.Files["EmployeePhoto"];
        if (photo is { Length: > 0 })
        {
            var (ok, pathOrError) = await SavePhotoAsync(photo, employeeId);
            if (!ok)
            {
                StatusMessage = pathOrError;
                return RedirectToPage();
            }
            var currentPhoto = editable.FirstOrDefault(x => x.Key == "PhotoPath")?.OldValue;
            proposed.Add(new DataChangeRequestStore.ProposedField
            {
                Key = "PhotoPath",
                OldValue = currentPhoto,
                NewValue = pathOrError
            });
        }

        if (proposed.Count == 0)
        {
            StatusMessage = "لم تُدخِل أي قيمة جديدة لتعديلها.";
            return RedirectToPage();
        }

        var reason = Request.Form["dcReason"].ToString();
        reason = string.IsNullOrWhiteSpace(reason) ? "طلب تعديل بيانات من بوابة الموظف" : reason.Trim();

        // وضع التعديل: احذف الطلب المعلّق القديم أولاً (استبدال) قبل إنشاء الجديد.
        if (int.TryParse(Request.Form["editId"], out var editId) && editId > 0)
            await DataChangeRequestStore.DeletePendingRequestAsync(_dbContext, editId, employeeId);

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO SelfServiceRequests (EmployeeId, RequestType, CreatedAt, Reason, Status)
VALUES (@Emp, @Type, SYSUTCDATETIME(), @Reason, 'Pending');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@Type", DataChangeRequestStore.RequestTypeLabel);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
            });

        var saved = requestId > 0 ? await DataChangeRequestStore.SaveFieldsAsync(_dbContext, requestId, proposed) : 0;
        if (saved == 0)
        {
            if (requestId > 0)
                await HrmsDatabase.ExecuteAsync(_dbContext, "DELETE FROM SelfServiceRequests WHERE Id=@r",
                    cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId));
            StatusMessage = "لم تُدخِل أي قيمة مختلفة عن الحالية.";
            return RedirectToPage();
        }

        await ApprovalWorkflowEngine.StartAsync(_dbContext, requestId, DataChangeRequestStore.RequestTypeLabel, employeeId);
        StatusMessage = $"تم إرسال طلب تعديل البيانات ({saved} حقل) وهو الآن قيد المراجعة.";
        return RedirectToPage();
    }

    private async Task LoadAsync(int employeeId)
    {
        await DataChangeRequestStore.EnsureAsync(_dbContext);
        Fields = employeeId > 0
            ? await DataChangeRequestStore.ListEditableAsync(_dbContext, employeeId)
            : new();

        foreach (var f in Fields.Where(x => x.Kind == "select"))
            Options[f.Key] = await DataChangeRequestStore.OptionsAsync(_dbContext, f.OptionsKey);

        CurrentPhotoPath = Fields.FirstOrDefault(x => x.Key == "PhotoPath")?.OldValue ?? string.Empty;

        PendingRequests = employeeId > 0
            ? await DataChangeRequestStore.ListPendingForEmployeeAsync(_dbContext, employeeId)
            : new();
    }

    private async Task<(bool Ok, string Value)> SavePhotoAsync(IFormFile photo, int employeeId)
    {
        var ext = Path.GetExtension(photo.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedPhotoExt.Contains(ext))
            return (false, "صيغة الصورة غير مدعومة (JPG/PNG/WEBP).");
        if (photo.Length > 5 * 1024 * 1024)
            return (false, "حجم الصورة أكبر من 5MB.");
        if (!await UploadSignatureValidator.IsValidImageAsync(photo))
            return (false, "محتوى الملف ليس صورة صالحة.");

        var dir = Path.Combine(_environment.WebRootPath, "uploads", "employee-photos");
        Directory.CreateDirectory(dir);
        var storedName = $"emp_{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext.ToLowerInvariant()}";
        var physicalPath = Path.Combine(dir, storedName);
        await using (var stream = System.IO.File.Create(physicalPath))
        {
            await photo.CopyToAsync(stream);
        }
        return (true, $"/uploads/employee-photos/{storedName}");
    }

    private async Task<int> ResolveEmployeeIdAsync()
    {
        var employeeIdClaim = User.FindFirstValue("EmployeeId");
        if (int.TryParse(employeeIdClaim, out var claimEmployeeId) && claimEmployeeId > 0)
            return claimEmployeeId;

        var username = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(username))
            return await HrmsDatabase.ScalarAsync<int>(
                _dbContext,
                "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @Username AND IsActive = 1",
                command => HrmsDatabase.AddParameter(command, "@Username", username));
        return 0;
    }
}
