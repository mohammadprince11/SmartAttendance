using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Shared;

/// <summary>
/// يقرأ رمز الموظف المحسوم واسمه كي يبدأ <c>_EmployeePicker</c> معبَّأً بعد الحفظ
/// أو عند فتح صفحةٍ برابطٍ يحمل معرّف موظف.
///
/// **لماذا مساعدٌ مشترك؟** المنسدلة القديمة كانت تحمل الاسم داخلها فلا تحتاج قراءة
/// إضافية؛ المنتقي لا يحمّل أحداً، فكل شاشة منقولة تحتاج هذا الصفّ الواحد. بلا
/// مساعد تتكرّر الاستعلامة نفسها بسبع صفحات — وتنحرف واحدةٌ منها يوماً ما.
/// </summary>
public static class EmployeePickerLookup
{
    public sealed record Identity(string Code, string Name);

    /// <summary>يرجع <c>null</c> إذا كان المعرّف صفراً أو لا يقابل موظفاً.</summary>
    public static async Task<Identity?> LoadAsync(ApplicationDbContext db, int employeeId)
    {
        if (employeeId <= 0)
        {
            return null;
        }

        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP (1) ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName
FROM Employees
WHERE Id = @Id AND ISNULL(IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", employeeId),
            reader => new Identity(
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "FullName")));

        return rows.FirstOrDefault();
    }
}
