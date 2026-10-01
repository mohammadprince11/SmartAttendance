using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Localization;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Companies;

public class CreateModel : PageModel
{
    private readonly ICompanyService _companyService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILocalizationDictionaryService _dictionary;

    public CreateModel(
        ICompanyService companyService,
        ApplicationDbContext dbContext,
        ILocalizationDictionaryService dictionary)
    {
        _companyService = companyService;
        _dbContext = dbContext;
        _dictionary = dictionary;
    }

    [BindProperty]
    public CompanyCreateViewModel Company { get; set; } = new();

    [BindProperty, Required, StringLength(200, MinimumLength = 2)]
    public string ArabicName { get; set; } = string.Empty;

    [BindProperty, Required, StringLength(200, MinimumLength = 2)]
    public string EnglishName { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public bool Onboarding { get; set; }

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Remove("Company.Name");
        if (!ModelState.IsValid)
            return Page();

        var tenantId = TenantContext.GetTenantId(User);
        if (tenantId is not > 0)
            return Forbid();

        var isFirstCompany = !await _dbContext.Companies
            .IgnoreQueryFilters()
            .AnyAsync(
                item => item.TenantId == tenantId.Value && !item.IsDeleted,
                HttpContext.RequestAborted);

        ArabicName = ArabicName.Trim();
        EnglishName = EnglishName.Trim();
        Company.Name = ArabicName;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            HttpContext.RequestAborted);

        var created = await _companyService.CreateAsync(Company, tenantId.Value);

        if (!created)
        {
            ErrorMessage = "تعذر إنشاء الشركة أو تعذر تحديد المنظومة التابعة لها.";
            return Page();
        }

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .SingleAsync(
                item => item.TenantId == tenantId.Value && item.Code == Company.Code,
                HttpContext.RequestAborted);

        var languageCatalog = await _dictionary.GetLanguagesAsync(HttpContext.RequestAborted);
        var arabic = languageCatalog.FirstOrDefault(item =>
            string.Equals(item.Code, "ar-IQ", StringComparison.OrdinalIgnoreCase));
        var english = languageCatalog.FirstOrDefault(item =>
            string.Equals(item.Code, "en-US", StringComparison.OrdinalIgnoreCase));

        if (arabic is null || english is null)
        {
            await transaction.RollbackAsync(HttpContext.RequestAborted);
            ErrorMessage = "يجب توفير اللغتين العربية والإنجليزية في قاموس النظام قبل إنشاء الشركة.";
            return Page();
        }

        _dbContext.CompanyLanguages.AddRange(
            BuildLanguage(company.Id, arabic, isDefault: true),
            BuildLanguage(company.Id, english, isDefault: false));

        _dbContext.LocalizedEntityValues.AddRange(
            BuildCompanyName(company.Id, "ar-IQ", ArabicName),
            BuildCompanyName(company.Id, "en-US", EnglishName));

        await _dbContext.SaveChangesAsync(HttpContext.RequestAborted);
        await transaction.CommitAsync(HttpContext.RequestAborted);

        var continueOnboarding = Onboarding || isFirstCompany;
        TempData["SuccessMessage"] = continueOnboarding
            ? "تم إنشاء الشركة. أكمل الآن بقية خطوات التأسيس بالترتيب."
            : "Company created successfully.";

        return continueOnboarding
            ? RedirectToPage("/Setup/Index", new { onboarding = true })
            : RedirectToPage("./Index");
    }

    private static CompanyLanguage BuildLanguage(
        int companyId,
        DictionaryLanguage language,
        bool isDefault) =>
        new()
        {
            CompanyId = companyId,
            CultureCode = language.Code,
            NativeName = language.NativeName,
            EnglishName = language.EnglishName,
            Direction = language.Direction,
            IsDefault = isDefault,
            IsRequired = true,
            IsActive = true
        };

    private static LocalizedEntityValue BuildCompanyName(
        int companyId,
        string cultureCode,
        string value) =>
        new()
        {
            CompanyId = companyId,
            EntityType = "Company",
            EntityId = companyId,
            FieldName = "Name",
            CultureCode = cultureCode,
            Value = value,
            TranslationStatus = "Manual"
        };
}
