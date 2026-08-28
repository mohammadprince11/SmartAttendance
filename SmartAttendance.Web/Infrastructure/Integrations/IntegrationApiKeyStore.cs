using System.Security.Cryptography;
using System.Text;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Integrations;

/// <summary>مفاتيح machine-to-machine مجزّأة، مستقلة تماماً عن توكنات جلسات الموظفين.</summary>
public static class IntegrationApiKeyStore
{
    public sealed record KeyInfo(
        int Id, int CompanyId, string Name, string ScopesCsv, bool IsActive,
        DateTime? ExpiresAt, DateTime? LastUsedAt);

    public sealed record Identity(int Id, int CompanyId, string Name, string ScopesCsv)
    {
        public bool HasScope(string scope) => ScopesCsv.Split(
                ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(scope, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<string> IssueAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, string name,
        string scopesCsv, DateTime? expiresAt = null)
    {
        if (companyId <= 0 || !scope.Allows(companyId))
            throw new UnauthorizedAccessException("Integration key company is outside the effective scope.");
        var token = "zyn_" + Base64Url(RandomNumberGenerator.GetBytes(32));
        await HrmsDatabase.ExecuteAsync(db, """
INSERT INTO IntegrationApiKeys
    (CompanyId, Name, TokenHash, ScopesCsv, IsActive, ExpiresAt, CreatedAt)
VALUES (@CompanyId,@Name,@Hash,@Scopes,1,@Expires,SYSUTCDATETIME());
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@Name", name.Trim());
            HrmsDatabase.AddParameter(command, "@Hash", Hash(token));
            HrmsDatabase.AddParameter(command, "@Scopes", scopesCsv);
            HrmsDatabase.AddParameter(command, "@Expires", expiresAt is null ? DBNull.Value : expiresAt.Value);
        });
        return token;
    }

    public static Task<List<KeyInfo>> ListAsync(ApplicationDbContext db, CompanyScope scope, int companyId)
    {
        if (companyId <= 0 || !scope.Allows(companyId))
            throw new UnauthorizedAccessException("Integration key company is outside the effective scope.");
        return HrmsDatabase.QueryAsync(db, $"""
SELECT Id,CompanyId,Name,ScopesCsv,IsActive,ExpiresAt,LastUsedAt
FROM IntegrationApiKeys
WHERE CompanyId=@CompanyId AND {scope.ToSqlPredicate("CompanyId")}
ORDER BY Name,Id;
""", command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            reader => new KeyInfo(
                HrmsDatabase.GetInt(reader, "Id"), HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetString(reader, "Name"), HrmsDatabase.GetString(reader, "ScopesCsv"),
                HrmsDatabase.GetBool(reader, "IsActive"), HrmsDatabase.GetDateTime(reader, "ExpiresAt"),
                HrmsDatabase.GetDateTime(reader, "LastUsedAt")));
    }

    public static async Task<Identity?> ValidateAsync(
        ApplicationDbContext db, string token, string requiredScope)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return null;
        var rows = await HrmsDatabase.QueryAsync(db, """
SELECT TOP 1 Id,CompanyId,Name,ScopesCsv
FROM IntegrationApiKeys
WHERE TokenHash=@Hash AND IsActive=1
  AND (ExpiresAt IS NULL OR ExpiresAt>SYSUTCDATETIME());
""", command => HrmsDatabase.AddParameter(command, "@Hash", Hash(token)),
            reader => new Identity(
                HrmsDatabase.GetInt(reader, "Id"), HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetString(reader, "Name"), HrmsDatabase.GetString(reader, "ScopesCsv")));
        var identity = rows.FirstOrDefault();
        if (identity is null || !identity.HasScope(requiredScope)) return null;
        await HrmsDatabase.ExecuteAsync(db,
            "UPDATE IntegrationApiKeys SET LastUsedAt=SYSUTCDATETIME() WHERE Id=@Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", identity.Id));
        return identity;
    }

    public static Task RevokeAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, int id)
    {
        if (companyId <= 0 || !scope.Allows(companyId))
            throw new UnauthorizedAccessException("Integration key company is outside the effective scope.");
        return HrmsDatabase.ExecuteAsync(db, $"""
UPDATE IntegrationApiKeys SET IsActive=0,RevokedAt=SYSUTCDATETIME()
WHERE Id=@Id AND CompanyId=@CompanyId AND {scope.ToSqlPredicate("CompanyId")};
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Id", id);
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
        });
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
