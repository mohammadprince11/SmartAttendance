using System.ComponentModel.DataAnnotations;

namespace SmartAttendance.Application.Employees.ViewModels;

public class EmployeeCreateViewModel
{
    [Required(ErrorMessage = "كود الموظف مطلوب.")]
    [StringLength(50, ErrorMessage = "كود الموظف يجب ألا يتجاوز 50 حرفاً.")]
    public string EmployeeNo { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم الموظف مطلوب.")]
    [StringLength(200, ErrorMessage = "اسم الموظف يجب ألا يتجاوز 200 حرف.")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "الرقم الوطني يجب ألا يتجاوز 50 حرفاً.")]
    public string? NationalId { get; set; }

    [StringLength(100)] public string? FirstName { get; set; }
    [StringLength(100)] public string? SecondName { get; set; }
    [StringLength(100)] public string? ThirdName { get; set; }
    [StringLength(100)] public string? LastName { get; set; }
    [StringLength(100)] public string? FirstNameEn { get; set; }
    [StringLength(100)] public string? SecondNameEn { get; set; }
    [StringLength(100)] public string? ThirdNameEn { get; set; }
    [StringLength(100)] public string? LastNameEn { get; set; }

    public bool IsCitizen { get; set; } = true;

    [StringLength(50)] public string? PassportNo { get; set; }
    [StringLength(150)] public string? SponsorName { get; set; }
    [StringLength(50)] public string? Religion { get; set; }

    [EmailAddress(ErrorMessage = "صيغة البريد الشخصي غير صحيحة.")]
    [StringLength(200)] public string? PersonalEmail { get; set; }

    [StringLength(100)] public string? MotherCountry { get; set; }
    [StringLength(100)] public string? MotherCity { get; set; }
    [StringLength(20)] public string? PhoneExtension { get; set; }

    public DateOnly? JoiningDate { get; set; }

    [StringLength(50)] public string? WorkType { get; set; }
    [StringLength(100)] public string? JobGrade { get; set; }

    [StringLength(50, ErrorMessage = "رقم الهاتف يجب ألا يتجاوز 50 حرفاً.")]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صحيحة.")]
    [StringLength(200, ErrorMessage = "البريد الإلكتروني يجب ألا يتجاوز 200 حرف.")]
    public string? Email { get; set; }

    public string? Position { get; set; }

    public int? PositionId { get; set; }

    [Required(ErrorMessage = "تاريخ التعيين مطلوب.")]
    public DateOnly HireDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly? BirthDate { get; set; }

    public string? Country { get; set; }

    public string? Nationality { get; set; }

    public string? Gender { get; set; }

    public string? MaritalStatus { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(1, int.MaxValue, ErrorMessage = "اختر موقع العمل.")]
    public int BranchId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "اختر القسم.")]
    public int DepartmentId { get; set; }
}
