using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class PeopleIdentityDuplicateStore
{
    private sealed record CandidateRow(
        int EmployeeId,
        int CompanyId,
        int BranchId,
        int DepartmentId,
        string EmployeeNo,
        string FullName,
        string DocumentType,
        string NormalizedDocumentNumber,
        bool IsActive);

    public static async Task<IdentityDuplicateResult> FindAsync(
        ApplicationDbContext db,
        PeopleDataScope requesterScope,
        int currentCompanyId,
        string documentType,
        string? documentNumber)
    {
        ArgumentNullException.ThrowIfNull(requesterScope);

        var normalized = IdentityDocumentNormalizer.NormalizeNumber(documentNumber);
        var settings = await PeopleAiSettingsStore.GetAsync(db, currentCompanyId);

        if (normalized.Length == 0)
        {
            return new IdentityDuplicateResult(
                documentType,
                normalized,
                settings.DuplicateAction,
                []);
        }

        await PeopleIdentityBootstrap.EnsureLegacyIdentityRowsAsync(db);

        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT d.EmployeeId, d.CompanyId, e.BranchId, e.DepartmentId,
       ISNULL(e.EmployeeNo, '') AS EmployeeNo,
       ISNULL(e.FullName, '') AS FullName,
       d.DocumentType, d.NormalizedDocumentNumber,
       ISNULL(e.IsActive, 0) AS IsActive
FROM dbo.EmployeeIdentityDocuments d
JOIN dbo.Employees e ON e.Id = d.EmployeeId
WHERE d.DocumentType = @DocumentType
  AND d.NormalizedDocumentNumber = @NormalizedNumber
  AND d.IsCurrent = 1
  AND ISNULL(e.IsDeleted, 0) = 0;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@DocumentType", documentType);
                HrmsDatabase.AddParameter(command, "@NormalizedNumber", normalized);
            },
            reader => new CandidateRow(
                HrmsDatabase.GetInt(reader, "EmployeeId"),
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetInt(reader, "BranchId"),
                HrmsDatabase.GetInt(reader, "DepartmentId"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "FullName"),
                HrmsDatabase.GetString(reader, "DocumentType"),
                HrmsDatabase.GetString(reader, "NormalizedDocumentNumber"),
                HrmsDatabase.GetBool(reader, "IsActive")));

        var candidates = new List<IdentityDuplicateCandidate>();
        foreach (var row in rows.GroupBy(x => x.EmployeeId).Select(x => x.First()))
        {
            var visible = requesterScope.AllowsEmployee(
                row.EmployeeId,
                row.CompanyId,
                row.BranchId,
                row.DepartmentId);

            var included = settings.DuplicateScope switch
            {
                PeopleAiDuplicateScope.CurrentCompany =>
                    row.CompanyId == currentCompanyId && visible,
                PeopleAiDuplicateScope.AuthorizedCompanies => visible,
                PeopleAiDuplicateScope.WholeTenant => true,
                _ => visible
            };

            if (!included)
            {
                continue;
            }

            candidates.Add(new IdentityDuplicateCandidate(
                visible ? row.EmployeeId : 0,
                visible ? row.CompanyId : 0,
                visible ? row.EmployeeNo : string.Empty,
                visible ? row.FullName : "مطابقة موجودة ضمن نطاق غير مصرح بعرضه",
                row.DocumentType,
                row.NormalizedDocumentNumber,
                visible ? row.IsActive : true,
                visible));
        }

        return new IdentityDuplicateResult(
            documentType,
            normalized,
            settings.DuplicateAction,
            candidates);
    }
}
