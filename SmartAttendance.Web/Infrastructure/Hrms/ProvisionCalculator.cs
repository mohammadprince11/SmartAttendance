using SmartAttendance.Domain.Leave;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// حساب الاحتياطي (نمط كيان — «احتياطي/مخصصات» الالتزامات): الالتزام المتراكم الذي
/// يجب أن تحتجزه الشركة لكل موظف <b>نشط</b> بتاريخ محدّد = مخصص مكافأة نهاية الخدمة
/// (كأن الموظف تُرك اليوم — بشرائح <see cref="EndOfServiceStore.ComputeGratuity"/>
/// على آخر أساسي) + مخصص رصيد الإجازات السنوية غير المستخدمة (أيام × الأجر اليومي).
/// حساب مُجمَّع (استعلامات bulk) لكل الموظفين دفعةً واحدة. تقرير للقراءة فقط.
/// </summary>
public static class ProvisionCalculator
{
    public sealed class Row
    {
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public DateOnly? HireDate { get; set; }
        public decimal Basic { get; set; }
        public decimal Years { get; set; }
        public decimal EosProvision { get; set; }
        public decimal LeaveDays { get; set; }
        public decimal LeaveProvision { get; set; }
        public decimal Total => EosProvision + LeaveProvision;
    }

    public sealed class Result
    {
        public List<Row> Rows { get; set; } = new();
        public decimal TotalEos { get; set; }
        public decimal TotalLeave { get; set; }
        public decimal Total => TotalEos + TotalLeave;
        public int EmployeeCount => Rows.Count;
    }

    /// <summary>
    /// يحسب الاحتياطي بتاريخ <paramref name="asOf"/> لسنة الرصيد <paramref name="year"/>
    /// لموظفي الشركة النشطين (أو الكل عند null). فلتر اختياري بالبحث/القسم/الفرع.
    /// </summary>
    public static async Task<Result> ComputeAsync(
        ApplicationDbContext db, DateOnly asOf, int year,
        int? companyId = null, string? search = null, string? department = null, string? branch = null)
    {
        await EmployeeFinancialInfoSchema.EnsureAsync(db);
        await LeaveBalanceSchema.EnsureAsync(db);

        // 1) الموظفون النشطون + الأساسي + التعيين + التنظيم (استعلام واحد)
        var employees = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT e.Id, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(d.Name, N'') AS DepartmentName, ISNULL(b.Name, N'') AS BranchName,
       ISNULL(f.BasicSalary, 0) AS BasicSalary, COALESCE(e.HireDate, e.JoiningDate) AS HireDate,
       ISNULL(b.CompanyId, 0) AS CompanyId
FROM Employees e
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
LEFT JOIN EmployeeFinancialInfos f ON f.EmployeeId = e.Id AND ISNULL(f.IsDeleted,0) = 0
WHERE ISNULL(e.IsDeleted,0) = 0 AND ISNULL(e.IsActive,1) = 1
ORDER BY e.EmployeeNo;
""",
            command => { },
            reader => new
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Name = HrmsDatabase.GetString(reader, "FullName"),
                Dept = HrmsDatabase.GetString(reader, "DepartmentName"),
                Branch = HrmsDatabase.GetString(reader, "BranchName"),
                Basic = reader["BasicSalary"] is decimal bs ? bs : 0,
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId")
            });

        if (companyId is > 0) employees = employees.Where(e => e.CompanyId == companyId).ToList();
        if (!string.IsNullOrWhiteSpace(department)) employees = employees.Where(e => e.Dept == department).ToList();
        if (!string.IsNullOrWhiteSpace(branch)) employees = employees.Where(e => e.Branch == branch).ToList();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var v = search.Trim();
            employees = employees.Where(e =>
                e.No.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // 2) تجاوزات رصيد الإجازة السنوية للسنة (المستحق + المرحّل) — استعلام واحد
        var annualType = (int)Domain.Enums.LeaveType.Annual;
        var overrides = (await HrmsDatabase.QueryAsync(
            db,
            "SELECT EmployeeId, EntitledDays, CarriedOverDays FROM LeaveBalances WHERE [Year] = @Year AND LeaveType = @Type;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Year", year);
                HrmsDatabase.AddParameter(command, "@Type", annualType);
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                Entitled = reader["EntitledDays"] is decimal en ? en : 0,
                Carried = reader["CarriedOverDays"] is decimal c ? c : 0
            })).ToDictionary(x => x.EmployeeId, x => (x.Entitled, x.Carried));

        // 3) الإجازات السنوية المعتمدة المتقاطعة مع السنة → المستهلَك لكل موظف — استعلام واحد
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var approved = (int)Domain.Enums.LeaveStatus.Approved;
        var used = new Dictionary<int, decimal>();
        var leaveRows = await HrmsDatabase.QueryAsync(
            db,
            "SELECT EmployeeId, FromDate, ToDate FROM LeaveRequests WHERE Status = @Status AND LeaveType = @Type AND FromDate <= @End AND ToDate >= @Start AND ISNULL(IsDeleted,0)=0;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Status", approved);
                HrmsDatabase.AddParameter(command, "@Type", annualType);
                HrmsDatabase.AddParameter(command, "@Start", yearStart.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@End", yearEnd.ToDateTime(TimeOnly.MinValue));
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                From = HrmsDatabase.GetDateOnly(reader, "FromDate") ?? default,
                To = HrmsDatabase.GetDateOnly(reader, "ToDate") ?? default
            });
        foreach (var l in leaveRows)
        {
            var start = l.From > yearStart ? l.From : yearStart;
            var end = l.To < yearEnd ? l.To : yearEnd;
            var days = end.DayNumber - start.DayNumber + 1;
            if (days <= 0) continue;
            used[l.EmployeeId] = used.GetValueOrDefault(l.EmployeeId) + days;
        }

        var defaultAnnual = IraqiLeavePolicy.GetDefaultEntitlement(Domain.Enums.LeaveType.Annual) ?? 0;
        var result = new Result();

        foreach (var e in employees)
        {
            var years = e.HireDate is { } hire ? EndOfServiceStore.YearsOfService(hire, asOf) : 0;
            var (eos, _) = EndOfServiceStore.ComputeGratuity(years, e.Basic);

            var entitled = overrides.TryGetValue(e.Id, out var o) ? o.Entitled + o.Carried : defaultAnnual;
            var remaining = entitled - used.GetValueOrDefault(e.Id);
            var leaveDays = remaining > 0 ? remaining : 0;
            var dailyRate = e.Basic > 0 ? Math.Round(e.Basic / 30m, 4) : 0;
            var leaveProvision = Math.Round(leaveDays * dailyRate, 2);

            result.Rows.Add(new Row
            {
                EmployeeId = e.Id,
                EmployeeNo = e.No,
                EmployeeName = e.Name,
                Department = e.Dept,
                Branch = e.Branch,
                HireDate = e.HireDate,
                Basic = e.Basic,
                Years = years,
                EosProvision = eos,
                LeaveDays = leaveDays,
                LeaveProvision = leaveProvision
            });
            result.TotalEos += eos;
            result.TotalLeave += leaveProvision;
        }

        result.Rows = result.Rows.OrderByDescending(r => r.Total).ToList();
        return result;
    }
}
