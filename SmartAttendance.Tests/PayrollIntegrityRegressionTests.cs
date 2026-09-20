using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class PayrollIntegrityRegressionTests
{
    [Fact]
    public void Service_end_days_reduce_salary_factor_without_removing_employee()
    {
        var policy = new AttendanceSalaryLink.Policy(
            AttendanceSalaryLink.Lenient, 1m, false, 8m, 30);

        var decision = AttendanceSalaryLink.Evaluate(
            policy, workDays: 30, presentDays: 20, absentDays: 0,
            workedHours: 160m, preEmploymentUnpaidDays: 0,
            postEmploymentUnpaidDays: 10);

        Assert.True(decision.Include);
        Assert.Equal(decimal.Round(20m / 30m, 6), decimal.Round(decision.Factor, 6));
        Assert.NotNull(decision.Note);
        Assert.Contains("10", decision.Note!);
    }

    [Theory]
    [InlineData(PayrollTransactionStore.Income, SmartAttendance.Domain.Enums.PayrollCutoffType.Additions)]
    [InlineData(PayrollTransactionStore.Deduction, SmartAttendance.Domain.Enums.PayrollCutoffType.Deductions)]
    [InlineData(PayrollTransactionStore.Overtime, SmartAttendance.Domain.Enums.PayrollCutoffType.Overtime)]
    [InlineData(PayrollTransactionStore.SalaryDays, SmartAttendance.Domain.Enums.PayrollCutoffType.SalaryChanges)]
    [InlineData(PayrollTransactionStore.LeaveEncashment, SmartAttendance.Domain.Enums.PayrollCutoffType.Leaves)]
    public void Payroll_transaction_types_map_to_their_cutoff_policy(string txType, SmartAttendance.Domain.Enums.PayrollCutoffType expected) =>
        Assert.Equal(expected, PayrollTransactionStore.CutoffTypeFor(txType));

    [Fact]
    public void Payslip_email_contains_auditable_totals_and_components()
    {
        var run = new PayrollRunStore.PayrollRun { Year = 2026, Month = 9, BatchNo = "2026-9-N-1" };
        var line = new PayrollRunStore.PayrollLine
        {
            EmployeeNo = "0015",
            EmployeeName = "Test Employee",
            GrossSalary = 1_200_000m,
            TaxAmount = 50_000m,
            GosiEmployee = 60_000m,
            OtherDeductions = 20_000m,
            NetSalary = 1_070_000m,
            PayrollCurrency = "IQD",
            Components = new()
            {
                new PayrollRunStore.Component { ItemName = "Basic", Amount = 1_000_000m, IsAddition = true, Kind = "Basic" },
                new PayrollRunStore.Component { ItemName = "Allowance", Amount = 200_000m, IsAddition = true, Kind = "Allowance" },
                new PayrollRunStore.Component { ItemName = "Loan", Amount = 20_000m, IsAddition = false, Kind = "Deduction" }
            }
        };

        var body = PayrollPayslipDeliveryStore.BuildBody(run, line);
        Assert.Contains("0015", body);
        Assert.Contains("1,070,000.00", body);
        Assert.Contains("Allowance", body);
        Assert.Contains("Loan", body);
    }

    [Fact]
    public void Payroll_source_enforces_locked_attendance_and_company_cutoffs()
    {
        var root = FindRoot();
        var runStore = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs"));
        var periodPolicy = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "AttendancePeriodPolicy.cs"));
        var txStore = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollTransactionStore.cs"));

        Assert.Contains("m.Status = N'Locked'", runStore);
        Assert.DoesNotContain("m.Status IN (N'Approved',N'Locked')", runStore);
        Assert.Contains("p.CompanyId == companyId.Value", periodPolicy);
        Assert.Contains("p.Priority descending", periodPolicy);
        Assert.Contains("PayrollCutoffType.Penalties", runStore);
        Assert.Contains("PayrollCutoffType.Terminations", runStore);
        Assert.Contains("ResolveRunPeriodsAsync", txStore);
        Assert.Contains("t.TransactionDate BETWEEN @AddFrom AND @AddTo", txStore);
    }

    [Fact]
    public void Payroll_run_uses_one_company_rate_basis_for_day_and_hour_amounts()
    {
        var root = FindRoot();
        var runStore = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs"));

        Assert.Contains("var salaryDivisor = PayrollDivisorPolicy.Divisor(salaryDaysBasis, daysInPeriod);", runStore);
        Assert.Contains("var dailyRate = PayrollRateBasis.DailyRate(basic, salaryDivisor);", runStore);
        Assert.Contains("var hourlyRate = PayrollRateBasis.HourlyRate(dailyRate, standardDailyHours);", runStore);
        Assert.Contains("t.Days.Value * dailyRate", runStore);
        Assert.DoesNotContain("Math.Round(basic / 30m, 4)", runStore);
        Assert.DoesNotContain("Math.Round(dailyRate / 8m, 4)", runStore);
    }

    [Fact]
    public void Payroll_source_persists_deferred_deductions_and_validates_leave_encashment()
    {
        var root = FindRoot();
        var deferred = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollDeferredDeductionStore.cs"));
        var leavePolicy = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "LeaveEncashmentPolicy.cs"));
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Payroll", "LeaveEncashment.cshtml.cs"));

        Assert.Contains("PayrollDeferredDeductions", deferred);
        Assert.Contains("RecordResultAsync", deferred);
        Assert.Contains("AvailableAnnualDaysAsync", leavePolicy);
        Assert.Contains("ValidateAsync", page);
        Assert.Contains("SaveManyAsync", page);
        Assert.Contains("OnPostImportAsync", page);
    }

    [Fact]
    public void End_of_service_is_policy_driven_and_company_scoped()
    {
        var root = FindRoot();
        var store = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "EndOfServiceStore.cs"));
        var provision = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "ProvisionCalculator.cs"));
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Payroll", "EndOfService.cshtml.cs"));

        Assert.DoesNotContain("DefaultTiers", store);
        Assert.Contains("EndOfServicePolicy.Compute", store);
        Assert.Contains("ISNULL(e.CompanyId, 0) AS CompanyId", provision);
        Assert.DoesNotContain("ISNULL(b.CompanyId, 0) AS CompanyId", provision);
        Assert.Contains("EndOfServicePolicy.LoadAsync", provision);
        Assert.Contains("EndOfServicePolicy.LoadAsync", page);
        Assert.Contains("GratuityCalculationMode", store);
        Assert.Contains("GratuityWeeksPerYear", store);

        var migrator = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs"));
        Assert.Contains("20260920-07-payroll-end-of-service-audit-snapshot", migrator);
        Assert.Contains("GratuityBasisAmount", migrator);
        Assert.Contains("20260920-08-payroll-eos-offcycle-link", migrator);
        Assert.Contains("PayrollTransactionId", migrator);

        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", store);
        Assert.Contains("PaymentType = \"OutSalary\"", store);
        Assert.Contains("Source = \"EndOfService\"", store);

        var transactions = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollTransactionStore.cs"));
        Assert.Contains("ISNULL(PaymentType, N'InSalary') = N'InSalary'", transactions);
    }

    [Fact]
    public void Payslip_delivery_and_reversal_are_idempotent_by_contract()
    {
        var root = FindRoot();
        var delivery = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollPayslipDeliveryStore.cs"));
        var runStore = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs"));
        var runsPage = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Payroll", "Runs.cshtml.cs"));

        Assert.Contains("UX_PayrollPayslipDeliveries_RunEmployee", delivery);
        Assert.Contains("Status=N'Sending'", delivery);
        Assert.Contains("Status=N'PayslipSent'", delivery);
        Assert.Contains("PayrollPayslipDeliveryStore.SendRunAsync", runsPage);
        Assert.Contains("GetCompanyAsync", runsPage);
        Assert.Contains("IsolationLevel.Serializable", runStore);
        Assert.Contains("WITH (UPDLOCK,HOLDLOCK)", runStore);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
