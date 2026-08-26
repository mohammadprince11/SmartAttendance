using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// AUDIT-001 — سجلّ التدقيق لم يكن يسجّل عمليات الحذف (كشفه فحص استغلال AUTHZ-003:
/// لو استُغلّت ثغرةُ حذفٍ عابرة للشركات لَما أمكن كشفها). هذا يثبت أن
/// <see cref="SecurityAuditLog.WriteDeletionAsync"/> يكتب صفّاً صحيحاً فعلاً.
///
/// <para>⚠️ <c>SmartAttendance_Test</c> حصراً · يكتب صفّاً بمعرّفٍ اصطناعيّ عالٍ
/// ويحذفه بعده — لا يُبقي أثراً. غياب القاعدة ⟹ تخطٍّ.</para>
/// </summary>
public sealed class SecurityAuditLogTests : IAsyncLifetime
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION") ??
        "Server=localhost;Database=SmartAttendance_Test;Trusted_Connection=True;" +
        "TrustServerCertificate=True;MultipleActiveResultSets=True";

    // معرّفٌ اصطناعيّ عالٍ لا يتصادم مع طلباتٍ حقيقية.
    private const int SyntheticEntityId = 990_099;
    private const string Marker = "AUDIT001-TEST";

    private ApplicationDbContext _db = null!;
    private bool _available;

    public async Task InitializeAsync()
    {
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);
        try
        {
            await _db.Database.OpenConnectionAsync();
            _available = true;
        }
        catch
        {
            _available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_available)
        {
            try
            {
                await HrmsDatabase.ExecuteAsync(_db,
                    "DELETE FROM AuditLogs WHERE EntityId = @Id AND UserName = @U;",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Id", SyntheticEntityId.ToString());
                        HrmsDatabase.AddParameter(command, "@U", Marker);
                    });
            }
            catch { /* best-effort */ }
        }

        try { await _db.Database.CloseConnectionAsync(); } catch { }
        await _db.DisposeAsync();
    }

    [SkippableFact]
    public async Task WriteDeletion_PersistsAuditRow_WithActorEntityAndAction()
    {
        Skip.IfNot(_available, "SmartAttendance_Test غير متاحة محلياً.");

        await SecurityAuditLog.WriteDeletionAsync(
            _db, "LeaveRequest", SyntheticEntityId, Marker, "203.0.113.7", "regression");

        var rows = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT EntityName, EntityId, Action, UserName, IpAddress
FROM AuditLogs
WHERE EntityId = @Id AND UserName = @U;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", SyntheticEntityId.ToString());
                HrmsDatabase.AddParameter(command, "@U", Marker);
            },
            reader => new
            {
                Entity = HrmsDatabase.GetString(reader, "EntityName"),
                Id = HrmsDatabase.GetString(reader, "EntityId"),
                Action = HrmsDatabase.GetString(reader, "Action"),
                User = HrmsDatabase.GetString(reader, "UserName"),
                Ip = HrmsDatabase.GetString(reader, "IpAddress")
            });

        var row = Assert.Single(rows);
        Assert.Equal("LeaveRequest", row.Entity);
        Assert.Equal(SyntheticEntityId.ToString(), row.Id);
        Assert.Equal("Delete", row.Action);
        Assert.Equal(Marker, row.User);
        Assert.Equal("203.0.113.7", row.Ip);
    }

    [Fact]
    public void DeletePage_WritesAudit_AfterSuccessfulDeletion()
    {
        // سقّاطة: تربط الإصلاح بمساره فلا يُحذف نداء التدقيق لاحقاً بلا قصد.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);

        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Pages", "LeaveRequests", "Delete.cshtml.cs"));

        Assert.Contains("SecurityAuditLog.WriteDeletionAsync", source);
        Assert.Contains("\"LeaveRequest\"", source);
    }
}
