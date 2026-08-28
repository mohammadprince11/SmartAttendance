using System.Reflection;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class HrmsDatabaseScalarConversionTests
{
    private static readonly MethodInfo ConvertScalarMethod =
        typeof(HrmsDatabase).GetMethod(
            "ConvertScalar",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("HrmsDatabase.ConvertScalar was not found.");

    [Fact]
    public void ConvertScalar_ConvertsInt32ToNullableInt32()
    {
        var result = Invoke<int?>(2);

        Assert.Equal(2, result);
    }

    [Fact]
    public void ConvertScalar_ConvertsDecimalToNullableDecimal()
    {
        var result = Invoke<decimal?>(123.45m);

        Assert.Equal(123.45m, result);
    }

    [Theory]
    [InlineData(null)]
    public void ConvertScalar_ReturnsDefaultForNull(object? value)
    {
        Assert.Null(Invoke<int?>(value));
    }

    [Fact]
    public void ConvertScalar_ReturnsDefaultForDbNull()
    {
        Assert.Null(Invoke<int?>(DBNull.Value));
    }

    private static T? Invoke<T>(object? value)
    {
        var closedMethod = ConvertScalarMethod.MakeGenericMethod(typeof(T));
        return (T?)closedMethod.Invoke(null, [value]);
    }
}
