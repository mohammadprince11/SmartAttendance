using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.DisciplinaryRules;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(ApplicationDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    public string Tab { get; private set; } = "setup";
    public int SelectedCategoryId { get; private set; }
    public List<ViolationCategory> Categories { get; private set; } = new();
    public List<ViolationType> ViolationTypes { get; private set; } = new();
    public List<PenaltyRule> PenaltyRules { get; private set; } = new();
    public DisciplinarySettings Settings { get; private set; } = new();
    public List<MessageTemplate> MessageTemplates { get; private set; } = new();
    public List<TemplateType> TemplateTypes { get; private set; } = new();
    public List<FormTextBlock> TextBlocks { get; private set; } = new();

    public string[] FontFamilies { get; } = { "Tahoma", "Arial", "Calibri", "Times New Roman", "Cairo" };

    public string[] TemplateTokens { get; } =
    {
        "{EmployeeName}", "{EmployeeCode}", "{Department}", "{Position}",
        "{ViolationDate}", "{ViolationCategory}", "{ViolationName}", "{ViolationDescription}",
        "{OccurrenceNumber}", "{PenaltyAction}", "{FinancialImpact}", "{ApprovedBy}"
    };

    public string DefaultTemplateBody { get; } = """
السيد/ة: {EmployeeName}
الرقم الوظيفي: {EmployeeCode}
القسم: {Department}

نود إعلامكم بأنه تم تسجيل مخالفة بحقكم بتاريخ {ViolationDate}
وذلك بسبب: {ViolationName}

فئة المخالفة:
{ViolationCategory}

وصف المخالفة:
{ViolationDescription}

وبناءً على لائحة الجزاءات المعتمدة، تقرر تطبيق العقوبة التالية:
{PenaltyAction}

الأثر المالي:
{FinancialImpact}

يرجى الالتزام بتعليمات الشركة لتجنب تكرار المخالفة.

قسم الموارد البشرية
""";

    public string DefaultMainBodyText { get; } = """
إشعار مخالفة وجزاء

السيد/ة: {EmployeeName}
الرقم الوظيفي: {EmployeeCode}
القسم: {Department}
تاريخ المخالفة: {ViolationDate}

المخالفة: {ViolationName}
فئة المخالفة: {ViolationCategory}
العقوبة: {PenaltyAction}
الأثر المالي: {FinancialImpact}
""";

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string? tab, int? categoryId)
    {
        await LoadPageAsync(tab, categoryId);
        return Page();
    }

    public async Task<IActionResult> OnPostSeedLibraryAsync()
    {
        await EnsureTablesAsync();
        await SeedDefaultLibraryAsync(false);
        StatusMessage = "تم تحميل / تحديث مكتبة إعدادات المخالفات الأولية.";
        return RedirectToPage(new { tab = "setup" });
    }


    public async Task<IActionResult> OnPostSaveSettingsAsync(int appealWindowDays, string? defaultTemplateType, string? approvingAuthorityName, string? documentNumberFormat, string? formHeader, string? formFooter)
    {
        await EnsureTablesAsync();
        await UpsertSettingAsync("RequiresCommitteeApproval", Request.Form.ContainsKey("requiresCommitteeApproval") ? "true" : "false");
        await UpsertSettingAsync("AllowEmployeeAppeal", Request.Form.ContainsKey("allowEmployeeAppeal") ? "true" : "false");
        await UpsertSettingAsync("AppealWindowDays", Math.Max(0, appealWindowDays).ToString());
        await UpsertSettingAsync("DefaultTemplateType", string.IsNullOrWhiteSpace(defaultTemplateType) ? "PenaltyNotice" : defaultTemplateType.Trim());
        await UpsertSettingAsync("ApprovingAuthorityName", string.IsNullOrWhiteSpace(approvingAuthorityName) ? "قسم الموارد البشرية" : approvingAuthorityName.Trim());
        await UpsertSettingAsync("DocumentNumberFormat", string.IsNullOrWhiteSpace(documentNumberFormat) ? "DISC-{yyyy}-{0000}" : documentNumberFormat.Trim());
        await UpsertSettingAsync("FormHeader", formHeader?.Trim() ?? string.Empty);
        await UpsertSettingAsync("FormFooter", formFooter?.Trim() ?? string.Empty);
        StatusMessage = "تم حفظ إعدادات التهيئة.";
        return RedirectToPage(new { tab = "setup" });
    }


    public async Task<IActionResult> OnPostSeedBodyTextLayersAsync()
    {
        await EnsureTablesAsync();
        await SeedBodyTextLayersAsync();

        
        await UpsertSettingAsync("BodyTextLayersSeeded", "true");StatusMessage = "تم إنشاء النصوص الأساسية كطبقات منفصلة. افتح كل طبقة وعدّلها مثل Word.";
        return RedirectToPage(new { tab = "designer" });
    }

    public async Task<IActionResult> OnPostSaveA4FormAsync(IFormFile? a4FormFile)
    {
        await EnsureTablesAsync();

        var saved = await SaveA4FormFileAsync(a4FormFile);
        if (!string.IsNullOrWhiteSpace(saved.Path))
        {
            await UpsertSettingAsync("A4FormFilePath", saved.Path);
            await UpsertSettingAsync("A4FormFileType", saved.Type);
            StatusMessage = "A4 form saved.";
        }
        else
        {
            StatusMessage = "Choose an A4 form as PNG, JPG, WEBP, or PDF.";
        }

        return RedirectToPage(new { tab = "designer" });
    }
    public async Task<IActionResult> OnPostSaveFormImagesAsync(IFormFile? headerImage, IFormFile? footerImage)
    {
        await EnsureTablesAsync();

        var headerPath = await SaveImageAsync(headerImage, "header");
        if (!string.IsNullOrWhiteSpace(headerPath))
        {
            await UpsertSettingAsync("HeaderImagePath", headerPath);
        }

        var footerPath = await SaveImageAsync(footerImage, "footer");
        if (!string.IsNullOrWhiteSpace(footerPath))
        {
            await UpsertSettingAsync("FooterImagePath", footerPath);
        }

        StatusMessage = "تم حفظ صور الهيدر والفوتر.";
        return RedirectToPage(new { tab = "designer" });
    }

    public async Task<IActionResult> OnPostSaveMainBodyDesignAsync(
        string? mainBodyText,
        decimal mainBodyXPercent,
        decimal mainBodyYPercent,
        decimal mainBodyWidthPercent,
        string mainBodyFontFamily,
        int mainBodyFontSize,
        string mainBodyFontColor,
        string mainBodyTextAlign)
    {
        await EnsureTablesAsync();

        await UpsertSettingAsync("MainBodyText", string.IsNullOrWhiteSpace(mainBodyText) ? DefaultMainBodyText : mainBodyText.Trim());
        await UpsertSettingAsync("MainBodyXPercent", Clamp(mainBodyXPercent, 0, 100).ToString("0.##"));
        await UpsertSettingAsync("MainBodyYPercent", Clamp(mainBodyYPercent, 0, 100).ToString("0.##"));
        await UpsertSettingAsync("MainBodyWidthPercent", Clamp(mainBodyWidthPercent, 5, 100).ToString("0.##"));
        await UpsertSettingAsync("MainBodyFontFamily", NormalizeFont(mainBodyFontFamily));
        await UpsertSettingAsync("MainBodyFontSize", Math.Clamp(mainBodyFontSize, 8, 60).ToString());
        await UpsertSettingAsync("MainBodyFontColor", NormalizeColor(mainBodyFontColor));
        await UpsertSettingAsync("MainBodyIsBold", Request.Form.ContainsKey("mainBodyIsBold") ? "true" : "false");
        await UpsertSettingAsync("MainBodyTextAlign", NormalizeTextAlign(mainBodyTextAlign));

        StatusMessage = "تم حفظ تعديل النص الأساسي داخل الورقة.";
        return RedirectToPage(new { tab = "designer" });
    }

    public async Task<IActionResult> OnPostCreateTextBlockAsync(
        string area,
        string text,
        decimal xPercent,
        decimal yPercent,
        decimal widthPercent,
        string fontFamily,
        int fontSize,
        string fontColor,
        string textAlign)
    {
        await EnsureTablesAsync();

        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "اكتب نص صندوق النص أولاً.";
            return RedirectToPage(new { tab = "designer" });
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO DisciplinaryFormTextBlocks
(Area, Text, XPercent, YPercent, WidthPercent, FontFamily, FontSize, FontColor, IsBold, TextAlign, IsActive, CreatedAt)
VALUES
(@Area, @Text, @XPercent, @YPercent, @WidthPercent, @FontFamily, @FontSize, @FontColor, @IsBold, @TextAlign, @IsActive, SYSUTCDATETIME());
""",
            command => AddTextBlockParameters(command, area, text, xPercent, yPercent, widthPercent, fontFamily, fontSize, fontColor, textAlign));

        StatusMessage = "تمت إضافة صندوق نص إلى الفورمة.";
        return RedirectToPage(new { tab = "designer" });
    }

    public async Task<IActionResult> OnPostUpdateTextBlockAsync(
        int id,
        string area,
        string text,
        decimal xPercent,
        decimal yPercent,
        decimal widthPercent,
        string fontFamily,
        int fontSize,
        string fontColor,
        string textAlign)
    {
        await EnsureTablesAsync();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE DisciplinaryFormTextBlocks
SET Area = @Area,
    Text = @Text,
    XPercent = @XPercent,
    YPercent = @YPercent,
    WidthPercent = @WidthPercent,
    FontFamily = @FontFamily,
    FontSize = @FontSize,
    FontColor = @FontColor,
    IsBold = @IsBold,
    TextAlign = @TextAlign,
    IsActive = @IsActive
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                AddTextBlockParameters(command, area, text, xPercent, yPercent, widthPercent, fontFamily, fontSize, fontColor, textAlign);
            });

        StatusMessage = "تم تعديل صندوق النص.";
        return RedirectToPage(new { tab = "designer" });
    }

    public async Task<IActionResult> OnPostDeleteTextBlockAsync(int id)
    {
        await EnsureTablesAsync();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "DELETE FROM DisciplinaryFormTextBlocks WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        StatusMessage = "تم حذف صندوق النص.";
        return RedirectToPage(new { tab = "designer" });
    }

    private void AddTextBlockParameters(
        DbCommand command,
        string area,
        string text,
        decimal xPercent,
        decimal yPercent,
        decimal widthPercent,
        string fontFamily,
        int fontSize,
        string fontColor,
        string textAlign)
    {
        HrmsDatabase.AddParameter(command, "@Area", NormalizeArea(area));
        HrmsDatabase.AddParameter(command, "@Text", text.Trim());
        HrmsDatabase.AddParameter(command, "@XPercent", Clamp(xPercent, 0, 100));
        HrmsDatabase.AddParameter(command, "@YPercent", Clamp(yPercent, 0, 100));
        HrmsDatabase.AddParameter(command, "@WidthPercent", Clamp(widthPercent, 5, 100));
        HrmsDatabase.AddParameter(command, "@FontFamily", NormalizeFont(fontFamily));
        HrmsDatabase.AddParameter(command, "@FontSize", Math.Clamp(fontSize, 8, 60));
        HrmsDatabase.AddParameter(command, "@FontColor", NormalizeColor(fontColor));
        HrmsDatabase.AddParameter(command, "@IsBold", Request.Form.ContainsKey("isBold"));
        HrmsDatabase.AddParameter(command, "@TextAlign", NormalizeTextAlign(textAlign));
        HrmsDatabase.AddParameter(command, "@IsActive", Request.Form.ContainsKey("isActive"));
    }

    private async Task<(string Path, string Type)> SaveA4FormFileAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".pdf"
        };

        if (!allowed.Contains(extension))
        {
            return (string.Empty, string.Empty);
        }

        var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "disciplinary-forms");
        Directory.CreateDirectory(uploadsRoot);

        var fileName = $"a4-form_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
        var fullPath = Path.Combine(uploadsRoot, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream);

        var type = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "Pdf" : "Image";
        return ($"/uploads/disciplinary-forms/{fileName}", type);
    }
    private async Task<string> SaveImageAsync(IFormFile? file, string prefix)
    {
        if (file == null || file.Length == 0)
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp"
        };

        if (!allowed.Contains(extension))
        {
            StatusMessage = "نوع الصورة غير مدعوم. استخدم PNG أو JPG أو WEBP.";
            return string.Empty;
        }

        var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "disciplinary-forms");
        Directory.CreateDirectory(uploadsRoot);

        var fileName = $"{prefix}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
        var fullPath = Path.Combine(uploadsRoot, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream);

        return $"/uploads/disciplinary-forms/{fileName}";
    }

    public string BuildTextBlockStyle(FormTextBlock block)
    {
        var fontWeight = block.IsBold ? "900" : "700";
        return $"right:{block.XPercent:0.##}%;top:{block.YPercent:0.##}%;width:{block.WidthPercent:0.##}%;font-family:'{block.FontFamily}',Tahoma,Arial,sans-serif;font-size:{block.FontSize}px;color:{block.FontColor};font-weight:{fontWeight};text-align:{block.TextAlign};";
    }

    public string BuildMainBodyStyle()
    {
        var fontWeight = Settings.MainBodyIsBold ? "900" : "700";
        return $"right:{Settings.MainBodyXPercent:0.##}%;top:{Settings.MainBodyYPercent:0.##}%;width:{Settings.MainBodyWidthPercent:0.##}%;font-family:'{Settings.MainBodyFontFamily}',Tahoma,Arial,sans-serif;font-size:{Settings.MainBodyFontSize}px;color:{Settings.MainBodyFontColor};font-weight:{fontWeight};text-align:{Settings.MainBodyTextAlign};";
    }

    public async Task<IActionResult> OnPostCreateCategoryAsync(string name, string? description, int displayOrder = 10)
    {
        await EnsureTablesAsync();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "اكتب اسم الفئة أولاً.";
            return RedirectToPage(new { tab = "library" });
        }

        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
INSERT INTO DisciplinaryViolationCategories (Name, Description, DisplayOrder, IsActive, CreatedAt)
VALUES (@Name, @Description, @DisplayOrder, 1, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Name", name.Trim());
                HrmsDatabase.AddParameter(command, "@Description", description?.Trim() ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@DisplayOrder", displayOrder);
            });

        StatusMessage = "تمت إضافة فئة مخالفة جديدة.";
        return RedirectToPage(new { tab = "library" });
    }

    public async Task<IActionResult> OnPostUpdateCategoryAsync(int id, string name, string? description, int displayOrder)
    {
        await EnsureTablesAsync();
        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
UPDATE DisciplinaryViolationCategories
SET Name = @Name, Description = @Description, DisplayOrder = @DisplayOrder, IsActive = @IsActive
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Name", name.Trim());
                HrmsDatabase.AddParameter(command, "@Description", description?.Trim() ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@DisplayOrder", displayOrder);
                HrmsDatabase.AddParameter(command, "@IsActive", Request.Form.ContainsKey("isActive"));
            });

        StatusMessage = "تم تعديل فئة المخالفة.";
        return RedirectToPage(new { tab = "library", categoryId = id });
    }

    public async Task<IActionResult> OnPostCreateViolationTypeAsync(int categoryId, string name, string? description, string severity, int validityMonths, string countingPeriod)
    {
        await EnsureTablesAsync();
        if (categoryId <= 0 || string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "اختر الفئة واكتب اسم المخالفة.";
            return RedirectToPage(new { tab = "library", categoryId });
        }

        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
INSERT INTO DisciplinaryViolationTypes
(CategoryId, Name, Description, Severity, ValidityMonths, CountingPeriod, IncludeInEvaluation, ShowToEmployee, IsActive, CreatedAt)
VALUES
(@CategoryId, @Name, @Description, @Severity, @ValidityMonths, @CountingPeriod, @IncludeInEvaluation, @ShowToEmployee, @IsActive, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CategoryId", categoryId);
                HrmsDatabase.AddParameter(command, "@Name", name.Trim());
                HrmsDatabase.AddParameter(command, "@Description", description?.Trim() ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@Severity", NormalizeSeverity(severity));
                HrmsDatabase.AddParameter(command, "@ValidityMonths", Math.Max(1, validityMonths));
                HrmsDatabase.AddParameter(command, "@CountingPeriod", NormalizePeriod(countingPeriod));
                HrmsDatabase.AddParameter(command, "@IncludeInEvaluation", Request.Form.ContainsKey("includeInEvaluation"));
                HrmsDatabase.AddParameter(command, "@ShowToEmployee", Request.Form.ContainsKey("showToEmployee"));
                HrmsDatabase.AddParameter(command, "@IsActive", Request.Form.ContainsKey("isActive"));
            });

        StatusMessage = "تمت إضافة مخالفة جديدة داخل الفئة.";
        return RedirectToPage(new { tab = "library", categoryId });
    }

    public async Task<IActionResult> OnPostUpdateViolationTypeAsync(int id, int categoryId, string name, string? description, string severity, int validityMonths, string countingPeriod)
    {
        await EnsureTablesAsync();
        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
UPDATE DisciplinaryViolationTypes
SET CategoryId = @CategoryId,
    Name = @Name,
    Description = @Description,
    Severity = @Severity,
    ValidityMonths = @ValidityMonths,
    CountingPeriod = @CountingPeriod,
    IncludeInEvaluation = @IncludeInEvaluation,
    ShowToEmployee = @ShowToEmployee,
    IsActive = @IsActive
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@CategoryId", categoryId);
                HrmsDatabase.AddParameter(command, "@Name", name.Trim());
                HrmsDatabase.AddParameter(command, "@Description", description?.Trim() ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@Severity", NormalizeSeverity(severity));
                HrmsDatabase.AddParameter(command, "@ValidityMonths", Math.Max(1, validityMonths));
                HrmsDatabase.AddParameter(command, "@CountingPeriod", NormalizePeriod(countingPeriod));
                HrmsDatabase.AddParameter(command, "@IncludeInEvaluation", Request.Form.ContainsKey("includeInEvaluation"));
                HrmsDatabase.AddParameter(command, "@ShowToEmployee", Request.Form.ContainsKey("showToEmployee"));
                HrmsDatabase.AddParameter(command, "@IsActive", Request.Form.ContainsKey("isActive"));
            });

        StatusMessage = "تم تعديل المخالفة.";
        return RedirectToPage(new { tab = "library", categoryId });
    }

    public async Task<IActionResult> OnPostCreatePenaltyRuleAsync(int violationTypeId, int occurrenceFrom, int occurrenceTo, string countingPeriod, string penaltyAction, string financialImpactType, decimal financialValue, int validityMonths, string calculationMode)
    {
        await EnsureTablesAsync();
        if (violationTypeId <= 0 || string.IsNullOrWhiteSpace(penaltyAction))
        {
            StatusMessage = "اختر المخالفة واكتب العقوبة.";
            return RedirectToPage(new { tab = "library" });
        }

        await SavePenaltyRuleAsync(null, violationTypeId, occurrenceFrom, occurrenceTo, countingPeriod, penaltyAction, financialImpactType, financialValue, validityMonths, calculationMode);
        var categoryId = await GetViolationCategoryIdAsync(violationTypeId);
        StatusMessage = "تمت إضافة جزاء للتكرار.";
        return RedirectToPage(new { tab = "library", categoryId });
    }

    public async Task<IActionResult> OnPostUpdatePenaltyRuleAsync(int id, int violationTypeId, int occurrenceFrom, int occurrenceTo, string countingPeriod, string penaltyAction, string financialImpactType, decimal financialValue, int validityMonths, string calculationMode)
    {
        await EnsureTablesAsync();
        await SavePenaltyRuleAsync(id, violationTypeId, occurrenceFrom, occurrenceTo, countingPeriod, penaltyAction, financialImpactType, financialValue, validityMonths, calculationMode);
        var categoryId = await GetViolationCategoryIdAsync(violationTypeId);
        StatusMessage = "تم تعديل جزاء التكرار.";
        return RedirectToPage(new { tab = "library", categoryId });
    }

    public async Task<IActionResult> OnPostDeletePenaltyRuleAsync(int id)
    {
        await EnsureTablesAsync();
        var categoryId = await HrmsDatabase.ScalarAsync<int>(_dbContext,
            """
SELECT TOP 1 t.CategoryId
FROM DisciplinaryPenaltyRules r
INNER JOIN DisciplinaryViolationTypes t ON t.Id = r.ViolationTypeId
WHERE r.Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        await HrmsDatabase.ExecuteAsync(_dbContext, "DELETE FROM DisciplinaryPenaltyRules WHERE Id = @Id;", command => HrmsDatabase.AddParameter(command, "@Id", id));
        StatusMessage = "تم حذف جزاء التكرار.";
        return RedirectToPage(new { tab = "library", categoryId });
    }


    public async Task<IActionResult> OnPostCreateTemplateTypeAsync(string name, string code, string? description)
    {
        await EnsureTablesAsync();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            StatusMessage = "اكتب اسم ونوع القالب.";
            return RedirectToPage(new { tab = "templates" });
        }

        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
IF NOT EXISTS (SELECT 1 FROM DisciplinaryTemplateTypes WHERE Code = @Code)
BEGIN
    INSERT INTO DisciplinaryTemplateTypes(Name, Code, Description, IsActive, CreatedAt)
    VALUES(@Name, @Code, @Description, 1, SYSUTCDATETIME());
END
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Name", name.Trim());
                HrmsDatabase.AddParameter(command, "@Code", NormalizeTemplateCode(code));
                HrmsDatabase.AddParameter(command, "@Description", description?.Trim() ?? string.Empty);
            });

        StatusMessage = "تمت إضافة نوع قالب جديد.";
        return RedirectToPage(new { tab = "templates" });
    }

    public async Task<IActionResult> OnPostUpdateTemplateTypeAsync(int id, string name, string code, string? description)
    {
        await EnsureTablesAsync();
        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
UPDATE DisciplinaryTemplateTypes
SET Name = @Name,
    Code = @Code,
    Description = @Description,
    IsActive = @IsActive
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Name", name.Trim());
                HrmsDatabase.AddParameter(command, "@Code", NormalizeTemplateCode(code));
                HrmsDatabase.AddParameter(command, "@Description", description?.Trim() ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@IsActive", Request.Form.ContainsKey("isActive"));
            });

        StatusMessage = "تم تعديل نوع القالب.";
        return RedirectToPage(new { tab = "templates" });
    }

    public async Task<IActionResult> OnPostCreateTemplateAsync(string name, string templateType, string subject, string body)
    {
        await EnsureTablesAsync();
        var normalizedType = NormalizeTemplateCode(templateType);
        if (Request.Form.ContainsKey("isDefault")) await ClearDefaultTemplateAsync(normalizedType);

        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
INSERT INTO DisciplinaryMessageTemplates (Name, TemplateType, Subject, Body, IsDefault, IsActive, CreatedAt)
VALUES (@Name, @TemplateType, @Subject, @Body, @IsDefault, @IsActive, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Name", name.Trim());
                HrmsDatabase.AddParameter(command, "@TemplateType", normalizedType);
                HrmsDatabase.AddParameter(command, "@Subject", subject.Trim());
                HrmsDatabase.AddParameter(command, "@Body", body.Trim());
                HrmsDatabase.AddParameter(command, "@IsDefault", Request.Form.ContainsKey("isDefault"));
                HrmsDatabase.AddParameter(command, "@IsActive", Request.Form.ContainsKey("isActive"));
            });

        StatusMessage = "تمت إضافة قالب رسالة جديد.";
        return RedirectToPage(new { tab = "templates" });
    }

    public async Task<IActionResult> OnPostUpdateTemplateAsync(int id, string name, string templateType, string subject, string body)
    {
        await EnsureTablesAsync();
        var normalizedType = NormalizeTemplateCode(templateType);
        if (Request.Form.ContainsKey("isDefault")) await ClearDefaultTemplateAsync(normalizedType);

        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
UPDATE DisciplinaryMessageTemplates
SET Name = @Name,
    TemplateType = @TemplateType,
    Subject = @Subject,
    Body = @Body,
    IsDefault = @IsDefault,
    IsActive = @IsActive
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Name", name.Trim());
                HrmsDatabase.AddParameter(command, "@TemplateType", normalizedType);
                HrmsDatabase.AddParameter(command, "@Subject", subject.Trim());
                HrmsDatabase.AddParameter(command, "@Body", body.Trim());
                HrmsDatabase.AddParameter(command, "@IsDefault", Request.Form.ContainsKey("isDefault"));
                HrmsDatabase.AddParameter(command, "@IsActive", Request.Form.ContainsKey("isActive"));
            });

        StatusMessage = "تم تعديل قالب الرسالة.";
        return RedirectToPage(new { tab = "templates" });
    }

    private async Task SavePenaltyRuleAsync(int? id, int violationTypeId, int occurrenceFrom, int occurrenceTo, string countingPeriod, string penaltyAction, string financialImpactType, decimal financialValue, int validityMonths, string calculationMode)
    {
        occurrenceFrom = Math.Max(1, occurrenceFrom);
        occurrenceTo = Math.Max(occurrenceFrom, occurrenceTo);
        validityMonths = Math.Max(1, validityMonths);

        var sql = id.HasValue
            ? """
UPDATE DisciplinaryPenaltyRules
SET ViolationTypeId = @ViolationTypeId,
    OccurrenceFrom = @OccurrenceFrom,
    OccurrenceTo = @OccurrenceTo,
    CountingPeriod = @CountingPeriod,
    PenaltyAction = @PenaltyAction,
    FinancialImpactType = @FinancialImpactType,
    FinancialValue = @FinancialValue,
    ValidityMonths = @ValidityMonths,
    CalculationMode = @CalculationMode,
    RequiresApproval = @RequiresApproval
WHERE Id = @Id;
"""
            : """
INSERT INTO DisciplinaryPenaltyRules
(ViolationTypeId, OccurrenceFrom, OccurrenceTo, CountingPeriod, PenaltyAction, FinancialImpactType, FinancialValue, ValidityMonths, CalculationMode, RequiresApproval, IsActive, CreatedAt)
VALUES
(@ViolationTypeId, @OccurrenceFrom, @OccurrenceTo, @CountingPeriod, @PenaltyAction, @FinancialImpactType, @FinancialValue, @ValidityMonths, @CalculationMode, @RequiresApproval, 1, SYSUTCDATETIME());
""";

        await HrmsDatabase.ExecuteAsync(_dbContext, sql, command =>
        {
            if (id.HasValue) HrmsDatabase.AddParameter(command, "@Id", id.Value);
            HrmsDatabase.AddParameter(command, "@ViolationTypeId", violationTypeId);
            HrmsDatabase.AddParameter(command, "@OccurrenceFrom", occurrenceFrom);
            HrmsDatabase.AddParameter(command, "@OccurrenceTo", occurrenceTo);
            HrmsDatabase.AddParameter(command, "@CountingPeriod", NormalizePeriod(countingPeriod));
            HrmsDatabase.AddParameter(command, "@PenaltyAction", penaltyAction.Trim());
            HrmsDatabase.AddParameter(command, "@FinancialImpactType", NormalizeFinancialType(financialImpactType));
            HrmsDatabase.AddParameter(command, "@FinancialValue", Math.Max(0, financialValue));
            HrmsDatabase.AddParameter(command, "@ValidityMonths", validityMonths);
            HrmsDatabase.AddParameter(command, "@CalculationMode", NormalizeCalculationMode(calculationMode));
            HrmsDatabase.AddParameter(command, "@RequiresApproval", Request.Form.ContainsKey("requiresApproval"));
        });
    }

    private async Task LoadPageAsync(string? tab, int? categoryId)
    {
        await EnsureTablesAsync();
        // NEXORA: auto seed disabled for clean database reset.
        Tab = NormalizeTab(tab);
        SelectedCategoryId = categoryId.GetValueOrDefault();
        Settings = await LoadSettingsAsync();
        Categories = await LoadCategoriesAsync();
        if (SelectedCategoryId > 0 && Categories.All(x => x.Id != SelectedCategoryId)) SelectedCategoryId = 0;
        ViolationTypes = await LoadViolationTypesAsync();
        PenaltyRules = await LoadPenaltyRulesAsync();
        TemplateTypes = await LoadTemplateTypesAsync();
        MessageTemplates = await LoadMessageTemplatesAsync();
        TextBlocks = await LoadTextBlocksAsync();
    }

    private async Task EnsureTablesAsync()
    {
await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
IF OBJECT_ID('DisciplinaryViolationCategories', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryViolationCategories
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(180) NOT NULL,
        Description nvarchar(max) NULL,
        DisplayOrder int NOT NULL DEFAULT(10),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryViolationTypes', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryViolationTypes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CategoryId int NOT NULL,
        Name nvarchar(250) NOT NULL,
        Description nvarchar(max) NULL,
        Severity nvarchar(40) NOT NULL DEFAULT('B'),
        ValidityMonths int NOT NULL DEFAULT(6),
        CountingPeriod nvarchar(40) NOT NULL DEFAULT('Monthly'),
        IncludeInEvaluation bit NOT NULL DEFAULT(1),
        ShowToEmployee bit NOT NULL DEFAULT(1),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryPenaltyRules', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryPenaltyRules
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ViolationTypeId int NOT NULL,
        OccurrenceFrom int NOT NULL,
        OccurrenceTo int NOT NULL,
        CountingPeriod nvarchar(40) NOT NULL DEFAULT('Monthly'),
        PenaltyAction nvarchar(250) NOT NULL,
        FinancialImpactType nvarchar(40) NOT NULL DEFAULT('None'),
        FinancialValue decimal(18,2) NOT NULL DEFAULT(0),
        ValidityMonths int NOT NULL DEFAULT(6),
        CalculationMode nvarchar(40) NOT NULL DEFAULT('Cumulative'),
        RequiresApproval bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryMessageTemplates', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryMessageTemplates
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(180) NOT NULL,
        TemplateType nvarchar(60) NOT NULL DEFAULT('PenaltyNotice'),
        Subject nvarchar(250) NOT NULL,
        Body nvarchar(max) NOT NULL,
        IsDefault bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('DisciplinaryTemplateTypes', 'U') IS NULL
BEGIN
    CREATE TABLE DisciplinaryTemplateTypes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(180) NOT NULL,
        Code nvarchar(80) NOT NULL,
        Description nvarchar(max) NULL,
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

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

    private async Task SeedDefaultLibraryAsync(bool onlyIfEmpty)
    {
var count = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT COUNT(1) FROM DisciplinaryViolationCategories;");
        if (onlyIfEmpty && count > 0) { await SeedTemplateTypesAndSettingsAsync(); return; }

        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
IF NOT EXISTS (SELECT 1 FROM DisciplinaryViolationCategories WHERE Name = N'مخالفات الحضور والانصراف')
    INSERT INTO DisciplinaryViolationCategories(Name, Description, DisplayOrder) VALUES(N'مخالفات الحضور والانصراف', N'التأخير، الغياب، الخروج المبكر، ونسيان البصمة.', 10);
IF NOT EXISTS (SELECT 1 FROM DisciplinaryViolationCategories WHERE Name = N'مخالفات المظهر العام')
    INSERT INTO DisciplinaryViolationCategories(Name, Description, DisplayOrder) VALUES(N'مخالفات المظهر العام', N'الزي الرسمي، النظافة الشخصية، والمظهر اللائق بالعمل.', 20);
IF NOT EXISTS (SELECT 1 FROM DisciplinaryViolationCategories WHERE Name = N'مخالفات نظام العمل')
    INSERT INTO DisciplinaryViolationCategories(Name, Description, DisplayOrder) VALUES(N'مخالفات نظام العمل', N'الالتزام بالإجراءات الداخلية وأصول ومعدات الشركة.', 30);
IF NOT EXISTS (SELECT 1 FROM DisciplinaryViolationCategories WHERE Name = N'مخالفات سلوكية')
    INSERT INTO DisciplinaryViolationCategories(Name, Description, DisplayOrder) VALUES(N'مخالفات سلوكية', N'التعامل، الاحترام، الانضباط، وتقبل التوجيه.', 40);

DECLARE @Attendance int = (SELECT TOP 1 Id FROM DisciplinaryViolationCategories WHERE Name = N'مخالفات الحضور والانصراف');
DECLARE @Appearance int = (SELECT TOP 1 Id FROM DisciplinaryViolationCategories WHERE Name = N'مخالفات المظهر العام');
DECLARE @WorkSystem int = (SELECT TOP 1 Id FROM DisciplinaryViolationCategories WHERE Name = N'مخالفات نظام العمل');
DECLARE @Behavior int = (SELECT TOP 1 Id FROM DisciplinaryViolationCategories WHERE Name = N'مخالفات سلوكية');

IF NOT EXISTS (SELECT 1 FROM DisciplinaryViolationTypes WHERE Name = N'التأخر عن موعد الدوام الرسمي')
    INSERT INTO DisciplinaryViolationTypes(CategoryId, Name, Description, Severity, ValidityMonths, CountingPeriod, IncludeInEvaluation, ShowToEmployee)
    VALUES(@Attendance, N'التأخر عن موعد الدوام الرسمي', N'تأخر الموظف عن وقت بداية الدوام حسب الشفت المعتمد.', N'B', 6, N'Monthly', 1, 1);
IF NOT EXISTS (SELECT 1 FROM DisciplinaryViolationTypes WHERE Name = N'عدم الالتزام بالزي والمظهر العام')
    INSERT INTO DisciplinaryViolationTypes(CategoryId, Name, Description, Severity, ValidityMonths, CountingPeriod, IncludeInEvaluation, ShowToEmployee)
    VALUES(@Appearance, N'عدم الالتزام بالزي والمظهر العام', N'عدم الالتزام بالزي أو المظهر المهني المطلوب في موقع العمل.', N'A', 3, N'Monthly', 1, 1);
IF NOT EXISTS (SELECT 1 FROM DisciplinaryViolationTypes WHERE Name = N'الإهمال في المحافظة على أصول الشركة')
    INSERT INTO DisciplinaryViolationTypes(CategoryId, Name, Description, Severity, ValidityMonths, CountingPeriod, IncludeInEvaluation, ShowToEmployee)
    VALUES(@WorkSystem, N'الإهمال في المحافظة على أصول الشركة', N'عدم المحافظة على أصول الشركة أو استخدامها بطريقة تعرضها للتلف أو الضياع.', N'B', 12, N'Monthly', 1, 1);
IF NOT EXISTS (SELECT 1 FROM DisciplinaryViolationTypes WHERE Name = N'استخدام معدات زملاء العمل دون إذن أو عدم إعادتها')
    INSERT INTO DisciplinaryViolationTypes(CategoryId, Name, Description, Severity, ValidityMonths, CountingPeriod, IncludeInEvaluation, ShowToEmployee)
    VALUES(@WorkSystem, N'استخدام معدات زملاء العمل دون إذن أو عدم إعادتها', N'استخدام معدات زملاء العمل دون إذن، أو إتلافها عمدًا، أو عدم إعادتها.', N'B', 12, N'Monthly', 1, 1);
IF NOT EXISTS (SELECT 1 FROM DisciplinaryViolationTypes WHERE Name = N'سوء التعامل أو المشادة داخل العمل')
    INSERT INTO DisciplinaryViolationTypes(CategoryId, Name, Description, Severity, ValidityMonths, CountingPeriod, IncludeInEvaluation, ShowToEmployee)
    VALUES(@Behavior, N'سوء التعامل أو المشادة داخل العمل', N'سلوك غير لائق أو مشادة تؤثر على بيئة العمل.', N'C', 12, N'SixMonths', 1, 1);

DECLARE @Late int = (SELECT TOP 1 Id FROM DisciplinaryViolationTypes WHERE Name = N'التأخر عن موعد الدوام الرسمي');
DECLARE @AssetCare int = (SELECT TOP 1 Id FROM DisciplinaryViolationTypes WHERE Name = N'الإهمال في المحافظة على أصول الشركة');

IF @Late IS NOT NULL AND NOT EXISTS (SELECT 1 FROM DisciplinaryPenaltyRules WHERE ViolationTypeId = @Late)
BEGIN
    INSERT INTO DisciplinaryPenaltyRules(ViolationTypeId, OccurrenceFrom, OccurrenceTo, CountingPeriod, PenaltyAction, FinancialImpactType, FinancialValue, ValidityMonths, CalculationMode)
    VALUES
    (@Late, 1, 1, N'Monthly', N'خصم نصف يوم من الراتب', N'Days', 0.50, 6, N'Cumulative'),
    (@Late, 2, 2, N'Monthly', N'خصم ثلاثة أرباع يوم من الراتب', N'Days', 0.75, 6, N'Cumulative'),
    (@Late, 3, 3, N'Monthly', N'خصم يوم كامل من الراتب', N'Days', 1.00, 6, N'Cumulative'),
    (@Late, 4, 999, N'Monthly', N'رفع للإدارة مع استمرار الخصم حسب القرار', N'Days', 1.00, 12, N'Cumulative');
END;

IF @AssetCare IS NOT NULL AND NOT EXISTS (SELECT 1 FROM DisciplinaryPenaltyRules WHERE ViolationTypeId = @AssetCare)
BEGIN
    INSERT INTO DisciplinaryPenaltyRules(ViolationTypeId, OccurrenceFrom, OccurrenceTo, CountingPeriod, PenaltyAction, FinancialImpactType, FinancialValue, ValidityMonths, CalculationMode)
    VALUES
    (@AssetCare, 1, 1, N'Monthly', N'خصم يوم من الراتب', N'Days', 1.00, 12, N'Cumulative'),
    (@AssetCare, 2, 2, N'Monthly', N'خصم يومين من الراتب', N'Days', 2.00, 12, N'Cumulative'),
    (@AssetCare, 3, 999, N'Monthly', N'إنذار نهائي ورفع للإدارة', N'None', 0.00, 12, N'Cumulative');
END;

IF NOT EXISTS (SELECT 1 FROM DisciplinaryMessageTemplates WHERE TemplateType = N'PenaltyNotice')
BEGIN
    INSERT INTO DisciplinaryMessageTemplates(Name, TemplateType, Subject, Body, IsDefault, IsActive)
    VALUES(N'قالب إشعار عقوبة افتراضي', N'PenaltyNotice', N'إشعار مخالفة وجزاء - {EmployeeName}',
N'السيد/ة: {EmployeeName}
الرقم الوظيفي: {EmployeeCode}
القسم: {Department}

نود إعلامكم بأنه تم تسجيل مخالفة بحقكم بتاريخ {ViolationDate}
وذلك بسبب: {ViolationName}

فئة المخالفة:
{ViolationCategory}

وصف المخالفة:
{ViolationDescription}

وبناءً على لائحة الجزاءات المعتمدة، تقرر تطبيق العقوبة التالية:
{PenaltyAction}

الأثر المالي:
{FinancialImpact}

يرجى الالتزام بتعليمات الشركة لتجنب تكرار المخالفة.

قسم الموارد البشرية', 1, 1);
END;
""");
        await SeedTemplateTypesAndSettingsAsync();
    }

    private async Task SeedTemplateTypesAndSettingsAsync()
    {
await HrmsDatabase.ExecuteAsync(_dbContext,
            """
IF NOT EXISTS (SELECT 1 FROM DisciplinaryTemplateTypes WHERE Code = N'PenaltyNotice')
    INSERT INTO DisciplinaryTemplateTypes(Name, Code, Description, IsActive) VALUES(N'إشعار عقوبة', N'PenaltyNotice', N'القالب الأساسي لإشعار الموظف بالمخالفة والجزاء.', 1);
IF NOT EXISTS (SELECT 1 FROM DisciplinaryTemplateTypes WHERE Code = N'Warning')
    INSERT INTO DisciplinaryTemplateTypes(Name, Code, Description, IsActive) VALUES(N'إنذار', N'Warning', N'قالب الإنذارات الرسمية.', 1);
IF NOT EXISTS (SELECT 1 FROM DisciplinaryTemplateTypes WHERE Code = N'SalaryDeduction')
    INSERT INTO DisciplinaryTemplateTypes(Name, Code, Description, IsActive) VALUES(N'استقطاع راتب', N'SalaryDeduction', N'قالب المخالفات التي ينتج عنها أثر مالي.', 1);
IF NOT EXISTS (SELECT 1 FROM DisciplinaryTemplateTypes WHERE Code = N'FinalWarning')
    INSERT INTO DisciplinaryTemplateTypes(Name, Code, Description, IsActive) VALUES(N'إنذار نهائي', N'FinalWarning', N'قالب الإنذار النهائي أو إنذار الفصل.', 1);
""");
        await UpsertSettingIfMissingAsync("RequiresCommitteeApproval", "false");
        await UpsertSettingIfMissingAsync("AllowEmployeeAppeal", "true");
        await UpsertSettingIfMissingAsync("AppealWindowDays", "3");
        await UpsertSettingIfMissingAsync("DefaultTemplateType", "PenaltyNotice");
        await UpsertSettingIfMissingAsync("ApprovingAuthorityName", "قسم الموارد البشرية");
        await UpsertSettingIfMissingAsync("DocumentNumberFormat", "DISC-{yyyy}-{0000}");
        await UpsertSettingIfMissingAsync("FormHeader", "");
        await UpsertSettingIfMissingAsync("FormFooter", "");
        await UpsertSettingIfMissingAsync("HeaderImagePath", "");
        await UpsertSettingIfMissingAsync("FooterImagePath", "");
        await UpsertSettingIfMissingAsync("A4FormFilePath", "");
        await UpsertSettingIfMissingAsync("A4FormFileType", "");
        await UpsertSettingIfMissingAsync("MainBodyText", DefaultMainBodyText);
        await UpsertSettingIfMissingAsync("MainBodyXPercent", "8");
        await UpsertSettingIfMissingAsync("MainBodyYPercent", "8");
        await UpsertSettingIfMissingAsync("MainBodyWidthPercent", "84");
        await UpsertSettingIfMissingAsync("MainBodyFontFamily", "Tahoma");
        await UpsertSettingIfMissingAsync("MainBodyFontSize", "13");
        await UpsertSettingIfMissingAsync("MainBodyFontColor", "#0b1d31");
        await UpsertSettingIfMissingAsync("MainBodyIsBold", "true");
        await UpsertSettingIfMissingAsync("MainBodyTextAlign", "right");
}

    private async Task SeedBodyTextLayersAsync()
    {
        var bodyCount = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT COUNT(1) FROM DisciplinaryFormTextBlocks WHERE Area = 'Body';");

        if (bodyCount > 0)
        {
            return;
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO DisciplinaryFormTextBlocks
(Area, Text, XPercent, YPercent, WidthPercent, FontFamily, FontSize, FontColor, IsBold, TextAlign, IsActive, CreatedAt)
VALUES
(N'Body', N'إشعار مخالفة وجزاء', 0, 7, 100, N'Tahoma', 20, N'#0b1d31', 1, N'center', 1, SYSUTCDATETIME()),
(N'Body', N'السيد/ة: {EmployeeName}', 8, 20, 84, N'Tahoma', 13, N'#0b1d31', 1, N'right', 1, SYSUTCDATETIME()),
(N'Body', N'الرقم الوظيفي: {EmployeeCode}', 8, 26, 84, N'Tahoma', 13, N'#0b1d31', 1, N'right', 1, SYSUTCDATETIME()),
(N'Body', N'القسم: {Department}', 8, 32, 84, N'Tahoma', 13, N'#0b1d31', 1, N'right', 1, SYSUTCDATETIME()),
(N'Body', N'تاريخ المخالفة: {ViolationDate}', 8, 38, 84, N'Tahoma', 13, N'#0b1d31', 1, N'right', 1, SYSUTCDATETIME()),
(N'Body', N'المخالفة: {ViolationName}', 8, 50, 84, N'Tahoma', 13, N'#0b1d31', 1, N'right', 1, SYSUTCDATETIME()),
(N'Body', N'فئة المخالفة: {ViolationCategory}', 8, 56, 84, N'Tahoma', 13, N'#0b1d31', 1, N'right', 1, SYSUTCDATETIME()),
(N'Body', N'العقوبة: {PenaltyAction}', 8, 68, 84, N'Tahoma', 13, N'#0b1d31', 1, N'right', 1, SYSUTCDATETIME()),
(N'Body', N'الأثر المالي: {FinancialImpact}', 8, 74, 84, N'Tahoma', 13, N'#0b1d31', 1, N'right', 1, SYSUTCDATETIME());
""");
    }

    private async Task UpsertSettingIfMissingAsync(string key, string value)
    {
        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
IF NOT EXISTS (SELECT 1 FROM DisciplinarySettings WHERE [Key] = @Key)
    INSERT INTO DisciplinarySettings([Key], [Value], UpdatedAt) VALUES(@Key, @Value, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Key", key);
                HrmsDatabase.AddParameter(command, "@Value", value);
            });
    }

    private async Task UpsertSettingAsync(string key, string value)
    {
        await HrmsDatabase.ExecuteAsync(_dbContext,
            """
IF EXISTS (SELECT 1 FROM DisciplinarySettings WHERE [Key] = @Key)
    UPDATE DisciplinarySettings SET [Value] = @Value, UpdatedAt = SYSUTCDATETIME() WHERE [Key] = @Key;
ELSE
    INSERT INTO DisciplinarySettings([Key], [Value], UpdatedAt) VALUES(@Key, @Value, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Key", key);
                HrmsDatabase.AddParameter(command, "@Value", value);
            });
    }

    private async Task<DisciplinarySettings> LoadSettingsAsync()
    {
        var items = await HrmsDatabase.QueryAsync(_dbContext,
            "SELECT [Key], ISNULL([Value], '') AS [Value] FROM DisciplinarySettings;",
            command => { },
            reader => new KeyValuePair<string, string>(HrmsDatabase.GetString(reader, "Key"), HrmsDatabase.GetString(reader, "Value")));
        var map = items.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        return new DisciplinarySettings
        {
            RequiresCommitteeApproval = GetBool(map, "RequiresCommitteeApproval"),
            AllowEmployeeAppeal = GetBool(map, "AllowEmployeeAppeal", true),
            AppealWindowDays = GetInt(map, "AppealWindowDays", 3),
            DefaultTemplateType = GetString(map, "DefaultTemplateType", "PenaltyNotice"),
            ApprovingAuthorityName = GetString(map, "ApprovingAuthorityName", "قسم الموارد البشرية"),
            DocumentNumberFormat = GetString(map, "DocumentNumberFormat", "DISC-{yyyy}-{0000}"),
            FormHeader = GetString(map, "FormHeader", string.Empty),
            FormFooter = GetString(map, "FormFooter", string.Empty),
            HeaderImagePath = GetString(map, "HeaderImagePath", string.Empty),
            FooterImagePath = GetString(map, "FooterImagePath", string.Empty),
            A4FormFilePath = GetString(map, "A4FormFilePath", string.Empty),
            A4FormFileType = GetString(map, "A4FormFileType", string.Empty),
            MainBodyText = GetString(map, "MainBodyText", DefaultMainBodyText),
            MainBodyXPercent = GetDecimalSetting(map, "MainBodyXPercent", 8),
            MainBodyYPercent = GetDecimalSetting(map, "MainBodyYPercent", 8),
            MainBodyWidthPercent = GetDecimalSetting(map, "MainBodyWidthPercent", 84),
            MainBodyFontFamily = GetString(map, "MainBodyFontFamily", "Tahoma"),
            MainBodyFontSize = GetInt(map, "MainBodyFontSize", 13),
            MainBodyFontColor = GetString(map, "MainBodyFontColor", "#0b1d31"),
            MainBodyIsBold = GetBool(map, "MainBodyIsBold", true),
            MainBodyTextAlign = GetString(map, "MainBodyTextAlign", "right")
        };
    }

    private async Task<List<ViolationCategory>> LoadCategoriesAsync()
    {
        return await HrmsDatabase.QueryAsync(_dbContext,
            """
SELECT c.Id, c.Name, ISNULL(c.Description, '') AS Description, c.DisplayOrder, c.IsActive, c.CreatedAt,
       (SELECT COUNT(1) FROM DisciplinaryViolationTypes t WHERE t.CategoryId = c.Id) AS TypesCount
FROM DisciplinaryViolationCategories c
ORDER BY c.DisplayOrder, c.Name;
""",
            command => { },
            reader => new ViolationCategory
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                Description = HrmsDatabase.GetString(reader, "Description"),
                DisplayOrder = HrmsDatabase.GetInt(reader, "DisplayOrder"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
                TypesCount = HrmsDatabase.GetInt(reader, "TypesCount")
            });
    }

    private async Task<List<ViolationType>> LoadViolationTypesAsync()
    {
        return await HrmsDatabase.QueryAsync(_dbContext,
            """
SELECT t.Id, t.CategoryId, c.Name AS CategoryName, t.Name, ISNULL(t.Description, '') AS Description, t.Severity,
       t.ValidityMonths, t.CountingPeriod, t.IncludeInEvaluation, t.ShowToEmployee, t.IsActive, t.CreatedAt,
       (SELECT COUNT(1) FROM DisciplinaryPenaltyRules r WHERE r.ViolationTypeId = t.Id) AS RulesCount
FROM DisciplinaryViolationTypes t
INNER JOIN DisciplinaryViolationCategories c ON c.Id = t.CategoryId
ORDER BY c.DisplayOrder, t.Name;
""",
            command => { },
            reader => new ViolationType
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                CategoryId = HrmsDatabase.GetInt(reader, "CategoryId"),
                CategoryName = HrmsDatabase.GetString(reader, "CategoryName"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                Description = HrmsDatabase.GetString(reader, "Description"),
                Severity = HrmsDatabase.GetString(reader, "Severity"),
                ValidityMonths = HrmsDatabase.GetInt(reader, "ValidityMonths"),
                CountingPeriod = HrmsDatabase.GetString(reader, "CountingPeriod"),
                IncludeInEvaluation = HrmsDatabase.GetBool(reader, "IncludeInEvaluation"),
                ShowToEmployee = HrmsDatabase.GetBool(reader, "ShowToEmployee"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
                RulesCount = HrmsDatabase.GetInt(reader, "RulesCount")
            });
    }

    private async Task<List<PenaltyRule>> LoadPenaltyRulesAsync()
    {
        return await HrmsDatabase.QueryAsync(_dbContext,
            """
SELECT r.Id, r.ViolationTypeId, t.CategoryId, t.Name AS ViolationName, c.Name AS CategoryName, r.OccurrenceFrom,
       r.OccurrenceTo, r.CountingPeriod, r.PenaltyAction, r.FinancialImpactType, r.FinancialValue, r.ValidityMonths,
       r.CalculationMode, r.RequiresApproval, r.IsActive, r.CreatedAt
FROM DisciplinaryPenaltyRules r
INNER JOIN DisciplinaryViolationTypes t ON t.Id = r.ViolationTypeId
INNER JOIN DisciplinaryViolationCategories c ON c.Id = t.CategoryId
ORDER BY c.DisplayOrder, t.Name, r.OccurrenceFrom;
""",
            command => { },
            reader => new PenaltyRule
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                ViolationTypeId = HrmsDatabase.GetInt(reader, "ViolationTypeId"),
                CategoryId = HrmsDatabase.GetInt(reader, "CategoryId"),
                ViolationName = HrmsDatabase.GetString(reader, "ViolationName"),
                CategoryName = HrmsDatabase.GetString(reader, "CategoryName"),
                OccurrenceFrom = HrmsDatabase.GetInt(reader, "OccurrenceFrom"),
                OccurrenceTo = HrmsDatabase.GetInt(reader, "OccurrenceTo"),
                CountingPeriod = HrmsDatabase.GetString(reader, "CountingPeriod"),
                PenaltyAction = HrmsDatabase.GetString(reader, "PenaltyAction"),
                FinancialImpactType = HrmsDatabase.GetString(reader, "FinancialImpactType"),
                FinancialValue = GetDecimal(reader, "FinancialValue"),
                ValidityMonths = HrmsDatabase.GetInt(reader, "ValidityMonths"),
                CalculationMode = HrmsDatabase.GetString(reader, "CalculationMode"),
                RequiresApproval = HrmsDatabase.GetBool(reader, "RequiresApproval"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }


    private async Task<List<TemplateType>> LoadTemplateTypesAsync()
    {
        return await HrmsDatabase.QueryAsync(_dbContext,
            """
SELECT Id, Name, Code, ISNULL(Description, '') AS Description, IsActive, CreatedAt
FROM DisciplinaryTemplateTypes
ORDER BY Name;
""",
            command => { },
            reader => new TemplateType
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                Code = HrmsDatabase.GetString(reader, "Code"),
                Description = HrmsDatabase.GetString(reader, "Description"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    private async Task<List<MessageTemplate>> LoadMessageTemplatesAsync()
    {
        return await HrmsDatabase.QueryAsync(_dbContext,
            """
SELECT Id, Name, TemplateType, Subject, Body, IsDefault, IsActive, CreatedAt
FROM DisciplinaryMessageTemplates
ORDER BY IsDefault DESC, Name;
""",
            command => { },
            reader => new MessageTemplate
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                TemplateType = HrmsDatabase.GetString(reader, "TemplateType"),
                Subject = HrmsDatabase.GetString(reader, "Subject"),
                Body = HrmsDatabase.GetString(reader, "Body"),
                IsDefault = HrmsDatabase.GetBool(reader, "IsDefault"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }


    private async Task<List<FormTextBlock>> LoadTextBlocksAsync()
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
            reader => new FormTextBlock
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

    private async Task<int> GetViolationCategoryIdAsync(int violationTypeId)
    {
        return await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 CategoryId FROM DisciplinaryViolationTypes WHERE Id = @Id;", command => HrmsDatabase.AddParameter(command, "@Id", violationTypeId));
    }

    private async Task ClearDefaultTemplateAsync(string templateType)
    {
await HrmsDatabase.ExecuteAsync(_dbContext, "UPDATE DisciplinaryMessageTemplates SET IsDefault = 0 WHERE TemplateType = @TemplateType;", command => HrmsDatabase.AddParameter(command, "@TemplateType", templateType));
    }

    public string DisplaySeverityShort(string severity) => severity == "FinalWarning" ? "FW" : severity;
    public string DisplayOccurrenceRange(int from, int to) => to >= 999 ? $"{from}+" : from == to ? from.ToString() : $"{from} - {to}";
    public string DisplayFinancialImpact(string type, decimal value) => type.Equals("None", StringComparison.OrdinalIgnoreCase) || value <= 0 ? "لا يوجد" : $"{value:0.##} {DisplayFinancialUnit(type)}";
    public string DisplayFinancialUnit(string type) => type switch { "Days" => "يوم", "Hours" => "ساعة", "Amount" => "مبلغ", _ => string.Empty };
    public string DisplayTemplateType(string type)
    {
        var templateType = TemplateTypes.FirstOrDefault(x => x.Code.Equals(type, StringComparison.OrdinalIgnoreCase));
        return templateType?.Name ?? type;
    }

    private static string NormalizeTab(string? tab) => tab switch { "library" => "library", "designer" => "designer", "templates" => "templates", _ => "setup" };
    private static string NormalizeSeverity(string? value) => value switch { "A" => "A", "B" => "B", "C" => "C", "FinalWarning" => "FinalWarning", _ => "B" };
    private static string NormalizePeriod(string? value) => value switch { "Monthly" => "Monthly", "SixMonths" => "SixMonths", "Yearly" => "Yearly", "Contract" => "Contract", _ => "Monthly" };
    private static string NormalizeFinancialType(string? value) => value switch { "Days" => "Days", "Hours" => "Hours", "Amount" => "Amount", _ => "None" };
    private static string NormalizeCalculationMode(string? value) => value switch { "Cumulative" => "Cumulative", "ReplacePrevious" => "ReplacePrevious", "DifferenceOnly" => "DifferenceOnly", _ => "Cumulative" };
    private static string NormalizeTemplateCode(string? value)
    {
        var cleaned = new string((value ?? "PenaltyNotice").Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "PenaltyNotice" : cleaned;
    }


    private static string NormalizeArea(string? value)
    {
        return value switch
        {
            "Header" => "Header",
            "Footer" => "Footer",
            _ => "Body"
        };
    }

    private string NormalizeFont(string? value)
    {
        return FontFamilies.Contains(value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? value!
            : "Tahoma";
    }

    private static string NormalizeTextAlign(string? value)
    {
        return value switch
        {
            "left" => "left",
            "center" => "center",
            _ => "right"
        };
    }

    private static string NormalizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#0b1d31";
        }

        var cleaned = value.Trim();
        if (cleaned.Length == 7 && cleaned.StartsWith("#") && cleaned.Skip(1).All(Uri.IsHexDigit))
        {
            return cleaned;
        }

        return "#0b1d31";
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static bool GetBool(Dictionary<string, string> map, string key, bool defaultValue = false)
    {
        return map.TryGetValue(key, out var value) ? value.Equals("true", StringComparison.OrdinalIgnoreCase) : defaultValue;
    }

    private static int GetInt(Dictionary<string, string> map, string key, int defaultValue)
    {
        return map.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : defaultValue;
    }

    private static decimal GetDecimalSetting(Dictionary<string, string> map, string key, decimal defaultValue)
    {
        return map.TryGetValue(key, out var value) && decimal.TryParse(value, out var result)
            ? result
            : defaultValue;
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


public sealed class DisciplinarySettings
{
    public bool RequiresCommitteeApproval { get; set; }
    public bool AllowEmployeeAppeal { get; set; } = true;
    public int AppealWindowDays { get; set; } = 3;
    public string DefaultTemplateType { get; set; } = "PenaltyNotice";
    public string ApprovingAuthorityName { get; set; } = "قسم الموارد البشرية";
    public string DocumentNumberFormat { get; set; } = "DISC-{yyyy}-{0000}";
    public string FormHeader { get; set; } = "";
    public string FormFooter { get; set; } = "";
    public string HeaderImagePath { get; set; } = "";
    public string FooterImagePath { get; set; } = "";
    public string A4FormFilePath { get; set; } = "";
    public string A4FormFileType { get; set; } = "";
    public string MainBodyText { get; set; } = "";
    public decimal MainBodyXPercent { get; set; } = 8;
    public decimal MainBodyYPercent { get; set; } = 8;
    public decimal MainBodyWidthPercent { get; set; } = 84;
    public string MainBodyFontFamily { get; set; } = "Tahoma";
    public int MainBodyFontSize { get; set; } = 13;
    public string MainBodyFontColor { get; set; } = "#0b1d31";
    public bool MainBodyIsBold { get; set; } = true;
    public string MainBodyTextAlign { get; set; } = "right";
}

public sealed class ViolationCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int TypesCount { get; set; }
}

public sealed class ViolationType
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Severity { get; set; } = "B";
    public int ValidityMonths { get; set; } = 6;
    public string CountingPeriod { get; set; } = "Monthly";
    public bool IncludeInEvaluation { get; set; }
    public bool ShowToEmployee { get; set; }
    public bool IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int RulesCount { get; set; }
}

public sealed class PenaltyRule
{
    public int Id { get; set; }
    public int ViolationTypeId { get; set; }
    public int CategoryId { get; set; }
    public string ViolationName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int OccurrenceFrom { get; set; }
    public int OccurrenceTo { get; set; }
    public string CountingPeriod { get; set; } = "Monthly";
    public string PenaltyAction { get; set; } = "";
    public string FinancialImpactType { get; set; } = "None";
    public decimal FinancialValue { get; set; }
    public int ValidityMonths { get; set; } = 6;
    public string CalculationMode { get; set; } = "Cumulative";
    public bool RequiresApproval { get; set; }
    public bool IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public sealed class MessageTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TemplateType { get; set; } = "PenaltyNotice";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
}


public sealed class TemplateType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
}


public sealed class FormTextBlock
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


