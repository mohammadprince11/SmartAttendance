namespace SmartAttendance.Application.Holidays.ViewModels;

public class HolidayDetailsViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateOnly HolidayDate { get; set; }

    public bool IsRecurring { get; set; }

    public string? Description { get; set; }
}
