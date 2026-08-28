using System.Collections;
using System.Reflection;

namespace SmartAttendance.Application.Common.Mapping;

public interface IModelMapper
{
    TDestination Map<TDestination>(object source);
}

/// <summary>
/// Small, deterministic convention mapper for the application's entity/view-model boundary.
/// It replaces the licensed runtime mapper and keeps the few relationship-derived fields explicit.
/// </summary>
public sealed class ConventionModelMapper : IModelMapper
{
    public TDestination Map<TDestination>(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var destinationType = typeof(TDestination);
        if (TryGetEnumerableElement(destinationType, out var elementType) && source is IEnumerable items)
        {
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var item in items) list.Add(MapObject(item!, elementType));
            return (TDestination)list;
        }

        return (TDestination)MapObject(source, destinationType);
    }

    private static object MapObject(object source, Type destinationType)
    {
        var destination = Activator.CreateInstance(destinationType)
            ?? throw new InvalidOperationException($"Cannot create {destinationType.FullName}.");
        var sourceProperties = source.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead)
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var target in destinationType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanWrite))
        {
            if (!sourceProperties.TryGetValue(target.Name, out var origin)) continue;
            var value = origin.GetValue(source);
            if (TryConvertValue(value, target.PropertyType, out var converted))
                target.SetValue(destination, converted);
        }

        SetRelationshipFields(source, destination, destinationType);
        return destination;
    }

    private static void SetRelationshipFields(object source, object destination, Type destinationType)
    {
        SetIfPresent(destination, destinationType, "CompanyName", ReadPath(source, "Company", "Name"));
        SetIfPresent(destination, destinationType, "DepartmentName", ReadPath(source, "Department", "Name"));
        SetIfPresent(destination, destinationType, "BranchName",
            ReadPath(source, "Branch", "Name") ?? ReadPath(source, "Department", "Branch", "Name"));
        SetIfPresent(destination, destinationType, "EmployeeNo", ReadPath(source, "Employee", "EmployeeNo"));
        SetIfPresent(destination, destinationType, "EmployeeName", ReadPath(source, "Employee", "FullName"));
        SetIfPresent(destination, destinationType, "DeviceName", ReadPath(source, "Device", "Name"));
        SetIfPresent(destination, destinationType, "ShiftCode", ReadPath(source, "Shift", "Code"));
        SetIfPresent(destination, destinationType, "ShiftName", ReadPath(source, "Shift", "Name"));

        var from = ReadPath(source, "FromDate");
        var to = ReadPath(source, "ToDate");
        if (from is DateOnly fromDate && to is DateOnly toDate)
            SetIfPresent(destination, destinationType, "TotalDays", toDate.DayNumber - fromDate.DayNumber + 1);
    }

    private static object? ReadPath(object source, params string[] path)
    {
        object? value = source;
        foreach (var segment in path)
        {
            if (value is null) return null;
            value = value.GetType().GetProperty(segment, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        }
        return value;
    }

    private static void SetIfPresent(object destination, Type destinationType, string propertyName, object? value)
    {
        if (value is null) return;
        var property = destinationType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite == true && TryConvertValue(value, property.PropertyType, out var converted))
            property.SetValue(destination, converted);
    }

    private static bool TryConvertValue(object? value, Type targetType, out object? converted)
    {
        if (value is null)
        {
            converted = null;
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null;
        }

        if (targetType.IsInstanceOfType(value))
        {
            converted = value;
            return true;
        }

        var effectiveTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveTarget.IsInstanceOfType(value))
        {
            converted = value;
            return true;
        }

        if (effectiveTarget.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(effectiveTarget);
            if (value.GetType().IsEnum)
                value = Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()));
            converted = Enum.ToObject(effectiveTarget, Convert.ChangeType(value, underlying)!);
            return true;
        }

        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(effectiveTarget))
        {
            converted = Convert.ChangeType(value, effectiveTarget);
            return true;
        }

        converted = null;
        return false;
    }

    private static bool TryGetEnumerableElement(Type type, out Type elementType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }
        elementType = null!;
        return false;
    }
}
