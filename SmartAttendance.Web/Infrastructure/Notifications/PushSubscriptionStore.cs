using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// اشتراكات Web-Push للأجهزة — كل صف = متصفّح/جهاز اشترك بالإشعارات لموظف. المفتاح
/// الفريد هو Endpoint (يتغيّر بتغيّر الاشتراك). يُنشئه العميل (push.js) ويُرسله لنقطة
/// الاشتراك، ويُحذف حين يرفضه مزوّد الدفع (410 Gone) أو يلغي المستخدم.
/// </summary>
public static class PushSubscriptionStore
{
    public sealed record Subscription(string Endpoint, string P256dh, string Auth);

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('PushSubscriptions', 'U') IS NULL
BEGIN
    CREATE TABLE PushSubscriptions
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        Endpoint nvarchar(500) NOT NULL,
        P256dh nvarchar(200) NOT NULL,
        Auth nvarchar(100) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_PushSub_Endpoint ON PushSubscriptions (Endpoint);
    CREATE INDEX IX_PushSub_Employee ON PushSubscriptions (EmployeeId);
END;
""");
    }

    /// <summary>يحفظ/يحدّث اشتراكاً (upsert على Endpoint) مربوطاً بموظف.</summary>
    public static async Task SaveAsync(ApplicationDbContext dbContext, int employeeId, Subscription sub)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
MERGE PushSubscriptions AS target
USING (SELECT @Endpoint AS Endpoint) AS source ON target.Endpoint = source.Endpoint
WHEN MATCHED THEN UPDATE SET EmployeeId = @Emp, P256dh = @P256dh, Auth = @Auth
WHEN NOT MATCHED THEN INSERT (EmployeeId, Endpoint, P256dh, Auth)
    VALUES (@Emp, @Endpoint, @P256dh, @Auth);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@Endpoint", sub.Endpoint);
                HrmsDatabase.AddParameter(command, "@P256dh", sub.P256dh);
                HrmsDatabase.AddParameter(command, "@Auth", sub.Auth);
            });
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, string endpoint)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM PushSubscriptions WHERE Endpoint = @Endpoint;",
            command => HrmsDatabase.AddParameter(command, "@Endpoint", endpoint));
    }

    public static async Task<List<Subscription>> ListByEmployeeAsync(ApplicationDbContext dbContext, int employeeId)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT Endpoint, P256dh, Auth FROM PushSubscriptions WHERE EmployeeId = @Emp;",
            command => HrmsDatabase.AddParameter(command, "@Emp", employeeId),
            reader => new Subscription(
                HrmsDatabase.GetString(reader, "Endpoint"),
                HrmsDatabase.GetString(reader, "P256dh"),
                HrmsDatabase.GetString(reader, "Auth")));
    }
}
