using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تفويضات قرار الموافقة المؤقتة. المخطط يُدار حصراً بواسطة SqlSchemaMigrator؛
/// وكل قراءة وكتابة تحمل CompanyId صريحاً وتتحقق من المستخدمين قبل الحفظ.
/// </summary>
public static class ApprovalDelegationStore
{
    public sealed record Row(
        int Id, int CompanyId, string DelegatorUserName, string DelegateUserName,
        DateTime StartsAt, DateTime EndsAt, bool IsActive, string CreatedBy,
        DateTime CreatedAt, DateTime? RevokedAt, string? RevokedBy);

    public sealed record SaveResult(bool Ok, string Message, int Id = 0);

    public static async Task<List<Row>> ListAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int companyId)
    {
        Demand(scope, companyId);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT Id,CompanyId,DelegatorUserName,DelegateUserName,StartsAt,EndsAt,IsActive,
       CreatedBy,CreatedAt,RevokedAt,RevokedBy
FROM ApprovalDelegations
WHERE CompanyId=@CompanyId
ORDER BY IsActive DESC,EndsAt DESC,Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            reader => new Row(
                HrmsDatabase.GetInt(reader,"Id"), HrmsDatabase.GetInt(reader,"CompanyId"),
                HrmsDatabase.GetString(reader,"DelegatorUserName"), HrmsDatabase.GetString(reader,"DelegateUserName"),
                HrmsDatabase.GetDateTime(reader,"StartsAt") ?? DateTime.MinValue,
                HrmsDatabase.GetDateTime(reader,"EndsAt") ?? DateTime.MinValue,
                HrmsDatabase.GetBool(reader,"IsActive"), HrmsDatabase.GetString(reader,"CreatedBy"),
                HrmsDatabase.GetDateTime(reader,"CreatedAt") ?? DateTime.MinValue,
                HrmsDatabase.GetDateTime(reader,"RevokedAt"), HrmsDatabase.GetString(reader,"RevokedBy")));
    }

    public static async Task<SaveResult> CreateAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int companyId,
        string delegatorUserName, string delegateUserName, DateTime startsAtUtc,
        DateTime endsAtUtc, string createdBy)
    {
        Demand(scope, companyId);
        delegatorUserName=(delegatorUserName??string.Empty).Trim();
        delegateUserName=(delegateUserName??string.Empty).Trim();
        if (delegatorUserName.Length==0||delegateUserName.Length==0)
            return new(false,"اختر المفوِّض والمفوَّض إليه.");
        if (delegatorUserName.Equals(delegateUserName,StringComparison.OrdinalIgnoreCase))
            return new(false,"لا يمكن تفويض المستخدم لنفسه.");
        if (endsAtUtc<=startsAtUtc||endsAtUtc<=DateTime.UtcNow)
            return new(false,"نهاية التفويض يجب أن تكون بعد بدايته وفي المستقبل.");

        var validUsers=await HrmsDatabase.ScalarAsync<int>(dbContext,"""
SELECT COUNT(DISTINCT u.Id)
FROM SystemUsers u
INNER JOIN Employees e ON e.Id=u.EmployeeId AND ISNULL(e.IsDeleted,0)=0
WHERE ISNULL(u.IsDeleted,0)=0 AND u.IsActive=1 AND e.CompanyId=@CompanyId
  AND (u.UserName=@Delegator OR u.UserName=@Delegate);
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@CompanyId",companyId);
            HrmsDatabase.AddParameter(command,"@Delegator",delegatorUserName);
            HrmsDatabase.AddParameter(command,"@Delegate",delegateUserName);
        });
        if(validUsers!=2) return new(false,"كلا المستخدمين يجب أن يكونا نشطين ومرتبطين بموظفين من نفس الشركة.");

        var id=await HrmsDatabase.ScalarAsync<int>(dbContext,"""
IF EXISTS(SELECT 1 FROM ApprovalDelegations WITH(UPDLOCK,HOLDLOCK)
          WHERE CompanyId=@CompanyId AND DelegatorUserName=@Delegator AND IsActive=1
            AND StartsAt<@EndsAt AND EndsAt>@StartsAt)
BEGIN SELECT 0; RETURN; END;
INSERT INTO ApprovalDelegations(CompanyId,DelegatorUserName,DelegateUserName,StartsAt,EndsAt,IsActive,CreatedBy)
VALUES(@CompanyId,@Delegator,@Delegate,@StartsAt,@EndsAt,1,@CreatedBy);
SELECT CAST(SCOPE_IDENTITY() AS int);
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@CompanyId",companyId); HrmsDatabase.AddParameter(command,"@Delegator",delegatorUserName);
            HrmsDatabase.AddParameter(command,"@Delegate",delegateUserName); HrmsDatabase.AddParameter(command,"@StartsAt",startsAtUtc);
            HrmsDatabase.AddParameter(command,"@EndsAt",endsAtUtc); HrmsDatabase.AddParameter(command,"@CreatedBy",createdBy);
        });
        return id>0 ? new(true,"تم حفظ التفويض المؤقت.",id) : new(false,"يوجد تفويض نشط متداخل للمستخدم نفسه.");
    }

    public static async Task<bool> RevokeAsync(
        ApplicationDbContext dbContext, CompanyScope scope, int companyId, int id, string revokedBy)
    {
        Demand(scope,companyId);
        return await HrmsDatabase.ScalarAsync<int>(dbContext,"""
UPDATE ApprovalDelegations SET IsActive=0,RevokedAt=SYSUTCDATETIME(),RevokedBy=@Actor
WHERE Id=@Id AND CompanyId=@CompanyId AND IsActive=1;
SELECT @@ROWCOUNT;
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@Id",id); HrmsDatabase.AddParameter(command,"@CompanyId",companyId);
            HrmsDatabase.AddParameter(command,"@Actor",revokedBy);
        })==1;
    }

    public static async Task<List<string>> ActiveDelegatorsAsync(
        ApplicationDbContext dbContext, int requestId, string delegateUserName)
        => await HrmsDatabase.QueryAsync(dbContext,"""
SELECT DISTINCT d.DelegatorUserName
FROM ApprovalDelegations d
INNER JOIN SelfServiceRequests r ON r.Id=@RequestId
INNER JOIN Employees requester ON requester.Id=r.EmployeeId AND ISNULL(requester.IsDeleted,0)=0
INNER JOIN SystemUsers sourceUser ON sourceUser.UserName=d.DelegatorUserName AND sourceUser.IsActive=1 AND ISNULL(sourceUser.IsDeleted,0)=0
INNER JOIN Employees sourceEmployee ON sourceEmployee.Id=sourceUser.EmployeeId AND ISNULL(sourceEmployee.IsDeleted,0)=0
INNER JOIN SystemUsers targetUser ON targetUser.UserName=d.DelegateUserName AND targetUser.IsActive=1 AND ISNULL(targetUser.IsDeleted,0)=0
INNER JOIN Employees targetEmployee ON targetEmployee.Id=targetUser.EmployeeId AND ISNULL(targetEmployee.IsDeleted,0)=0
WHERE d.CompanyId=requester.CompanyId AND sourceEmployee.CompanyId=d.CompanyId AND targetEmployee.CompanyId=d.CompanyId
  AND d.DelegateUserName=@Delegate AND d.IsActive=1
  AND d.StartsAt<=SYSUTCDATETIME() AND d.EndsAt>SYSUTCDATETIME();
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@RequestId",requestId);
            HrmsDatabase.AddParameter(command,"@Delegate",delegateUserName);
        }, reader => HrmsDatabase.GetString(reader,"DelegatorUserName"));

    private static void Demand(CompanyScope scope,int companyId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if(companyId<=0||!scope.Allows(companyId)) throw new UnauthorizedAccessException("الشركة خارج نطاق المستخدم.");
    }
}
