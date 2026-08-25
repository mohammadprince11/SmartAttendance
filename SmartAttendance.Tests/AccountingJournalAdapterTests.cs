using System.Text;
using System.Text.Json;
using SmartAttendance.Web.Infrastructure.Integrations;

namespace SmartAttendance.Tests;

public sealed class AccountingJournalAdapterTests
{
    private static readonly AccountingJournalAdapter.Account[] Accounts =
    [
        new(AccountingJournalAdapter.PayrollExpense, "5100", "Payroll expense"),
        new(AccountingJournalAdapter.GosiExpense, "5110", "Employer GOSI expense"),
        new(AccountingJournalAdapter.TaxPayable, "2100", "Tax payable"),
        new(AccountingJournalAdapter.GosiPayable, "2110", "GOSI payable"),
        new(AccountingJournalAdapter.OtherDeductionPayable, "2120", "Other deductions"),
        new(AccountingJournalAdapter.NetPayable, "2200", "Net salaries payable")
    ];

    [Fact]
    public void Build_CreatesBalancedDoubleEntryJournal()
    {
        var journal = AccountingJournalAdapter.Build(
            7, "PAY-2026-08", 2026, 8, new DateOnly(2026, 8, 31),
            new AccountingJournalAdapter.Totals(1000, 100, 50, 120, 50, 800), Accounts);

        Assert.True(journal.IsBalanced);
        Assert.Equal(1120, journal.TotalDebit);
        Assert.Equal(1120, journal.TotalCredit);
        Assert.Equal(6, journal.Lines.Count);
        Assert.Single(journal.Lines, line =>
            line.AccountRole == AccountingJournalAdapter.NetPayable && line.Credit == 800);
    }

    [Fact]
    public void Build_RejectsMissingMappingOrUnbalancedPayrollTotals()
    {
        Assert.Throws<InvalidOperationException>(() => AccountingJournalAdapter.Build(
            1, "B", 2026, 8, new DateOnly(2026, 8, 31),
            new AccountingJournalAdapter.Totals(100, 0, 0, 0, 0, 100), Accounts[..^1]));
        Assert.Throws<InvalidOperationException>(() => AccountingJournalAdapter.Build(
            1, "B", 2026, 8, new DateOnly(2026, 8, 31),
            new AccountingJournalAdapter.Totals(100, 10, 0, 0, 0, 100), Accounts));
    }

    [Fact]
    public void CsvAndJson_AreMachineReadableAndPreserveAuditFields()
    {
        var journal = AccountingJournalAdapter.Build(
            7, "PAY,08", 2026, 8, new DateOnly(2026, 8, 31),
            new AccountingJournalAdapter.Totals(1000, 100, 50, 120, 50, 800), Accounts);
        var csv = AccountingJournalAdapter.Csv(journal);
        var bom = Encoding.UTF8.GetPreamble();
        Assert.Equal(bom, csv[..bom.Length]);
        Assert.Contains("\"PAY,08\"", Encoding.UTF8.GetString(csv[bom.Length..]), StringComparison.Ordinal);

        using var json = JsonDocument.Parse(AccountingJournalAdapter.Json(journal));
        Assert.Equal(7, json.RootElement.GetProperty("RunId").GetInt32());
        Assert.Equal(1120, json.RootElement.GetProperty("TotalDebit").GetDecimal());
        Assert.True(json.RootElement.GetProperty("IsBalanced").GetBoolean());
    }

    [Fact]
    public void AccountingContract_IsTenantScopedAuditedAndAvailableOnlyAfterIssue()
    {
        var root = FindRoot();
        var store = Read(root, "SmartAttendance.Web", "Infrastructure", "Integrations", "AccountingMappingStore.cs");
        var journalStore = Read(root, "SmartAttendance.Web", "Infrastructure", "Integrations", "AccountingJournalStore.cs");
        var page = Read(root, "SmartAttendance.Web", "Pages", "Payroll", "RunDetail.cshtml.cs");
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");

        Assert.Contains("scope.Allows(companyId)", store, StringComparison.Ordinal);
        Assert.Contains("scope.ToSqlPredicate", store, StringComparison.Ordinal);
        Assert.Contains("Status IN (N'Issued',N'PayslipSent')", journalStore, StringComparison.Ordinal);
        Assert.Contains("AccountingJournalExports", journalStore, StringComparison.Ordinal);
        Assert.Contains("accounting.journal.exported", page, StringComparison.Ordinal);
        Assert.Contains("20260826-07-accounting-adapter", migration, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
