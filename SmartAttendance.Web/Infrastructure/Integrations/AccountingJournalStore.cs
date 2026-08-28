using System.Security.Cryptography;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Integrations;

public static class AccountingJournalStore
{
    public static async Task<AccountingJournalAdapter.Journal?> BuildForRunAsync(
        ApplicationDbContext db, CompanyScope scope, int runId)
    {
        var run = (await HrmsDatabase.QueryAsync(db, $"""
SELECT Id,CompanyId,BatchNo,[Year],[Month],Status
FROM PayrollRuns
WHERE Id=@Id AND CompanyId IS NOT NULL AND {scope.ToSqlPredicate("CompanyId")}
  AND Status IN (N'Issued',N'PayslipSent');
""", command => HrmsDatabase.AddParameter(command, "@Id", runId),
            reader => new
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
                BatchNo = HrmsDatabase.GetString(reader, "BatchNo"),
                Year = HrmsDatabase.GetInt(reader, "Year"),
                Month = HrmsDatabase.GetInt(reader, "Month")
            })).FirstOrDefault();
        if (run is null || !scope.Allows(run.CompanyId)) return null;

        var totals = (await HrmsDatabase.QueryAsync(db, """
SELECT ISNULL(SUM(GrossSalary),0) Gross,ISNULL(SUM(TaxAmount),0) Tax,
       ISNULL(SUM(GosiEmployee),0) GosiEmployee,ISNULL(SUM(GosiCompany),0) GosiCompany,
       ISNULL(SUM(OtherDeductions),0) OtherDeductions,ISNULL(SUM(NetSalary),0) Net
FROM PayrollRunLines WHERE RunId=@RunId;
""", command => HrmsDatabase.AddParameter(command, "@RunId", runId),
            reader => new AccountingJournalAdapter.Totals(
                GetDecimal(reader, "Gross"), GetDecimal(reader, "Tax"),
                GetDecimal(reader, "GosiEmployee"), GetDecimal(reader, "GosiCompany"),
                GetDecimal(reader, "OtherDeductions"), GetDecimal(reader, "Net")))).Single();

        var mappings = await AccountingMappingStore.ListAsync(db, scope, run.CompanyId);
        var accounts = mappings.Select(mapping => new AccountingJournalAdapter.Account(
            mapping.AccountRole, mapping.AccountCode, mapping.AccountName)).ToList();
        var postingDate = new DateOnly(run.Year, run.Month, DateTime.DaysInMonth(run.Year, run.Month));
        return AccountingJournalAdapter.Build(
            run.Id, run.BatchNo, run.Year, run.Month, postingDate, totals, accounts);
    }

    public static Task RecordExportAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, int runId,
        string format, byte[] payload, string userName)
    {
        if (companyId <= 0 || !scope.Allows(companyId))
            throw new UnauthorizedAccessException("Accounting export company is outside the effective scope.");
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        return HrmsDatabase.ExecuteAsync(db, $"""
INSERT INTO AccountingJournalExports
    (CompanyId,RunId,Format,PayloadHash,ExportedBy,ExportedAt)
SELECT @CompanyId,@RunId,@Format,@Hash,@User,SYSUTCDATETIME()
WHERE EXISTS (
    SELECT 1 FROM PayrollRuns r WHERE r.Id=@RunId AND r.CompanyId=@CompanyId
      AND {scope.ToSqlPredicate("r.CompanyId")});
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@RunId", runId);
            HrmsDatabase.AddParameter(command, "@Format", format);
            HrmsDatabase.AddParameter(command, "@Hash", hash);
            HrmsDatabase.AddParameter(command, "@User", userName);
        });
    }

    private static decimal GetDecimal(System.Data.Common.DbDataReader reader, string name) =>
        reader[name] is decimal value ? value : Convert.ToDecimal(reader[name]);
}
