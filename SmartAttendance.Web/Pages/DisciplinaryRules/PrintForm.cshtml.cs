using System.Data.Common;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.DisciplinaryRules;

public class PrintFormModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public PrintFormModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public PrintDisciplinarySettings Settings { get; private set; } = new();
    public List<PrintFormTextBlock> TextBlocks { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await EnsureTablesAsync();
        Settings = await LoadSettingsAsync();
        TextBlocks = await LoadTextBlocksAsync();
    }

    private async Task EnsureTablesAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF OBJECT_ID('DisciplinarySettings', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinarySettings
    (
        [Key] nvarchar(120) NOT NULL PRIMARY KEY,
        [Value] nvarchar(max) NULL,
        UpdatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryFormTextBlocks', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryFormTextBlocks
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Area nvarchar(30) NOT NULL DEFAULT('Body'),
        Text nvarchar(max) NOT NULL,
        XPercent decimal(18,2) NOT NULL DEFAULT(8),
        YPercent decimal(18,2) NOT NULL DEFAULT(25),
        WidthPercent decimal(18,2) NOT NULL DEFAULT(84),
        FontFamily nvarchar(80) NOT NULL DEFAULT('Tahoma'),
        FontSize int NOT NULL DEFAULT(14),
        FontColor nvarchar(20) NOT NULL DEFAULT('#0b1d31'),
        IsBold bit NOT NULL DEFAULT(0),
        TextAlign nvarchar(20) NOT NULL DEFAULT('right'),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;
""");
    }

    private async Task<PrintDisciplinarySettings> LoadSettingsAsync()
    {
        var items = await HrmsDatabase.QueryAsync(
            _dbContext,
            "SELECT [Key], ISNULL([Value], '') AS [Value] FROM DisciplinarySettings;",
            command => { },
            reader => new KeyValuePair<string, string>(
                HrmsDatabase.GetString(reader, "Key"),
                HrmsDatabase.GetString(reader, "Value")));

        var map = items.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        return new PrintDisciplinarySettings
        {
            ApprovingAuthorityName = GetString(map, "ApprovingAuthorityName", "قسم الموارد البشرية"),
            HeaderImagePath = GetString(map, "HeaderImagePath", string.Empty),
            FooterImagePath = GetString(map, "FooterImagePath", string.Empty),
            A4FormFilePath = GetString(map, "A4FormFilePath", string.Empty),
            A4FormFileType = GetString(map, "A4FormFileType", string.Empty)
        };
    }

    private async Task<List<PrintFormTextBlock>> LoadTextBlocksAsync()
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
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
    TextAlign,
    IsActive,
    CreatedAt
FROM DisciplinaryFormTextBlocks
ORDER BY
    CASE Area WHEN 'Header' THEN 1 WHEN 'Body' THEN 2 ELSE 3 END,
    YPercent,
    XPercent;
""",
            command => { },
            reader => new PrintFormTextBlock
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Area = HrmsDatabase.GetString(reader, "Area"),
                Text = HrmsDatabase.GetString(reader, "Text"),
                XPercent = GetDecimal(reader, "XPercent"),
                YPercent = GetDecimal(reader, "YPercent"),
                WidthPercent = GetDecimal(reader, "WidthPercent"),
                FontFamily = HrmsDatabase.GetString(reader, "FontFamily"),
                FontSize = HrmsDatabase.GetInt(reader, "FontSize"),
                FontColor = HrmsDatabase.GetString(reader, "FontColor"),
                IsBold = HrmsDatabase.GetBool(reader, "IsBold"),
                TextAlign = HrmsDatabase.GetString(reader, "TextAlign"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    public string BuildTextBlockStyle(PrintFormTextBlock block)
    {
        var fontWeight = block.IsBold ? "900" : "700";
        return $"right:{block.XPercent:0.##}%;top:{block.YPercent:0.##}%;width:{block.WidthPercent:0.##}%;font-family:'{block.FontFamily}',Tahoma,Arial,sans-serif;font-size:{block.FontSize}px;color:{block.FontColor};font-weight:{fontWeight};text-align:{block.TextAlign};";
    }

    public string ReplaceSampleTokens(string text)
    {
        return text
            .Replace("{EmployeeName}", "محمد علي")
            .Replace("{EmployeeCode}", "EMP-001")
            .Replace("{Department}", "قسم الموارد البشرية")
            .Replace("{Position}", "موظف")
            .Replace("{ViolationDate}", DateTime.Today.ToString("yyyy-MM-dd"))
            .Replace("{ViolationCategory}", "مخالفات الحضور والانصراف")
            .Replace("{ViolationName}", "التأخر عن موعد الدوام الرسمي")
            .Replace("{ViolationDescription}", "تأخر الموظف عن بداية الدوام حسب الشفت المعتمد.")
            .Replace("{OccurrenceNumber}", "1")
            .Replace("{PenaltyAction}", "خصم نصف يوم من الراتب")
            .Replace("{FinancialImpact}", "0.5 يوم")
            .Replace("{ApprovedBy}", Settings.ApprovingAuthorityName);
    }

    private static string GetString(Dictionary<string, string> map, string key, string defaultValue)
    {
        return map.TryGetValue(key, out var value) ? value : defaultValue;
    }

    private static decimal GetDecimal(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToDecimal(reader.GetValue(ordinal));
    }
}

public sealed class PrintDisciplinarySettings
{
    public string ApprovingAuthorityName { get; set; } = "قسم الموارد البشرية";
    public string HeaderImagePath { get; set; } = "";
    public string FooterImagePath { get; set; } = "";
    public string A4FormFilePath { get; set; } = "";
    public string A4FormFileType { get; set; } = "";
}

public sealed class PrintFormTextBlock
{
    public int Id { get; set; }
    public string Area { get; set; } = "Body";
    public string Text { get; set; } = "";
    public decimal XPercent { get; set; }
    public decimal YPercent { get; set; }
    public decimal WidthPercent { get; set; }
    public string FontFamily { get; set; } = "Tahoma";
    public int FontSize { get; set; } = 14;
    public string FontColor { get; set; } = "#0b1d31";
    public bool IsBold { get; set; }
    public string TextAlign { get; set; } = "right";
    public bool IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
}




