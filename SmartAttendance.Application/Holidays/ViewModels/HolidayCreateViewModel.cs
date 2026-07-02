namespace SmartAttendance.Application.Holidays.ViewModels;

public class HolidayCreateViewModel
{
    public string Name { get; set; } = string.Empty;

    public DateOnly HolidayDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public bool IsRecurring { get; set; }

    public string? Description { get; set; }
}
