using SmartAttendance.Domain.Leave;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

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
        ApplicationDbContext db, CompanyScope scope, DateOnly asOf, int year,
        int? companyId = null, string? search = null, string? department = null, string? branch = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsDeniedAll) return new Result();
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
  AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")}
  AND (@CompanyId IS NULL OR e.CompanyId = @CompanyId)
  AND (@Department IS NULL OR d.Name = @Department)
  AND (@Branch IS NULL OR b.Name = @Branch)
  AND (@Search IS NULL OR e.EmployeeNo LIKE N'%' + @Search + N'%' OR e.FullName LIKE N'%' + @Search + N'%')
ORDER BY e.EmployeeNo;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId is > 0 ? companyId.Value : DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Department", DbValue(department));
                HrmsDatabase.AddParameter(command, "@Branch", DbValue(branch));
                HrmsDatabase.AddParameter(command, "@Search", DbValue(search));
            },
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

        // 2) تجاوزات رصيد الإجازة السنوية للسنة (المستحق + المرحّل) — استعلام واحد
        var annualType = (int)Domain.Enums.LeaveType.Annual;
        var overrides = (await HrmsDatabase.QueryAsync(
            db,
            $"SELECT l.EmployeeId, l.EntitledDays, l.CarriedOverDays FROM LeaveBalances l INNER JOIN Employees e ON e.Id=l.EmployeeId WHERE l.[Year] = @Year AND l.LeaveType = @Type AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")} AND (@CompanyId IS NULL OR e.CompanyId=@CompanyId);",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Year", year);
                HrmsDatabase.AddParameter(command, "@Type", annualType);
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId is > 0 ? companyId.Value : DBNull.Value);
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
            $"SELECT l.EmployeeId, l.FromDate, l.ToDate FROM LeaveRequests l INNER JOIN Employees e ON e.Id=l.EmployeeId WHERE l.Status = @Status AND l.LeaveType = @Type AND l.FromDate <= @End AND l.ToDate >= @Start AND ISNULL(l.IsDeleted,0)=0 AND {EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")} AND (@CompanyId IS NULL OR e.CompanyId=@CompanyId);",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Status", approved);
                HrmsDatabase.AddParameter(command, "@Type", annualType);
                HrmsDatabase.AddParameter(command, "@Start", yearStart.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@End", yearEnd.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId is > 0 ? companyId.Value : DBNull.Value);
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

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}
