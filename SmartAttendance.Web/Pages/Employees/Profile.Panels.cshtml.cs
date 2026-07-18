using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// 360° file panels merged into the employee profile page's detail tab bar:
/// family/dependents, education, experience, certificates. Separate partial so
/// the large main ProfileModel stays untouched. Uses the employee id in <c>Id</c>.
/// </summary>
public partial class ProfileModel
{
    public List<EmployeeDependent> Dependents { get; set; } = new();
    public List<EmployeeEducation> Educations { get; set; } = new();
    public List<EmployeeExperience> Experiences { get; set; } = new();
    public List<EmployeeCertificate> Certificates { get; set; } = new();

    public int DependentCount => Dependents.Count;
    public int SupportedCount => Dependents.Count(d => d.IsDependent);
    public int EmergencyContactCount => Dependents.Count(d => d.IsEmergencyContact);

    [TempData] public string? PanelSuccess { get; set; }
    [TempData] public string? PanelError { get; set; }

    [BindProperty] public DependentInput Dependent { get; set; } = new();
    [BindProperty] public EducationInput Education { get; set; } = new();
    [BindProperty] public ExperienceInput Experience { get; set; } = new();
    [BindProperty] public CertificateInput Certificate { get; set; } = new();

    public async Task LoadPanelsAsync()
    {
        await EmployeeDependentSchema.EnsureAsync(_dbContext);
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);

        Dependents = await _dbContext.EmployeeDependents.AsNoTracking()
            .Where(d => d.EmployeeId == Id)
            .OrderBy(d => d.Relation).ThenBy(d => d.Name).ToListAsync();

        Educations = await _dbContext.EmployeeEducations.AsNoTracking()
            .Where(x => x.EmployeeId == Id)
            .OrderByDescending(x => x.IsLatest).ThenByDescending(x => x.ToDate).ToListAsync();

        Experiences = await _dbContext.EmployeeExperiences.AsNoTracking()
            .Where(x => x.EmployeeId == Id).OrderByDescending(x => x.ToDate).ToListAsync();

        Certificates = await _dbContext.EmployeeCertificates.AsNoTracking()
            .Where(x => x.EmployeeId == Id).OrderByDescending(x => x.IssueDate).ToListAsync();
    }

    private async Task<bool> PanelEmployeeExistsAsync() =>
        await _dbContext.Employees.AnyAsync(e => e.Id == Id && !e.IsDeleted);

    private IActionResult BackTo(string tab) => RedirectToPage("./Profile", null, new { Id }, tab);

    private (string user, DateTime now) PanelStamp() => (User.Identity?.Name ?? "System", DateTime.UtcNow);

    private static string? CleanText(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ---- Dependents ----
    public async Task<IActionResult> OnPostSaveDependentAsync()
    {
        await EmployeeDependentSchema.EnsureAsync(_dbContext);
        if (!await PanelEmployeeExistsAsync()) return NotFound();
        if (string.IsNullOrWhiteSpace(Dependent.Name)) { PanelError = "اسم المعال مطلوب."; return BackTo("family"); }
        var (user, now) = PanelStamp();
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
        return BackTo("family");
    }

    public async Task<IActionResult> OnPostDeleteDependentAsync(int recordId)
    {
        var e = await _dbContext.EmployeeDependents.FirstOrDefaultAsync(d => d.Id == recordId && d.EmployeeId == Id);
        if (e != null) { e.IsDeleted = true; e.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = User.Identity?.Name ?? "System"; await _dbContext.SaveChangesAsync(); PanelSuccess = "تم الحذف."; }
        return BackTo("family");
    }

    // ---- Education ----
    public async Task<IActionResult> OnPostSaveEducationAsync()
    {
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        if (!await PanelEmployeeExistsAsync()) return NotFound();
        if (string.IsNullOrWhiteSpace(Education.University)) { PanelError = "اسم الجامعة مطلوب."; return BackTo("education"); }
        var (user, now) = PanelStamp();
        var e = Education.Id > 0
            ? await _dbContext.EmployeeEducations.FirstOrDefaultAsync(x => x.Id == Education.Id && x.EmployeeId == Id)
            : null;
        if (e == null) { e = new EmployeeEducation { EmployeeId = Id, CreatedAt = now, CreatedBy = user }; _dbContext.EmployeeEducations.Add(e); }
        else { e.UpdatedAt = now; e.UpdatedBy = user; }
        e.Country = CleanText(Education.Country);
        e.University = Education.University.Trim();
        e.Degree = CleanText(Education.Degree);
        e.Major = CleanText(Education.Major);
        e.FromDate = Education.FromDate;
        e.ToDate = Education.ToDate;
        e.IsLatest = Education.IsLatest;
        e.Note = CleanText(Education.Note);
        await _dbContext.SaveChangesAsync();
        PanelSuccess = "تم حفظ المؤهل.";
        return BackTo("education");
    }

    public async Task<IActionResult> OnPostDeleteEducationAsync(int recordId)
    {
        var e = await _dbContext.EmployeeEducations.FirstOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == Id);
        if (e != null) { e.IsDeleted = true; await _dbContext.SaveChangesAsync(); PanelSuccess = "تم الحذف."; }
        return BackTo("education");
    }

    // ---- Experience ----
    public async Task<IActionResult> OnPostSaveExperienceAsync()
    {
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        if (!await PanelEmployeeExistsAsync()) return NotFound();
        if (string.IsNullOrWhiteSpace(Experience.CompanyName)) { PanelError = "اسم الشركة مطلوب."; return BackTo("experience"); }
        var (user, now) = PanelStamp();
        var e = Experience.Id > 0
            ? await _dbContext.EmployeeExperiences.FirstOrDefaultAsync(x => x.Id == Experience.Id && x.EmployeeId == Id)
            : null;
        if (e == null) { e = new EmployeeExperience { EmployeeId = Id, CreatedAt = now, CreatedBy = user }; _dbContext.EmployeeExperiences.Add(e); }
        else { e.UpdatedAt = now; e.UpdatedBy = user; }
        e.CompanyName = Experience.CompanyName.Trim();
        e.Country = CleanText(Experience.Country);
        e.JobTitle = CleanText(Experience.JobTitle);
        e.FromDate = Experience.FromDate;
        e.ToDate = Experience.ToDate;
        e.Note = CleanText(Experience.Note);
        await _dbContext.SaveChangesAsync();
        PanelSuccess = "تم حفظ الخبرة.";
        return BackTo("experience");
    }

    public async Task<IActionResult> OnPostDeleteExperienceAsync(int recordId)
    {
        var e = await _dbContext.EmployeeExperiences.FirstOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == Id);
        if (e != null) { e.IsDeleted = true; await _dbContext.SaveChangesAsync(); PanelSuccess = "تم الحذف."; }
        return BackTo("experience");
    }

    // ---- Certificate ----
    public async Task<IActionResult> OnPostSaveCertificateAsync()
    {
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        if (!await PanelEmployeeExistsAsync()) return NotFound();
        if (string.IsNullOrWhiteSpace(Certificate.Name)) { PanelError = "اسم الشهادة مطلوب."; return BackTo("certificates"); }
        var (user, now) = PanelStamp();
        var e = Certificate.Id > 0
            ? await _dbContext.EmployeeCertificates.FirstOrDefaultAsync(x => x.Id == Certificate.Id && x.EmployeeId == Id)
            : null;
        if (e == null) { e = new EmployeeCertificate { EmployeeId = Id, CreatedAt = now, CreatedBy = user }; _dbContext.EmployeeCertificates.Add(e); }
        else { e.UpdatedAt = now; e.UpdatedBy = user; }
        e.Name = Certificate.Name.Trim();
        e.ReferenceNo = CleanText(Certificate.ReferenceNo);
        e.IssueDate = Certificate.IssueDate;
        e.FromDate = Certificate.FromDate;
        e.ToDate = Certificate.ToDate;
        e.Note = CleanText(Certificate.Note);
        await _dbContext.SaveChangesAsync();
        PanelSuccess = "تم حفظ الشهادة.";
        return BackTo("certificates");
    }

    public async Task<IActionResult> OnPostDeleteCertificateAsync(int recordId)
    {
        var e = await _dbContext.EmployeeCertificates.FirstOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == Id);
        if (e != null) { e.IsDeleted = true; await _dbContext.SaveChangesAsync(); PanelSuccess = "تم الحذف."; }
        return BackTo("certificates");
    }

    public string RelationText(DependentRelation relation) => relation switch
    {
        DependentRelation.Spouse => "شريك",
        DependentRelation.Son => "إبن",
        DependentRelation.Daughter => "بنت",
        DependentRelation.Relative => "قريب",
        _ => relation.ToString()
    };

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

    public class EducationInput
    {
        public int Id { get; set; }
        public string? Country { get; set; }
        public string University { get; set; } = string.Empty;
        public string? Degree { get; set; }
        public string? Major { get; set; }
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public bool IsLatest { get; set; }
        public string? Note { get; set; }
    }

    public class ExperienceInput
    {
        public int Id { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? JobTitle { get; set; }
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public string? Note { get; set; }
    }

    public class CertificateInput
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ReferenceNo { get; set; }
        public DateOnly? IssueDate { get; set; }
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public string? Note { get; set; }
    }
}
