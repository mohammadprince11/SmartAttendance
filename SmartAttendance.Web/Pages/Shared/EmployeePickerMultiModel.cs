namespace SmartAttendance.Web.Pages.Shared;

/// <summary>
/// معطيات منتقي الموظفين **المتعدّد** (<c>_EmployeePickerMulti.cshtml</c>).
///
/// نظير <see cref="EmployeePickerModel"/> لكن لحقلٍ يقبل عدّة موظفين
/// (<c>int[]</c>) — كاستهداف الإعلانات والاستطلاعات.
///
/// **لماذا مكوّن ثانٍ لا رايةٌ على الأول؟** المفرد عقده «رمز + اسم» بحقلين
/// ثابتين، والمتعدّد عقده قائمة شرائح تنمو وتنكمش وحقول مخفيّة بعدد المختارين.
/// دمجهما كان يعني مكوّناً يفعل شيئين بنصف إتقان. النافذة نفسها مشتركة بينهما.
/// </summary>
public sealed class EmployeePickerMultiModel
{
    /// <summary>اسم الحقل المُرسَل — يتكرّر مرّةً لكل موظف مختار فيربط بـ<c>int[]</c>.</summary>
    public string Name { get; set; } = "employeeIds";

    /// <summary>عنوان فوق المكوّن؛ فارغ ⟹ بلا عنوان.</summary>
    public string? Label { get; set; } = "الموظفون المحدّدون";

    /// <summary>المختارون ابتداءً — كي لا يبدأ المكوّن فارغاً عند إعادة العرض.</summary>
    public IReadOnlyList<Selection> Selected { get; set; } = Array.Empty<Selection>();

    /// <summary>يشمل البحثُ منتهيَ الخدمة.</summary>
    public bool IncludeInactive { get; set; }

    public sealed record Selection(int Id, string Code, string Name);
}
