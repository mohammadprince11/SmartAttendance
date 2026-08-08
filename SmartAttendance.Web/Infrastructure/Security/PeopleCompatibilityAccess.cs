using SmartAttendance.Application.Common.Security;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class PeopleCompatibilityAccess
{
    // P0-3 — العمليّات الحسّاسة/العالمية التي **لا** تمنحها التوافقية ضمنيّاً لأي
    // دورٍ غير (Admin · HR Manager). كانت التوافقية تمنح HR Officer وBranch Manager
    // كلَّ شيءٍ عدا ManagePermissions — أي حذف/إنهاء خدمة/إعادة تعيين/استيراد/تصدير/
    // تغيير إسناد بلا تخصيصٍ صريح (تصعيد صلاحية). الآن تتطلّب هذه تخصيصاً صريحاً
    // بأدوار الوصول؛ ويبقى للدورين القراءةُ والتعديل الأساسي للتوافق الخلفيّ.
    private static readonly HashSet<string> SensitiveCompatibilityDenied =
        new(StringComparer.OrdinalIgnoreCase)
        {
            PeoplePermissionCodes.Delete,
            PeoplePermissionCodes.AdministrativeDelete,
            PeoplePermissionCodes.EndService,
            PeoplePermissionCodes.Rehire,
            PeoplePermissionCodes.Import,
            PeoplePermissionCodes.Export,
            PeoplePermissionCodes.ChangeAssignment,
            PeoplePermissionCodes.ManagePermissions,
            // التعويض بيانٌ ماليّ حسّاس — لا يُمنَح ضمنيّاً لغير (Admin · HR Manager).
            PeoplePermissionCodes.ViewCompensation,
            PeoplePermissionCodes.EditCompensation,
        };

    public static bool IsAllowed(string? role, string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(role) ||
            string.IsNullOrWhiteSpace(permissionCode) ||
            !PeoplePermissionCodes.All.Contains(permissionCode))
        {
            return false;
        }

        // الأدمن ومدير الموارد البشرية: توافقيةٌ كاملة — سلوكٌ مشروع لا يُكسَر.
        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("HR Manager", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // الموظف الإداري ومدير الفرع: توافقيةٌ للقراءة/التعديل الأساسي فقط —
        // العمليّات الحسّاسة (أعلاه) تتطلّب تخصيصاً صريحاً (P0-3).
        if (role.Equals("HR Officer", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("Branch Manager", StringComparison.OrdinalIgnoreCase))
        {
            return !SensitiveCompatibilityDenied.Contains(permissionCode);
        }

        return false;
    }
}
