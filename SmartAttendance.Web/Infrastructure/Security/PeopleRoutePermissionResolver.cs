using SmartAttendance.Application.Common.Security;

namespace SmartAttendance.Web.Infrastructure.Security;

public enum PeoplePermissionScopeMode
{
    DataSet = 1,
    Employee = 2,
    Global = 3
}

public sealed record PeopleRoutePermissionRequirement(
    string PermissionCode,
    PeoplePermissionScopeMode ScopeMode);

public static class PeopleRoutePermissionResolver
{
    public static PeopleRoutePermissionRequirement? Resolve(
        HttpContext context,
        string normalizedPath)
    {
        // ═══════════ مسارات تستهدف موظفاً خارج /employees ═══════════
        //
        // كان المحلِّل يعرف مسارات `/employees/*` وحدها، فكل ما عداها يُفحص بمستوى
        // «هل يفتح هذا الدور هذه الشاشة؟» لا «هل هذا الصفّ لك؟» — وهو جذر كل ثغرة
        // رصدها مسح العزل. تسجيل المسار هنا ينقل قراره لمحرك الصلاحيات، فيُفحص
        // الموظف المستهدَف بنطاق البيانات كاملاً (شركة · فرع · قسم · استثناءات).
        //
        // ⚠️ **شرط التسجيل**: أن يكون الموظف المستهدَف **بالطلب نفسه**، فـ
        // `PeopleTargetEmployeeResolver` يقرأه من المسار أو الاستعلام أو النموذج.
        // مسارٌ هدفه مشتقّ بعد قراءة القاعدة (مثل `/documents/view?Id=` حيث المعرّف
        // معرّف **وثيقة**) لا يُسجَّل هنا — المحلِّل لا يستطيع استخراجه، والتسجيل
        // يجعله يفشل دائماً فيحجب شاشةً مشروعة. تلك تُحرَس داخلها.
        //
        // ⚠️ ولا يُسجَّل مسار **سرد** (كثيرون لا واحد): لا هدف يُفحص، والتسجيل
        // يحجبه كلياً. يُرشَّح استعلامه بدل ذلك.
        //
        // التوافقية محفوظة: `GetPeopleDataScopeAsync` يجعل النطاق غير مقيَّد حين
        // يسمح كتالوج الأدوار ولا توجد قواعد صلاحيات صريحة — فمن كان يصل يبقى
        // يصل، والتقييد يقع فقط على من له نطاق مضبوط فعلاً.

        if (normalizedPath.StartsWith("/leavebalances/adjust", StringComparison.Ordinal))
        {
            return Employee(PeoplePermissionCodes.Edit);
        }

        if (normalizedPath.StartsWith("/payroll/terminationsettlement", StringComparison.Ordinal))
        {
            // GET بلا موظف يفتح شاشة الاختيار فقط ولا يقرأ بيانات شخص. فرض نطاق
            // Employee هنا يجعل المحلّل يفشل لعدم وجود هدف، فيُحجب حتى Admin.
            // عند اختيار موظف أو الإرسال يبقى الحارس الصفّي إلزامياً.
            if (HttpMethods.IsGet(context.Request.Method) &&
                (!int.TryParse(context.Request.Query["EmployeeId"], out var targetEmployeeId) ||
                 targetEmployeeId <= 0))
            {
                return null;
            }

            return Employee(PeoplePermissionCodes.EndService);
        }

        if (normalizedPath.StartsWith("/employees" ) is false &&
            normalizedPath.StartsWith("/employeedocuments", StringComparison.Ordinal))
        {
            // الشاشة تسرد وتستقبل رفعاً لموظف محدَّد. حين يُمرَّر موظف يُفحص هدفاً؛
            // وبلا موظف تبقى سرداً مُرشَّحاً بالاستعلام (لا يصل المحلِّل لهدف فيسقط
            // للتوافقية، وهو المطلوب هنا لا حجب الشاشة).
            var target = context.Request.Query["EmployeeId"].ToString();
            if (!string.IsNullOrWhiteSpace(target) && target != "0")
            {
                return Employee(PeoplePermissionCodes.UploadDocument);
            }
        }

        if (normalizedPath == "/employees" ||
            normalizedPath == "/employees/index")
        {
            return DataSet(PeoplePermissionCodes.ViewDirectory);
        }

        if (normalizedPath.StartsWith("/employees/create", StringComparison.Ordinal))
        {
            return Global(PeoplePermissionCodes.Create);
        }

        if (normalizedPath.StartsWith("/employees/edit", StringComparison.Ordinal))
        {
            return Employee(PeoplePermissionCodes.Edit);
        }

        if (normalizedPath.StartsWith("/employees/delete", StringComparison.Ordinal))
        {
            // حذفٌ إداريٌّ لا «حذف» عامّ: صلاحيةٌ مخصّصة لا يمنحها تفويض الإنهاء العاديّ،
            // والصفحة نفسها ترفض التنفيذ إن وُجد أثرٌ تشغيليّ (تُوجّه لإنهاء الخدمة).
            return Employee(PeoplePermissionCodes.AdministrativeDelete);
        }

        if (normalizedPath.StartsWith("/employees/import", StringComparison.Ordinal))
        {
            return Global(PeoplePermissionCodes.Import);
        }

        if (normalizedPath.StartsWith("/employees/endservicelist", StringComparison.Ordinal))
        {
            return DataSet(PeoplePermissionCodes.ViewLifecycle);
        }

        if (normalizedPath.StartsWith("/employees/endservice", StringComparison.Ordinal))
        {
            return Employee(PeoplePermissionCodes.EndService);
        }

        if (normalizedPath.StartsWith("/employees/rehire", StringComparison.Ordinal))
        {
            return Employee(PeoplePermissionCodes.Rehire);
        }

        if (normalizedPath.StartsWith("/employees/lifecycle", StringComparison.Ordinal))
        {
            return Employee(PeoplePermissionCodes.ViewLifecycle);
        }

        if (normalizedPath.StartsWith("/employees/profile", StringComparison.Ordinal))
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                var handler = context.Request.Query["handler"].ToString();

                if (handler.Equals("ReassignFromModal", StringComparison.OrdinalIgnoreCase) ||
                    handler.Equals("ReassignFromModalV2", StringComparison.OrdinalIgnoreCase))
                {
                    return Employee(PeoplePermissionCodes.Rehire);
                }

                if (handler.Equals("UploadProfileAreaFile", StringComparison.OrdinalIgnoreCase))
                {
                    return Employee(PeoplePermissionCodes.UploadDocument);
                }

                // التعويض (الراتب/البدلات) بيانٌ ماليّ حسّاس: تعديلُه يتطلّب صلاحية
                // EditCompensation لا مجرّد People.Edit — فـPOST مُلفَّق لا يتخطّى إخفاء
                // الواجهة (CanViewSalary). راجع تقرير أمان People (P0).
                if (handler.Equals("SaveAllowance", StringComparison.OrdinalIgnoreCase) ||
                    handler.Equals("DeleteAllowance", StringComparison.OrdinalIgnoreCase))
                {
                    return Employee(PeoplePermissionCodes.EditCompensation);
                }

                if (handler.Equals("DeleteProfileAreaFile", StringComparison.OrdinalIgnoreCase))
                {
                    return Employee(PeoplePermissionCodes.DeleteDocument);
                }

                return Employee(PeoplePermissionCodes.Edit);
            }

            return Employee(PeoplePermissionCodes.ViewProfile);
        }

        if (normalizedPath.StartsWith("/employeepermissions", StringComparison.Ordinal))
        {
            return Global(PeoplePermissionCodes.ManagePermissions);
        }


        return null;
    }

    private static PeopleRoutePermissionRequirement DataSet(string permissionCode) =>
        new(permissionCode, PeoplePermissionScopeMode.DataSet);

    private static PeopleRoutePermissionRequirement Employee(string permissionCode) =>
        new(permissionCode, PeoplePermissionScopeMode.Employee);

    private static PeopleRoutePermissionRequirement Global(string permissionCode) =>
        new(permissionCode, PeoplePermissionScopeMode.Global);
}
