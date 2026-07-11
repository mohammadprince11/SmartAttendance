using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Setup.Services;
using SmartAttendance.Application.Setup.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Services;

public class SetupService : ISetupService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ApplicationDbContext _dbContext;

    public SetupService(IUnitOfWork unitOfWork, ApplicationDbContext dbContext)
    {
        _unitOfWork = unitOfWork;
        _dbContext = dbContext;
    }

    public async Task<SystemSetupViewModel> GetSetupStatusAsync()
    {
        var companies = (await _unitOfWork.Companies.GetAllAsync()).ToList();
        var branches = (await _unitOfWork.Branches.GetAllAsync()).ToList();
        var departments = (await _unitOfWork.Departments.GetAllAsync()).ToList();
        var employees = (await _unitOfWork.Employees.GetAllAsync()).ToList();
        var devices = (await _unitOfWork.Devices.GetAllAsync()).ToList();
        var shifts = (await _unitOfWork.Shifts.GetAllAsync()).ToList();
        var employeeShifts = (await _unitOfWork.EmployeeShifts.GetAllAsync()).ToList();
        var attendanceRecords = (await _unitOfWork.AttendanceRecords.GetAllAsync()).ToList();
        var holidays = (await _unitOfWork.Holidays.GetAllAsync()).ToList();
        var leaveRequests = (await _unitOfWork.LeaveRequests.GetAllAsync()).ToList();

        var activeEmployees = employees.Where(x => x.IsActive).ToList();

        var employeesWithCurrentShift = employeeShifts
            .Where(x => x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= DateOnly.FromDateTime(DateTime.Today)))
            .Select(x => x.EmployeeId)
            .ToHashSet();

        var model = new SystemSetupViewModel
        {
            CompaniesCount = companies.Count,
            BranchesCount = branches.Count,
            DepartmentsCount = departments.Count,
            EmployeesCount = employees.Count,
            ActiveEmployeesCount = activeEmployees.Count,
            DevicesCount = devices.Count,
            ShiftsCount = shifts.Count,
            EmployeeShiftsCount = employeeShifts.Count,
            EmployeesWithoutCurrentShiftCount = activeEmployees.Count(x => !employeesWithCurrentShift.Contains(x.Id)),
            AttendanceRecordsCount = attendanceRecords.Count,
            HolidaysCount = holidays.Count,
            ApprovedLeavesCount = leaveRequests.Count(x => x.Status == LeaveStatus.Approved),
            Shifts = shifts
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new SetupDropdownItemViewModel
                {
                    Id = x.Id,
                    Text = $"{x.Code} - {x.Name} ({x.StartTime:HH:mm} - {x.EndTime:HH:mm})"
                })
                .ToList()
        };

        model.Steps = BuildSteps(model);

        return model;
    }

    public async Task<SetupActionResultViewModel> BulkAssignShiftAsync(BulkAssignShiftViewModel model)
    {
        var shift = await _unitOfWork.Shifts.GetByIdAsync(model.ShiftId);

        if (shift == null)
            return Failure("Selected shift was not found.");

        var employees = (await _unitOfWork.Employees.GetAllAsync())
            .Where(x => x.IsActive)
            .ToList();

        var employeeShifts = (await _unitOfWork.EmployeeShifts.GetAllAsync()).ToList();

        var currentShiftEmployeeIds = employeeShifts
            .Where(x => x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= model.EffectiveFrom))
            .Select(x => x.EmployeeId)
            .ToHashSet();

        var targetEmployees = model.OnlyEmployeesWithoutCurrentShift
            ? employees.Where(x => !currentShiftEmployeeIds.Contains(x.Id)).ToList()
            : employees;

        var addedCount = 0;
        var weeklyOffDays = NormalizeWeeklyOffDays(model.WeeklyOffDays);

        foreach (var employee in targetEmployees)
        {
            if (!model.OnlyEmployeesWithoutCurrentShift)
            {
                var oldAssignments = employeeShifts
                    .Where(x => x.EmployeeId == employee.Id && x.IsCurrent)
                    .ToList();

                foreach (var assignment in oldAssignments)
                {
                    assignment.IsCurrent = false;
                    assignment.EffectiveTo = model.EffectiveFrom.AddDays(-1);
                    _unitOfWork.EmployeeShifts.Update(assignment);
                }
            }

            var newAssignment = new EmployeeShift
            {
                EmployeeId = employee.Id,
                ShiftId = shift.Id,
                EffectiveFrom = model.EffectiveFrom,
                EffectiveTo = null,
                IsCurrent = true,
                WeeklyOffDays = weeklyOffDays
            };

            await _unitOfWork.EmployeeShifts.AddAsync(newAssignment);
            addedCount++;
        }

        if (addedCount > 0)
            await _unitOfWork.SaveChangesAsync();

        return Success(
            addedCount == 0
                ? "No employees needed shift assignment."
                : $"Shift assigned successfully to {addedCount} employees.",
            addedCount);
    }

    public async Task<CompanySetupViewModel?> GetCompanySetupAsync(int companyId)
    {
        var company = await _dbContext.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == companyId && !x.IsDeleted);

        if (company == null)
            return null;

        var payrollSettings = await _dbContext.CompanyPayrollSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId);

        var cutoffPolicies = await GetPayrollCutoffPoliciesAsync(companyId);

        return new CompanySetupViewModel
        {
            CompanyId = company.Id,
            CompanyCode = company.Code,
            Profile = MapCompanyProfile(company),
            PayrollSettings = payrollSettings == null
                ? new CompanyPayrollSettingsViewModel
                {
                    CompanyId = company.Id,
                    PayrollFrequency = PayrollFrequency.Monthly,
                    PeriodStartDay = 1,
                    PeriodEndDay = 30,
                    IsActive = true
                }
                : MapPayrollSettings(payrollSettings),
            CutoffPolicies = cutoffPolicies.ToList()
        };
    }

    public async Task<SetupActionResultViewModel> UpdateCompanyProfileAsync(CompanySetupProfileViewModel model)
    {
        var validation = ValidateCompanyProfile(model);
        if (validation != null)
            return Failure(validation);

        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(x => x.Id == model.CompanyId && !x.IsDeleted);

        if (company == null)
            return Failure("Company was not found.");

        company.Name = model.Name.Trim();
        company.Email = NormalizeNullable(model.Email);
        company.Phone = NormalizeNullable(model.Phone);
        company.Address = NormalizeNullable(model.Address);
        company.LogoPath = NormalizeNullable(model.LogoPath);
        company.CountryCode = NormalizeUpperNullable(model.CountryCode);
        company.CurrencyCode = NormalizeUpperNullable(model.CurrencyCode);
        company.TimeZoneId = NormalizeNullable(model.TimeZoneId);
        company.IsActive = model.IsActive;
        company.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Success("Company setup profile was updated successfully.", 1);
    }

    public async Task<SetupActionResultViewModel> SavePayrollSettingsAsync(CompanyPayrollSettingsViewModel model)
    {
        var validation = ValidatePayrollSettings(model);
        if (validation != null)
            return Failure(validation);

        var companyExists = await _dbContext.Companies
            .AsNoTracking()
            .AnyAsync(x => x.Id == model.CompanyId && !x.IsDeleted);

        if (!companyExists)
            return Failure("Company was not found.");

        var settings = await _dbContext.CompanyPayrollSettings
            .FirstOrDefaultAsync(x => x.CompanyId == model.CompanyId);

        if (settings == null)
        {
            settings = new CompanyPayrollSetting
            {
                CompanyId = model.CompanyId,
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.CompanyPayrollSettings.AddAsync(settings);
        }

        settings.PayrollFrequency = model.PayrollFrequency;
        settings.PeriodStartDay = model.PeriodStartDay;
        settings.PeriodEndDay = model.PeriodEndDay;
        settings.PaymentDay = model.PaymentDay;
        settings.IsActive = model.IsActive;
        settings.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Success("Company payroll settings were saved successfully.", 1);
    }

    public async Task<IReadOnlyList<PayrollCutoffPolicyViewModel>> GetPayrollCutoffPoliciesAsync(int companyId)
    {
        return await _dbContext.PayrollCutoffPolicies
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.PolicyType)
            .ThenBy(x => x.Priority)
            .ThenByDescending(x => x.EffectiveFrom)
            .ThenBy(x => x.Name)
            .Select(x => new PayrollCutoffPolicyViewModel
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                Name = x.Name,
                PolicyType = x.PolicyType,
                CutoffBasis = x.CutoffBasis,
                DayOfMonth = x.DayOfMonth,
                OffsetDays = x.OffsetDays,
                CutoffTime = x.CutoffTime,
                EffectiveFrom = x.EffectiveFrom,
                EffectiveTo = x.EffectiveTo,
                Priority = x.Priority,
                Notes = x.Notes,
                IsActive = x.IsActive
            })
            .ToListAsync();
    }

    public async Task<PayrollCutoffPolicyViewModel?> GetPayrollCutoffPolicyAsync(int companyId, int policyId)
    {
        return await _dbContext.PayrollCutoffPolicies
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == policyId)
            .Select(x => new PayrollCutoffPolicyViewModel
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                Name = x.Name,
                PolicyType = x.PolicyType,
                CutoffBasis = x.CutoffBasis,
                DayOfMonth = x.DayOfMonth,
                OffsetDays = x.OffsetDays,
                CutoffTime = x.CutoffTime,
                EffectiveFrom = x.EffectiveFrom,
                EffectiveTo = x.EffectiveTo,
                Priority = x.Priority,
                Notes = x.Notes,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync();
    }

    public async Task<SetupActionResultViewModel> SavePayrollCutoffPolicyAsync(PayrollCutoffPolicyViewModel model)
    {
        var validation = ValidateCutoffPolicy(model);
        if (validation != null)
            return Failure(validation);

        var companyExists = await _dbContext.Companies
            .AsNoTracking()
            .AnyAsync(x => x.Id == model.CompanyId && !x.IsDeleted);

        if (!companyExists)
            return Failure("Company was not found.");

        PayrollCutoffPolicy policy;

        if (model.Id > 0)
        {
            var existingPolicy = await _dbContext.PayrollCutoffPolicies
                .FirstOrDefaultAsync(x => x.Id == model.Id && x.CompanyId == model.CompanyId);

            if (existingPolicy == null)
                return Failure("Payroll cutoff policy was not found.");

            policy = existingPolicy;
        }
        else
        {
            policy = new PayrollCutoffPolicy
            {
                CompanyId = model.CompanyId,
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.PayrollCutoffPolicies.AddAsync(policy);
        }

        policy.Name = model.Name.Trim();
        policy.PolicyType = model.PolicyType;
        policy.CutoffBasis = model.CutoffBasis;
        policy.DayOfMonth = model.CutoffBasis == PayrollCutoffBasis.DayOfMonth
            ? model.DayOfMonth
            : null;
        policy.OffsetDays = model.CutoffBasis == PayrollCutoffBasis.DayOfMonth
            ? null
            : model.OffsetDays;
        policy.CutoffTime = model.CutoffTime;
        policy.EffectiveFrom = model.EffectiveFrom;
        policy.EffectiveTo = model.EffectiveTo;
        policy.Priority = model.Priority;
        policy.Notes = NormalizeNullable(model.Notes);
        policy.IsActive = model.IsActive;
        policy.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Success(
            model.Id > 0
                ? "Payroll cutoff policy was updated successfully."
                : "Payroll cutoff policy was created successfully.",
            1);
    }

    public async Task<SetupActionResultViewModel> DeletePayrollCutoffPolicyAsync(int companyId, int policyId)
    {
        var policy = await _dbContext.PayrollCutoffPolicies
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == policyId);

        if (policy == null)
            return Failure("Payroll cutoff policy was not found.");

        policy.IsDeleted = true;
        policy.IsActive = false;
        policy.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Success("Payroll cutoff policy was deleted successfully.", 1);
    }

    private static CompanySetupProfileViewModel MapCompanyProfile(Company company)
    {
        return new CompanySetupProfileViewModel
        {
            CompanyId = company.Id,
            CompanyCode = company.Code,
            Name = company.Name,
            Email = company.Email,
            Phone = company.Phone,
            Address = company.Address,
            LogoPath = company.LogoPath,
            CountryCode = company.CountryCode,
            CurrencyCode = company.CurrencyCode,
            TimeZoneId = company.TimeZoneId,
            IsActive = company.IsActive
        };
    }

    private static CompanyPayrollSettingsViewModel MapPayrollSettings(CompanyPayrollSetting settings)
    {
        return new CompanyPayrollSettingsViewModel
        {
            Id = settings.Id,
            CompanyId = settings.CompanyId,
            PayrollFrequency = settings.PayrollFrequency,
            PeriodStartDay = settings.PeriodStartDay,
            PeriodEndDay = settings.PeriodEndDay,
            PaymentDay = settings.PaymentDay,
            IsActive = settings.IsActive
        };
    }

    private static string? ValidateCompanyProfile(CompanySetupProfileViewModel model)
    {
        if (model.CompanyId <= 0)
            return "A valid company is required.";

        if (string.IsNullOrWhiteSpace(model.Name))
            return "Company name is required.";

        if (model.Name.Trim().Length > 200)
            return "Company name cannot exceed 200 characters.";

        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            if (model.Email.Trim().Length > 200 || !MailAddress.TryCreate(model.Email.Trim(), out _))
                return "Company email is invalid.";
        }

        if (NormalizeNullable(model.Phone)?.Length > 50)
            return "Company phone cannot exceed 50 characters.";

        if (NormalizeNullable(model.Address)?.Length > 500)
            return "Company address cannot exceed 500 characters.";

        if (NormalizeNullable(model.LogoPath)?.Length > 500)
            return "Company logo path cannot exceed 500 characters.";

        var countryCode = NormalizeUpperNullable(model.CountryCode);
        if (countryCode != null && countryCode.Length != 2)
            return "Country code must contain exactly 2 characters.";

        var currencyCode = NormalizeUpperNullable(model.CurrencyCode);
        if (currencyCode != null && currencyCode.Length != 3)
            return "Currency code must contain exactly 3 characters.";

        if (NormalizeNullable(model.TimeZoneId)?.Length > 100)
            return "Time zone identifier cannot exceed 100 characters.";

        return null;
    }

    private static string? ValidatePayrollSettings(CompanyPayrollSettingsViewModel model)
    {
        if (model.CompanyId <= 0)
            return "A valid company is required.";

        if (!Enum.IsDefined(model.PayrollFrequency))
            return "Payroll frequency is invalid.";

        if (model.PeriodStartDay is < 1 or > 31)
            return "Payroll period start day must be between 1 and 31.";

        if (model.PeriodEndDay is < 1 or > 31)
            return "Payroll period end day must be between 1 and 31.";

        if (model.PaymentDay.HasValue && model.PaymentDay.Value is < 1 or > 31)
            return "Payroll payment day must be between 1 and 31.";

        return null;
    }

    private static string? ValidateCutoffPolicy(PayrollCutoffPolicyViewModel model)
    {
        if (model.CompanyId <= 0)
            return "A valid company is required.";

        if (string.IsNullOrWhiteSpace(model.Name))
            return "Policy name is required.";

        if (model.Name.Trim().Length > 150)
            return "Policy name cannot exceed 150 characters.";

        if (!Enum.IsDefined(model.PolicyType))
            return "Payroll cutoff policy type is invalid.";

        if (!Enum.IsDefined(model.CutoffBasis))
            return "Payroll cutoff basis is invalid.";

        if (model.CutoffBasis == PayrollCutoffBasis.DayOfMonth)
        {
            if (!model.DayOfMonth.HasValue || model.DayOfMonth.Value is < 1 or > 31)
                return "Day of month must be between 1 and 31 for this cutoff basis.";
        }
        else
        {
            if (!model.OffsetDays.HasValue || model.OffsetDays.Value is < 0 or > 3660)
                return "Offset days must be between 0 and 3660 for this cutoff basis.";
        }

        if (model.EffectiveTo.HasValue && model.EffectiveTo.Value < model.EffectiveFrom)
            return "Effective-to date cannot be earlier than effective-from date.";

        if (model.Priority is < 0 or > 9999)
            return "Priority must be between 0 and 9999.";

        if (NormalizeNullable(model.Notes)?.Length > 1000)
            return "Policy notes cannot exceed 1000 characters.";

        return null;
    }

    private static SetupActionResultViewModel Success(string message, int affectedCount)
    {
        return new SetupActionResultViewModel
        {
            Success = true,
            AffectedCount = affectedCount,
            Message = message
        };
    }

    private static SetupActionResultViewModel Failure(string message)
    {
        return new SetupActionResultViewModel
        {
            Success = false,
            AffectedCount = 0,
            Message = message
        };
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? NormalizeUpperNullable(string? value)
    {
        var normalized = NormalizeNullable(value);
        return normalized?.ToUpperInvariant();
    }

    private static List<SetupStepViewModel> BuildSteps(SystemSetupViewModel model)
    {
        return new List<SetupStepViewModel>
        {
            new()
            {
                Order = 1,
                Title = "Companies",
                Status = model.IsCompaniesReady ? "Ready" : "Missing",
                Message = model.IsCompaniesReady ? $"{model.CompaniesCount} company records found." : "Import companies first.",
                LinkText = "Open Companies",
                LinkUrl = "/Companies"
            },
            new()
            {
                Order = 2,
                Title = "Branches",
                Status = model.IsBranchesReady ? "Ready" : "Missing",
                Message = model.IsBranchesReady ? $"{model.BranchesCount} branch records found." : "Import branches after companies.",
                LinkText = "Open Branches",
                LinkUrl = "/Branches"
            },
            new()
            {
                Order = 3,
                Title = "Departments",
                Status = model.IsDepartmentsReady ? "Ready" : "Missing",
                Message = model.IsDepartmentsReady ? $"{model.DepartmentsCount} department records found." : "Import departments after branches.",
                LinkText = "Open Departments",
                LinkUrl = "/Departments"
            },
            new()
            {
                Order = 4,
                Title = "Employees",
                Status = model.IsEmployeesReady ? "Ready" : "Missing",
                Message = model.IsEmployeesReady ? $"{model.EmployeesCount} employee records found." : "Import employees using unified EmployeeNo.",
                LinkText = "Open Employees",
                LinkUrl = "/Employees"
            },
            new()
            {
                Order = 5,
                Title = "Shifts",
                Status = model.IsShiftsReady ? "Ready" : "Missing",
                Message = model.IsShiftsReady ? $"{model.ShiftsCount} shift records found." : "Create or import at least one shift.",
                LinkText = "Open Shifts",
                LinkUrl = "/Shifts"
            },
            new()
            {
                Order = 6,
                Title = "Employee Shifts",
                Status = model.IsEmployeeShiftsReady ? "Ready" : "Need Action",
                Message = model.IsEmployeeShiftsReady
                    ? "All active employees have current shift assignment."
                    : $"{model.EmployeesWithoutCurrentShiftCount} active employees do not have a current shift.",
                LinkText = "Open Employee Shifts",
                LinkUrl = "/EmployeeShifts"
            },
            new()
            {
                Order = 7,
                Title = "Devices",
                Status = model.IsDevicesReady ? "Ready" : "Optional",
                Message = model.IsDevicesReady ? $"{model.DevicesCount} device records found." : "Import devices to track branch/device source.",
                LinkText = "Open Devices",
                LinkUrl = "/Devices"
            },
            new()
            {
                Order = 8,
                Title = "Attendance Import",
                Status = model.AttendanceRecordsCount > 0 ? "Ready" : "Waiting",
                Message = model.AttendanceRecordsCount > 0
                    ? $"{model.AttendanceRecordsCount} raw attendance records found."
                    : "Import attendance after employees and shifts are ready.",
                LinkText = "Open Attendance Import",
                LinkUrl = "/AttendanceImports"
            },
            new()
            {
                Order = 9,
                Title = "Processing & Reports",
                Status = model.AttendanceRecordsCount > 0 ? "Ready" : "Waiting",
                Message = model.AttendanceRecordsCount > 0
                    ? "You can process attendance and review reports."
                    : "Reports need attendance records first.",
                LinkText = "Open Reports",
                LinkUrl = "/AttendanceReports/Daily"
            }
        };
    }

    private static string? NormalizeWeeklyOffDays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Join(",",
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeDayName)
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeDayName(string day)
    {
        return day.Trim().ToLowerInvariant() switch
        {
            "sun" or "sunday" or "\u0627\u0644\u0627\u062d\u062f" or "\u0627\u0644\u0623\u062d\u062f" => "Sunday",
            "mon" or "monday" or "\u0627\u0644\u0627\u062b\u0646\u064a\u0646" or "\u0627\u0644\u0625\u062b\u0646\u064a\u0646" => "Monday",
            "tue" or "tuesday" or "\u0627\u0644\u062b\u0644\u0627\u062b\u0627\u0621" => "Tuesday",
            "wed" or "wednesday" or "\u0627\u0644\u0627\u0631\u0628\u0639\u0627\u0621" or "\u0627\u0644\u0623\u0631\u0628\u0639\u0627\u0621" => "Wednesday",
            "thu" or "thursday" or "\u0627\u0644\u062e\u0645\u064a\u0633" => "Thursday",
            "fri" or "friday" or "\u0627\u0644\u062c\u0645\u0639\u0629" => "Friday",
            "sat" or "saturday" or "\u0627\u0644\u0633\u0628\u062a" => "Saturday",
            _ => day.Trim()
        };
    }
}