using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قفل تعاونيّ على مستوى **القاعدة** لا العملية — يجعل عملاً دورياً يعمل على نسخة
/// واحدة في اللحظة مهما بلغ عدد النسخ.
///
/// <para><b>لماذا:</b> الخدمات الخلفية (<c>AddHostedService</c>) تعمل على <b>كل</b>
/// نسخة. بثلاث نسخ يصل الموظف ثلاثة إشعارات — والمولّد اليوميّ يحرس بجدول أحداث
/// لكن الحراسة نفسها «اقرأ ثم اكتب» فتتسابق. القفل يحسمها قبل أن تبدأ.</para>
///
/// <para><b>مهلة صفر عمداً:</b> عملٌ دوريّ لا ينتظر دوره — إن كانت نسخة أخرى تعمل
/// فهذه التكّة ليست ناقصة بل <b>مؤدّاة</b>. الانتظار يكدّس التكّات ويضاعف الحمل
/// بلا فائدة. (بخلاف الهجرات عند الإقلاع: هناك الانتظار مطلوب لأن التخطّي يعني
/// العمل على مخطط ناقص.)</para>
///
/// <para><b>الاتصال يُفتح صراحةً ويبقى مفتوحاً:</b> نطاق <c>Session</c> مربوط
/// بالاتصال، وترك EF يفتحه ويغلقه لكل أمر يُفلت القفل بينهما.</para>
/// </summary>
public static class SqlDistributedLock
{
    /// <summary>
    /// يحاول أخذ القفل ثم ينفّذ <paramref name="action"/>. يعيد <c>false</c> بلا
    /// تنفيذ إن كان القفل مأخوذاً — وهذه ليست حالة خطأ.
    /// </summary>
    public static async Task<bool> TryRunAsync(
        ApplicationDbContext dbContext,
        string resource,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentNullException.ThrowIfNull(action);

        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;

        if (openedHere)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            var acquired = await ScalarAsync(connection, """
DECLARE @result int;
EXEC @result = sp_getapplock
    @Resource = @Res,
    @LockMode = 'Exclusive',
    @LockOwner = 'Session',
    @LockTimeout = 0;
SELECT @result;
""", resource, cancellationToken);

            // 0 مُنح فوراً · 1 مُنح بعد انتظار (لا يقع بمهلة صفر) · ما دونهما: مأخوذ.
            if (acquired < 0) return false;

            try
            {
                await action();
                return true;
            }
            finally
            {
                await ScalarAsync(connection,
                    "EXEC sp_releaseapplock @Resource = @Res, @LockOwner = 'Session'; SELECT 0;",
                    resource, CancellationToken.None);
            }
        }
        finally
        {
            if (openedHere)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static async Task<int> ScalarAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        string resource,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@Res";
        parameter.Value = resource;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int value ? value : -1;
    }
}
