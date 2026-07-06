using System.Data;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.HrSettings;

public static class HrSettingsStore
{
    public static async Task EnsureTablesAsync(ApplicationDbContext db)
    {
        await ExecuteAsync(db,
            """
IF OBJECT_ID('NexoraHrSettings', 'U') IS NULL
BEGIN
    CREATE TABLE NexoraHrSettings
    (
        SettingKey nvarchar(160) NOT NULL PRIMARY KEY,
        SettingValue nvarchar(max) NULL,
        UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID('NexoraTerminationReasons', 'U') IS NULL
BEGIN
    CREATE TABLE NexoraTerminationReasons
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(180) NOT NULL,
        IsMandatory bit NOT NULL DEFAULT(0),
        RequiresSelfService bit NOT NULL DEFAULT(1),
        EndOfServicePercent decimal(18,2) NOT NULL DEFAULT(100),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID('NexoraNotificationRules', 'U') IS NULL
BEGIN
    CREATE TABLE NexoraNotificationRules
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(220) NOT NULL,
        IsEnabled bit NOT NULL DEFAULT(0),
        Audience nvarchar(80) NULL,
        DaysBefore int NOT NULL DEFAULT(0),
        TriggerDescription nvarchar(500) NULL,
        SelectedItems nvarchar(500) NULL,
        SupervisorName nvarchar(220) NULL,
        DisplayOrder int NOT NULL DEFAULT(100),
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;
""");

        await SeedTerminationReasonsAsync(db);
        await SeedNotificationRulesAsync(db);
    }

    public static async Task<string> GetAsync(ApplicationDbContext db, string key, string fallback = "")
    {
        await EnsureTablesAsync(db);

        var value = await ScalarAsync<string?>(db,
            "SELECT SettingValue FROM NexoraHrSettings WHERE SettingKey = @Key;",
            command => Add(command, "@Key", key));

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public static async Task SetAsync(ApplicationDbContext db, string key, string? value)
    {
        await EnsureTablesAsync(db);

        await ExecuteAsync(db,
            """
MERGE NexoraHrSettings AS target
USING (SELECT @Key AS SettingKey, @Value AS SettingValue) AS source
ON target.SettingKey = source.SettingKey
WHEN MATCHED THEN
    UPDATE SET SettingValue = source.SettingValue, UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (SettingKey, SettingValue, UpdatedAt)
    VALUES (source.SettingKey, source.SettingValue, SYSUTCDATETIME());
""",
            command =>
            {
                Add(command, "@Key", key);
                Add(command, "@Value", value ?? string.Empty);
            });
    }

    public static async Task<List<TerminationReasonRow>> LoadTerminationReasonsAsync(ApplicationDbContext db)
    {
        await EnsureTablesAsync(db);

        return await QueryAsync(db,
            """
SELECT Id, Name, IsMandatory, RequiresSelfService, EndOfServicePercent, IsActive
FROM NexoraTerminationReasons
ORDER BY Id;
""",
            reader => new TerminationReasonRow
            {
                Id = ToInt(reader["Id"]),
                Name = ToStringValue(reader["Name"]),
                IsMandatory = ToBool(reader["IsMandatory"]),
                RequiresSelfService = ToBool(reader["RequiresSelfService"]),
                EndOfServicePercent = ToDecimal(reader["EndOfServicePercent"], 100),
                IsActive = ToBool(reader["IsActive"])
            });
    }

    public static async Task AddTerminationReasonAsync(ApplicationDbContext db, string name, bool isMandatory, bool requiresSelfService, decimal endOfServicePercent)
    {
        await EnsureTablesAsync(db);

        await ExecuteAsync(db,
            """
INSERT INTO NexoraTerminationReasons(Name, IsMandatory, RequiresSelfService, EndOfServicePercent, IsActive, CreatedAt)
VALUES(@Name, @IsMandatory, @RequiresSelfService, @EndOfServicePercent, 1, SYSUTCDATETIME());
""",
            command =>
            {
                Add(command, "@Name", name.Trim());
                Add(command, "@IsMandatory", isMandatory);
                Add(command, "@RequiresSelfService", requiresSelfService);
                Add(command, "@EndOfServicePercent", Math.Clamp(endOfServicePercent, 0, 100));
            });
    }

    public static async Task UpdateTerminationReasonAsync(ApplicationDbContext db, int id, string name, bool isMandatory, bool requiresSelfService, decimal endOfServicePercent, bool isActive)
    {
        await EnsureTablesAsync(db);

        await ExecuteAsync(db,
            """
UPDATE NexoraTerminationReasons
SET Name = @Name,
    IsMandatory = @IsMandatory,
    RequiresSelfService = @RequiresSelfService,
    EndOfServicePercent = @EndOfServicePercent,
    IsActive = @IsActive
WHERE Id = @Id;
""",
            command =>
            {
                Add(command, "@Id", id);
                Add(command, "@Name", name.Trim());
                Add(command, "@IsMandatory", isMandatory);
                Add(command, "@RequiresSelfService", requiresSelfService);
                Add(command, "@EndOfServicePercent", Math.Clamp(endOfServicePercent, 0, 100));
                Add(command, "@IsActive", isActive);
            });
    }

    public static async Task DeleteTerminationReasonAsync(ApplicationDbContext db, int id)
    {
        await EnsureTablesAsync(db);
        await ExecuteAsync(db, "DELETE FROM NexoraTerminationReasons WHERE Id = @Id;", command => Add(command, "@Id", id));
    }

    public static async Task<List<NotificationRuleRow>> LoadNotificationRulesAsync(ApplicationDbContext db)
    {
        await EnsureTablesAsync(db);

        return await QueryAsync(db,
            """
SELECT Id, Name, IsEnabled, Audience, DaysBefore, TriggerDescription, SelectedItems, SupervisorName, DisplayOrder
FROM NexoraNotificationRules
ORDER BY DisplayOrder, Id;
""",
            reader => new NotificationRuleRow
            {
                Id = ToInt(reader["Id"]),
                Name = ToStringValue(reader["Name"]),
                IsEnabled = ToBool(reader["IsEnabled"]),
                Audience = ToStringValue(reader["Audience"], "المشرفين"),
                DaysBefore = ToInt(reader["DaysBefore"]),
                TriggerDescription = ToStringValue(reader["TriggerDescription"]),
                SelectedItems = ToStringValue(reader["SelectedItems"], "كل العناصر المختارة"),
                SupervisorName = ToStringValue(reader["SupervisorName"]),
                DisplayOrder = ToInt(reader["DisplayOrder"])
            });
    }

    public static async Task ToggleNotificationRuleAsync(ApplicationDbContext db, int id, bool isEnabled)
    {
        await EnsureTablesAsync(db);

        await ExecuteAsync(db,
            "UPDATE NexoraNotificationRules SET IsEnabled = @IsEnabled WHERE Id = @Id;",
            command =>
            {
                Add(command, "@Id", id);
                Add(command, "@IsEnabled", isEnabled);
            });
    }

    public static async Task UpdateNotificationRuleAsync(ApplicationDbContext db, int id, string audience, int daysBefore, string selectedItems, string supervisorName)
    {
        await EnsureTablesAsync(db);

        await ExecuteAsync(db,
            """
UPDATE NexoraNotificationRules
SET Audience = @Audience,
    DaysBefore = @DaysBefore,
    SelectedItems = @SelectedItems,
    SupervisorName = @SupervisorName
WHERE Id = @Id;
""",
            command =>
            {
                Add(command, "@Id", id);
                Add(command, "@Audience", string.IsNullOrWhiteSpace(audience) ? "المشرفين" : audience.Trim());
                Add(command, "@DaysBefore", Math.Max(0, daysBefore));
                Add(command, "@SelectedItems", string.IsNullOrWhiteSpace(selectedItems) ? "كل العناصر المختارة" : selectedItems.Trim());
                Add(command, "@SupervisorName", string.IsNullOrWhiteSpace(supervisorName) ? DBNull.Value : supervisorName.Trim());
            });
    }

    private static async Task SeedTerminationReasonsAsync(ApplicationDbContext db)
    {
        var count = await ScalarAsync<int>(db, "SELECT COUNT(1) FROM NexoraTerminationReasons;");
        if (count > 0) return;

        await ExecuteAsync(db,
            """
INSERT INTO NexoraTerminationReasons(Name, IsMandatory, RequiresSelfService, EndOfServicePercent, IsActive, CreatedAt)
VALUES
(N'استقالة', 0, 1, 100, 1, SYSUTCDATETIME()),
(N'إقالة', 1, 1, 100, 1, SYSUTCDATETIME()),
(N'انتهاء عقد', 1, 1, 100, 1, SYSUTCDATETIME()),
(N'عدم الرغبة بتجديد العقد', 0, 1, 100, 1, SYSUTCDATETIME()),
(N'عدم اجتياز فترة التجربة', 0, 1, 100, 1, SYSUTCDATETIME()),
(N'عدم الرغبة بإكمال فترة التجربة', 0, 1, 100, 1, SYSUTCDATETIME());
""");
    }

    private static async Task SeedNotificationRulesAsync(ApplicationDbContext db)
    {
        var count = await ScalarAsync<int>(db, "SELECT COUNT(1) FROM NexoraNotificationRules;");
        if (count > 0) return;

        await ExecuteAsync(db,
            """
INSERT INTO NexoraNotificationRules(Name, IsEnabled, Audience, DaysBefore, TriggerDescription, SelectedItems, SupervisorName, DisplayOrder, CreatedAt)
VALUES
(N'عيد ميلاد موظف', 0, N'المشرفين', 0, N'عند حلول عيد ميلاد موظف', N'كل الموظفين', NULL, 10, SYSUTCDATETIME()),
(N'ذكرى عمل موظف', 0, N'المشرفين', 0, N'عند حلول ذكرى عمل موظف', N'كل الموظفين', NULL, 20, SYSUTCDATETIME()),
(N'الترحيب بموظف جديد', 0, N'المشرفين', 0, N'عند إضافة موظف جديد', N'كل الموظفين', NULL, 30, SYSUTCDATETIME()),
(N'إعادة تعيين الموظف', 0, N'المشرفين', 0, N'عند إعادة تعيين موظف', N'كل الموظفين', NULL, 40, SYSUTCDATETIME()),
(N'وداع موظف', 0, N'المشرفين', 0, N'عند مغادرة موظف', N'كل الموظفين', NULL, 50, SYSUTCDATETIME()),
(N'إنشاء صلاحية الوثيقة', 0, N'المشرفين', 0, N'عند إنشاء صلاحية وثيقة', N'كل الوثائق', NULL, 60, SYSUTCDATETIME()),
(N'العطل', 0, N'المشرفين', 0, N'عند إضافة عطلة', N'كل الفروع', NULL, 70, SYSUTCDATETIME()),
(N'فترة التجربة', 1, N'المدير المباشر والمشرفون', 10, N'قبل تاريخ انتهاء فترة التجربة', N'كل العناصر المختارة', NULL, 80, SYSUTCDATETIME()),
(N'سن التقاعد', 0, N'المشرفين', 30, N'قبل بلوغ سن التقاعد', N'كل الموظفين', NULL, 90, SYSUTCDATETIME()),
(N'عقود الموظفين', 1, N'المدير المباشر والمشرفون', 45, N'قبل انتهاء صلاحية العقد', N'كل العناصر المختارة', NULL, 100, SYSUTCDATETIME()),
(N'تجديد عقد', 0, N'المشرفين', 0, N'عند تجديد عقد', N'كل الموظفين', NULL, 110, SYSUTCDATETIME()),
(N'تمديد العقد', 0, N'المشرفين', 0, N'عند تمديد عقد', N'كل الموظفين', NULL, 120, SYSUTCDATETIME()),
(N'مخالفات الموظفين', 0, N'المشرفين', 0, N'عند تسجيل مخالفة موظف', N'كل الموظفين', NULL, 130, SYSUTCDATETIME()),
(N'الإجراءات التأديبية المتخذة', 0, N'المشرفين', 0, N'عند اتخاذ إجراء تأديبي', N'كل الموظفين', NULL, 140, SYSUTCDATETIME()),
(N'الإجراءات التأديبية المتجاهلة', 0, N'المشرفين', 0, N'عند تجاهل إجراء تأديبي', N'كل الموظفين', NULL, 150, SYSUTCDATETIME()),
(N'الإجراءات التأديبية التي تم إسقاطها', 0, N'المشرفين', 0, N'عند إسقاط إجراء تأديبي', N'كل الموظفين', NULL, 160, SYSUTCDATETIME()),
(N'مقابلات نهاية الخدمة', 0, N'المشرفين', 0, N'عند جدولة مقابلة نهاية خدمة', N'كل الموظفين', NULL, 170, SYSUTCDATETIME()),
(N'مشاركة التقارير', 0, N'المشرفين', 0, N'عند مشاركة تقرير', N'كل التقارير', NULL, 180, SYSUTCDATETIME()),
(N'رفض الموظفين', 0, N'المشرفين', 0, N'عند رفض موظف', N'كل الموظفين', NULL, 190, SYSUTCDATETIME()),
(N'اقتراحات وشكاوي', 1, N'المشرفين', 0, N'عندما يقوم الموظف بإرسال اقتراح أو شكوى', N'كل العناصر المختارة', NULL, 200, SYSUTCDATETIME()),
(N'مشاركة الوثائق', 0, N'المشرفين', 0, N'عند مشاركة وثيقة', N'كل الوثائق', NULL, 210, SYSUTCDATETIME()),
(N'الرد على الاقتراحات والشكاوي', 0, N'المشرفين', 0, N'عند الرد على اقتراح أو شكوى', N'كل العناصر المختارة', NULL, 220, SYSUTCDATETIME()),
(N'الإنتخابات و إستطلاعات الرأي', 0, N'المشرفين', 0, N'عند إنشاء استطلاع رأي', N'كل الموظفين', NULL, 230, SYSUTCDATETIME());
""");
    }

    private static async Task ExecuteAsync(ApplicationDbContext db, string sql, Action<IDbCommand>? configure = null)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static async Task<T> ScalarAsync<T>(ApplicationDbContext db, string sql, Action<IDbCommand>? configure = null)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? default! : (T)Convert.ChangeType(value, typeof(T));
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static async Task<List<T>> QueryAsync<T>(ApplicationDbContext db, string sql, Func<IDataRecord, T> map, Action<IDbCommand>? configure = null)
    {
        var result = new List<T>();
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Add(map(reader));
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }

        return result;
    }

    private static void Add(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static int ToInt(object value) => value == DBNull.Value ? 0 : Convert.ToInt32(value);
    private static decimal ToDecimal(object value, decimal fallback = 0) => value == DBNull.Value ? fallback : Convert.ToDecimal(value);
    private static bool ToBool(object value) => value != DBNull.Value && Convert.ToBoolean(value);
    private static string ToStringValue(object value, string fallback = "") => value == DBNull.Value ? fallback : Convert.ToString(value) ?? fallback;
}

public sealed class TerminationReasonRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public bool RequiresSelfService { get; set; }
    public decimal EndOfServicePercent { get; set; }
    public bool IsActive { get; set; }
}

public sealed class NotificationRuleRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string Audience { get; set; } = "المشرفين";
    public int DaysBefore { get; set; }
    public string TriggerDescription { get; set; } = string.Empty;
    public string SelectedItems { get; set; } = "كل العناصر المختارة";
    public string SupervisorName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
