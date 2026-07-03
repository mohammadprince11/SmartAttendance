using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Reports;

public class CreateModel : PageModel
{
    public List<ReportField> EmployeeFields { get; set; } = new();

    public List<ReportField> AttendanceFields { get; set; } = new();

    public void OnGet()
    {
        EmployeeFields = new List<ReportField>
        {
            new("EmployeeCode", "رقم الموظف", "Employee Code", "employees"),
            new("EmployeeName", "اسم الموظف", "Employee Name", "employees"),
            new("Branch", "الفرع", "Branch", "employees"),
            new("Department", "القسم", "Department", "employees"),
            new("Position", "المنصب", "Position", "employees"),
            new("HireDate", "تاريخ المباشرة", "Hire Date", "employees"),
            new("Active", "فعال", "Active", "employees"),
            new("Phone", "الهاتف", "Phone", "employees"),
            new("Email", "البريد الإلكتروني", "Email", "employees"),
            new("NationalId", "الرقم الوطني", "National ID", "employees")
        };

        AttendanceFields = new List<ReportField>
        {
            new("EmployeeCode", "رقم الموظف", "Employee Code", "attendance"),
            new("EmployeeName", "اسم الموظف", "Employee Name", "attendance"),
            new("Date", "التاريخ", "Date", "attendance"),
            new("CheckIn", "دخول", "Check In", "attendance"),
            new("CheckOut", "خروج", "Check Out", "attendance"),
            new("Status", "الحالة", "Status", "attendance"),
            new("Source", "المصدر", "Source", "attendance"),
            new("Device", "الجهاز", "Device", "attendance"),
            new("Branch", "الفرع", "Branch", "attendance"),
            new("Department", "القسم", "Department", "attendance")
        };
    }

    public record ReportField(string Key, string LabelAr, string LabelEn, string Source);
}
