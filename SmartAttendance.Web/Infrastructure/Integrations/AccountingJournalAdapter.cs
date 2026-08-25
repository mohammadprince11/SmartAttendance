using System.Text;
using System.Text.Json;
using System.Globalization;

namespace SmartAttendance.Web.Infrastructure.Integrations;

/// <summary>Adapter مركزي يحول إجماليات المسير إلى قيد مزدوج متوازن.</summary>
public static class AccountingJournalAdapter
{
    public const string PayrollExpense = "PayrollExpense";
    public const string GosiExpense = "GosiExpense";
    public const string TaxPayable = "TaxPayable";
    public const string GosiPayable = "GosiPayable";
    public const string OtherDeductionPayable = "OtherDeductionPayable";
    public const string NetPayable = "NetPayable";

    public static readonly string[] RequiredRoles =
    [
        PayrollExpense, GosiExpense, TaxPayable, GosiPayable,
        OtherDeductionPayable, NetPayable
    ];

    public sealed record Account(string Role, string Code, string Name);
    public sealed record Totals(
        decimal Gross, decimal Tax, decimal GosiEmployee, decimal GosiCompany,
        decimal OtherDeductions, decimal Net);
    public sealed record Line(
        string AccountRole, string AccountCode, string AccountName,
        decimal Debit, decimal Credit, string Memo);
    public sealed record Journal(
        int RunId, string BatchNo, int Year, int Month, DateOnly PostingDate,
        IReadOnlyList<Line> Lines)
    {
        public decimal TotalDebit => Lines.Sum(line => line.Debit);
        public decimal TotalCredit => Lines.Sum(line => line.Credit);
        public bool IsBalanced => TotalDebit == TotalCredit;
    }

    public static Journal Build(
        int runId, string batchNo, int year, int month, DateOnly postingDate,
        Totals totals, IReadOnlyCollection<Account> accounts)
    {
        var byRole = accounts.ToDictionary(account => account.Role, StringComparer.OrdinalIgnoreCase);
        var missing = RequiredRoles.Where(role => !byRole.ContainsKey(role)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Missing accounting mappings: " + string.Join(", ", missing));

        decimal R(decimal value) => decimal.Round(value, 3, MidpointRounding.AwayFromZero);
        var gross = R(totals.Gross);
        var gosiCompany = R(totals.GosiCompany);
        var lines = new List<Line>();
        Add(PayrollExpense, gross, 0, $"Payroll gross {month:00}/{year}");
        Add(GosiExpense, gosiCompany, 0, $"Employer social security {month:00}/{year}");
        Add(TaxPayable, 0, R(totals.Tax), $"Payroll tax {month:00}/{year}");
        Add(GosiPayable, 0, R(totals.GosiEmployee + totals.GosiCompany), $"Social security payable {month:00}/{year}");
        Add(OtherDeductionPayable, 0, R(totals.OtherDeductions), $"Other payroll deductions {month:00}/{year}");
        Add(NetPayable, 0, R(totals.Net), $"Net payroll payable {month:00}/{year}");
        lines = lines.Where(line => line.Debit != 0 || line.Credit != 0).ToList();

        var debit = lines.Sum(line => line.Debit);
        var credit = lines.Sum(line => line.Credit);
        if (debit != credit)
            throw new InvalidOperationException($"Accounting journal is not balanced. Debit={debit:0.###}; Credit={credit:0.###}.");
        return new Journal(runId, batchNo, year, month, postingDate, lines);

        void Add(string role, decimal debit, decimal credit, string memo)
        {
            var account = byRole[role];
            lines.Add(new Line(role, account.Code, account.Name, debit, credit, memo));
        }
    }

    public static byte[] Csv(Journal journal)
    {
        static string Cell(string value)
        {
            if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@') value = "'" + value;
            return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
                ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        }
        var text = new StringBuilder("Batch,PostingDate,AccountCode,AccountName,Debit,Credit,Memo\r\n");
        foreach (var line in journal.Lines)
            text.AppendJoin(',', Cell(journal.BatchNo), journal.PostingDate.ToString("yyyy-MM-dd"),
                Cell(line.AccountCode), Cell(line.AccountName), line.Debit.ToString("0.000", CultureInfo.InvariantCulture),
                line.Credit.ToString("0.000", CultureInfo.InvariantCulture), Cell(line.Memo)).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
    }

    public static byte[] Json(Journal journal) =>
        JsonSerializer.SerializeToUtf8Bytes(journal, new JsonSerializerOptions { WriteIndented = true });
}
