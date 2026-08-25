using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// مختبر الصيغ ومكتبتها — **الطبقة البنيوية الثالثة** (نظير <c>ComputedFormula</c> بكيان).
///
/// المحرّك (<see cref="SalaryFormulaEvaluator"/>) كان موجوداً منذ زمن **بلا واجهة**:
/// تُكتب الصيغة داخل عنصر راتب ولا تُختبر إلا بتشغيل مسير كامل، وخطأٌ فيها يعني
/// عنصراً يُتخطّى بصمت. هذه الصفحة تجعل الصيغة **تُجرَّب بقيم قبل أن تُستعمل**.
/// </summary>
[Authorize(Roles = RoleRouteCatalog.Admin)]
public class FormulasModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public FormulasModel(ApplicationDbContext db) => _db = db;

    public sealed record LibraryRow(int Id, string Name, string? Description, string Expression, bool IsActive);

    [BindProperty] public int FormulaId { get; set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public string Expression { get; set; } = string.Empty;
    [BindProperty] public bool IsActive { get; set; } = true;

    // قيم التجربة — تُملأ بافتراضات واقعية لا بأصفار، فصيغةٌ تُجرَّب على صفر
    // تعطي صفراً دائماً ولا تكشف شيئاً.
    [BindProperty] public decimal Basic { get; set; } = 1_000_000;
    [BindProperty] public decimal Allowances { get; set; } = 250_000;
    [BindProperty] public decimal Hours { get; set; } = 8;
    [BindProperty] public decimal Days { get; set; } = 30;

    public List<LibraryRow> Library { get; private set; } = new();
    public string? TestResult { get; private set; }
    public string? TestError { get; private set; }

    /// <summary>المتغيّرات المتاحة — نفس ما يمرّره المسير حرفياً، لا قائمةً موازية.</summary>
    public static readonly (string Key, string Label)[] Variables = SalaryItemStore.FormulaVars;

    public static readonly (string Expression, string Purpose)[] Examples =
    {
        ("Basic * 0.1", "نسبة من الأساسي"),
        ("MIN(Basic * 0.25, 300000)", "نسبة بسقف"),
        ("IF(Basic > 500000, Basic * 0.05, 0)", "شريحة مشروطة"),
        ("ROUND(Basic / 30 * Days, 2)", "تنسيب بأيام"),
        ("MAX(Gross - 1000000, 0) * 0.15", "ضريبة فوق حدّ إعفاء"),
        ("IF(Days > 0, Basic / MAX(Days, 1), 0)", "قسمة آمنة — القسمة على صفر تُفشل الصيغة كلّها")
    };

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>تجربة الصيغة بالقيم المدخلة — بلا حفظ ولا مساس بأي بيانات.</summary>
    public async Task<IActionResult> OnPostTestAsync()
    {
        await LoadAsync();
        Evaluate();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Expression))
        {
            TempData["ErrorMessage"] = "الاسم والصيغة إلزاميان.";
            return RedirectToPage();
        }

        // ⚠️ **لا تُحفظ صيغة لا تُقيَّم.** صيغةٌ معطوبة بالمكتبة تُنسخ لعنصر راتب
        // فيُتخطّى العنصر بصمت بكل مسير — وهو أسوأ من رفض الحفظ الآن.
        if (!SalaryFormulaEvaluator.TryEvaluate(Expression, BuildVariables(), out _, out var error))
        {
            await LoadAsync();
            TestError = error;
            TempData["ErrorMessage"] = $"لم تُحفظ — الصيغة غير صالحة: {error}";
            return Page();
        }

        if (FormulaId > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                _db,
                """
UPDATE SalaryFormulaLibrary
SET Name = @Name, Description = @Description, Expression = @Expression,
    IsActive = @IsActive, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", FormulaId);
                    HrmsDatabase.AddParameter(command, "@Name", Name.Trim());
                    HrmsDatabase.AddParameter(command, "@Description", Description);
                    HrmsDatabase.AddParameter(command, "@Expression", Expression.Trim());
                    HrmsDatabase.AddParameter(command, "@IsActive", IsActive);
                });
        }
        else
        {
            await HrmsDatabase.ExecuteAsync(
                _db,
                """
INSERT INTO SalaryFormulaLibrary (Name, Description, Expression, IsActive, CreatedBy)
VALUES (@Name, @Description, @Expression, @IsActive, @CreatedBy);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Name", Name.Trim());
                    HrmsDatabase.AddParameter(command, "@Description", Description);
                    HrmsDatabase.AddParameter(command, "@Expression", Expression.Trim());
                    HrmsDatabase.AddParameter(command, "@IsActive", IsActive);
                    HrmsDatabase.AddParameter(command, "@CreatedBy", User.Identity?.Name);
                });
        }

        TempData["SuccessMessage"] = "حُفظت الصيغة بعد التحقّق من صلاحيتها.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await HrmsDatabase.ExecuteAsync(
            _db,
            "UPDATE SalaryFormulaLibrary SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        TempData["SuccessMessage"] = "حُذفت الصيغة من المكتبة (عناصر الراتب التي نسختها لا تتأثر).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLoadAsync(int id)
    {
        await LoadAsync();

        if (Library.FirstOrDefault(row => row.Id == id) is { } row)
        {
            FormulaId = row.Id;
            Name = row.Name;
            Description = row.Description;
            Expression = row.Expression;
            IsActive = row.IsActive;
            Evaluate();
        }

        return Page();
    }

    /// <summary>
    /// قيم التجربة تُحوَّل لسياق المسير نفسه ثم يبني <see cref="PayrollFormulaVariables"/>
    /// القاموس. <b>لا يُبنى القاموس هنا بيده</b> — الطريقة السابقة هي التي جعلت
    /// المختبر يمرّر <c>Days=30, Hours=8</c> بينما يمرّر المسير صفرين، فتُجرَّب
    /// الصيغة بنجاح ثم تُنتج بالقسيمة رقماً آخر.
    /// </summary>
    /// <summary>
    /// القيم الثابتة المسمّاة — تُحمَّل مع الصفحة وتُدمج بفضاء التجربة، وإلا فشلت
    /// بالمختبر كل صيغة تستعمل ثابتاً بينما تنجح بالمسير: نفس عيب الافتراق مقلوباً.
    /// </summary>
    public List<SalaryConstantStore.SalaryConstant> Constants { get; private set; } = new();

    private Dictionary<string, decimal> ConstantMap() =>
        Constants.Where(c => c.IsActive)
                 .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, decimal> BuildVariables() =>
        PayrollFormulaVariables.Build(new PayrollFormulaVariables.Context
        {
            Basic = Basic,
            // شهر كامل بالتجربة: المستحقّ = الأساسي والمعامل = 1.
            ProratedBasic = Basic,
            Allowances = Allowances,
            Days = (int)Days,
            PresentDays = (int)Days,
            DaysInPeriod = (int)Days,
            Hours = Hours,
            // مشتقّان بنفس قاعدة المسير: الشهر ثلاثون يوماً واليوم ثماني ساعات.
            DailyRate = Basic / 30m,
            HourlyRate = Basic / 30m / 8m,
            Factor = 1m
        },
        ConstantMap());

    private void Evaluate()
    {
        if (string.IsNullOrWhiteSpace(Expression))
        {
            return;
        }

        if (SalaryFormulaEvaluator.TryEvaluate(Expression, BuildVariables(), out var result, out var error))
        {
            TestResult = result.ToString("N2");
            TestError = null;
        }
        else
        {
            TestResult = null;
            TestError = error;
        }
    }

    // ═══════════ القيم الثابتة المسمّاة (نظير «قيم ثابتة» بكيان) ═══════════

    [BindProperty] public int ConstantId { get; set; }
    [BindProperty] public string ConstantKey { get; set; } = string.Empty;
    [BindProperty] public string ConstantName { get; set; } = string.Empty;
    [BindProperty] public decimal ConstantValue { get; set; }
    [BindProperty] public string? ConstantNote { get; set; }

    public async Task<IActionResult> OnPostSaveConstantAsync()
    {
        var error = await SalaryConstantStore.SaveAsync(_db, new SalaryConstantStore.SalaryConstant
        {
            Id = ConstantId,
            Key = ConstantKey,
            Name = ConstantName,
            Value = ConstantValue,
            Note = ConstantNote,
            IsActive = true
        });

        if (error is not null)
        {
            TempData["ErrorMessage"] = $"لم يُحفظ الثابت: {error}";
        }
        else
        {
            TempData["SuccessMessage"] = "حُفظ الثابت — صار متاحاً بكل الصيغ فوراً.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteConstantAsync(int id)
    {
        await SalaryConstantStore.DeleteAsync(_db, id);
        TempData["SuccessMessage"] = "حُذف الثابت — راجع الصيغ التي كانت تستعمله.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Constants = await SalaryConstantStore.ListAsync(_db);
        await LoadLibraryAsync();
    }

    private async Task LoadLibraryAsync() =>
        Library = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT Id, Name, Description, Expression, IsActive
FROM SalaryFormulaLibrary
WHERE ISNULL(IsDeleted, 0) = 0
ORDER BY Name;
""",
            command => { },
            reader => new LibraryRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Name"),
                HrmsDatabase.GetString(reader, "Description"),
                HrmsDatabase.GetString(reader, "Expression"),
                HrmsDatabase.GetBool(reader, "IsActive")));
}
