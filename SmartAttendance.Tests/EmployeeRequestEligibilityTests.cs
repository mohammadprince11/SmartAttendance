using SmartAttendance.Domain.Entities;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public class EmployeeRequestEligibilityTests
{
    [Fact]
    public void Evaluate_BlocksAndListsEveryMissingLockedField()
    {
        var employee = CompleteEmployee();
        employee.EmployeeNo = " ";
        employee.DepartmentId = 0;
        employee.HireDate = default;

        var result = EmployeeRequestEligibility.Evaluate(employee, EmptySettings());

        Assert.False(result.IsEligible);
        Assert.Contains("كود الموظف", result.MissingFields);
        Assert.Contains("القسم", result.MissingFields);
        Assert.Contains("تاريخ التعيين (العقد)", result.MissingFields);
        Assert.Contains("لا يمكن تقديم أي طلب", result.Message);
    }

    [Fact]
    public void Evaluate_UsesAdditionalRequiredFieldsAndCustomLabel()
    {
        var employee = CompleteEmployee();
        employee.BirthDate = null;
        var settings = EmptySettings();
        settings["BirthDate"] = new EmployeeFieldControl.FieldSetting
        {
            Key = "BirthDate",
            IsRequired = true,
            IsVisible = true,
            CustomLabel = "ميلاد الموظف"
        };

        var result = EmployeeRequestEligibility.Evaluate(employee, settings);

        Assert.False(result.IsEligible);
        Assert.Equal(new[] { "ميلاد الموظف" }, result.MissingFields);
    }

    [Fact]
    public void Evaluate_AllowsCompleteRequiredProfile()
    {
        var result = EmployeeRequestEligibility.Evaluate(CompleteEmployee(), EmptySettings());

        Assert.True(result.IsEligible);
        Assert.Empty(result.MissingFields);
        Assert.Null(result.Message);
    }

    private static Dictionary<string, EmployeeFieldControl.FieldSetting> EmptySettings()
        => new(StringComparer.Ordinal);

    private static Employee CompleteEmployee() => new()
    {
        Id = 41,
        EmployeeNo = "EMP-041",
        FullName = "موظف اختبار",
        FirstName = "موظف",
        LastName = "اختبار",
        BranchId = 3,
        DepartmentId = 7,
        HireDate = new DateOnly(2025, 1, 1),
        IsActive = true
    };
}
