using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Integrations;

public static class AccountingMappingStore
{
    public sealed record Mapping(int Id, int CompanyId, string AccountRole, string AccountCode, string AccountName);

    public static Task<List<Mapping>> ListAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId)
    {
        Guard(scope, companyId);
        return HrmsDatabase.QueryAsync(db, $"""
SELECT Id,CompanyId,AccountRole,AccountCode,AccountName
FROM AccountingAccountMappings
WHERE CompanyId=@CompanyId AND {scope.ToSqlPredicate("CompanyId")}
ORDER BY AccountRole;
""", command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            reader => new Mapping(
                HrmsDatabase.GetInt(reader, "Id"), HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetString(reader, "AccountRole"), HrmsDatabase.GetString(reader, "AccountCode"),
                HrmsDatabase.GetString(reader, "AccountName")));
    }

    public static Task SaveAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId,
        string role, string code, string name)
    {
        Guard(scope, companyId);
        if (!AccountingJournalAdapter.RequiredRoles.Contains(role, StringComparer.Ordinal))
            throw new ArgumentException("Unknown accounting role.", nameof(role));
        return HrmsDatabase.ExecuteAsync(db, $"""
MERGE AccountingAccountMappings WITH (HOLDLOCK) AS target
USING (SELECT @CompanyId CompanyId,@Role AccountRole) source
ON target.CompanyId=source.CompanyId AND target.AccountRole=source.AccountRole
WHEN MATCHED AND {scope.ToSqlPredicate("target.CompanyId")} THEN
  UPDATE SET AccountCode=@Code,AccountName=@Name,UpdatedAt=SYSUTCDATETIME()
WHEN NOT MATCHED THEN
  INSERT(CompanyId,AccountRole,AccountCode,AccountName,CreatedAt)
  VALUES(@CompanyId,@Role,@Code,@Name,SYSUTCDATETIME());
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@Role", role);
            HrmsDatabase.AddParameter(command, "@Code", code.Trim());
            HrmsDatabase.AddParameter(command, "@Name", name.Trim());
        });
    }

    private static void Guard(CompanyScope scope, int companyId)
    {
        if (companyId <= 0 || !scope.Allows(companyId))
            throw new UnauthorizedAccessException("Accounting mapping company is outside the effective scope.");
    }
}
