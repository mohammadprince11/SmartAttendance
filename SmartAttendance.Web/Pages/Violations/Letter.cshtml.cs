using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Violations;

/// <summary>
/// عرض «كتاب العقوبة» الصادر — النصّ **المحفوظ** لا المولَّد الآن.
///
/// الفرق جوهريّ: لو رُكِّب من القالب عند كل فتح لتغيّر الكتاب كلّما عُدّل القالب،
/// فيقرأ الموظف اليوم غير ما وقّعه أمس. تعديل القالب يخصّ كتب المستقبل وحدها.
/// </summary>
public class LetterModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public LetterModel(ApplicationDbContext db) => _db = db;

    public string ReferenceNo { get; private set; } = string.Empty;
    public string EmployeeName { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTime? IssuedAt { get; private set; }

    /// <summary>رموز بقيت بلا استبدال — تُعرض تنبيهاً كي لا يُوقَّع كتابٌ ناقص.</summary>
    public IReadOnlyList<string> UnresolvedTokens { get; private set; } = Array.Empty<string>();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await ViolationCaseSchema.EnsureAsync(_db);

        var rows = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1
    ISNULL(v.ReferenceNo, N'') AS ReferenceNo,
    ISNULL(e.FullName, N'') AS EmployeeName,
    ISNULL(v.LetterSubject, N'') AS LetterSubject,
    ISNULL(v.LetterBody, N'') AS LetterBody,
    v.LetterIssuedAt
FROM EmployeeViolationCases v
LEFT JOIN Employees e ON e.Id = v.EmployeeId
WHERE v.Id = @Id AND ISNULL(v.IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            reader => new
            {
                ReferenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "EmployeeName"),
                Subject = HrmsDatabase.GetString(reader, "LetterSubject"),
                Body = HrmsDatabase.GetString(reader, "LetterBody"),
                IssuedAt = HrmsDatabase.GetDateTime(reader, "LetterIssuedAt")
            });

        var row = rows.FirstOrDefault();
        if (row is null || row.IssuedAt is null)
        {
            return NotFound();
        }

        ReferenceNo = row.ReferenceNo;
        EmployeeName = row.EmployeeName;
        Subject = row.Subject;
        Body = row.Body;
        IssuedAt = row.IssuedAt;
        UnresolvedTokens = DisciplinaryLetter.UnresolvedTokens(row.Body);

        return Page();
    }
}
