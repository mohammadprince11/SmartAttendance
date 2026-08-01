using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Violations;

/// <summary>
/// سجلّ الإجراءات التأديبية (نظير `/Employees/ViolationActions` بكيان).
///
/// كان عندنا **قواعدُ العقوبة** (`/DisciplinaryRules`) و**حالاتُ المخالفات**
/// (`/Violations`)، لكن لا شاشة تجيب: «ما العقوبات المطبَّقة فعلاً؟ وكم منها
/// ما زال قابلاً للطعن؟ وكم سقط؟».
///
/// وهي المستهلك الفعلي لـ<see cref="ViolationConfigPolicy"/> — بدونها تبقى تهيئة
/// المخالفات إعداداً لا أثر له.
/// </summary>
public class ActionsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ActionsModel(ApplicationDbContext db) => _db = db;

    public sealed record ActionRow(
        int Id,
        string ReferenceNo,
        string EmployeeName,
        string EmployeeNo,
        string ViolationTitle,
        string? FinalPenaltyAction,
        string FinancialImpactType,
        decimal DeductionAmount,
        DateOnly EventDate,
        DateOnly? NotifiedOn,
        DateOnly? DecidedOn);

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    /// <summary>Open = قابلة للطعن · Final = نهائية · Dropped = ساقطة.</summary>
    [BindProperty(SupportsGet = true)] public string StateFilter { get; set; } = "All";

    public List<ActionRow> Actions { get; private set; } = new();
    public int ObjectionDays { get; private set; }
    public int DropMonths { get; private set; }
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);

    public int OpenForObjection => Actions.Count(row =>
        ViolationConfigPolicy.CanObject(row.NotifiedOn, ObjectionDays, Today));

    public int Dropped => Actions.Count(row =>
        ViolationConfigPolicy.IsDropped(row.DecidedOn, DropMonths, Today));

    public decimal TotalDeductions => Actions.Sum(row => row.DeductionAmount);

    public string StatusOf(ActionRow row) =>
        ViolationConfigPolicy.StatusLabel(row.NotifiedOn, row.DecidedOn, ObjectionDays, DropMonths, Today);

    public bool IsDropped(ActionRow row) =>
        ViolationConfigPolicy.IsDropped(row.DecidedOn, DropMonths, Today);

    /// <summary>
    /// صارت تبويباً بـ`/Violations`. يبقى المسار حيّاً لروابطٍ قديمة ويعيد التوجيه،
    /// فلا نسخة ثانية من الشاشة ولا خروجٌ من سياق المخالفات.
    /// </summary>
    public IActionResult OnGet() => RedirectToPage("/Violations/Index", new { tab = "actions" });

    private async Task LegacyLoadAsync()
    {
        await ViolationCaseSchema.EnsureAsync(_db);

        ObjectionDays = int.TryParse(await SettingAsync(ViolationConfigPolicy.KeyObjectionDays), out var od) ? od : 0;
        DropMonths = int.TryParse(await SettingAsync(ViolationConfigPolicy.KeyDropMonths), out var dm) ? dm : 0;

        var search = string.IsNullOrWhiteSpace(Search) ? null : $"%{Search.Trim()}%";

        var all = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT c.Id, ISNULL(c.ReferenceNo, N'') AS ReferenceNo,
       ISNULL(e.FullName, N'') AS EmployeeName, ISNULL(e.EmployeeNo, N'') AS EmployeeNo,
       ISNULL(c.ViolationTitle, N'') AS ViolationTitle, c.FinalPenaltyAction,
       ISNULL(c.FinancialImpactType, N'None') AS FinancialImpactType,
       ISNULL(c.DeductionAmount, 0) AS DeductionAmount,
       c.EventDate, c.NotifiedOn, c.DecidedOn
FROM EmployeeViolationCases c
LEFT JOIN Employees e ON e.Id = c.EmployeeId
WHERE ISNULL(c.IsDeleted, 0) = 0
  AND (c.FinalPenaltyAction IS NOT NULL AND LTRIM(RTRIM(c.FinalPenaltyAction)) <> N'')
  AND (@Search IS NULL OR e.FullName LIKE @Search OR e.EmployeeNo LIKE @Search OR c.ReferenceNo LIKE @Search)
ORDER BY c.EventDate DESC, c.Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@Search", search),
            reader => new ActionRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "ReferenceNo"),
                HrmsDatabase.GetString(reader, "EmployeeName"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "ViolationTitle"),
                HrmsDatabase.GetString(reader, "FinalPenaltyAction"),
                HrmsDatabase.GetString(reader, "FinancialImpactType"),
                HrmsDatabase.GetNullableDecimal(reader, "DeductionAmount") ?? 0,
                HrmsDatabase.GetDateOnly(reader, "EventDate") ?? default,
                HrmsDatabase.GetDateOnly(reader, "NotifiedOn"),
                HrmsDatabase.GetDateOnly(reader, "DecidedOn")));

        // التصفية بالحالة تتمّ بالذاكرة لأنها **مشتقّة من التهيئة** لا مخزَّنة:
        // تغيير مدّة الإسقاط يجب أن يغيّر التصنيف فوراً بلا تعديل أي صفّ.
        Actions = StateFilter switch
        {
            "Open" => all.Where(row => ViolationConfigPolicy.CanObject(row.NotifiedOn, ObjectionDays, Today)).ToList(),
            "Dropped" => all.Where(row => ViolationConfigPolicy.IsDropped(row.DecidedOn, DropMonths, Today)).ToList(),
            "Final" => all.Where(row =>
                !ViolationConfigPolicy.CanObject(row.NotifiedOn, ObjectionDays, Today)
                && !ViolationConfigPolicy.IsDropped(row.DecidedOn, DropMonths, Today)
                && row.DecidedOn is not null).ToList(),
            _ => all
        };
    }

    private async Task<string> SettingAsync(string key) =>
        await HrmsDatabase.ScalarAsync<string>(
            _db,
            "SELECT TOP 1 [Value] FROM DisciplinarySettings WHERE [Key] = @Key;",
            command => HrmsDatabase.AddParameter(command, "@Key", key)) ?? "0";
}
