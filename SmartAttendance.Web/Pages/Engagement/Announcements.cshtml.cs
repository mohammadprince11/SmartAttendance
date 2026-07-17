using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Engagement;

public class AnnouncementsModel : EngagementPageModel
{
    public AnnouncementsModel(
        ApplicationDbContext dbContext,
        IAnnouncementService announcementService)
        : base(dbContext, announcementService)
    {
    }

    [BindProperty]
    public AnnouncementInput Announcement { get; set; } = new();

    public async Task OnGetAsync()
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);
        await LoadAudienceOptionsAsync();
        await LoadAnnouncementsAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var title = Announcement.Title?.Trim() ?? string.Empty;
        var body = Announcement.Body?.Trim() ?? string.Empty;
        var templateKey = NormalizeTemplateKey(Announcement.TemplateKey);
        var template = GetTemplate(templateKey);

        if (!templateKey.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Announcement.Category) ||
                Announcement.Category.Equals("عام", StringComparison.OrdinalIgnoreCase))
            {
                Announcement.Category = template.Category;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = BuildTemplateTitle(
                    templateKey,
                    Announcement.PersonName);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                body = BuildTemplateBody(
                    templateKey,
                    Announcement.PersonName,
                    Announcement.SecondaryName,
                    Announcement.DepartmentName,
                    Announcement.EffectiveDateText);
            }
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            StatusMessage = "يرجى إدخال عنوان ووصف الإعلان.";
            return RedirectToPage();
        }

        var targetType = string.IsNullOrWhiteSpace(Announcement.TargetType)
            ? "All"
            : Announcement.TargetType.Trim();

        var targetError = ValidateTarget(
            targetType,
            Announcement.EmployeeIds,
            Announcement.DepartmentId,
            Announcement.BranchId);

        if (!string.IsNullOrWhiteSpace(targetError))
        {
            StatusMessage = targetError;
            return RedirectToPage();
        }

        var request = new AnnouncementCreateRequest
        {
            LanguageCode = "ar",
            Title = title,
            Body = body,
            Category = string.IsNullOrWhiteSpace(Announcement.Category)
                ? "عام"
                : Announcement.Category.Trim(),
            PublishNow = Announcement.PublishNow,
            CommentsEnabled = false,
            ReactionsEnabled = true,
            AllEmployees = targetType.Equals("All", StringComparison.OrdinalIgnoreCase),
            BranchIds = targetType.Equals("Branch", StringComparison.OrdinalIgnoreCase) &&
                        Announcement.BranchId.HasValue
                ? new[] { Announcement.BranchId.Value }
                : Array.Empty<int>(),
            DepartmentIds = targetType.Equals("Department", StringComparison.OrdinalIgnoreCase) &&
                            Announcement.DepartmentId.HasValue
                ? new[] { Announcement.DepartmentId.Value }
                : Array.Empty<int>(),
            EmployeeIds = targetType.Equals("Employee", StringComparison.OrdinalIgnoreCase)
                ? (Announcement.EmployeeIds ?? Array.Empty<int>())
                : Array.Empty<int>()
        };

        var result = await AnnouncementService.CreateAsync(
            request,
            BuildAnnouncementActor(),
            HttpContext.RequestAborted);

        StatusMessage = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostArchiveAsync(int id)
    {
        var result = await AnnouncementService.ArchiveAsync(
            id,
            BuildAnnouncementActor(),
            HttpContext.RequestAborted);

        StatusMessage = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, bool publish)
    {
        var result = publish
            ? await AnnouncementService.PublishAsync(
                id,
                BuildAnnouncementActor(),
                HttpContext.RequestAborted)
            : await AnnouncementService.ArchiveAsync(
                id,
                BuildAnnouncementActor(),
                HttpContext.RequestAborted);

        StatusMessage = result.Message;
        return RedirectToPage();
    }

    private string BuildTemplateTitle(string templateKey, string? personName)
    {
        var name = string.IsNullOrWhiteSpace(personName) ? "أحد موظفينا" : personName.Trim();
        return templateKey switch
        {
            "holiday" => "إعلان عطلة رسمية",
            "circular" => "تعميم إداري",
            "workhours" => "تغيير أوقات الدوام",
            "welcome" => $"ترحيب بموظف جديد - {name}",
            "promotion" => $"تهنئة بالترقية - {name}",
            "appreciation" => $"شكر وتقدير - {name}",
            "marriage" => $"تهنئة بمناسبة الزواج - {name}",
            "condolence" => $"تعزية ومواساة - {name}",
            "newborn" => $"مولود جديد - {name}",
            "farewell" => $"وداع وشكر - {name}",
            _ => Announcement.Title?.Trim() ?? string.Empty
        };
    }

    private string BuildTemplateBody(string templateKey, string? personName, string? secondaryName, string? departmentName, string? effectiveDateText)
    {
        var name = string.IsNullOrWhiteSpace(personName) ? "زميلنا العزيز" : personName.Trim();
        var extra = string.IsNullOrWhiteSpace(secondaryName) ? string.Empty : secondaryName.Trim();
        var department = string.IsNullOrWhiteSpace(departmentName) ? "قسم الموارد البشرية" : departmentName.Trim();
        var date = string.IsNullOrWhiteSpace(effectiveDateText) ? string.Empty : effectiveDateText.Trim();

        return templateKey switch
        {
            "holiday" => $"تعلن إدارة الموارد البشرية عن عطلة رسمية{(string.IsNullOrWhiteSpace(date) ? "" : $" بتاريخ {date}")}. يرجى من جميع الموظفين الالتزام بالتعليمات الخاصة بالدوام والعودة حسب التوجيه المعتمد.",
            "circular" => $"يرجى من جميع الموظفين الاطلاع على هذا التعميم الإداري والعمل بمضمونه اعتباراً{(string.IsNullOrWhiteSpace(date) ? " من تاريخ النشر" : $" من تاريخ {date}")}. لأي استفسار يرجى التواصل مع الجهة المعنية.",
            "workhours" => $"نود إعلامكم بأنه تم تحديث أوقات الدوام{(string.IsNullOrWhiteSpace(extra) ? "" : $" إلى {extra}")}{(string.IsNullOrWhiteSpace(date) ? "" : $" اعتباراً من {date}")}. يرجى الالتزام بالتوقيت الجديد والتنسيق مع المسؤول المباشر.",
            "welcome" => $"نرحب بانضمام {name} إلى فريق العمل{(string.IsNullOrWhiteSpace(extra) ? "" : $" بمنصب {extra}")}. نتمنى له بداية موفقة ومسيرة ناجحة ضمن عائلة الشركة.",
            "promotion" => $"نبارك إلى {name} ترقيته{(string.IsNullOrWhiteSpace(extra) ? "" : $" إلى منصب {extra}")}. نتمنى له دوام النجاح والتوفيق في مهامه الجديدة، مع الشكر والتقدير لجهوده المميزة.",
            "appreciation" => $"تتقدم الشركة بالشكر والتقدير إلى {name}{(string.IsNullOrWhiteSpace(extra) ? "" : $" تقديراً لـ {extra}")}. نثمّن هذا العطاء ونتمنى له المزيد من النجاح والتميز.",
            "marriage" => $"تتقدم الشركة بخالص التهاني والتبريكات إلى {name} بمناسبة الزواج. نتمنى له حياة سعيدة مليئة بالمودة والنجاح، ودوام الفرح والتوفيق.",
            "condolence" => $"تتقدم الشركة بخالص العزاء والمواساة إلى {name}. سائلين الله أن يتغمد الفقيد بواسع رحمته، وأن يلهم أهله وذويه الصبر والسلوان.",
            "newborn" => $"تتقدم الشركة بأصدق التهاني إلى {name} بمناسبة المولود الجديد{(string.IsNullOrWhiteSpace(extra) ? "" : $" ({extra})")}. نسأل الله أن يجعله من مواليد السعادة والبركة.",
            "farewell" => $"تتقدم الشركة بالشكر والتقدير إلى {name} على ما قدمه من جهود خلال فترة عمله. نتمنى له دوام التوفيق والنجاح في مسيرته القادمة.",
            _ => Announcement.Body?.Trim() ?? string.Empty
        } + $"\n\n{department}";
    }
}
