using System.Data;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Pages.Violations;

public class PrintFormModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public PrintFormModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public ViolationPrintData? Violation { get; private set; }

    public PrintFormSettings Settings { get; private set; } = new();

    public List<PrintTextBlock> TextBlocks { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Settings = await LoadSettingsAsync();
        Violation = await LoadViolationAsync(id);

        if (Violation == null)
        {
            return NotFound();
        }

        TextBlocks = await LoadTextBlocksAsync();

        if (TextBlocks.Count == 0)
        {
            TextBlocks = BuildFallbackBlocks();
        }

        return Page();
    }

    public string ReplaceTokens(string text)
    {
        if (Violation == null)
        {
            return text;
        }

        return (text ?? string.Empty)
            .Replace("{EmployeeName}", Violation.EmployeeName)
            .Replace("{EmployeeCode}", Violation.EmployeeCode)
            .Replace("{Department}", Violation.Department)
            .Replace("{Position}", Violation.Position)
            .Replace("{ViolationDate}", Violation.EventDate.ToString("yyyy-MM-dd"))
            .Replace("{ViolationCategory}", Violation.ViolationCategory)
            .Replace("{ViolationName}", Violation.ViolationTitle)
            .Replace("{ViolationDescription}", Violation.Notes)
            .Replace("{PenaltyAction}", Violation.PenaltyAction)
            .Replace("{FinancialImpact}", Violation.FinancialImpactText)
            .Replace("{ApprovedBy}", "قسم الموارد البشرية")
            .Replace("{ReferenceNo}", Violation.ReferenceNo)
            .Replace("{DeductionAmount}", Violation.DeductionAmount.ToString("N0"));
    }

    public string BlockStyle(PrintTextBlock block)
    {
        var color = string.IsNullOrWhiteSpace(block.FontColor) ? "#0f172a" : block.FontColor;
        var font = string.IsNullOrWhiteSpace(block.FontFamily) ? "Tahoma" : block.FontFamily;
        var align = string.IsNullOrWhiteSpace(block.TextAlign) ? "right" : block.TextAlign;
        var weight = block.IsBold ? "900" : "700";

        return $"left:{block.XPercent:0.###}%;top:{block.YPercent:0.###}%;width:{block.WidthPercent:0.###}%;font-family:'{HtmlEncoder.Default.Encode(font)}',Tahoma,Arial,sans-serif;font-size:{block.FontSize:0.##}px;color:{HtmlEncoder.Default.Encode(color)};font-weight:{weight};text-align:{HtmlEncoder.Default.Encode(align)};";
    }

    private async Task<ViolationPrintData?> LoadViolationAsync(int id)
    {
        var rows = await QueryAsync(
            """
SELECT TOP 1
    v.Id,
    v.ReferenceNo,
    v.EmployeeId,
    ISNULL(e.EmployeeNo, N'') AS EmployeeCode,
    ISNULL(e.FullName, N'') AS EmployeeName,
    ISNULL(e.Position, N'-') AS Position,
    ISNULL(d.Name, N'-') AS DepartmentName,
    ISNULL(b.Name, N'-') AS BranchName,
    ISNULL(v.ViolationCategory, N'') AS ViolationCategory,
    ISNULL(v.ViolationTitle, N'') AS ViolationTitle,
    v.EventDate,
    ISNULL(v.Source, N'') AS Source,
    ISNULL(v.ActionStatus, N'') AS ActionStatus,
    ISNULL(v.Status, N'') AS Status,
    ISNULL(v.FinalPenaltyAction, ISNULL(v.ProposedAction, N'')) AS PenaltyAction,
    ISNULL(v.FinancialImpactType, N'None') AS FinancialImpactType,
    ISNULL(v.FinancialImpactValue, 0) AS FinancialImpactValue,
    ISNULL(v.DeductionAmount, 0) AS DeductionAmount,
    ISNULL(v.Notes, N'') AS Notes
FROM EmployeeViolationCases v
LEFT JOIN Employees e ON e.Id = v.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = d.BranchId
WHERE v.Id = @Id AND ISNULL(v.IsDeleted, 0) = 0;
""",
            reader =>
            {
                var type = ToStringValue(reader["FinancialImpactType"], "None");
                var value = ToDecimal(reader["FinancialImpactValue"]);
                var amount = ToDecimal(reader["DeductionAmount"]);

                return new ViolationPrintData
                {
                    Id = ToInt(reader["Id"]),
                    ReferenceNo = ToStringValue(reader["ReferenceNo"]),
                    EmployeeId = ToInt(reader["EmployeeId"]),
                    EmployeeCode = ToStringValue(reader["EmployeeCode"]),
                    EmployeeName = ToStringValue(reader["EmployeeName"]),
                    Position = ToStringValue(reader["Position"], "-"),
                    Department = ToStringValue(reader["DepartmentName"], "-"),
                    Branch = ToStringValue(reader["BranchName"], "-"),
                    ViolationCategory = ToStringValue(reader["ViolationCategory"]),
                    ViolationTitle = ToStringValue(reader["ViolationTitle"]),
                    EventDate = ToDate(reader["EventDate"]),
                    Source = ToStringValue(reader["Source"]),
                    ActionStatus = ToStringValue(reader["ActionStatus"]),
                    Status = ToStringValue(reader["Status"]),
                    PenaltyAction = ToStringValue(reader["PenaltyAction"]),
                    FinancialImpactType = type,
                    FinancialImpactValue = value,
                    DeductionAmount = amount,
                    Notes = ToStringValue(reader["Notes"]),
                    FinancialImpactText = BuildFinancialImpactText(type, value, amount)
                };
            },
            command => Add(command, "@Id", id));

        return rows.FirstOrDefault();
    }

    private async Task<PrintFormSettings> LoadSettingsAsync()
    {
        var settings = new PrintFormSettings();

        var exists = await ScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID('DisciplinarySettings', 'U') IS NOT NULL THEN 1 ELSE 0 END;");

        if (exists == 0)
        {
            return settings;
        }

        var rows = await QueryAsync(
            "SELECT [Key], [Value] FROM DisciplinarySettings;",
            reader => new KeyValuePair<string, string>(ToStringValue(reader["Key"]), ToStringValue(reader["Value"])));

        var map = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.OrdinalIgnoreCase);

        settings.A4FormFilePath = GetFirst(map, "A4FormFilePath", "CompanyA4FormPath", "A4FilePath", "A4FormPath");
        settings.A4FormFileType = GetFirst(map, "A4FormFileType", "CompanyA4FormType", "A4FileType", "A4FormType");

        return settings;
    }

    private async Task<List<PrintTextBlock>> LoadTextBlocksAsync()
    {
        var exists = await ScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID('DisciplinaryFormTextBlocks', 'U') IS NOT NULL THEN 1 ELSE 0 END;");

        if (exists == 0)
        {
            return new List<PrintTextBlock>();
        }

        return await QueryAsync(
            """
SELECT
    Id,
    Area,
    Text,
    XPercent,
    YPercent,
    WidthPercent,
    FontFamily,
    FontSize,
    FontColor,
    IsBold,
    TextAlign
FROM DisciplinaryFormTextBlocks
WHERE ISNULL(IsActive, 1) = 1
ORDER BY Id;
""",
            reader => new PrintTextBlock
            {
                Id = ToInt(reader["Id"]),
                Area = ToStringValue(reader["Area"]),
                Text = ToStringValue(reader["Text"]),
                XPercent = ToDecimal(reader["XPercent"]),
                YPercent = ToDecimal(reader["YPercent"]),
                WidthPercent = ToDecimal(reader["WidthPercent"], 80),
                FontFamily = ToStringValue(reader["FontFamily"], "Tahoma"),
                FontSize = ToDecimal(reader["FontSize"], 14),
                FontColor = ToStringValue(reader["FontColor"], "#0f172a"),
                IsBold = ToBool(reader["IsBold"]),
                TextAlign = ToStringValue(reader["TextAlign"], "right")
            });
    }

    private List<PrintTextBlock> BuildFallbackBlocks()
    {
        return new List<PrintTextBlock>
        {
            new() { Text = "إشعار مخالفة وجزاء", XPercent = 8, YPercent = 25, WidthPercent = 84, FontSize = 22, FontColor = "#ff00cc", IsBold = true, TextAlign = "center" },
            new() { Text = "السيد/ة: {EmployeeName}", XPercent = 48, YPercent = 40, WidthPercent = 42, FontSize = 13, IsBold = true, TextAlign = "right" },
            new() { Text = "الرقم الوظيفي: {EmployeeCode}", XPercent = 48, YPercent = 44, WidthPercent = 42, FontSize = 13, IsBold = true, TextAlign = "right" },
            new() { Text = "القسم: {Department}", XPercent = 48, YPercent = 48, WidthPercent = 42, FontSize = 13, IsBold = true, TextAlign = "right" },
            new() { Text = "تاريخ المخالفة: {ViolationDate}", XPercent = 48, YPercent = 52, WidthPercent = 42, FontSize = 13, IsBold = true, TextAlign = "right" },
            new() { Text = "المخالفة: {ViolationName}", XPercent = 48, YPercent = 59, WidthPercent = 42, FontSize = 13, IsBold = true, TextAlign = "right" },
            new() { Text = "العقوبة: {PenaltyAction}", XPercent = 48, YPercent = 66, WidthPercent = 42, FontSize = 13, IsBold = true, TextAlign = "right" },
            new() { Text = "الأثر المالي: {FinancialImpact}", XPercent = 48, YPercent = 70, WidthPercent = 42, FontSize = 13, IsBold = true, TextAlign = "right" }
        };
    }

    private async Task<T> ScalarAsync<T>(string sql, Action<IDbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            var value = await command.ExecuteScalarAsync();

            if (value == null || value == DBNull.Value)
            {
                return default!;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<T>> QueryAsync<T>(string sql, Func<IDataRecord, T> map, Action<IDbCommand>? configure = null)
    {
        var result = new List<T>();
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(map(reader));
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
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

    private static string GetFirst(Dictionary<string, string> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string BuildFinancialImpactText(string type, decimal value, decimal deductionAmount)
    {
        var baseText = type switch
        {
            "Days" => $"{value:0.##} يوم حسب اللائحة",
            "Hours" => $"{value:0.##} ساعة حسب اللائحة",
            "Amount" => $"{value:0.##} مبلغ ثابت حسب اللائحة",
            _ => "لا يوجد أثر مالي حسب اللائحة"
        };

        if (deductionAmount > 0)
        {
            return $"{baseText} - مبلغ الخصم: {deductionAmount:N0} د.ع";
        }

        return baseText;
    }

    private static int ToInt(object value) => value == DBNull.Value ? 0 : Convert.ToInt32(value);

    private static decimal ToDecimal(object value, decimal fallback = 0) => value == DBNull.Value ? fallback : Convert.ToDecimal(value);

    private static DateTime ToDate(object value) => value == DBNull.Value ? DateTime.Today : Convert.ToDateTime(value);

    private static bool ToBool(object value) => value != DBNull.Value && Convert.ToBoolean(value);

    private static string ToStringValue(object value, string fallback = "") => value == DBNull.Value ? fallback : Convert.ToString(value) ?? fallback;
}

public sealed class ViolationPrintData
{
    public int Id { get; set; }
    public string ReferenceNo { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string ViolationCategory { get; set; } = string.Empty;
    public string ViolationTitle { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ActionStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PenaltyAction { get; set; } = string.Empty;
    public string FinancialImpactType { get; set; } = "None";
    public decimal FinancialImpactValue { get; set; }
    public decimal DeductionAmount { get; set; }
    public string FinancialImpactText { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class PrintFormSettings
{
    public string A4FormFilePath { get; set; } = string.Empty;
    public string A4FormFileType { get; set; } = "Image";
}

public sealed class PrintTextBlock
{
    public int Id { get; set; }
    public string Area { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public decimal XPercent { get; set; }
    public decimal YPercent { get; set; }
    public decimal WidthPercent { get; set; } = 80;
    public string FontFamily { get; set; } = "Tahoma";
    public decimal FontSize { get; set; } = 14;
    public string FontColor { get; set; } = "#0f172a";
    public bool IsBold { get; set; }
    public string TextAlign { get; set; } = "right";
}
