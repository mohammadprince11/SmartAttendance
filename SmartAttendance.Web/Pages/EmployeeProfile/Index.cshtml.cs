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

    public int DependentCount => Dependents.Count;

    public int SupportedCount => Dependents.Count(d => d.IsDependent);

    public int EmergencyContactCount => Dependents.Count(d => d.IsEmergencyContact);

    [BindProperty]
    public DependentInput Input { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await EmployeeDependentSchema.EnsureAsync(_dbContext);

        if (!await LoadHeaderAsync())
        {
            return NotFound();
        }

        await LoadDependentsAsync();
        return Page();
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
}
