using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قراءة/كتابة أعضاء نطاق تشغيل المسير (<c>PayrollRunScopeMembers</c>).
///
/// الجدول يُنشأ بهجرة محكومة (<see cref="SqlSchemaMigrator"/> — <c>…-10</c>) لا
/// بالشفاء الذاتي. **غياب الصفوف يعني «كل الموظفين»** (القرار بالكود —
/// <see cref="PayrollRunScope.Includes"/>) فلا يُكتب صفٌّ لتشغيل بلا تحديد.
///
/// النطاق يُحفظ مع التشغيل لا مع الجلسة، لأن «إعادة الاحتساب» بعد شهر يجب أن
/// تلتزم بنفس النطاق، ولأن السؤال «لماذا غاب هذا الموظف؟» يجب أن يبقى مُجاباً.
/// </summary>
public static class PayrollRunScopeStore
{
    /// <summary>معرّفات موظفي النطاق؛ قائمة فارغة ⟹ التشغيل يشمل الكل.</summary>
    public static Task<List<int>> IdsAsync(ApplicationDbContext dbContext, int runId) =>
        HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT EmployeeId FROM PayrollRunScopeMembers WHERE RunId = @RunId ORDER BY Id;",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId),
            reader => HrmsDatabase.GetInt(reader, "EmployeeId"));

    /// <summary>موظفو النطاق بأسمائهم وأرقامهم — لعرض «من يشمله هذا التشغيل».</summary>
    public static Task<List<(int Id, string No, string Name)>> MembersAsync(
        ApplicationDbContext dbContext, int runId) =>
        HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT s.EmployeeId, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName
FROM PayrollRunScopeMembers s
LEFT JOIN Employees e ON e.Id = s.EmployeeId
WHERE s.RunId = @RunId
ORDER BY e.EmployeeNo;
""",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId),
            reader => (
                Id: HrmsDatabase.GetInt(reader, "EmployeeId"),
                No: HrmsDatabase.GetString(reader, "EmployeeNo"),
                Name: HrmsDatabase.GetString(reader, "FullName")));

    /// <summary>يستبدل نطاق التشغيل كاملاً. قائمة فارغة ⟹ حذف الصفوف = «الكل».</summary>
    public static async Task ReplaceAsync(
        ApplicationDbContext dbContext, int runId, IEnumerable<int> employeeIds)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM PayrollRunScopeMembers WHERE RunId = @RunId;",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId));

        foreach (var employeeId in employeeIds.Distinct())
        {
            var current = employeeId;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO PayrollRunScopeMembers (RunId, EmployeeId) VALUES (@RunId, @Emp);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@RunId", runId);
                    HrmsDatabase.AddParameter(command, "@Emp", current);
                });
        }
    }

    /// <summary>خريطة رمز الموظف ← معرّفه للنشطين — لمعاينة الأكواد الملصوقة.</summary>
    public static async Task<Dictionary<string, int>> ActiveCodeMapAsync(ApplicationDbContext dbContext)
    {
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1;",
            command => { },
            reader => (Code: HrmsDatabase.GetString(reader, "EmployeeNo"), Id: HrmsDatabase.GetInt(reader, "Id")));

        return PayrollRunScope.BuildCodeMap(rows);
    }

    /// <summary>
    /// معاينة أكواد ملصوقة قبل التشغيل: المطابِق باسمه ورقمه، والمفقود بنصّه.
    /// هذا هو الفارق عن كيان/ZenHR — هناك تُكتشف الأكواد الخاطئة بعد الاحتساب.
    /// </summary>
    public static async Task<(List<(int Id, string No, string Name)> Matched, List<string> Missing)>
        PreviewCodesAsync(ApplicationDbContext dbContext, string? raw)
    {
        var employees = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1;",
            command => { },
            reader => (
                Id: HrmsDatabase.GetInt(reader, "Id"),
                No: HrmsDatabase.GetString(reader, "EmployeeNo"),
                Name: HrmsDatabase.GetString(reader, "FullName")));

        var map = PayrollRunScope.BuildCodeMap(employees.Select(e => (e.No, e.Id)));
        var (ids, missing) = PayrollRunScope.MatchCodes(PayrollRunScope.ParseCodes(raw), map);

        var byId = employees.ToDictionary(e => e.Id);
        return (ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList(), missing);
    }
}
