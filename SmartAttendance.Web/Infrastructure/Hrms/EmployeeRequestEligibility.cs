using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// الحاجز المركزي لتقديم طلبات الخدمة الذاتية. مصدر الإلزام هو إعدادات حقول الموظف
/// نفسها، لذلك لا تنشأ قائمتان مختلفتان لما يعدّه النظام «بيانات أساسية مكتملة».
/// </summary>
public static class EmployeeRequestEligibility
{
    public sealed record Result(bool IsEligible, IReadOnlyList<string> MissingFields, string? Message)
    {
        public static Result MissingEmployee { get; } = new(
            false,
            Array.Empty<string>(),
            "لا يمكن تقديم الطلب لأن حسابك غير مرتبط بملف موظف صالح. راجع الموارد البشرية.");
    }

    public static async Task<Result> CheckAsync(
        ApplicationDbContext dbContext,
        int employeeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (employeeId <= 0)
            return Result.MissingEmployee;

        // المعرّف هنا مأخوذ من مطالبة الجلسة الموقّعة أو ربط اسم المستخدم، وليس من
        // المسار أو النموذج. نحصر الاستعلام بالموظف نفسه ونستبعد المحذوف منطقياً.
        var employee = await dbContext.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == employeeId && !row.IsDeleted,
                cancellationToken);

        if (employee is null)
            return Result.MissingEmployee;

        var settings = await EmployeeFieldControl.GetExistingSettingsAsync(dbContext);
        return Evaluate(employee, settings);
    }

    /// <summary>دالة نقية قابلة للاختبار لاكتشاف كل الحقول الناقصة بترتيب الشاشة.</summary>
    public static Result Evaluate(
        Employee? employee,
        Dictionary<string, EmployeeFieldControl.FieldSetting> settings)
    {
        if (employee is null)
            return Result.MissingEmployee;

        settings ??= new(StringComparer.Ordinal);
        var requiredKeys = EmployeeFieldControl.RequiredKeys(settings);
        var missing = new List<string>();

        foreach (var definition in EmployeeFieldControl.Catalog)
        {
            if (!requiredKeys.Contains(definition.Key))
                continue;

            var property = typeof(Employee).GetProperty(definition.Key);
            if (property is null || !IsMissing(property.GetValue(employee)))
                continue;

            var label = settings.TryGetValue(definition.Key, out var setting)
                && !string.IsNullOrWhiteSpace(setting.CustomLabel)
                    ? setting.CustomLabel.Trim()
                    : definition.Label;
            missing.Add(label);
        }

        if (missing.Count == 0)
            return new Result(true, Array.Empty<string>(), null);

        var fields = string.Join("، ", missing.Select(label => $"«{label}»"));
        return new Result(
            false,
            missing,
            $"لا يمكن تقديم أي طلب قبل إكمال البيانات الأساسية والإلزامية في ملفك: {fields}. راجع الموارد البشرية لتحديثها.");
    }

    private static bool IsMissing(object? value) => value switch
    {
        null => true,
        string text => string.IsNullOrWhiteSpace(text),
        int number => number <= 0,
        long number => number <= 0,
        DateOnly date => date == default,
        DateTime date => date == default,
        Guid guid => guid == Guid.Empty,
        _ => false
    };
}
