using SmartAttendance.Application.Common.Security;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// الشركات التي يملك المستخدم الحالي حقّ لمسها — قرارٌ مادّيّ يُستهلَك من طبقة
/// الـSQL الخام مباشرةً.
///
/// <para><b>لماذا نوعٌ منفصل عن <see cref="PeopleDataScope"/>؟</b> ذاك يجيب «هل
/// يُسمح بهذا <i>الموظف</i>؟» — سؤالٌ يفترض أن بيدك موظفاً بالفعل. وكثيرٌ من
/// الكيانات (دفعة مسير · مستند شركة · تقرير) ليست موظفاً، ويجب حسم شركتها
/// <b>قبل</b> قراءة صفٍّ واحد. فهذا النوع يستخرج البُعد الشركويّ وحده ويقدّمه
/// بصيغة تصلح شرطاً بالـSQL.</para>
///
/// <para><b>مغلق الفشل بالتصميم:</b> لا مُنشئ عامّ يعطي «مسموح بكل شيء» صدفةً —
/// غير المقيَّد يُطلب صراحةً بـ<see cref="Unrestricted"/>، وأيّ إخفاق باشتقاق
/// الهوية ينتهي بـ<see cref="DeniedAll"/>.</para>
/// </summary>
public sealed class CompanyScope
{
    private readonly HashSet<int> _allowed;

    private CompanyScope(bool isUnrestricted, IEnumerable<int> allowed)
    {
        IsUnrestricted = isUnrestricted;
        _allowed = allowed.Where(id => id > 0).ToHashSet();
    }

    /// <summary>الأدمن ومن لا قيد عليه — يرى كل الشركات.</summary>
    public static CompanyScope Unrestricted() => new(true, Array.Empty<int>());

    /// <summary>لا شركة إطلاقاً. الحالة الافتراضية عند أي شكّ.</summary>
    public static CompanyScope DeniedAll() => new(false, Array.Empty<int>());

    public static CompanyScope ForCompanies(IEnumerable<int> companyIds) =>
        new(false, companyIds);

    public bool IsUnrestricted { get; }

    /// <summary>الشركات المسموحة صراحةً — فارغة حين <see cref="IsUnrestricted"/>.</summary>
    public IReadOnlyCollection<int> AllowedCompanyIds => _allowed;

    /// <summary>لا شيء مسموح ⟹ كل قراءة تُرفض بلا استعلام.</summary>
    public bool IsDeniedAll => !IsUnrestricted && _allowed.Count == 0;

    /// <summary>
    /// هل يُسمح بكيانٍ شركتُه <paramref name="companyId"/>؟
    ///
    /// <para><b>‏<c>null</c> يعني «لا نعرف لأيّ شركةٍ هذا الصفّ» ⟹ يُسمح لغير
    /// المقيَّد فقط.</b> هذه الحالة حقيقية لا نظرية: الصفوف التي سبقت عمود الشركة
    /// تبقى بلا نسبة. رفضها للجميع يُخفي بيانات تاريخية عن مالكها الشرعيّ، وقبولها
    /// للجميع يفتح ثغرة — والوسط الصحيح أن يراها الأدمن وحده حتى تُنسَب.</para>
    /// </summary>
    public bool Allows(int? companyId)
    {
        if (IsUnrestricted) return true;
        if (companyId is not { } id || id <= 0) return false;
        return _allowed.Contains(id);
    }

    /// <summary>
    /// شرط SQL جاهز للحقن على عمود شركة، مع أسماء وسائطه.
    ///
    /// <para>يُعاد نصّاً لا وسيطاً مُجمَّعاً لأن الطبقة كلها SQL خام؛ والقيم تُبنى
    /// من أعداد صحيحة تحقّقنا منها بالمُنشئ (<c>id &gt; 0</c>) لا من مدخل مستخدم،
    /// فلا سطح حقن. غير المقيَّد يعيد <c>1=1</c> فلا يتغيّر أي استعلام قائم.</para>
    /// </summary>
    public string ToSqlPredicate(string columnExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnExpression);

        if (IsUnrestricted) return "1 = 1";
        if (IsDeniedAll) return "1 = 0";

        return $"{columnExpression} IN ({string.Join(", ", _allowed.OrderBy(id => id))})";
    }

    /// <summary>
    /// نظير <see cref="ToSqlPredicate"/> لجداول <b>التهيئة</b> (أنواع المناوبات ·
    /// القواعد · الاستطلاعات) حيث <c>CompanyId IS NULL</c> تعني «مشتركة/عالمية».
    ///
    /// <para><b>لماذا يلزم شرطٌ ثانٍ:</b> هذه الجداول سبقت وجود عمود الشركة، فصفوفها
    /// القديمة كلها <c>NULL</c>. لو طبّقنا <c>ToSqlPredicate</c> عليها لاختفت التهيئة
    /// القائمة عن **كل** دورٍ مقيَّد دفعةً واحدة — تعطيلٌ حيّ لمناوبات وقواعد مستعملة.
    /// فالمشترك يبقى مرئياً (السلوك القديم)، والصفوف الجديدة المنسوبة لشركة تُعزَل.</para>
    /// </summary>
    public string ToSharedConfigSqlPredicate(string columnExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnExpression);

        if (IsUnrestricted) return "1 = 1";
        // المحروم من كل الشركات يرى المشترك فقط — لا صفّاً منسوباً لأي شركة.
        if (IsDeniedAll) return $"{columnExpression} IS NULL";

        return $"({columnExpression} IS NULL OR {columnExpression} IN ({string.Join(", ", _allowed.OrderBy(id => id))}))";
    }
}

/// <summary>يستخرج نطاق شركات الطلب الحالي من محرك الصلاحيات القائم.</summary>
public interface ICompanyScopeProvider
{
    Task<CompanyScope> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// يبني <see cref="CompanyScope"/> من <see cref="IEffectiveScopeService"/> —
/// نفس المحرك الذي يحسم صلاحيات الموظفين، فلا يُخترع مصدر حقيقة ثانٍ يتباعد عنه.
/// </summary>
public sealed class CompanyScopeProvider : ICompanyScopeProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IEffectiveScopeService _effectiveScopeService;

    private CompanyScope? _cached;

    public CompanyScopeProvider(
        IHttpContextAccessor httpContextAccessor,
        IEffectiveScopeService effectiveScopeService)
    {
        _httpContextAccessor = httpContextAccessor;
        _effectiveScopeService = effectiveScopeService;
    }

    public async Task<CompanyScope> GetAsync(CancellationToken cancellationToken = default)
    {
        // الخدمة مسجَّلة Scoped فالكاش بعمر الطلب — والنطاق لا يتغيّر داخل الطلب.
        if (_cached is not null) return _cached;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null) return _cached = CompanyScope.DeniedAll();

        var role = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var isAdmin = RoleRouteCatalog.IsAdmin(role);
        var systemUserId = PeopleAccessContext.GetSystemUserId(httpContext) ?? 0;

        // الأدمن يمرّ قبل اشتقاق هوية النظام: حسابه قد يسبق مزامنتها، وحجبها عنه
        // يقفل النظام على نفسه.
        if (isAdmin) return _cached = CompanyScope.Unrestricted();

        if (systemUserId <= 0) return _cached = CompanyScope.DeniedAll();

        var scope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId, isAdmin: false, cancellationToken);

        if (scope.IsDeniedAll) return _cached = CompanyScope.DeniedAll();
        if (scope.IsUnrestricted) return _cached = CompanyScope.Unrestricted();

        // المرفوض يغلب المسموح — نفس ترتيب `PeopleDataScope.AllowsEmployee`.
        var denied = scope.DeniedCompanyIds.ToHashSet();
        var allowed = scope.AllowedCompanyIds.Where(id => !denied.Contains(id)).ToList();

        return _cached = allowed.Count == 0
            ? CompanyScope.DeniedAll()
            : CompanyScope.ForCompanies(allowed);
    }
}
