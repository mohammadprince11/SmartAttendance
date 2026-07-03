using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Reports;

public class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Area { get; set; }

    public string NormalizedArea => string.Equals(Area, "attendance", StringComparison.OrdinalIgnoreCase)
        ? "attendance"
        : string.Equals(Area, "hr", StringComparison.OrdinalIgnoreCase)
            ? "hr"
            : "all";

    public List<SystemReportCard> SystemReports { get; set; } = new();

    public void OnGet()
    {
        var reports = new List<SystemReportCard>
        {
            new() { Key = "employee-basic", Icon = "👥", TitleAr = "ملف الموظفين", TitleEn = "Employee File", DescriptionAr = "بيانات الموظفين الأساسية مع الفرع والقسم وتاريخ المباشرة.", DescriptionEn = "Basic employee data with branch, department, and hire date.", CategoryAr = "الموظفون", CategoryEn = "Employees", Area = "hr", Source = "employees", Fields = "EmployeeCode,EmployeeName,Branch,Department,HireDate,Active" },
            new() { Key = "employee-contact", Icon = "☎️", TitleAr = "قائمة اتصال الموظفين", TitleEn = "Employee Contact List", DescriptionAr = "أرقام الاتصال والبريد الإلكتروني والبيانات الأساسية.", DescriptionEn = "Phone numbers, emails, and basic employee data.", CategoryAr = "الموظفون", CategoryEn = "Employees", Area = "hr", Source = "employees", Fields = "EmployeeCode,EmployeeName,Phone,Email,Branch,Department,Active" },
            new() { Key = "new-joiners", Icon = "🆕", TitleAr = "المعينون الجدد", TitleEn = "New Joiners", DescriptionAr = "الموظفون الجدد حسب تاريخ المباشرة.", DescriptionEn = "New employees by hire date.", CategoryAr = "الموظفون", CategoryEn = "Employees", Area = "hr", Source = "employees", Fields = "EmployeeCode,EmployeeName,Branch,Department,HireDate,Active" },
            new() { Key = "daily-attendance", Icon = "📅", TitleAr = "الحضور اليومي", TitleEn = "Daily Attendance", DescriptionAr = "تقرير الدخول والخروج والحالة حسب التاريخ.", DescriptionEn = "Daily check-in, check-out, and attendance status.", CategoryAr = "الحضور", CategoryEn = "Attendance", Area = "attendance", Source = "attendance", Fields = "EmployeeCode,EmployeeName,Date,CheckIn,CheckOut,Source,Status,Device" },
            new() { Key = "late-employees", Icon = "⏱️", TitleAr = "تقرير التأخير", TitleEn = "Late Employees", DescriptionAr = "متابعة التأخيرات حسب الفترة والفرع والقسم.", DescriptionEn = "Track late employees by date range, branch, and department.", CategoryAr = "الحضور", CategoryEn = "Attendance", Area = "attendance", Source = "attendance", Fields = "EmployeeCode,EmployeeName,Date,CheckIn,CheckOut,Status,Branch,Department" },
            new() { Key = "absent-employees", Icon = "🚫", TitleAr = "تقرير الغياب", TitleEn = "Absent Employees", DescriptionAr = "مراجعة الموظفين غير الحاضرين حسب الفترة.", DescriptionEn = "Review absent employees by selected period.", CategoryAr = "الحضور", CategoryEn = "Attendance", Area = "attendance", Source = "attendance", Fields = "EmployeeCode,EmployeeName,Date,Status,Branch,Department" },
            new() { Key = "missing-punch", Icon = "⚠️", TitleAr = "البصمات الناقصة", TitleEn = "Missing Punch", DescriptionAr = "متابعة سجلات الدخول أو الخروج غير المكتملة.", DescriptionEn = "Track incomplete check-in or check-out records.", CategoryAr = "الحضور", CategoryEn = "Attendance", Area = "attendance", Source = "attendance", Fields = "EmployeeCode,EmployeeName,Date,CheckIn,CheckOut,Status,Source" },
            new() { Key = "payroll-attendance", Icon = "💰", TitleAr = "ملخص حضور الرواتب", TitleEn = "Payroll Attendance Summary", DescriptionAr = "ملخص الحضور والمعالجة لاستخدامات الرواتب.", DescriptionEn = "Attendance summary prepared for payroll review.", CategoryAr = "الرواتب", CategoryEn = "Payroll", Area = "attendance", Source = "attendance", Fields = "EmployeeCode,EmployeeName,Date,CheckIn,CheckOut,Status,Branch,Department" },
            new() { Key = "work-weekly-off", Icon = "🏖️", TitleAr = "العمل في يوم الأوف", TitleEn = "Work On Weekly Off", DescriptionAr = "تقرير العمل في أيام الأوف حسب الموظف والفترة.", DescriptionEn = "Work on weekly off days by employee and period.", CategoryAr = "الحضور", CategoryEn = "Attendance", Area = "attendance", Source = "attendance", Fields = "EmployeeCode,EmployeeName,Date,CheckIn,CheckOut,Status,Branch,Department" }
        };

        SystemReports = NormalizedArea == "all" ? reports : reports.Where(x => x.Area == NormalizedArea).ToList();
    }

    public class SystemReportCard
    {
        public string Key { get; set; } = "";
        public string Icon { get; set; } = "";
        public string TitleAr { get; set; } = "";
        public string TitleEn { get; set; } = "";
        public string DescriptionAr { get; set; } = "";
        public string DescriptionEn { get; set; } = "";
        public string CategoryAr { get; set; } = "";
        public string CategoryEn { get; set; } = "";
        public string Area { get; set; } = "";
        public string Source { get; set; } = "";
        public string Fields { get; set; } = "";
    }
}
