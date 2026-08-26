using SmartAttendance.Application.AttendanceRecords.ViewModels;
using SmartAttendance.Application.Common.Mapping;
using SmartAttendance.Application.EmployeeShifts.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.LeaveRequests.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Tests;

public sealed class ConventionModelMapperTests
{
    private readonly ConventionModelMapper _mapper = new();

    [Fact]
    public void Map_EmployeeList_PreservesNullableTenantKeyAndRelationshipFields()
    {
        var employee = new Employee
        {
            Id = 17,
            EmployeeNo = "E-017",
            FullName = "محمد علي زيدان",
            CompanyId = 91,
            BranchId = 4,
            DepartmentId = 8,
            HireDate = new DateOnly(2026, 1, 5),
            Department = new Department
            {
                Name = "الموارد البشرية",
                Branch = new Branch { Name = "بغداد" }
            }
        };

        var result = _mapper.Map<EmployeeListViewModel>(employee);

        Assert.Equal(91, result.CompanyId);
        Assert.Equal("E-017", result.EmployeeNo);
        Assert.Equal("محمد علي زيدان", result.FullName);
        Assert.Equal("الموارد البشرية", result.DepartmentName);
        Assert.Equal("بغداد", result.BranchName);
    }

    [Fact]
    public void Map_AttendanceDetails_PreservesEnumsNullablesAndRelatedNames()
    {
        var record = new AttendanceRecord
        {
            Id = 3,
            EmployeeId = 17,
            Employee = new Employee { EmployeeNo = "E-017", FullName = "محمد علي زيدان" },
            AttendanceDate = new DateOnly(2026, 8, 26),
            CheckIn = new DateTime(2026, 8, 26, 8, 0, 0),
            CheckOut = new DateTime(2026, 8, 26, 16, 0, 0),
            Source = AttendanceSource.Device,
            Status = AttendanceStatus.Present,
            DeviceId = 7,
            Device = new Device { Name = "البوابة الرئيسية" }
        };

        var result = _mapper.Map<AttendanceRecordDetailsViewModel>(record);

        Assert.Equal(AttendanceSource.Device, result.Source);
        Assert.Equal(AttendanceStatus.Present, result.Status);
        Assert.Equal(7, result.DeviceId);
        Assert.Equal("E-017", result.EmployeeNo);
        Assert.Equal("محمد علي زيدان", result.EmployeeName);
        Assert.Equal("البوابة الرئيسية", result.DeviceName);
    }

    [Fact]
    public void Map_LeaveList_ComputesInclusiveTotalDays()
    {
        var request = new LeaveRequest
        {
            EmployeeId = 17,
            Employee = new Employee { EmployeeNo = "E-017", FullName = "محمد علي زيدان" },
            LeaveType = LeaveType.Annual,
            Status = LeaveStatus.Approved,
            FromDate = new DateOnly(2026, 8, 26),
            ToDate = new DateOnly(2026, 8, 28)
        };

        var result = _mapper.Map<LeaveRequestListViewModel>(request);

        Assert.Equal(3, result.TotalDays);
        Assert.Equal("E-017", result.EmployeeNo);
        Assert.Equal("محمد علي زيدان", result.EmployeeName);
    }

    [Fact]
    public void Map_EmployeeShiftCollection_MapsEveryItemAndRelationshipFields()
    {
        var assignments = new[]
        {
            new EmployeeShift
            {
                EmployeeId = 17,
                Employee = new Employee { EmployeeNo = "E-017", FullName = "محمد علي زيدان" },
                ShiftId = 2,
                Shift = new Shift { Code = "DAY", Name = "النهاري" },
                EffectiveFrom = new DateOnly(2026, 8, 1),
                EffectiveTo = new DateOnly(2026, 12, 31),
                IsCurrent = true
            }
        };

        var result = _mapper.Map<IEnumerable<EmployeeShiftListViewModel>>(assignments).Single();

        Assert.Equal("E-017", result.EmployeeNo);
        Assert.Equal("محمد علي زيدان", result.EmployeeName);
        Assert.Equal("DAY", result.ShiftCode);
        Assert.Equal("النهاري", result.ShiftName);
        Assert.Equal(new DateOnly(2026, 12, 31), result.EffectiveTo);
    }

    [Fact]
    public void Map_CreateModelToEntity_PreservesOptionalAndRequiredValues()
    {
        var model = new EmployeeCreateViewModel
        {
            EmployeeNo = "E-017",
            FullName = "محمد علي زيدان",
            PositionId = 12,
            JoiningDate = new DateOnly(2026, 1, 2),
            HireDate = new DateOnly(2026, 1, 5),
            BranchId = 4,
            DepartmentId = 8,
            IsActive = true
        };

        var result = _mapper.Map<Employee>(model);

        Assert.Equal("E-017", result.EmployeeNo);
        Assert.Equal(12, result.PositionId);
        Assert.Equal(new DateOnly(2026, 1, 2), result.JoiningDate);
        Assert.Equal(4, result.BranchId);
        Assert.Equal(8, result.DepartmentId);
        Assert.True(result.IsActive);
    }
}
