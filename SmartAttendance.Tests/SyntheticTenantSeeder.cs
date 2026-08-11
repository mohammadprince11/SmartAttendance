using System.Data;
using System.Data.Common;

namespace SmartAttendance.Tests;

/// <summary>
/// باذرٌ اصطناعيّ مقادٌ بمخطّط القاعدة — يُدرج صفّاً أدنى صالحاً بأي جدول دون
/// معرفةٍ مسبقة بأعمدته (FIX-005).
///
/// <para><b>لماذا:</b> اختبار العزل التنفيذيّ (<see cref="CrossCompanyResourceDenialTests"/>)
/// كان يتخطّى 11 من 12 جدولاً لأن قاعدة الاختبار تفتقر صفوفاً من شركتين بها.
/// بذرُها يدوياً لكل جدول = 11 إدراجاً هشّاً يكرّر معرفة المخطّط (وهو بالضبط
/// النمط الذي انتقده Phase 5). بدلاً منه: نقرأ الأعمدة المطلوبة من
/// <c>INFORMATION_SCHEMA</c> ونملؤها بقيمٍ بحسب النوع — فيتكيّف مع أي مخطّط.</para>
///
/// <para><b>آمنٌ:</b> جداول الحارس بلا مفاتيح أجنبيّة (شفاءٌ ذاتيّ)، فالإدراج
/// الأدنى لا ينتهك قيداً. والتنظيف بالمعرّفات الملتقَطة يزيل كل أثر.</para>
/// </summary>
public sealed class SyntheticTenantSeeder
{
    private readonly DbConnection _connection;
    private readonly List<(string Table, int Id)> _inserted = new();

    public SyntheticTenantSeeder(DbConnection connection) => _connection = connection;

    /// <summary>
    /// يُدرج صفّاً بالجدول بأعمدةٍ مطلوبةٍ مملوءةٍ تلقائياً + التجاوزات المعطاة،
    /// ويُعيد <c>Id</c> المُدرَج. يتتبّع الصفّ للتنظيف.
    /// </summary>
    public async Task<int> InsertAsync(string table, IReadOnlyDictionary<string, object> overrides)
    {
        var required = await RequiredColumnsAsync(table);

        var columns = new List<string>();
        var values = new List<string>();
        var parameters = new List<(string Name, object Value)>();
        var index = 0;

        // نبدأ بالتجاوزات (EmployeeId · CompanyId …) ثم نكمل المطلوب بقيمٍ نوعيّة.
        foreach (var (column, value) in overrides)
        {
            columns.Add($"[{column}]");
            var name = $"@p{index++}";
            values.Add(name);
            parameters.Add((name, value));
        }

        foreach (var column in required)
        {
            if (overrides.ContainsKey(column.Name))
            {
                continue;
            }

            columns.Add($"[{column.Name}]");
            values.Add(PlaceholderLiteral(column.DataType));
        }

        var sql = $"INSERT INTO [{table}] ({string.Join(", ", columns)}) " +
                  $"OUTPUT INSERTED.[Id] VALUES ({string.Join(", ", values)});";

        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        _inserted.Add((table, id));
        return id;
    }

    /// <summary>يحذف كل ما أُدرج — بالترتيب العكسيّ، best-effort.</summary>
    public async Task CleanupAsync()
    {
        for (var i = _inserted.Count - 1; i >= 0; i--)
        {
            var (table, id) = _inserted[i];
            try
            {
                await using var command = _connection.CreateCommand();
                command.CommandText = $"DELETE FROM [{table}] WHERE [Id] = @id;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = id;
                command.Parameters.Add(parameter);
                await command.ExecuteNonQueryAsync();
            }
            catch
            {
                // best-effort — الجدول قد يكون حُذف بينهما.
            }
        }

        _inserted.Clear();
    }

    private sealed record ColumnInfo(string Name, string DataType);

    private async Task<List<ColumnInfo>> RequiredColumnsAsync(string table)
    {
        // مطلوب = NOT NULL · بلا افتراض · ليس هوية · ليس محسوباً.
        const string metadataSql = """
SELECT c.name, ty.name AS type_name
FROM sys.columns c
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE c.object_id = OBJECT_ID(@t)
  AND c.is_nullable = 0
  AND c.is_identity = 0
  AND c.is_computed = 0
  AND dc.definition IS NULL;
""";

        await using var command = _connection.CreateCommand();
        command.CommandText = metadataSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@t";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        var columns = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnInfo(reader.GetString(0), reader.GetString(1)));
        }

        return columns;
    }

    /// <summary>قيمةٌ حرفيّة صالحة بحسب نوع العمود — لا مدخل مستخدم، ثابتة بالكود.</summary>
    private static string PlaceholderLiteral(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "int" or "bigint" or "smallint" or "tinyint" => "0",
        "bit" => "0",
        "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => "0",
        "uniqueidentifier" => "NEWID()",
        "date" or "datetime" or "datetime2" or "smalldatetime" => "SYSUTCDATETIME()",
        "time" => "'00:00:00'",
        // نصّيّ: قيمةٌ **فريدة** (8 خانات من NEWID) تعالج أي فهرسٍ فريدٍ على عمودٍ
        // نصّيّ تلقائياً (مثل IX_Companies_Code) دون معرفةٍ مسبقةٍ بأي عمودٍ فريد.
        _ => "LEFT(CONVERT(varchar(36), NEWID()), 8)"
    };
}
