using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Violations;

public class IndexModel : PageModel
{
    public List<ViolationCase> Items { get; private set; } = new();

    public int TotalCount => Items.Count;

    public void OnGet()
    {
        Items = BuildDemoItems();
    }

    private static List<ViolationCase> BuildDemoItems()
    {
        return new List<ViolationCase>
        {
            new()
            {
                ReferenceNo = "VC26-766",
                EmployeeCode = "12722",
                EmployeeName = "روان عادل نشمي",
                Position = "كاشير",
                Branch = "AL-Bayaa",
                Department = "CCO",
                ViolationCategory = "مخالفات نظام العمل",
                ViolationTitle = "عدم تنفيذ تعليمات نظام العمل وسياسات الشركة",
                EventDate = new DateTime(2026, 7, 6),
                Source = "مباشر",
                ActionStatus = "بانتظار الإجراء",
                Status = "موافق عليه"
            },
            new()
            {
                ReferenceNo = "VC26-767",
                EmployeeCode = "10663",
                EmployeeName = "سجاد عقيل خلف",
                Position = "مشرف الصالة",
                Branch = "Rusafa",
                Department = "Fresh Food",
                ViolationCategory = "مخالفات الحضور والانصراف",
                ViolationTitle = "عدم تواجد الموظف في مكان عمله بدون إذن",
                EventDate = new DateTime(2026, 7, 5),
                Source = "مباشر",
                ActionStatus = "اتخذ الإجراء",
                Status = "تم اتخاذ الإجراء"
            },
            new()
            {
                ReferenceNo = "VC26-758",
                EmployeeCode = "14014",
                EmployeeName = "شيماء محمد شهاب",
                Position = "موظفة",
                Branch = "HQ",
                Department = "Finance",
                ViolationCategory = "مخالفات نظام العمل",
                ViolationTitle = "التقصير في المهام والمسؤوليات الوظيفية",
                EventDate = new DateTime(2026, 7, 4),
                Source = "HR",
                ActionStatus = "بانتظار الإجراء",
                Status = "بانتظار الاعتماد"
            },
            new()
            {
                ReferenceNo = "VC26-759",
                EmployeeCode = "14129",
                EmployeeName = "دنيا طالب هاشم",
                Position = "كاشير",
                Branch = "Jamila",
                Department = "Risk and Compliance",
                ViolationCategory = "سياسات عامة",
                ViolationTitle = "سلوك غير مهني داخل موقع العمل",
                EventDate = new DateTime(2026, 7, 4),
                Source = "النظام",
                ActionStatus = "بانتظار الإجراء",
                Status = "مسودة"
            },
            new()
            {
                ReferenceNo = "VC26-757",
                EmployeeCode = "12019",
                EmployeeName = "علي حسين نعمة",
                Position = "بائع",
                Branch = "AL-Dabbash",
                Department = "FMCG",
                ViolationCategory = "مخالفات الحضور والانصراف",
                ViolationTitle = "عدم تواجد الموظف في مكان عمله بدون إذن",
                EventDate = new DateTime(2026, 7, 3),
                Source = "فرع",
                ActionStatus = "اتخذ الإجراء",
                Status = "موافق عليه"
            },
            new()
            {
                ReferenceNo = "VC26-745",
                EmployeeCode = "12253",
                EmployeeName = "يوسف شاكر كريم",
                Position = "مساعد كاشير",
                Branch = "HQ",
                Department = "Finance",
                ViolationCategory = "مخالفات نظام العمل",
                ViolationTitle = "التحدث بأسلوب غير لائق مع المدير المباشر أو الزملاء",
                EventDate = new DateTime(2026, 7, 2),
                Source = "مباشر",
                ActionStatus = "بانتظار الإجراء",
                Status = "مرفوض"
            }
        };
    }
}

public sealed class ViolationCase
{
    public string ReferenceNo { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string ViolationCategory { get; set; } = string.Empty;
    public string ViolationTitle { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ActionStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public string StatusCss => Status switch
    {
        "موافق عليه" => "ok",
        "تم اتخاذ الإجراء" => "done",
        "بانتظار الاعتماد" => "pending",
        "مرفوض" => "rejected",
        _ => "draft"
    };
}
