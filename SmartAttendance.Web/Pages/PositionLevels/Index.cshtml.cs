using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Pages.PositionLevels;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public LookupForm Input { get; set; } = new();

    public List<LookupRow> Items { get; set; } = new();

    public async Task OnGetAsync(int? editId)
    {
        await SmartAttendance.Web.Infrastructure.Hrms.PositionSchema.EnsureAsync(_db);
        await SyncExistingValuesAsync();
        await LoadPageAsync(editId);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await SmartAttendance.Web.Infrastructure.Hrms.PositionSchema.EnsureAsync(_db);
        Input.Name = NormalizeText(Input.Name);
        Input.OriginalName = NormalizeText(Input.OriginalName);

        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            TempData["ErrorMessage"] = "اسم المستوى مطلوب.";
            await LoadPageAsync(Input.Id > 0 ? Input.Id : null);
            return Page();
        }

        var duplicateCount = Convert.ToInt32(await ExecuteScalarAsync(@"
SELECT COUNT(1)
FROM dbo.HrJobPositionLevels
WHERE LTRIM(RTRIM(Name)) = @Name
  AND Id <> @Id;
", command =>
        {
            AddParameter(command, "@Name", Input.Name);
            AddParameter(command, "@Id", Input.Id);
        }) ?? 0);

        if (duplicateCount > 0)
        {
            TempData["ErrorMessage"] = "اسم المستوى موجود مسبقاً.";
            await LoadPageAsync(Input.Id > 0 ? Input.Id : null);
            return Page();
        }

        if (Input.Id > 0)
        {
            var oldName = await GetNameAsync(Input.Id);
            if (string.IsNullOrWhiteSpace(oldName))
            {
                TempData["ErrorMessage"] = "المستوى المطلوب غير موجود.";
                return RedirectToPage();
            }

            await ExecuteNonQueryAsync(@"
UPDATE dbo.HrJobPositionLevels
SET Name = @Name,
    IsActive = @IsActive,
    UpdatedAt = SYSDATETIME()
WHERE Id = @Id;
", command =>
            {
                AddParameter(command, "@Name", Input.Name);
                AddParameter(command, "@IsActive", Input.IsActive);
                AddParameter(command, "@Id", Input.Id);
            });

            if (!string.Equals(NormalizeText(oldName), Input.Name, StringComparison.OrdinalIgnoreCase))
            {
                await UpdatePositionsValueAsync(oldName, Input.Name);
            }

            TempData["SuccessMessage"] = "تم حفظ المستوى بنجاح.";
        }
        else
        {
            await ExecuteNonQueryAsync(@"
INSERT INTO dbo.HrJobPositionLevels (Name, IsActive, CreatedAt)
VALUES (@Name, 1, SYSDATETIME());
", command => AddParameter(command, "@Name", Input.Name));

            TempData["SuccessMessage"] = "تم حفظ المستوى بنجاح.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        await SmartAttendance.Web.Infrastructure.Hrms.PositionSchema.EnsureAsync(_db);

        await ExecuteNonQueryAsync(@"
UPDATE dbo.HrJobPositionLevels
SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
    UpdatedAt = SYSDATETIME()
WHERE Id = @Id;
", command => AddParameter(command, "@Id", id));

        TempData["SuccessMessage"] = "تم تحديث حالة المستوى.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await SmartAttendance.Web.Infrastructure.Hrms.PositionSchema.EnsureAsync(_db);

        var name = await GetNameAsync(id);
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = "المستوى المطلوب غير موجود.";
            return RedirectToPage();
        }

        var usageCount = await CountUsageAsync(name);
        if (usageCount > 0)
        {
            TempData["ErrorMessage"] = "لا يمكن حذف مستوى مستخدم بمنصب.";
            return RedirectToPage();
        }

        await ExecuteNonQueryAsync(@"
DELETE FROM dbo.HrJobPositionLevels
WHERE Id = @Id;
", command => AddParameter(command, "@Id", id));

        TempData["SuccessMessage"] = "تم حذف المستوى بنجاح.";
        return RedirectToPage();
    }

    private async Task LoadPageAsync(int? editId)
    {
        Items = await ReadRowsAsync();

        if (editId.HasValue)
        {
            var editRow = Items.FirstOrDefault(item => item.Id == editId.Value);
            if (editRow != null)
            {
                Input = new LookupForm
                {
                    Id = editRow.Id,
                    OriginalName = editRow.Name,
                    Name = editRow.Name,
                    IsActive = editRow.IsActive
                };
            }
        }
    }

    private async Task SyncExistingValuesAsync()
    {
        await ExecuteNonQueryAsync(@"
INSERT INTO dbo.HrJobPositionLevels (Name, IsActive, CreatedAt)
SELECT DISTINCT LTRIM(RTRIM(Level)), 1, SYSDATETIME()
FROM dbo.HrJobPositions p
WHERE p.Level IS NOT NULL
  AND LTRIM(RTRIM(p.Level)) <> N''
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.HrJobPositionLevels item
      WHERE LTRIM(RTRIM(item.Name)) = LTRIM(RTRIM(p.Level))
  );
");
    }

    private async Task<List<LookupRow>> ReadRowsAsync()
    {
        var rows = new List<LookupRow>();
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    item.Id,
    item.Name,
    item.IsActive,
    (
        SELECT COUNT(1)
        FROM dbo.HrJobPositions position
        WHERE LTRIM(RTRIM(ISNULL(position.Level, N''))) = LTRIM(RTRIM(item.Name))
    ) AS UsageCount
FROM dbo.HrJobPositionLevels item
ORDER BY item.Name;";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                rows.Add(new LookupRow
                {
                    Id = reader.GetInt32(0),
                    Name = name,
                    IsActive = !reader.IsDBNull(2) && reader.GetBoolean(2),
                    UsageCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    SearchText = NormalizeText(name).ToLowerInvariant()
                });
            }
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        return rows;
    }

    private async Task<string?> GetNameAsync(int id)
    {
        var value = await ExecuteScalarAsync(@"
SELECT TOP 1 Name
FROM dbo.HrJobPositionLevels
WHERE Id = @Id;
", command => AddParameter(command, "@Id", id));

        return value == null || value == DBNull.Value ? null : Convert.ToString(value);
    }

    private async Task<int> CountUsageAsync(string name)
    {
        var value = await ExecuteScalarAsync(@"
SELECT COUNT(1)
FROM dbo.HrJobPositions
WHERE LTRIM(RTRIM(ISNULL(Level, N''))) = @Name;
", command => AddParameter(command, "@Name", NormalizeText(name)));

        return Convert.ToInt32(value ?? 0);
    }

    private async Task UpdatePositionsValueAsync(string oldName, string newName)
    {
        await ExecuteNonQueryAsync(@"
UPDATE dbo.HrJobPositions
SET Level = @NewName,
    UpdatedAt = SYSDATETIME()
WHERE LTRIM(RTRIM(ISNULL(Level, N''))) = @OldName;
", command =>
        {
            AddParameter(command, "@NewName", newName);
            AddParameter(command, "@OldName", NormalizeText(oldName));
        });
    }

    private async Task<object?> ExecuteScalarAsync(string sql, Action<DbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task ExecuteNonQueryAsync(string sql, Action<DbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), @"\s+", " ");
    }
}

public sealed class LookupForm
{
    public int Id { get; set; }

    public string? OriginalName { get; set; }

    public string? Name { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class LookupRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int UsageCount { get; set; }

    public string SearchText { get; set; } = string.Empty;
}
