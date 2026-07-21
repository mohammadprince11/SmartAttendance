using SmartAttendance.Domain.Common;

namespace SmartAttendance.Domain.Entities;

/// <summary>
/// Financial / payroll setup for an employee — one row per employee (1:1). Mirrors
/// the Kayan "المعلومات المالية" form (salary, social security, tax, end-of-service,
/// calculation control, and payment/bank details). Kept separate from
/// <see cref="Employee"/> so the payroll module can own it without bloating the core
/// employee record; all fields are optional (light validation, like Kayan).
/// </summary>
public class EmployeeFinancialInfo : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    // --- الراتب والعملة ---
    /// <summary>ISO currency code (IQD, USD, EUR…).</summary>
    public string? Currency { get; set; }

    /// <summary>Salary scale / grade name (سلم الرواتب) — optional.</summary>
    public string? SalaryScale { get; set; }

    public decimal? BasicSalary { get; set; }

    public decimal? DailySalary { get; set; }

    public decimal? HourlyRate { get; set; }

    // --- الضمان الاجتماعي ---
    public string? SocialSecurityType { get; set; }

    public decimal? SocialSecuritySalary { get; set; }

    public string? SocialSecurityNo { get; set; }

    public DateOnly? SocialSecurityJoinDate { get; set; }

    /// <summary>Previous social-security subscription months (opening balance).</summary>
    public int? SocialSecurityPreviousMonths { get; set; }

    public int? RetirementAge { get; set; }

    // --- الضريبة ---
    public string? TaxFile { get; set; }

    public string? TaxNo { get; set; }

    /// <summary>Opening tax year for the previous-balance figures below.</summary>
    public int? TaxYear { get; set; }

    public decimal? PreviousTaxSalary { get; set; }

    public decimal? PreviousTaxExemption { get; set; }

    public decimal? PreviousTaxAmount { get; set; }

    public decimal? PreviousMinSalary { get; set; }

    public decimal? PreviousMinTaxAmount { get; set; }

    public int? PreviousTaxMonths { get; set; }

    // --- مكافأة نهاية الخدمة ---
    public string? EndOfServiceSetup { get; set; }

    /// <summary>Compute end-of-service reward based on a configured condition.</summary>
    public bool EndOfServiceCompute { get; set; }

    public DateOnly? EndOfServiceStartDate { get; set; }

    public DateOnly? EndOfServiceDueDate { get; set; }

    // --- تحكّم الاحتساب ---
    /// <summary>Calculate previous salaries for a newly added employee.</summary>
    public bool CalcPreviousSalaries { get; set; }

    /// <summary>Exclude the employee from payroll calculation.</summary>
    public bool StopSalaryCalc { get; set; }

    public DateOnly? AdditionalSalaryStartDate { get; set; }

    // --- الدفع والبنك ---
    /// <summary>Cash / cheque / bank.</summary>
    public string? PaymentMethod { get; set; }

    public string? BankName { get; set; }

    public string? BankBranch { get; set; }

    public string? UnitNo { get; set; }

    public string? Iban { get; set; }

    public string? CardNo { get; set; }

    public string? MxpAccount { get; set; }

    /// <summary>Bank commitment / salary-transfer obligation (التزام بنكي).</summary>
    public bool BankCommitment { get; set; }

    public string? BankCommitmentFileName { get; set; }

    public string? BankCommitmentFilePath { get; set; }

    public string? AttachmentName { get; set; }

    public string? AttachmentPath { get; set; }
}
