using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Infrastructure.Security;

const int Year = 2026;
const int Month = 8;
const string DbName = "SmartAttendance_E2E_FiveCompanies_20260912_V3";
var cs = $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cs).Options;
await using var db = new ApplicationDbContext(options);

var scenarios = new[]
{
    new Scenario("SC-A","Atlas Pharma",21,20,2.0m,250000m,new[]{3m,5m,10m,15m},5m,12m,2000000m,1.50m),
    new Scenario("SC-B","Babylon Trading",26,25,1.5m,300000m,new[]{2m,4m,8m,12m},4m,10m,2500000m,1.75m),
    new Scenario("SC-C","Cedar Services",1,31,1.0m,200000m,new[]{5m,10m,15m,18m},6m,13m,3000000m,2.00m),
    new Scenario("SC-D","Dijla Logistics",11,10,2.0m,400000m,new[]{3m,7m,12m,17m},5m,15m,1800000m,1.25m),
    new Scenario("SC-E","Euphrates Tech",28,27,1.5m,150000m,new[]{4m,8m,12m,16m},3m,9m,0m,2.00m)
};
await HrmsDatabase.EnsureCreatedAsync(db);
await EmployeeFinancialInfoSchema.EnsureAsync(db);
await EmployeeTasksSchema.EnsureAsync(db);
await EmployeeEngagementSchema.EnsureAsync(db);
await PayrollConfigStore.EnsureAsync(db);
await PayrollTransactionStore.EnsureAsync(db);
await PayrollRunStore.EnsureAsync(db);
await DayAttendanceStore.EnsureAsync(db);
await MonthAttendanceStore.EnsureAsync(db);

await CleanBootstrapAsync(db);
var allEmployeeIds = new List<int>();
var companyIds = new List<int>();
var expectedByCompany = new Dictionary<int, CompanyExpectation>();

foreach (var (scenario, companyIndex) in scenarios.Select((s,i)=>(s,i+1)))
{
    var company = new Company
    {
        Name=scenario.Name, Code=scenario.Code, CurrencyCode="IQD", CountryCode="IQ",
        TimeZoneId="Asia/Baghdad", IsActive=true
    };
    var branch = new Branch { Name=$"{scenario.Name} HQ", Code=$"{scenario.Code}-HQ", Company=company, IsActive=true };
    var dept = new Department { Name="Operations", Code=$"{scenario.Code}-OPS", Company=company, Branch=branch, IsActive=true };
    db.AddRange(company,branch,dept);
    await db.SaveChangesAsync();
    companyIds.Add(company.Id);
    foreach (var type in Enum.GetValues<PayrollCutoffType>())
    {
        var policy = new PayrollCutoffPolicy
        {
            CompanyId=company.Id, Name=$"{scenario.Code} {type}", FromDay=scenario.FromDay, ToDay=scenario.ToDay,
            PolicyType=type, CutoffBasis=PayrollCutoffBasis.DayOfMonth, DayOfMonth=scenario.ToDay,
            EffectiveFrom=new DateOnly(2026,1,1), Priority=100, IsActive=true,
            Notes=$"Synthetic E2E policy for {scenario.Code}"
        };
        db.PayrollCutoffPolicies.Add(policy);
        await db.SaveChangesAsync();
        db.PayrollCutoffPolicyTypes.Add(new PayrollCutoffPolicyType { PayrollCutoffPolicyId=policy.Id, PolicyType=type });
        await db.SaveChangesAsync();
    }

    var scope = CompanyScope.ForCompanies(new[]{company.Id});
    await AttendanceSalaryLinkSettings.SaveAsync(db,company.Id,
        new AttendanceSalaryLink.Policy(AttendanceSalaryLink.Strict,scenario.AbsenceFactor,false,8m));
    await MissingPunchPayrollPolicy.SavePercentAsync(db,company.Id,25m);
    await HrSettingsStore.SetCompanyAsync(db,company.Id,PayrollDivisorPolicy.SalaryDaysBasisKey,PayrollDivisorPolicy.BasisPeriodDays);
    await HrSettingsStore.SetCompanyAsync(db,company.Id,PayrollDivisorPolicy.StandardDailyHoursKey,"8");
    await HrSettingsStore.SetCompanyAsync(db,company.Id,"Payroll.OvertimeBaseMode",PayrollEarningBase.ModeBasic);
    await HrSettingsStore.SetCompanyAsync(db,company.Id,"Payroll.GosiTaxBase",companyIndex%2==0?"FullBasic":"Prorated");

    var taxId = await SaveTaxAsync(db,scope,company.Id,scenario);
    var gosiId = await PayrollConfigStore.SaveGosiProfileAsync(db,scope,new PayrollConfigStore.GosiProfile
    {
        CompanyId=company.Id, Name=$"{scenario.Code} GOSI", EmployeeRate=scenario.GosiEmployee,
        CompanyRate=scenario.GosiCompany, Ceiling=scenario.GosiCeiling, IsActive=true, SortOrder=1
    });
    var employees = new List<Employee>();
    for (var i=1;i<=25;i++)
    {
        var employee = new Employee
        {
            EmployeeNo=$"{scenario.Code}-{i:000}", FullName=$"{scenario.Code} Employee {i:00}",
            FirstName=$"Emp{i:00}", LastName=scenario.Code, CompanyId=company.Id,
            BranchId=branch.Id, DepartmentId=dept.Id, HireDate=new DateOnly(2025,1,1),
            JoiningDate=new DateOnly(2025,1,1), WorkType="FTE", ContractType="Full Time",
            EmploymentStatus="Active", Position="Staff", IsActive=true, Nationality="Iraqi"
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        employees.Add(employee);
        allEmployeeIds.Add(employee.Id);

        var basic = 650000m + companyIndex*125000m + i*27500m;
        db.EmployeeFinancialInfos.Add(new EmployeeFinancialInfo
        {
            EmployeeId=employee.Id, Currency="IQD", BasicSalary=basic,
            TaxProfileId=taxId, GosiProfileId=gosiId, TaxBaseMode="SalaryComponents",
            GosiBaseMode="SalaryComponents", PaymentMethod=i%3==0?"Cash":"Bank",
            BankName=i%3==0?null:"E2E Bank", Iban=i%3==0?null:$"IQ{companyIndex:00}{i:000000000000000000}"
        });
        await AddCompletedOnboardingAsync(db,employee.Id);
    }
    await db.SaveChangesAsync();
    var (attendancePeriod, policyName) = await AttendancePeriodPolicy.ResolveFromPolicyAsync(
        db,Year,Month,PayrollCutoffType.Attendance,company.Id);
    var dates = attendancePeriod.EachDay().ToList();
    var workDates = dates.Where(d=>d.DayOfWeek!=DayOfWeek.Friday).ToList();
    var holidayDate = workDates[0];

    for (var eIndex=0;eIndex<employees.Count;eIndex++)
    {
        var emp=employees[eIndex];
        foreach (var date in dates)
        {
            var status="Present"; var dayKind="Work";
            DateTime? checkIn=date.ToDateTime(new TimeOnly(8,30));
            DateTime? checkOut=date.ToDateTime(new TimeOnly(16,30));
            decimal late=0m, worked=8m;

            if (date.DayOfWeek==DayOfWeek.Friday)
            { status="Weekend"; dayKind="Weekend"; checkIn=checkOut=null; worked=0m; }
            else if (date==holidayDate)
            { status="Holiday"; checkIn=checkOut=null; worked=0m; }
            else if (date==workDates[1] && (eIndex+1)%5==0)
            { status="Leave"; checkIn=checkOut=null; worked=0m; }
            else if (date==workDates[2] && (eIndex+1)%7==0)
            { status="LeaveUnpaid"; checkIn=checkOut=null; worked=0m; }
            else if (date==workDates[3])
            { status="Absent"; checkIn=checkOut=null; worked=0m; }
            else if (date==workDates[4])
            { status="Incomplete"; checkOut=null; worked=0m; }
            else if (date==workDates[5])
            { status="Late"; checkIn=date.ToDateTime(new TimeOnly(9,0)); checkOut=date.ToDateTime(new TimeOnly(17,0)); late=.5m; }
            else if (date==workDates[6] && (eIndex+1)%4==0)
            { status="Rest"; dayKind="Rest"; checkIn=checkOut=null; worked=0m; }

            await InsertDayAsync(db,emp.Id,date,dayKind,status,checkIn,checkOut,late,worked);
            if (status is "Present" or "Late" or "Incomplete")
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    EmployeeId=emp.Id, AttendanceDate=date, CheckIn=checkIn!.Value, CheckOut=checkOut,
                    Source=AttendanceSource.Device, Status=status=="Late"?AttendanceStatus.Late:AttendanceStatus.Present,
                    Notes=status=="Incomplete"?"Synthetic missing OUT punch":"Synthetic E2E attendance"
                });
            }
        }

        if ((eIndex+1)%5==0)
            db.LeaveRequests.Add(new LeaveRequest { EmployeeId=emp.Id,LeaveType=LeaveType.Annual,Status=LeaveStatus.Approved,
                FromDate=workDates[1],ToDate=workDates[1],Reason="Synthetic annual leave" });
        if ((eIndex+1)%7==0)
            db.LeaveRequests.Add(new LeaveRequest { EmployeeId=emp.Id,LeaveType=LeaveType.Unpaid,Status=LeaveStatus.Approved,
                FromDate=workDates[2],ToDate=workDates[2],Reason="Synthetic unpaid leave" });
        var otDate=workDates[^2];
        var otHours=2m+((eIndex+1)%5);
        var requestId=await HrmsDatabase.ScalarAsync<int>(db,"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,RequestDate,FromDate,ToDate,Reason,Status,
 ManagerStatus,HrStatus,CreatedAt,CreatedBy,ReviewedBy,ReviewNote)
VALUES(@Emp,N'OvertimeRequest',@D,@D,@D,@Reason,N'Approved',N'Approved',N'Approved',SYSUTCDATETIME(),N'e2e',N'e2e',N'Synthetic approved overtime');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",cmd=>
        {
            HrmsDatabase.AddParameter(cmd,"@Emp",emp.Id);
            HrmsDatabase.AddParameter(cmd,"@D",otDate.ToDateTime(TimeOnly.MinValue));
            HrmsDatabase.AddParameter(cmd,"@Reason",$"Synthetic overtime {otHours:0.##}h");
        });
        await PayrollTransactionStore.SaveAsync(db,scope,new PayrollTransactionStore.Transaction
        {
            EmployeeId=emp.Id,Year=Year,Month=Month,TxType=PayrollTransactionStore.Overtime,
            ItemName="Approved overtime",Hours=otHours,RateFactor=scenario.OvertimeFactor,Amount=0m,Taxable=true,
            PaymentType="InSalary",TransactionDate=otDate,Status="Approved",Source="OvertimeRequest",
            Note=$"Approved SelfServiceRequest #{requestId}"
        },"e2e");
        if ((eIndex+1)%4==0)
            await PayrollTransactionStore.SaveAsync(db,scope,new PayrollTransactionStore.Transaction
            {
                EmployeeId=emp.Id,Year=Year,Month=Month,TxType=PayrollTransactionStore.Income,
                ItemName="Performance bonus",Amount=50000m+companyIndex*5000m,Taxable=true,
                PaymentType="InSalary",TransactionDate=workDates[^3],Status="Approved",Source="Synthetic E2E"
            },"e2e");
        if ((eIndex+1)%6==0)
            await PayrollTransactionStore.SaveAsync(db,scope,new PayrollTransactionStore.Transaction
            {
                EmployeeId=emp.Id,Year=Year,Month=Month,TxType=PayrollTransactionStore.Deduction,
                ItemName="Other deduction",Amount=25000m+companyIndex*2500m,Taxable=false,
                PaymentType="InSalary",TransactionDate=workDates[^4],Status="Approved",Source="Synthetic E2E"
            },"e2e");
    }
    await db.SaveChangesAsync();

    await MonthAttendanceStore.BuildMonthAsync(db,scope,Year,Month,company.Id);
    var monthRows=await MonthAttendanceStore.ListAsync(db,scope,Year,Month);
    var approval=await MonthAttendanceStore.ApproveWithGateAsync(db,scope,monthRows.Select(x=>x.Id).ToArray());
    var locked=await MonthAttendanceStore.LockAsync(db,scope,monthRows.Select(x=>x.Id).ToArray());
    if (approval.Blocked>0 || locked!=25)
        throw new InvalidOperationException($"Attendance lock failed for {scenario.Code}: approved={approval.Approved}, blocked={approval.Blocked}, locked={locked}");
    var created=await PayrollRunStore.CreateRunAsync(db,scope,company.Id,Year,Month);
    if (!created.Ok) throw new InvalidOperationException($"Run create failed {scenario.Code}: {created.Message}");
    var calculated=await PayrollRunStore.CalculateAsync(db,created.RunId,"e2e-scenario");
    if (!calculated.Ok) throw new InvalidOperationException($"Run calculate failed {scenario.Code}: {calculated.Message}");
    var lines=await PayrollRunStore.ListLinesAsync(db,created.RunId);
    if (lines.Count!=25) throw new InvalidOperationException($"{scenario.Code}: expected 25 payroll lines, got {lines.Count}");

    var first=lines.OrderBy(x=>x.EmployeeNo).First();
    var expectedFactor=Math.Round(1m-scenario.AbsenceFactor/attendancePeriod.DayCount,6);
    if (Math.Abs(Math.Round(first.AttendanceFactor,6)-expectedFactor)>0.000001m)
        throw new InvalidOperationException($"{scenario.Code}: attendance factor {first.AttendanceFactor} != {expectedFactor}");
    var firstBasic=650000m+companyIndex*125000m+27500m;
    var expectedMissing=MissingPunchPayrollPolicy.Calculate(firstBasic/attendancePeriod.DayCount,1,25m);
    var missingComponent=first.Components.FirstOrDefault(x=>x.Kind=="MissingPunch")?.Amount ?? 0m;
    if (Math.Abs(missingComponent-expectedMissing)>0.02m)
        throw new InvalidOperationException($"{scenario.Code}: missing punch {missingComponent} != {expectedMissing}");
    if (!first.Components.Any(x=>x.Kind=="Overtime")) throw new InvalidOperationException($"{scenario.Code}: overtime missing");
    if (lines.Sum(x=>x.TaxAmount)<=0m) throw new InvalidOperationException($"{scenario.Code}: tax was not calculated");
    if (lines.Sum(x=>x.GosiEmployee)<=0m || lines.Sum(x=>x.GosiCompany)<=0m)
        throw new InvalidOperationException($"{scenario.Code}: GOSI was not calculated");

    var lockRun=await PayrollRunStore.LockAsync(db,created.RunId);
    if (!lockRun.Item1) throw new InvalidOperationException($"{scenario.Code}: payroll lock failed: {lockRun.Item2}");
    expectedByCompany[company.Id]=new CompanyExpectation(
        scenario.Code,company.Id,created.RunId,attendancePeriod.From,attendancePeriod.To,
        scenario.AbsenceFactor,25m,scenario.OvertimeFactor,lines.Count,
        lines.Sum(x=>x.GrossSalary),lines.Sum(x=>x.TaxAmount),lines.Sum(x=>x.GosiEmployee),
        lines.Sum(x=>x.GosiCompany),lines.Sum(x=>x.OtherDeductions),lines.Sum(x=>x.NetSalary),
        first.AttendanceFactor,missingComponent);

    Console.WriteLine($"OK {scenario.Code}: period {attendancePeriod.From:yyyy-MM-dd}..{attendancePeriod.To:yyyy-MM-dd}, " +
        $"employees={lines.Count}, gross={lines.Sum(x=>x.GrossSalary):N2}, net={lines.Sum(x=>x.NetSalary):N2}");
}

await VerifyScenarioAsync(db,expectedByCompany,allEmployeeIds);
Console.WriteLine("SCENARIO_OK");
Console.WriteLine($"Companies={expectedByCompany.Count}; Employees={allEmployeeIds.Count}; PayrollRuns={expectedByCompany.Count}");
foreach (var x in expectedByCompany.Values.OrderBy(x=>x.Code))
    Console.WriteLine($"{x.Code}|Run={x.RunId}|Cutoff={x.From:yyyy-MM-dd}:{x.To:yyyy-MM-dd}|AbsFactor={x.AbsenceFactor}|Missing={x.MissingPercent}%|OT={x.OvertimeFactor}|Lines={x.Lines}|Gross={x.Gross:N2}|Tax={x.Tax:N2}|GosiEmp={x.GosiEmp:N2}|GosiCo={x.GosiCo:N2}|OtherDed={x.OtherDed:N2}|Net={x.Net:N2}|FirstFactor={x.FirstFactor:0.######}|FirstMissing={x.FirstMissing:N2}");
static async Task CleanBootstrapAsync(ApplicationDbContext db)
{
    await HrmsDatabase.ExecuteAsync(db,"""
DELETE d FROM DayAttendances d INNER JOIN Employees e ON e.Id=d.EmployeeId WHERE e.EmployeeNo IN (N'E2E-001',N'E2E-002');
DELETE a FROM AttendanceRecords a INNER JOIN Employees e ON e.Id=a.EmployeeId WHERE e.EmployeeNo IN (N'E2E-001',N'E2E-002');
DELETE t FROM EmployeeTasks t INNER JOIN Employees e ON e.Id=t.EmployeeId WHERE e.EmployeeNo IN (N'E2E-001',N'E2E-002');
DELETE f FROM EmployeeFinancialInfos f INNER JOIN Employees e ON e.Id=f.EmployeeId WHERE e.EmployeeNo IN (N'E2E-001',N'E2E-002');
DELETE FROM Employees WHERE EmployeeNo IN (N'E2E-001',N'E2E-002');
DELETE FROM Departments WHERE Code IN (N'E2E-DA',N'E2E-DB');
DELETE FROM Branches WHERE Code IN (N'E2E-BA',N'E2E-BB');
DELETE FROM Companies WHERE Code IN (N'E2E-A',N'E2E-B');
""");
}

static async Task<int> SaveTaxAsync(ApplicationDbContext db,CompanyScope scope,int companyId,Scenario s)
{
    var cuts=new decimal[]{0m,250000m,500000m,1000000m};
    var tos=new decimal?[]{250000m,500000m,1000000m,null};
    var profile=new PayrollConfigStore.TaxProfile
    { CompanyId=companyId,Name=$"{s.Code} TAX",ExemptionAmount=s.TaxExemption,IsActive=true,SortOrder=1 };
    for(var i=0;i<4;i++) profile.Brackets.Add(new PayrollConfigStore.TaxBracket
    { FromAmount=cuts[i],ToAmount=tos[i],Rate=s.TaxRates[i] });
    return await PayrollConfigStore.SaveTaxProfileAsync(db,scope,profile);
}
