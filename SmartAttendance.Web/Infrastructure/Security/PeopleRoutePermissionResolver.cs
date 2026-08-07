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
            return Employee(PeoplePermissionCodes.Delete);
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
