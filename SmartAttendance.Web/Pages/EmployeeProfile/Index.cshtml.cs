using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeProfile;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int EmployeeId { get; set; }

    public EmployeeHeader Header { get; set; } = new();

    public List<EmployeeDependent> Dependents { get; set; } = new();

    public List<EmployeeEducation> Educations { get; set; } = new();

    public List<EmployeeExperience> Experiences { get; set; } = new();

    public List<EmployeeCertificate> Certificates { get; set; } = new();

    public int DependentCount => Dependents.Count;

    public int SupportedCount => Dependents.Count(d => d.IsDependent);

    public int EmergencyContactCount => Dependents.Count(d => d.IsEmergencyContact);

    [BindProperty]
    public DependentInput Input { get; set; } = new();

    [BindProperty]
    public EducationInputModel EducationInput { get; set; } = new();

    [BindProperty]
    public ExperienceInputModel ExperienceInput { get; set; } = new();

    [BindProperty]
    public CertificateInputModel CertificateInput { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await EmployeeDependentSchema.EnsureAsync(_dbContext);
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);

        if (!await LoadHeaderAsync())
        {
            return NotFound();
        }

        await LoadDependentsAsync();
        await LoadRecordsAsync();
        return Page();
    }

    private async Task LoadRecordsAsync()
    {
        Educations = await _dbContext.EmployeeEducations.AsNoTracking()
            .Where(x => x.EmployeeId == EmployeeId)
            .OrderByDescending(x => x.IsLatest).ThenByDescending(x => x.ToDate)
            .ToListAsync();

        Experiences = await _dbContext.EmployeeExperiences.AsNoTracking()
            .Where(x => x.EmployeeId == EmployeeId)
            .OrderByDescending(x => x.ToDate)
            .ToListAsync();

        Certificates = await _dbContext.EmployeeCertificates.AsNoTracking()
            .Where(x => x.EmployeeId == EmployeeId)
            .OrderByDescending(x => x.IssueDate)
            .ToListAsync();
    }

    private async Task<bool> EmployeeExistsAsync() =>
        await _dbContext.Employees.AnyAsync(e => e.Id == EmployeeId && !e.IsDeleted);

    private (string user, DateTime now) Stamp() => (User.Identity?.Name ?? "System", DateTime.UtcNow);

    // ---- Education ----
    public async Task<IActionResult> OnPostSaveEducationAsync()
    {
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        if (!await EmployeeExistsAsync()) return NotFound();
        if (string.IsNullOrWhiteSpace(EducationInput.University))
        {
            ErrorMessage = "اسم الجامعة مطلوب."; return RedirectToPage(new { EmployeeId });
        }
        var (user, now) = Stamp();
        var e = EducationInput.Id > 0
            ? await _dbContext.EmployeeEducations.FirstOrDefaultAsync(x => x.Id == EducationInput.Id && x.EmployeeId == EmployeeId)
            : null;
        if (e == null)
        {
            e = new EmployeeEducation { EmployeeId = EmployeeId, CreatedAt = now, CreatedBy = user };
            _dbContext.EmployeeEducations.Add(e);
        }
        else { e.UpdatedAt = now; e.UpdatedBy = user; }
        e.Country = Clean(EducationInput.Country);
        e.University = EducationInput.University.Trim();
        e.Degree = Clean(EducationInput.Degree);
        e.Major = Clean(EducationInput.Major);
        e.FromDate = EducationInput.FromDate;
        e.ToDate = EducationInput.ToDate;
        e.IsLatest = EducationInput.IsLatest;
        e.Note = Clean(EducationInput.Note);
        await _dbContext.SaveChangesAsync();
        SuccessMessage = "تم حفظ المؤهل التعليمي.";
        return RedirectToPage(new { EmployeeId });
    }

    public async Task<IActionResult> OnPostDeleteEducationAsync(int recordId)
    {
        var e = await _dbContext.EmployeeEducations.FirstOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == EmployeeId);
        if (e != null) { e.IsDeleted = true; await _dbContext.SaveChangesAsync(); SuccessMessage = "تم الحذف."; }
        return RedirectToPage(new { EmployeeId });
    }

    // ---- Experience ----
    public async Task<IActionResult> OnPostSaveExperienceAsync()
    {
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        if (!await EmployeeExistsAsync()) return NotFound();
        if (string.IsNullOrWhiteSpace(ExperienceInput.CompanyName))
        {
            ErrorMessage = "اسم الشركة مطلوب."; return RedirectToPage(new { EmployeeId });
        }
        var (user, now) = Stamp();
        var e = ExperienceInput.Id > 0
            ? await _dbContext.EmployeeExperiences.FirstOrDefaultAsync(x => x.Id == ExperienceInput.Id && x.EmployeeId == EmployeeId)
            : null;
        if (e == null)
        {
            e = new EmployeeExperience { EmployeeId = EmployeeId, CreatedAt = now, CreatedBy = user };
            _dbContext.EmployeeExperiences.Add(e);
        }
        else { e.UpdatedAt = now; e.UpdatedBy = user; }
        e.CompanyName = ExperienceInput.CompanyName.Trim();
        e.Country = Clean(ExperienceInput.Country);
        e.JobTitle = Clean(ExperienceInput.JobTitle);
        e.FromDate = ExperienceInput.FromDate;
        e.ToDate = ExperienceInput.ToDate;
        e.Note = Clean(ExperienceInput.Note);
        await _dbContext.SaveChangesAsync();
        SuccessMessage = "تم حفظ الخبرة.";
        return RedirectToPage(new { EmployeeId });
    }

    public async Task<IActionResult> OnPostDeleteExperienceAsync(int recordId)
    {
        var e = await _dbContext.EmployeeExperiences.FirstOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == EmployeeId);
        if (e != null) { e.IsDeleted = true; await _dbContext.SaveChangesAsync(); SuccessMessage = "تم الحذف."; }
        return RedirectToPage(new { EmployeeId });
    }

    // ---- Certificate ----
    public async Task<IActionResult> OnPostSaveCertificateAsync()
    {
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        if (!await EmployeeExistsAsync()) return NotFound();
        if (string.IsNullOrWhiteSpace(CertificateInput.Name))
        {
            ErrorMessage = "اسم الشهادة مطلوب."; return RedirectToPage(new { EmployeeId });
        }
        var (user, now) = Stamp();
        var e = CertificateInput.Id > 0
            ? await _dbContext.EmployeeCertificates.FirstOrDefaultAsync(x => x.Id == CertificateInput.Id && x.EmployeeId == EmployeeId)
            : null;
        if (e == null)
        {
            e = new EmployeeCertificate { EmployeeId = EmployeeId, CreatedAt = now, CreatedBy = user };
            _dbContext.EmployeeCertificates.Add(e);
        }
        else { e.UpdatedAt = now; e.UpdatedBy = user; }
        e.Name = CertificateInput.Name.Trim();
        e.ReferenceNo = Clean(CertificateInput.ReferenceNo);
        e.IssueDate = CertificateInput.IssueDate;
        e.FromDate = CertificateInput.FromDate;
        e.ToDate = CertificateInput.ToDate;
        e.Note = Clean(CertificateInput.Note);
        await _dbContext.SaveChangesAsync();
        SuccessMessage = "تم حفظ الشهادة.";
        return RedirectToPage(new { EmployeeId });
    }

    public async Task<IActionResult> OnPostDeleteCertificateAsync(int recordId)
    {
        var e = await _dbContext.EmployeeCertificates.FirstOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == EmployeeId);
        if (e != null) { e.IsDeleted = true; await _dbContext.SaveChangesAsync(); SuccessMessage = "تم الحذف."; }
        return RedirectToPage(new { EmployeeId });
    }

    public async Task<IActionResult> OnPostSaveDependentAsync()
    {
        await EmployeeDependentSchema.EnsureAsync(_dbContext);

        var employeeExists = await _dbContext.Employees
            .AnyAsync(e => e.Id == EmployeeId && !e.IsDeleted);

        if (!employeeExists)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ErrorMessage = "اسم المعال مطلوب.";
            return RedirectToPage(new { EmployeeId });
        }

        var userName = User.Identity?.Name ?? "System";
        var now = DateTime.UtcNow;

        EmployeeDependent entity;
        if (Input.Id > 0)
        {
            entity = await _dbContext.EmployeeDependents
                .FirstOrDefaultAsync(d => d.Id == Input.Id && d.EmployeeId == EmployeeId)
                ?? throw new InvalidOperationException();
            entity.UpdatedAt = now;
            entity.UpdatedBy = userName;
        }
        else
        {
            entity = new EmployeeDependent
            {
                EmployeeId = EmployeeId,
                CreatedAt = now,
                CreatedBy = userName
            };
            _dbContext.EmployeeDependents.Add(entity);
        }

        entity.Relation = Input.Relation;
        entity.Name = Input.Name.Trim();
        entity.NameOther = Clean(Input.NameOther);
        entity.BirthDate = Input.BirthDate;
        entity.MarriageDate = Input.MarriageDate;
        entity.Religion = Clean(Input.Religion);
        entity.Nationality = Clean(Input.Nationality);
        entity.NationalId = Clean(Input.NationalId);
        entity.PassportNo = Clean(Input.PassportNo);
        entity.IsCitizen = Input.IsCitizen;
        entity.MaritalStatus = Clean(Input.MaritalStatus);
        entity.IsEmergencyContact = Input.IsEmergencyContact;
        entity.IsSpecialNeeds = Input.IsSpecialNeeds;
        entity.IsWorking = Input.IsWorking;
        entity.IsDependent = Input.IsDependent;
        entity.MobilePhone = Clean(Input.MobilePhone);
        entity.CompanyName = Clean(Input.CompanyName);
        entity.Note = Clean(Input.Note);

        await _dbContext.SaveChangesAsync();

        SuccessMessage = Input.Id > 0 ? "تم تحديث المعال." : "تمت إضافة المعال.";
        return RedirectToPage(new { EmployeeId });
    }

    public async Task<IActionResult> OnPostDeleteDependentAsync(int dependentId)
    {
        var entity = await _dbContext.EmployeeDependents
            .FirstOrDefaultAsync(d => d.Id == dependentId && d.EmployeeId == EmployeeId);

        if (entity != null)
        {
            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = User.Identity?.Name ?? "System";
            await _dbContext.SaveChangesAsync();
            SuccessMessage = "تم حذف المعال.";
        }

        return RedirectToPage(new { EmployeeId });
    }

    private async Task<bool> LoadHeaderAsync()
    {
        var header = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == EmployeeId && !e.IsDeleted)
            .Select(e => new EmployeeHeader
            {
                Id = e.Id,
                EmployeeNo = e.EmployeeNo,
                FullName = e.FullName,
                IsActive = e.IsActive,
                DepartmentName = e.Department.Name,
                BranchName = e.Branch.Name,
                Position = e.Position,
                Nationality = e.Nationality,
                HireDate = e.HireDate,
                BirthDate = e.BirthDate,
                Phone = e.Phone,
                Email = e.Email,
                PhotoPath = e.PhotoPath
            })
            .FirstOrDefaultAsync();

        if (header == null)
        {
            return false;
        }

        Header = header;
        return true;
    }

    private async Task LoadDependentsAsync()
    {
        Dependents = await _dbContext.EmployeeDependents
            .AsNoTracking()
            .Where(d => d.EmployeeId == EmployeeId)
            .OrderBy(d => d.Relation)
            .ThenBy(d => d.Name)
            .ToListAsync();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public string RelationText(DependentRelation relation) => relation switch
    {
        DependentRelation.Spouse => "شريك",
        DependentRelation.Son => "إبن",
        DependentRelation.Daughter => "بنت",
        DependentRelation.Relative => "قريب",
        _ => relation.ToString()
    };

    public string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "؟";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0][..1] : $"{parts[0][..1]}{parts[1][..1]}";
    }

    public class EmployeeHeader
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string? Position { get; set; }
        public string? Nationality { get; set; }
        public DateOnly? HireDate { get; set; }
        public DateOnly? BirthDate { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? PhotoPath { get; set; }

        public string InitialsText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FullName)) return "؟";
                var parts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length == 1 ? parts[0][..1] : $"{parts[0][..1]}{parts[1][..1]}";
            }
        }
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

    public class EducationInputModel
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

    public class ExperienceInputModel
    {
        public int Id { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? JobTitle { get; set; }
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public string? Note { get; set; }
    }

    public class CertificateInputModel
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
