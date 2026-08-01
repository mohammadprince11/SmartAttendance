namespace SmartAttendance.Web.Pages.Shared;

/// <summary>
/// معطيات منتقي الموظف المشترك (<c>_EmployeePicker.cshtml</c>).
///
/// الاستعمال بأي صفحة سطرٌ واحد:
/// <code>
/// &lt;partial name="_EmployeePicker" model="new EmployeePickerModel { Name = "employeeId", SelectedId = Model.EmployeeId }" /&gt;
/// </code>
/// </summary>
public sealed class EmployeePickerModel
{
    /// <summary>اسم الحقل المخفيّ الذي يُرسَل بالنموذج (معرّف الموظف).</summary>
    public string Name { get; set; } = "employeeId";

    /// <summary>عنوان فوق المكوّن؛ فارغ ⟹ بلا عنوان.</summary>
    public string? Label { get; set; } = "الموظف";

    public int SelectedId { get; set; }

    /// <summary>الرمز والاسم المعروضان ابتداءً — تُعبَّأ من الخادم كي لا يبدأ المكوّن فارغاً بعد الحفظ.</summary>
    public string? SelectedCode { get; set; }

    public string? SelectedName { get; set; }

    /// <summary>يُرسِل النموذج فور الاختيار — لشاشات «اختر موظفاً فيُعاد التحميل».</summary>
    public bool SubmitOnSelect { get; set; }

    /// <summary>
    /// يشمل البحثُ منتهيَ الخدمة. الافتراضي <c>false</c> لأن أغلب الشاشات تُسنِد
    /// لموظفٍ عامل. تفعّله شاشات ما بعد الإنهاء (تسوية الإنهاء) وإلا اختفى منها
    /// **موضوعها نفسه** — من أُنهيت خدمته.
    /// </summary>
    public bool IncludeInactive { get; set; }
}
