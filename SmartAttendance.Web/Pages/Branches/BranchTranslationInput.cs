namespace SmartAttendance.Web.Pages.Branches;

public sealed class BranchTranslationInput
{
    public int CompanyId { get; set; }
    public string CultureCode { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public string Direction { get; set; } = "ltr";
    public bool IsDefault { get; set; }
    public bool IsRequired { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
}
