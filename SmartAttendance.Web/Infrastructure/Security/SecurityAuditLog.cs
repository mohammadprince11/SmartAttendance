using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// كاتب تدقيقٍ مركزيّ للعمليات الحسّاسة (AUDIT-001).
///
/// <para><b>لماذا:</b> فحص استغلال AUTHZ-003 كشف أن <c>AuditLogs</c> <b>لا يسجّل
/// عمليات الحذف</b> — يغطّي المصادقة والوثائق والاستطلاعات فقط. فلو استُغلّت ثغرةُ
/// حذفٍ عابرة للشركات لَما أمكن كشفها. هذا المساعد يوحّد كتابة التدقيق (كانت
/// إدراجاتٍ مبعثرة بكل متجر) ويجعل تسجيل الحذف سطراً واحداً.</para>
///
/// <para><b>خصوصيّة (قاعدة حمراء):</b> يسجّل هويّة الفاعل والمورد ونوع العملية —
/// لا محتوى المورد الحسّاس. لا رواتب ولا بيانات شخصية بالحقول.</para>
///
/// <para><b>مغلق الفشل بلطف:</b> تعذّر كتابة التدقيق لا يُسقط العملية الأصلية
/// (الحذف تمّ) — لكنه يُسجَّل تحذيراً. التدقيق أثرٌ لا شرطُ صحّة.</para>
/// </summary>
public static class SecurityAuditLog
{
    /// <summary>يسجّل حذفاً حسّاساً: من حذف، ماذا، من أين.</summary>
    public static async Task WriteDeletionAsync(
        ApplicationDbContext db,
        string entityName,
        int entityId,
        string? userName,
        string? ipAddress,
        string? details = null)
    {
        await WriteAsync(db, entityName, entityId, "Delete", userName, ipAddress, details);
    }

    /// <summary>يسجّل عمليةً حسّاسة عامّة.</summary>
    public static async Task WriteAsync(
        ApplicationDbContext db,
        string entityName,
        int entityId,
        string action,
        string? userName,
        string? ipAddress,
        string? details = null)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF OBJECT_ID('AuditLogs','U') IS NOT NULL
    INSERT INTO AuditLogs (EntityName, EntityId, Action, OldValues, UserName, IpAddress, CreatedAt)
    VALUES (@Entity, @EntityId, @Action, @Details, @User, @Ip, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Entity", entityName);
                HrmsDatabase.AddParameter(command, "@EntityId", entityId.ToString());
                HrmsDatabase.AddParameter(command, "@Action", action);
                HrmsDatabase.AddParameter(command, "@Details", (object?)details ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@User", (object?)userName ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Ip", (object?)ipAddress ?? DBNull.Value);
            });
    }
}
