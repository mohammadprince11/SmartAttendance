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
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Engagement;

[Authorize]
[RequestSizeLimit(6 * 1024 * 1024)]
public class StudioModel(ApplicationDbContext db, IAnnouncementService service, ILocalizationDictionaryService? dictionary = null) : EngagementPageModel(db, service)
{
    [BindProperty(SupportsGet = true)] public int CompanyId { get; set; }
    [BindProperty] public string? FreePrimaryLanguage { get; set; }
    [BindProperty] public List<FreeAnnouncementText> FreeTexts { get; set; } = [];
    [BindProperty] public IFormFile? FreeImage { get; set; }
    public bool FreeFormActive { get; private set; }
    public IReadOnlyList<DictionaryLanguage> FreeLanguages { get; private set; } = [];
    [BindProperty] public string TemplateJson { get; set; } = "";
    [BindProperty] public Guid Revision { get; set; }
    [BindProperty(SupportsGet = true)] public bool Embed { get; set; }
    [BindProperty] public string TemplateKey { get; set; } = "welcome";
    [BindProperty] public Dictionary<string, string> Values { get; set; } = new();
    [BindProperty] public int SubjectEmployeeId { get; set; }
    public string? SubjectEmployeeName { get; private set; }
    public string? SubjectEmployeeEnglishName { get; private set; }
    public string? SubjectEmployeeCode { get; private set; }
    public string? SubjectPositionName { get; private set; }
    [BindProperty] public int? NewPositionId { get; set; }
    public List<PositionChoice> Positions { get; private set; } = [];
    public record PositionChoice(int Id, string Name);
    [BindProperty] public string? Design { get; set; }
    [BindProperty] public string Fit { get; set; } = "contain";
    [BindProperty] public string Position { get; set; } = "center";
    [BindProperty] public string TextPlacement { get; set; } = "above";
    [BindProperty] public Guid RequestId { get; set; } = Guid.NewGuid();
    [BindProperty] public bool PublishNow { get; set; } = true;
    [BindProperty] public bool CommentsEnabled { get; set; } = true;
    [BindProperty] public int[] EmployeeIds { get; set; } = [];
    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public string DesignName { get; set; } = "";
    [BindProperty] public string? DesignTemplateKey { get; set; }
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
        FreeLanguages = dictionary == null ? [] : await dictionary.GetLanguagesAsync(Ct);
        FreePrimaryLanguage ??= FreeLanguages.FirstOrDefault(l => l.Code.Equals(System.Globalization.CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase))?.Code
            ?? FreeLanguages.FirstOrDefault(l => l.IsDefault)?.Code ?? FreeLanguages.FirstOrDefault()?.Code;
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
        Positions = await DbContext.HrJobPositions.AsNoTracking().Where(p => p.CompanyId == CompanyId && p.IsActive)
            .OrderBy(p => p.ArabicName).Select(p => new PositionChoice(p.Id, p.ArabicName)).ToListAsync(Ct);
        var subjects = SubjectEmployeeId > 0 ? await SubjectEmployees() : null;
        var subject = subjects == null ? null : await SubjectDetails(subjects.Where(e => e.Id == SubjectEmployeeId)).SingleOrDefaultAsync(Ct);
        SubjectEmployeeName = subject?.FullName;
        SubjectEmployeeEnglishName = subject?.EnglishName;
        SubjectEmployeeCode = subject?.EmployeeNo;
        SubjectPositionName = subject?.PositionName;
    }
    public record SubjectDetail(int Id, string EmployeeNo, string FullName, string? PositionName,
        string? FirstNameEn, string? SecondNameEn, string? ThirdNameEn, string? LastNameEn)
    {
        public string EnglishName => string.Join(' ', new[] { FirstNameEn, SecondNameEn, ThirdNameEn, LastNameEn }
            .Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
    }
    // PositionId is preferred; legacy text remains a fallback. Both lookups stay company-scoped.
    private IQueryable<SubjectDetail> SubjectDetails(IQueryable<Employee> employees) => employees.Select(e => new SubjectDetail(
        e.Id, e.EmployeeNo, e.FullName,
        DbContext.HrJobPositions.Where(p => p.Id == e.PositionId && p.CompanyId == CompanyId).Select(p => p.ArabicName).FirstOrDefault() ?? e.Position,
        e.FirstNameEn, e.SecondNameEn, e.ThirdNameEn, e.LastNameEn));
    // Directory permission AND both employee scopes AND the selected company, before SQL reads.
    private async Task<IQueryable<Employee>?> SubjectEmployees()
    {
        if (!await AuthorizedCompany()) return null;
        var permissions = HttpContext.RequestServices.GetRequiredService<IPermissionAuthorizationService>();
        var effective = HttpContext.RequestServices.GetRequiredService<IEffectiveScopeService>();
        var userId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);
        var compatibility = PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewDirectory);
        if (!await permissions.HasPermissionAsync(userId, PeoplePermissionCodes.ViewDirectory, compatibility, Ct)) return null;
        var rules = await permissions.GetPeopleDataScopeAsync(userId, PeoplePermissionCodes.ViewDirectory, compatibility, Ct);
        var access = await effective.GetEmployeesAccessScopeAsync(userId, role.Equals("Admin", StringComparison.OrdinalIgnoreCase), Ct);
        return DbContext.Employees.AsNoTracking().Where(e => e.CompanyId == CompanyId && !e.IsDeleted)
            .ApplyPeopleDataScope(rules).ApplyPeopleDataScope(access);
    }
    public async Task<IActionResult> OnGetSubjectEmployeesAsync(string? q)
    {
        Response.Headers.CacheControl = "private, no-store";
        var query = await SubjectEmployees();
        if (query == null) return Forbid();
        var term = (q ?? "").Trim();
        if (term.Length > 100) return BadRequest();
        if (term.Length > 0) query = query.Where(e => e.FullName.Contains(term) || e.EmployeeNo.Contains(term));
        var rows = await SubjectDetails(query.OrderByDescending(e => e.EmployeeNo == term).ThenBy(e => e.EmployeeNo).Take(51)).ToListAsync(Ct);
        return new JsonResult(new { total = Math.Min(rows.Count, 50), capped = rows.Count > 50,
            items = rows.Take(50).Select(e => new { id = e.Id, code = e.EmployeeNo, name = e.FullName, englishName = e.EnglishName, positionName = e.PositionName, unit = "", hierarchy = "" }) });
    }
    public virtual async Task<IActionResult> OnGetAsync() { await Load(); return CompanyId > 0 && await AuthorizedCompany() ? Page() : Forbid(); }
    public IActionResult OnGetSaved() => Content("<!doctype html><html><body><script>window.parent.postMessage({type:'zynora-announcement-saved'},window.location.origin);</script></body></html>", "text/html");
    private IActionResult ComposeSuccess() => Embed
        ? RedirectToPage("/Engagement/Studio", new { handler = "Saved", Embed = true })
        : RedirectToPage("/Engagement/Index", new { tab = "work" });
    private async Task<IActionResult> Error(string message)
    {
        if (dictionary != null)
        {
            var catalog = await dictionary.GetCatalogAsync(System.Globalization.CultureInfo.CurrentUICulture.Name, Ct);
            if (catalog.TryGetValue(message, out var translated) && !string.IsNullOrWhiteSpace(translated)) message = translated;
        }
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
        // Upload is an independent form: compose/template fields are not submitted here.
        foreach (var key in ModelState.Keys.Where(k => k != nameof(Upload) && k != nameof(DesignName) && k != nameof(DesignTemplateKey) && k != nameof(CompanyId)).ToArray())
            ModelState.Remove(key);
        if (Upload == null || Upload.Length is < 24 or > 5242880 || string.IsNullOrWhiteSpace(DesignName) || DesignName.Length > 150)
            return await Error("اختر صورة PNG أو JPEG بحد أقصى 5 ميغابايت وأدخل اسم التصميم.");
        using var stream = new MemoryStream(); await Upload.CopyToAsync(stream, Ct); var bytes = stream.ToArray();
        SmartAttendance.Web.Infrastructure.AnnouncementImageNormalizer.Result normalized;
        try { normalized = await SmartAttendance.Web.Infrastructure.AnnouncementImageNormalizer.NormalizeAsync(bytes, Ct); }
        catch (InvalidDataException ex) { return await Error(ex.Message); }
        var image = new AnnouncementStudioDesign { CompanyId = CompanyId, Name = DesignName.Trim(), ContentType = normalized.ContentType, Data = normalized.Data };
        var linkError = await AssignDesignAsync(image.Id, DesignTemplateKey);
        if (linkError != null) return await Error(linkError);
        DbContext.AnnouncementStudioDesigns.Add(image);
        try { await DbContext.SaveChangesAsync(Ct); }
        catch (DbUpdateException) { return await Error("تغيّر ربط التصاميم. أعد تحميل الصفحة وحاول مجدداً."); }
        StatusMessage = "تمت إضافة التصميم إلى مكتبة الشركة.";
        return RedirectToPage(new { CompanyId });
    }
    // Membership is stored in the existing company-scoped template profiles; no schema change.
    private async Task<string?> AssignDesignAsync(Guid designId, string? templateKey)
    {
        var rows = await DbContext.AnnouncementStudioProfiles.Where(p => p.CompanyId == CompanyId).ToListAsync(Ct);
        var selectedRow = rows.SingleOrDefault(p => p.Key == templateKey);
        var selected = selectedRow == null ? AnnouncementStudio.Defaults().FirstOrDefault(t => t.Key == templateKey)
            : JsonSerializer.Deserialize<StudioTemplate>(selectedRow.DefinitionJson);
        if (string.IsNullOrWhiteSpace(templateKey) || selected == null || selectedRow?.IsActive == false)
            return "اختر نوع إعلان فعالاً لربط التصميم به.";
        if (selectedRow == null && dictionary != null)
            foreach (var language in await dictionary.GetLanguagesAsync(Ct))
                AnnouncementTemplateDictionary.Apply(selected, language.Code, await dictionary.GetCatalogAsync(language.Code, Ct));
        if (!selected.DesignIds.Contains(designId) && selected.DesignIds.Count >= 30)
            return "القالب يحتوي 30 تصميماً. أزل ربط أحد التصاميم أولاً.";
        foreach (var row in rows.Where(p => p.Key != templateKey))
        {
            var definition = JsonSerializer.Deserialize<StudioTemplate>(row.DefinitionJson)!;
            if (definition.DesignIds.RemoveAll(id => id == designId) == 0) continue;
            row.DefinitionJson = JsonSerializer.Serialize(definition); row.Revision = Guid.NewGuid();
        }
        if (!selected.DesignIds.Contains(designId)) selected.DesignIds.Add(designId);
        if (selectedRow == null)
        {
            selectedRow = new() { CompanyId = CompanyId, Key = templateKey };
            DbContext.AnnouncementStudioProfiles.Add(selectedRow);
        }
        selectedRow.DefinitionJson = JsonSerializer.Serialize(selected); selectedRow.Revision = Guid.NewGuid();
        return null;
    }
    public async Task<IActionResult> OnPostLinkDesignAsync(Guid id)
    {
        if (!CanEditLibrary || !await AuthorizedCompany()) return Forbid();
        if (!await DbContext.AnnouncementStudioDesigns.AnyAsync(d => d.CompanyId == CompanyId && d.Id == id && d.IsActive, Ct)) return NotFound();
        // No fields from the compose or template editor belong to this form.
        foreach (var key in ModelState.Keys.Where(k => k != nameof(DesignTemplateKey) && k != nameof(CompanyId) && k != nameof(id)).ToArray()) ModelState.Remove(key);
        var error = await AssignDesignAsync(id, DesignTemplateKey);
        if (error != null) return await Error(error);
        try { await DbContext.SaveChangesAsync(Ct); }
        catch (DbUpdateException) { return await Error("تغيّر ربط التصاميم. أعد تحميل الصفحة وحاول مجدداً."); }
        StatusMessage = "تم حفظ ربط التصميم بنوع الإعلان.";
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
    public async Task<IActionResult> OnPostFreeComposeAsync()
    {
        FreeFormActive = true;
        TemplateKey = "__free";
        if (!await AuthorizedCompany()) return Forbid();
        await Load();
        // Only this form's own fields are relevant; template/library inputs are separate forms.
        foreach (var key in ModelState.Keys.Where(k => k is not (nameof(CompanyId) or nameof(FreePrimaryLanguage) or nameof(FreeImage) or nameof(RequestId) or nameof(PublishNow) or nameof(CommentsEnabled)) && !k.StartsWith(nameof(FreeTexts) + "[") && !k.StartsWith(nameof(EmployeeIds))).ToArray())
            ModelState.Remove(key);
        if (!ModelState.IsValid) return await Error("راجع حقول الإعلان الحر.");
        List<StudioRendered> texts;
        try { texts = FreeAnnouncement.Normalize(FreeTexts, FreePrimaryLanguage, FreeLanguages.Select(l => l.Code)); }
        catch (ArgumentException ex) { return await Error(ex.Message); }
        var selected = EmployeeIds.Where(id => id > 0).Distinct().ToArray();
        var subjects = selected.Length == 0 ? null : await SubjectEmployees();
        if (selected.Length > 0 && (subjects == null || await subjects.CountAsync(e => selected.Contains(e.Id), Ct) != selected.Length))
            return await Error("أحد الموظفين المستهدفين خارج الشركة.");
        AnnouncementImageUpload? image = null;
        if (FreeImage != null)
        {
            if (FreeImage.Length is < 24 or > 5242880) return await Error("اختر صورة PNG أو JPEG بحد أقصى 5 ميغابايت وأدخل اسم التصميم.");
            using var stream = new MemoryStream(); await FreeImage.CopyToAsync(stream, Ct);
            try
            {
                var normalized = await SmartAttendance.Web.Infrastructure.AnnouncementImageNormalizer.NormalizeAsync(stream.ToArray(), Ct);
                image = new(Guid.NewGuid(), CompanyId, texts[0].Title[..Math.Min(150, texts[0].Title.Length)], normalized.ContentType, normalized.Data);
            }
            catch (InvalidDataException ex) { return await Error(ex.Message); }
        }
        var primary = texts[0];
        var result = await AnnouncementService.CreateAsync(new()
        {
            RequestId = RequestId == Guid.Empty ? Guid.NewGuid() : RequestId,
            LanguageCode = primary.LanguageCode, Title = primary.Title, Body = primary.Body, Translations = texts,
            Category = "عام", PublishNow = PublishNow, CommentsEnabled = CommentsEnabled,
            CompanyIds = selected.Length == 0 ? [CompanyId] : [], EmployeeIds = selected,
            ImageUpload = image,
            PresentationJson = JsonSerializer.Serialize(new StudioPresentation(image?.Id ?? Guid.Empty, "contain", "center", "above", PrimaryLanguage: primary.LanguageCode))
        }, BuildAnnouncementActor(), Ct);
        if (!result.Success) return await Error(result.Message);
        StatusMessage = result.Message;
        return ComposeSuccess();
    }
    public async Task<IActionResult> OnPostComposeAsync()
    {
        if (!await AuthorizedCompany()) return Forbid();
        await Load();
        var template = Templates.FirstOrDefault(t => t.Definition.Key == TemplateKey && t.IsActive)?.Definition;
        if (template == null) return await Error("القالب غير موجود أو غير فعال.");
        string? englishPersonName = null;
        if (template.Fields.Any(f => f.Key == "person"))
        {
            var subjects = await SubjectEmployees();
            var subject = subjects == null ? null : await SubjectDetails(subjects.Where(e => e.Id == SubjectEmployeeId)).SingleOrDefaultAsync(Ct);
            if (subject == null || string.IsNullOrWhiteSpace(subject.FullName)) return await Error("اختر موظفاً من الشركة بالاسم أو الكود.");
            Values["person"] = subject.FullName; // Never trust posted names or current positions.
            englishPersonName = subject.EnglishName; // Read from the scoped employee record, never the posted form.
            foreach (var key in new[] { "position", "oldPosition" }.Where(key => template.Fields.Any(f => f.Key == key)))
            {
                if (string.IsNullOrWhiteSpace(subject.PositionName)) return await Error("لم يُسجل منصب لهذا الموظف. حدّث ملف الموظف أولاً.");
                Values[key] = subject.PositionName;
            }
        }
        if (template.Fields.Any(f => f.Key == "newPosition"))
        {
            var position = await DbContext.HrJobPositions.AsNoTracking()
                .Where(p => p.CompanyId == CompanyId && p.IsActive && p.Id == NewPositionId).Select(p => p.ArabicName).SingleOrDefaultAsync(Ct);
            if (string.IsNullOrWhiteSpace(position)) return await Error("اختر المنصب الجديد من مناصب الشركة الفعالة.");
            if (position == Values.GetValueOrDefault("oldPosition")) return await Error("المنصب الجديد يجب أن يختلف عن المنصب الحالي.");
            Values["newPosition"] = position; // Announcement snapshot only; never update Employee.PositionId.
        }
        List<StudioRendered> texts;
        try { texts = AnnouncementStudio.Render(template, Values, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)), englishPersonName); }
        catch (ArgumentException e) { return await Error(e.Message); }
        var designId = Guid.TryParse(Design, out var id) ? id : Guid.Empty;
        var asset = Design?.StartsWith("builtin:", StringComparison.Ordinal) == true ? Design[8..] : null;
        if ((!string.IsNullOrEmpty(Design) && designId == Guid.Empty && asset == null) ||
            !AnnouncementStudio.IsDesignAllowed(template, designId, asset) ||
            (designId != Guid.Empty && !Designs.Any(d => d.Id == designId)))
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
        StatusMessage = result.Message; return ComposeSuccess();
    }
}
