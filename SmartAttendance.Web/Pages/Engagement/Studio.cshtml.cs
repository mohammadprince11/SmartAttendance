using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Engagement;

[Authorize]
[RequestSizeLimit(6 * 1024 * 1024)]
public class StudioModel(ApplicationDbContext db, IAnnouncementService service, ILocalizationDictionaryService? dictionary = null) : EngagementPageModel(db, service)
{
    [BindProperty(SupportsGet = true)] public int CompanyId { get; set; }
    [BindProperty] public string TemplateJson { get; set; } = "";
    [BindProperty] public Guid Revision { get; set; }
    [BindProperty] public string TemplateKey { get; set; } = "welcome";
    [BindProperty] public Dictionary<string, string> Values { get; set; } = new();
    [BindProperty] public string Design { get; set; } = "builtin:welcome";
    [BindProperty] public string Fit { get; set; } = "contain";
    [BindProperty] public string Position { get; set; } = "center";
    [BindProperty] public string TextPlacement { get; set; } = "above";
    [BindProperty] public Guid RequestId { get; set; } = Guid.NewGuid();
    [BindProperty] public bool PublishNow { get; set; }
    [BindProperty] public bool CommentsEnabled { get; set; }
    [BindProperty] public int[] EmployeeIds { get; set; } = [];
    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public string DesignName { get; set; } = "";
    public List<CompanyChoice> Companies { get; set; } = [];
    public List<TemplateChoice> Templates { get; set; } = [];
    public List<DesignChoice> Designs { get; set; } = [];
    public bool CanEditLibrary => RoleRouteCatalog.IsAdmin(User.FindFirstValue(ClaimTypes.Role));
    public record CompanyChoice(int Id, string Name);
    public record TemplateChoice(StudioTemplate Definition, Guid Revision, bool IsActive);
    public record DesignChoice(Guid Id, string Name);
    private CancellationToken Ct => HttpContext.RequestAborted;

    private async Task<bool> AuthorizedCompany()
    {
        var scope = await GetCompanyScopeAsync();
        return CompanyId > 0 && scope.Allows(CompanyId) && await DbContext.Companies.AnyAsync(c => c.Id == CompanyId && c.IsActive, Ct);
    }
    private async Task Load()
    {
        var scope = await GetCompanyScopeAsync();
        Companies = await DbContext.Companies.AsNoTracking().Where(c => c.IsActive && (scope.IsUnrestricted || scope.AllowedCompanyIds.Contains(c.Id)))
            .Select(c => new CompanyChoice(c.Id, c.Name)).ToListAsync(Ct);
        if (CompanyId == 0) CompanyId = Companies.FirstOrDefault()?.Id ?? 0;
        if (!await AuthorizedCompany()) return;
        var saved = await DbContext.AnnouncementStudioProfiles.AsNoTracking().Where(p => p.CompanyId == CompanyId).ToListAsync(Ct);
        Templates = AnnouncementStudio.Defaults().Where(d => saved.All(s => s.Key != d.Key)).Select(d => new TemplateChoice(d, Guid.Empty, true)).ToList();
        if (dictionary != null)
        {
            foreach (var language in await dictionary.GetLanguagesAsync(Ct))
            {
                var catalog = await dictionary.GetCatalogAsync(language.Code, Ct);
                foreach (var builtin in Templates) AnnouncementTemplateDictionary.Apply(builtin.Definition, language.Code, catalog);
            }
        }
        Templates.AddRange(saved.Select(p => new TemplateChoice(JsonSerializer.Deserialize<StudioTemplate>(p.DefinitionJson)!, p.Revision, p.IsActive)));
        Designs = await DbContext.AnnouncementStudioDesigns.AsNoTracking().Where(d => d.CompanyId == CompanyId && d.IsActive)
            .Select(d => new DesignChoice(d.Id, d.Name)).ToListAsync(Ct);
    }
    public async Task<IActionResult> OnGetAsync() { await Load(); return CompanyId > 0 && await AuthorizedCompany() ? Page() : Forbid(); }
    private async Task<IActionResult> Error(string message)
    {
        ModelState.AddModelError("", message); await Load(); return Page();
    }
    public async Task<IActionResult> OnPostSaveTemplateAsync()
    {
        if (!CanEditLibrary || !await AuthorizedCompany()) return Forbid();
        StudioTemplate? definition;
        try { definition = TemplateJson.Length <= 100000 ? JsonSerializer.Deserialize<StudioTemplate>(TemplateJson) : null; }
        catch (JsonException) { return await Error("بيانات القالب غير صالحة."); }
        if (definition == null) return await Error("بيانات القالب غير صالحة.");
        var validation = AnnouncementStudio.Validate(definition);
        if (validation != null) return await Error(validation);
        var ids = definition.DesignIds.Distinct().ToArray();
        if (await DbContext.AnnouncementStudioDesigns.CountAsync(d => d.CompanyId == CompanyId && d.IsActive && ids.Contains(d.Id), Ct) != ids.Length)
            return await Error("إحدى الصور خارج نطاق الشركة أو غير فعالة.");
        var row = await DbContext.AnnouncementStudioProfiles.SingleOrDefaultAsync(p => p.CompanyId == CompanyId && p.Key == definition.Key, Ct);
        if (row != null && row.Revision != Revision) return await Error("القالب تغيّر بواسطة مستخدم آخر. أعد تحميله قبل الحفظ.");
        if (row == null) { row = new() { CompanyId = CompanyId, Key = definition.Key }; DbContext.AnnouncementStudioProfiles.Add(row); }
        row.DefinitionJson = JsonSerializer.Serialize(definition); row.Revision = Guid.NewGuid(); row.IsActive = true;
        try { await DbContext.SaveChangesAsync(Ct); }
        catch (DbUpdateException) { return await Error("لم يُحفظ القالب بسبب تعارض. أعد تحميل الصفحة."); }
        StatusMessage = "تم حفظ القالب. الإعلانات السابقة لا تتغير."; return RedirectToPage(new { CompanyId });
    }
    public async Task<IActionResult> OnPostToggleTemplateAsync(string key)
    {
        if (!CanEditLibrary || !await AuthorizedCompany()) return Forbid();
        var row = await DbContext.AnnouncementStudioProfiles.SingleOrDefaultAsync(p => p.CompanyId == CompanyId && p.Key == key, Ct);
        if (row == null)
        {
            var builtin = AnnouncementStudio.Defaults().FirstOrDefault(p => p.Key == key);
            if (builtin == null) return NotFound();
            row = new() { CompanyId = CompanyId, Key = key, DefinitionJson = JsonSerializer.Serialize(builtin) };
            DbContext.AnnouncementStudioProfiles.Add(row);
        }
        row.IsActive = !row.IsActive; row.Revision = Guid.NewGuid(); await DbContext.SaveChangesAsync(Ct);
        return RedirectToPage(new { CompanyId });
    }
    public async Task<IActionResult> OnPostUploadAsync()
    {
        if (!CanEditLibrary || !await AuthorizedCompany()) return Forbid();
        if (Upload == null || Upload.Length is < 24 or > 5242880 || string.IsNullOrWhiteSpace(DesignName) || DesignName.Length > 150)
            return await Error("اختر صورة PNG أو JPEG بحد أقصى 5 ميغابايت وأدخل اسم التصميم.");
        using var stream = new MemoryStream(); await Upload.CopyToAsync(stream, Ct); var bytes = stream.ToArray();
        var type = DetectImage(bytes);
        if (type == null) return await Error("الصورة غير صالحة. المسموح PNG أو JPEG فقط.");
        DbContext.AnnouncementStudioDesigns.Add(new() { CompanyId = CompanyId, Name = DesignName.Trim(), ContentType = type, Data = bytes });
        await DbContext.SaveChangesAsync(Ct); StatusMessage = "تمت إضافة التصميم إلى مكتبة الشركة.";
        return RedirectToPage(new { CompanyId });
    }
    public static string? DetectImage(byte[] data)
    {
        // Reject active document formats; validate signatures, raster dimensions, and terminators.
        if (data.Length < 24 || data.Length > 5242880) return null;
        if (data.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) && data.AsSpan(12,4).SequenceEqual("IHDR"u8))
        {
            var w = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16,4));
            var h = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20,4));
            return w is > 0 and <= 8192 && h is > 0 and <= 8192 && (long)w*h <= 32000000 && data.Length >= 36 && data.AsSpan(data.Length-8,4).SequenceEqual("IEND"u8) ? "image/png" : null;
        }
        if (data[0] == 255 && data[1] == 216 && data[^2] == 255 && data[^1] == 217)
        {
            for (var i = 2; i + 9 < data.Length;)
            {
                if (data[i++] != 255) return null;
                while (i < data.Length && data[i] == 255) i++;
                if (i >= data.Length) return null;
                var marker = data[i++];
                if (marker == 0xda) break;
                if (i + 2 > data.Length) return null;
                var len = (data[i] << 8) + data[i+1];
                if (len < 2 || i + len > data.Length) return null;
                if (marker is 0xc0 or 0xc1 or 0xc2)
                {
                    if (len < 8) return null;
                    var h = (data[i+3]<<8)+data[i+4]; var w = (data[i+5]<<8)+data[i+6];
                    return w is > 0 and <= 8192 && h is > 0 and <= 8192 && (long)w*h <= 32000000 ? "image/jpeg" : null;
                }
                i += len;
            }
        }
        return null;
    }
    public async Task<IActionResult> OnGetImageAsync(Guid id)
    {
        if (!await AuthorizedCompany()) return Forbid();
        var image = await DbContext.AnnouncementStudioDesigns.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.CompanyId == CompanyId, Ct);
        if (image == null) return NotFound();
        Response.Headers.CacheControl = "private, no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(image.Data, image.ContentType);
    }
    public async Task<IActionResult> OnPostDisableDesignAsync(Guid id)
    {
        if (!CanEditLibrary || !await AuthorizedCompany()) return Forbid();
        var image = await DbContext.AnnouncementStudioDesigns.SingleOrDefaultAsync(d => d.Id == id && d.CompanyId == CompanyId, Ct);
        if (image == null) return NotFound();
        image.IsActive = false; await DbContext.SaveChangesAsync(Ct); return RedirectToPage(new { CompanyId });
    }
    public async Task<IActionResult> OnPostComposeAsync()
    {
        if (!await AuthorizedCompany()) return Forbid();
        await Load();
        var template = Templates.FirstOrDefault(t => t.Definition.Key == TemplateKey && t.IsActive)?.Definition;
        if (template == null) return await Error("القالب غير موجود أو غير فعال.");
        List<StudioRendered> texts;
        try { texts = AnnouncementStudio.Render(template, Values, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3))); }
        catch (ArgumentException e) { return await Error(e.Message); }
        var designId = Guid.TryParse(Design, out var id) ? id : Guid.Empty;
        var asset = Design.StartsWith("builtin:", StringComparison.Ordinal) ? Design[8..] : null;
        if (designId != Guid.Empty && (!Designs.Any(d => d.Id == designId) || (template.DesignIds.Count > 0 && !template.DesignIds.Contains(designId))))
            return await Error("التصميم غير متاح لهذا القالب.");
        var selected = EmployeeIds.Where(id => id > 0).Distinct().ToArray();
        if (await DbContext.Employees.CountAsync(e => selected.Contains(e.Id) && e.CompanyId == CompanyId && !e.IsDeleted, Ct) != selected.Length)
            return await Error("أحد الموظفين المستهدفين خارج الشركة.");
        var first = texts.First();
        var result = await AnnouncementService.CreateAsync(new()
        {
            RequestId = RequestId == Guid.Empty ? Guid.NewGuid() : RequestId,
            LanguageCode = first.LanguageCode, Title = first.Title, Body = first.Body, Translations = texts,
            Category = template.Name, PublishNow = PublishNow, CommentsEnabled = CommentsEnabled,
            CompanyIds = selected.Length == 0 ? new[] { CompanyId } : [], EmployeeIds = selected,
            PresentationJson = JsonSerializer.Serialize(new StudioPresentation(designId, Fit, Position, TextPlacement, asset))
        }, BuildAnnouncementActor(), Ct);
        if (!result.Success) return await Error(result.Message);
        StatusMessage = result.Message; return RedirectToPage("/Engagement/Index", new { tab = "announcements" });
    }
}
