using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// Structured 360° data that lives inside the employee profile page but isn't a
/// file upload: the family / dependents list shown as a card inside the
/// "ملفات الموظف" tab. Separate partial so the large main ProfileModel stays
/// untouched. Uses the employee id in <c>Id</c>.
/// </summary>
public partial class ProfileModel
{
    public List<EmployeeDependent> Dependents { get; set; } = new();
    public List<EmployeeFileRecord> FileRecords { get; set; } = new();

    public int DependentCount => Dependents.Count;
    public int SupportedCount => Dependents.Count(d => d.IsDependent);
    public int EmergencyContactCount => Dependents.Count(d => d.IsEmergencyContact);

    public IEnumerable<EmployeeFileRecord> RecordsOf(EmployeeRecordType type) =>
        FileRecords.Where(r => r.RecordType == type);

    [TempData] public string? PanelSuccess { get; set; }
    [TempData] public string? PanelError { get; set; }

    [BindProperty] public DependentInput Dependent { get; set; } = new();
    [BindProperty] public FileRecordInput RecordInput { get; set; } = new();
    [BindProperty] public IFormFile? RecordAttachment { get; set; }

    public async Task LoadPanelsAsync()
    {
        await EmployeeDependentSchema.EnsureAsync(_dbContext);
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);

        Dependents = await _dbContext.EmployeeDependents.AsNoTracking()
            .Where(d => d.EmployeeId == Id)
            .OrderBy(d => d.Relation).ThenBy(d => d.Name).ToListAsync();

        FileRecords = await _dbContext.EmployeeFileRecords.AsNoTracking()
            .Where(r => r.EmployeeId == Id)
            .OrderByDescending(r => r.ToDate).ThenByDescending(r => r.Id).ToListAsync();
    }

    private IActionResult BackToFiles() => RedirectToPage("./Profile", null, new { Id }, "profile-files");

    private static string? CleanText(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public async Task<IActionResult> OnPostSaveDependentAsync()
    {
        await EmployeeDependentSchema.EnsureAsync(_dbContext);
        if (!await _dbContext.Employees.AnyAsync(e => e.Id == Id && !e.IsDeleted)) return NotFound();
        if (string.IsNullOrWhiteSpace(Dependent.Name)) { PanelError = "اسم المعال مطلوب."; return BackToFiles(); }

        var user = User.Identity?.Name ?? "System";
        var now = DateTime.UtcNow;
        var e = Dependent.Id > 0
            ? await _dbContext.EmployeeDependents.FirstOrDefaultAsync(d => d.Id == Dependent.Id && d.EmployeeId == Id)
            : null;
        if (e == null) { e = new EmployeeDependent { EmployeeId = Id, CreatedAt = now, CreatedBy = user }; _dbContext.EmployeeDependents.Add(e); }
        else { e.UpdatedAt = now; e.UpdatedBy = user; }

        e.Relation = Dependent.Relation;
        e.Name = Dependent.Name.Trim();
        e.NameOther = CleanText(Dependent.NameOther);
        e.BirthDate = Dependent.BirthDate;
        e.MarriageDate = Dependent.MarriageDate;
        e.Religion = CleanText(Dependent.Religion);
        e.Nationality = CleanText(Dependent.Nationality);
        e.NationalId = CleanText(Dependent.NationalId);
        e.PassportNo = CleanText(Dependent.PassportNo);
        e.IsCitizen = Dependent.IsCitizen;
        e.MaritalStatus = CleanText(Dependent.MaritalStatus);
        e.IsEmergencyContact = Dependent.IsEmergencyContact;
        e.IsSpecialNeeds = Dependent.IsSpecialNeeds;
        e.IsWorking = Dependent.IsWorking;
        e.IsDependent = Dependent.IsDependent;
        e.MobilePhone = CleanText(Dependent.MobilePhone);
        e.CompanyName = CleanText(Dependent.CompanyName);
        e.Note = CleanText(Dependent.Note);

        await _dbContext.SaveChangesAsync();
        PanelSuccess = "تم حفظ المعال.";
        return BackToFiles();
    }

    public async Task<IActionResult> OnPostDeleteDependentAsync(int recordId)
    {
        var e = await _dbContext.EmployeeDependents.FirstOrDefaultAsync(d => d.Id == recordId && d.EmployeeId == Id);
        if (e != null)
        {
            e.IsDeleted = true;
            e.UpdatedAt = DateTime.UtcNow;
            e.UpdatedBy = User.Identity?.Name ?? "System";
            await _dbContext.SaveChangesAsync();
            PanelSuccess = "تم الحذف.";
        }
        return BackToFiles();
    }

    // ---- Structured file records (education/experience/certificate/training/medical) ----
    public async Task<IActionResult> OnPostSaveRecordAsync()
    {
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        if (!await _dbContext.Employees.AnyAsync(e => e.Id == Id && !e.IsDeleted)) return NotFound();
        if (string.IsNullOrWhiteSpace(RecordInput.Title)) { PanelError = "العنوان مطلوب."; return BackToFiles(); }

        var user = User.Identity?.Name ?? "System";
        var now = DateTime.UtcNow;
        var r = RecordInput.Id > 0
            ? await _dbContext.EmployeeFileRecords.FirstOrDefaultAsync(x => x.Id == RecordInput.Id && x.EmployeeId == Id)
            : null;
        if (r == null) { r = new EmployeeFileRecord { EmployeeId = Id, CreatedAt = now, CreatedBy = user }; _dbContext.EmployeeFileRecords.Add(r); }
        else { r.UpdatedAt = now; r.UpdatedBy = user; }

        r.RecordType = RecordInput.RecordType;
        r.Title = RecordInput.Title.Trim();
        r.Subtitle = CleanText(RecordInput.Subtitle);
        r.Country = CleanText(RecordInput.Country);
        r.RefNo = CleanText(RecordInput.RefNo);
        r.FromDate = RecordInput.FromDate;
        r.ToDate = RecordInput.ToDate;
        r.Note = CleanText(RecordInput.Note);

        var (name, path) = await SaveRecordFileAsync(RecordAttachment);
        if (path != null) { r.AttachmentName = name; r.AttachmentPath = path; }

        await _dbContext.SaveChangesAsync();
        PanelSuccess = "تم حفظ السجل.";
        return BackToFiles();
    }

    public async Task<IActionResult> OnPostDeleteRecordAsync(int recordId)
    {
        var r = await _dbContext.EmployeeFileRecords.FirstOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == Id);
        if (r != null) { r.IsDeleted = true; r.UpdatedAt = DateTime.UtcNow; r.UpdatedBy = User.Identity?.Name ?? "System"; await _dbContext.SaveChangesAsync(); PanelSuccess = "تم الحذف."; }
        return BackToFiles();
    }

    private static readonly string[] AllowedRecordFileExtensions =
        { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".doc", ".docx", ".xls", ".xlsx" };

    private async Task<(string? name, string? path)> SaveRecordFileAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0) return (null, null);
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedRecordFileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return (null, null);
        if (file.Length > 10 * 1024 * 1024) return (null, null);

        var root = Path.Combine(_environment.WebRootPath, "uploads", "employee-records");
        Directory.CreateDirectory(root);
        var fileName = $"rec_{Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext.ToLowerInvariant()}";
        await using (var stream = System.IO.File.Create(Path.Combine(root, fileName)))
        {
            await file.CopyToAsync(stream);
        }
        return (Path.GetFileName(file.FileName), $"/uploads/employee-records/{fileName}");
    }

    public string RelationText(DependentRelation relation) => relation switch
    {
        DependentRelation.Spouse => "شريك",
        DependentRelation.Son => "إبن",
        DependentRelation.Daughter => "بنت",
        DependentRelation.Relative => "قريب",
        _ => relation.ToString()
    };

    public class FileRecordInput
    {
        public int Id { get; set; }
        public EmployeeRecordType RecordType { get; set; } = EmployeeRecordType.Education;
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Country { get; set; }
        public string? RefNo { get; set; }
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public string? Note { get; set; }
    }

    public class DependentInput
    {
        public int Id { get; set; }
        public DependentRelation Relation { get; set; } = DependentRelation.Spouse;
        public string Name { get; set; } = string.Empty;
        public string? NameOther { get; set; }
        public DateOnly? BirthDate { get; set; }
        public DateOnly? MarriageDate { get; set; }
        public string? Religion { get; set; }
        public string? Nationality { get; set; }
        public string? NationalId { get; set; }
        public string? PassportNo { get; set; }
        public bool IsCitizen { get; set; }
        public string? MaritalStatus { get; set; }
        public bool IsEmergencyContact { get; set; }
        public bool IsSpecialNeeds { get; set; }
        public bool IsWorking { get; set; }
        public bool IsDependent { get; set; }
        public string? MobilePhone { get; set; }
        public string? CompanyName { get; set; }
        public string? Note { get; set; }
    }
}
