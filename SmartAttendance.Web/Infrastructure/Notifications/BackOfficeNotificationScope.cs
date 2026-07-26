namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// نطاق رؤية جرس الـback-office: يحوّل دور المستخدم الحالي (كنص claim) واسمه إلى مجموعة
/// شروط SQL نقيّة تحدّد أيّ صفوف <c>SystemNotifications</c> يراها. مصدر الحقيقة الوحيد
/// لكل من عارض الجرس ومعالِج «تعليم كمقروء» — فلا يقدر مستخدم تعليم صفٍّ خارج نطاقه.
///
/// قاعدة المطابقة: الأدمن يرى كل ما لا يستهدف «Employee» حصراً (مشرف أعلى). أدوار HR
/// (يبدأ اسمها بـ HR) ترى الرمز المختصر «HR» الذي تكتبه التدفّقات + اسم دورها الحرفي.
/// بقية الأدوار ترى اسم دورها الحرفي فقط. الجميع يرى ما وُجّه لاسمه عبر <c>TargetUser</c>.
/// «Employee» خارج النطاق كلياً (إشعاراته تصل بوابته عبر UserNotification).
/// </summary>
public static class BackOfficeNotificationScope
{
    public const string EmployeeRole = "Employee";
    public const string AdminRole = "Admin";
    public const string LegacyHrToken = "HR";

    /// <param name="SeesAllNonEmployee">صحيح للأدمن: يرى كل صفّ لا يستهدف الموظفين حصراً.</param>
    /// <param name="RoleTokens">رموز الأدوار الحرفية التي يطابقها هذا المستخدم على <c>TargetRole</c>.</param>
    /// <param name="Username">اسم المستخدم لمطابقة <c>TargetUser</c> (فارغ = لا مطابقة شخصية).</param>
    public sealed record Scope(
        bool SeesAllNonEmployee,
        IReadOnlyList<string> RoleTokens,
        string Username)
    {
        /// <summary>لا يرى شيئاً إطلاقاً (موظف أو دور فارغ بلا اسم).</summary>
        public bool IsEmpty =>
            !SeesAllNonEmployee &&
            RoleTokens.Count == 0 &&
            string.IsNullOrWhiteSpace(Username);
    }

    /// <summary>نطاق فارغ ثابت — لا يطابق أي صفّ.</summary>
    public static readonly Scope Empty = new(false, Array.Empty<string>(), string.Empty);

    public static Scope Resolve(string? role, string? username)
    {
        var normalizedRole = (role ?? string.Empty).Trim();
        var normalizedUser = (username ?? string.Empty).Trim();

        // الموظف خارج نطاق الـback-office كلياً: إشعاراته قناتها بوابته لا هذا الجرس.
        if (normalizedRole.Equals(EmployeeRole, StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        if (normalizedRole.Equals(AdminRole, StringComparison.OrdinalIgnoreCase))
        {
            return new Scope(true, Array.Empty<string>(), normalizedUser);
        }

        var tokens = new List<string>();
        if (!string.IsNullOrWhiteSpace(normalizedRole))
        {
            tokens.Add(normalizedRole);

            // التدفّقات القديمة (الموافقات/الطلبات المالية/المخالفات) تكتب الرمز المختصر «HR»
            // بينما دور الدخول الفعلي «HR Manager»/«HR Officer» — فنطابق الاثنين.
            if (normalizedRole.StartsWith("HR", StringComparison.OrdinalIgnoreCase) &&
                !normalizedRole.Equals(LegacyHrToken, StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(LegacyHrToken);
            }
        }

        return new Scope(false, tokens, normalizedUser);
    }

    /// <summary>
    /// يبني جسم شرط WHERE (بلا كلمة WHERE) وقائمة معطياته المرتّبة من النطاق — نقيّ وقابل
    /// للاختبار. يعيد <c>1 = 0</c> لنطاقٍ فارغ (لا يرى شيئاً). أسماء المعطيات: <c>@role0..</c>
    /// و<c>@scopeUser</c>. يستهلكه العارض ومعالِج التعليم معاً فيتّحد الأمان.
    /// </summary>
    public static (string Where, IReadOnlyList<(string Name, object Value)> Parameters) BuildFilter(Scope scope)
    {
        var parameters = new List<(string Name, object Value)>();
        var clauses = new List<string>();

        if (scope.SeesAllNonEmployee)
        {
            clauses.Add("ISNULL(TargetRole, N'') <> N'" + EmployeeRole + "'");
        }

        for (var i = 0; i < scope.RoleTokens.Count; i++)
        {
            var name = "@role" + i;
            parameters.Add((name, scope.RoleTokens[i]));
            clauses.Add("TargetRole = " + name);
        }

        if (!string.IsNullOrWhiteSpace(scope.Username))
        {
            parameters.Add(("@scopeUser", scope.Username));
            clauses.Add("TargetUser = @scopeUser");
        }

        if (clauses.Count == 0)
        {
            return ("1 = 0", parameters);
        }

        return (string.Join(" OR ", clauses), parameters);
    }
}
