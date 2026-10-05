using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Web.Pages.EmployeeUpdates;

public partial class IndexModel
{
    public static IReadOnlyList<UpdateField> ProfileFieldDefinitions() => BuildSections().SelectMany(s => s.Fields).ToList();
    public static bool IsFinancialField(UpdateField field) => field.Target is "compensation" or "financial-custom";
    public static string OptionLabel(string value) => value switch
    {
        "Male" => "ذكر", "Female" => "أنثى", "Single" => "أعزب", "Married" => "متزوج",
        "Divorced" => "مطلق", "Widowed" => "أرمل", "Separated" => "منفصل", _ => value
    };
    public bool CanViewFinancial { get; private set; }
    public List<EmployeeLookupOption> ProfileBranches { get; private set; } = new();
    public List<EmployeeLookupOption> ProfilePositions { get; private set; } = new();
    private List<DepartmentOption> ProfileDepartments { get; set; } = new();

    public static bool ValidFieldValue(UpdateField field, string value)
    {
        if (field.ReadOnly || (field.IsRequired && string.IsNullOrWhiteSpace(value))) return false;
        if (field.InputType == "select" && value.Length > 0 &&
            !field.Options.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(value)) return false;
        var property = ProfileProperty(field);
        if (property != null)
        {
            if ((Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType) == typeof(int) &&
                field.Key is "BranchId" or "DepartmentId" or "PositionId" or "DirectManagerId" &&
                value.Length > 0 && (!int.TryParse(value, out var id) || id <= 0)) return false;
            return TryProfileValue(property.PropertyType, value, out _);
        }
        if (field.Target is not ("custom" or "financial-custom")) return false;
        return field.InputType switch
        {
            "checkbox" => value is "true" or "false" or "",
            "date" => value.Length == 0 || TryProfileValue(typeof(DateOnly), value, out _),
            "number" => value.Length == 0 || TryProfileValue(typeof(decimal), value, out _),
            _ => value.Length <= 4000
        };
    }

    private bool ValidStoredFieldValue(UpdateField field, string value)
    {
        if (!ValidFieldValue(field, value)) return false;
        var property = ProfileProperty(field);
        if (property?.PropertyType != typeof(string)) return true;
        var limit = _dbContext.Model.FindEntityType(property.DeclaringType!)?.FindProperty(property.Name)?.GetMaxLength();
        return !limit.HasValue || value.Length <= limit.Value;
    }

    private async Task LoadProfileAssignmentOptionsAsync(int employeeId)
    {
        var location = (await LoadEmployeeLocationAsync(employeeId))!.Value;
        ProfileBranches = await _dbContext.Branches.AsNoTracking()
            .Where(b => b.CompanyId == location.CompanyId && !b.IsDeleted && b.IsActive)
            .OrderBy(b => b.Name).Select(b => new EmployeeLookupOption { Id = b.Id, Name = b.Name }).ToListAsync();
        ProfileDepartments = await _dbContext.Departments.AsNoTracking()
            .Where(d => d.CompanyId == location.CompanyId && !d.IsDeleted && d.IsActive)
            .OrderBy(d => d.Name).Select(d => new DepartmentOption { Id = d.Id, Name = d.Name }).ToListAsync();
        ProfilePositions = await _dbContext.HrJobPositions.AsNoTracking()
            .Where(p => p.CompanyId == location.CompanyId && p.IsActive)
            .OrderBy(p => p.ArabicName).Select(p => new EmployeeLookupOption { Id = p.Id, Name = p.ArabicName }).ToListAsync();
    }

    private async Task<bool> ValidProfileAssignmentAsync(int employeeId, List<UpdateChange> changes)
    {
        var employee = await _dbContext.Employees.AsNoTracking().SingleAsync(e => e.Id == employeeId);
        var location = (await LoadEmployeeLocationAsync(employeeId))!.Value;
        var values = changes.ToDictionary(c => c.FieldKey, c => c.NewValue, StringComparer.OrdinalIgnoreCase);
        int? Id(string key, int? current) => values.TryGetValue(key, out var value)
            ? (int.TryParse(value, out var id) && id > 0 ? id : null) : current;
        var branchId = Id("BranchId", employee.BranchId);
        var departmentId = Id("DepartmentId", employee.DepartmentId);
        var positionId = Id("PositionId", employee.PositionId);
        var managerId = Id("DirectManagerId", employee.DirectManagerId);
        if (changes.Any(c => AssignmentFieldKeys.Contains(c.FieldKey)))
        {
            if (branchId == null || departmentId == null ||
                !await _dbContext.Branches.AnyAsync(b => b.Id == branchId && b.CompanyId == location.CompanyId && b.IsActive && !b.IsDeleted) ||
                !await _dbContext.Departments.AnyAsync(d => d.Id == departmentId && d.CompanyId == location.CompanyId && d.IsActive && !d.IsDeleted && (d.BranchId == null || d.BranchId == branchId)) ||
                (positionId != null && !await _dbContext.HrJobPositions.AnyAsync(p => p.Id == positionId && p.CompanyId == location.CompanyId && p.IsActive && (p.DepartmentId == null || p.DepartmentId == departmentId))) ||
                managerId == employeeId || (managerId != null && !await _dbContext.Employees.AnyAsync(e => e.Id == managerId && e.Branch.CompanyId == location.CompanyId && e.IsActive && !e.IsDeleted))) return false;
            if (_actor == null || !_actor.DirectoryScope.AllowsEmployee(employeeId, location.CompanyId, branchId.Value, departmentId.Value) ||
                !_actor.AccessRoleScope.AllowsEmployee(employeeId, location.CompanyId, branchId.Value, departmentId.Value)) return false;
        }
        if (values.TryGetValue("EmployeeNo", out var code) && (string.IsNullOrWhiteSpace(code) ||
            await _dbContext.Employees.AnyAsync(e => e.Id != employeeId && e.EmployeeNo == code))) return false;
        return true;
    }
    // Explicit allow-list: never bind query/form keys to arbitrary entity properties.
    public static readonly string[] AdditionalEmployeeKeys =
    ["JoiningDate", "Gender", "MaritalStatus", "Country", "IsCitizen", "PassportNo",
     "SponsorName", "Religion", "MotherCountry", "MotherCity", "PersonalEmail",
     "PhoneExtension", "WorkType", "JobGrade", "ContractEndDate"];

    private static List<UpdateSection> UseProfileFields(List<UpdateSection> sections)
    {
        var employee = sections.Single(s => s.Key == "employee-info").Fields;
        Replace(employee, "FullName", f => f with { ReadOnly = true }); // translated/structured name has its own editor
        Replace(employee, "Position", f => f with { Key = "PositionId", InputType = "select-position-id" });
        Replace(employee, "DepartmentId", f => f with { Label = "القسم" });
        Replace(employee, "HireDate", f => f with { Label = "تاريخ التعيين (العقد)", IsRequired = true });
        foreach (var (key, label) in new (string, string)[] {
            ("JoiningDate", "تاريخ المباشرة الفعلية"), ("Gender", "الجنس"),
            ("MaritalStatus", "الحالة الاجتماعية"), ("Country", "البلد"), ("IsCitizen", "مواطن"),
            ("PassportNo", "رقم جواز السفر"), ("SponsorName", "الكفيل"), ("Religion", "الديانة"),
            ("MotherCountry", "البلد الأم"), ("MotherCity", "المدينة الأم"),
            ("PersonalEmail", "البريد الشخصي"), ("PhoneExtension", "امتداد الهاتف"),
            ("WorkType", "نوع الدوام"), ("JobGrade", "الدرجة الوظيفية"), ("ContractEndDate", "نهاية العقد") })
        {
            var type = key.EndsWith("Date") ? "date" : key == "IsCitizen" ? "checkbox" : "text";
            var options = key == "Gender" ? "Male\nFemale" : key == "MaritalStatus" ? "Single\nMarried\nDivorced\nWidowed\nSeparated" : "";
            employee.Add(new(key, label, "employee", options.Length > 0 ? "select" : type, "", options));
        }
        employee.Add(new("BranchId", "موقع العمل", "employee", "select-branch", "", IsRequired: true));
        Replace(employee, "Country", f => f with { InputType = "select", Options = string.Join('\n',
            SmartAttendance.Web.Infrastructure.Ui.ZynoraEmployeeLookups.PrimaryCountries.Select(o => o.Value)) });
        Replace(sections.Single(s => s.Key == "extra").Fields, "ContractType", f => f with { Target = "employee" });

        // Multi-record payroll/attendance operations must not masquerade as scalar profile edits.
        foreach (var fieldList in sections.Select(s => s.Fields))
            for (var i = 0; i < fieldList.Count; i++)
                if (fieldList[i].Key is "Allowances" or "Deductions" or "BankAccount" or "ShiftName" or "AttendanceRule" or "GraceMinutes" or "WorkHours")
                    fieldList[i] = fieldList[i] with { ReadOnly = true };

        var financial = sections.Single(s => s.Key == "financial").Fields;
        foreach (var (key, label) in new (string, string)[] {
            ("SalaryScale", "سلم الرواتب"), ("DailySalary", "الراتب اليومي"), ("HourlyRate", "سعر ساعة العمل"),
            ("GosiProfileId", "ملف الضمان"), ("SocialSecurityType", "نوع الضمان"),
            ("SocialSecuritySalary", "راتب الضمان"), ("GosiBaseMode", "مصدر وعاء الضمان"),
            ("SocialSecurityNo", "رقم الضمان"), ("SocialSecurityJoinDate", "تاريخ الانضمام للضمان"),
            ("SocialSecurityPreviousMonths", "اشتراكات سابقة (أشهر)"), ("RetirementAge", "سن التقاعد"),
            ("TaxProfileId", "ملف الضريبة"), ("TaxFile", "وصف ملف الضريبة"), ("TaxNo", "الرقم الضريبي"),
            ("TaxYear", "السنة الضريبية"), ("CurrentTaxSalary", "راتب الضريبة الراهن"),
            ("TaxBaseMode", "مصدر وعاء الضريبة"), ("PreviousTaxSalary", "راتب الضريبة السابق"),
            ("PreviousTaxExemption", "إعفاء الضريبة السابق"), ("PreviousTaxAmount", "مبلغ الضريبة السابق"),
            ("PreviousMinSalary", "راتب الحد الأدنى السابق"), ("PreviousMinTaxAmount", "ضريبة الحد الأدنى السابق"),
            ("PreviousTaxMonths", "أشهر الضرائب السابقة"), ("EndOfServiceSetup", "إعداد مكافأة نهاية الخدمة"),
            ("EndOfServiceStartDate", "تاريخ بدء المكافأة"), ("EndOfServiceDueDate", "تاريخ استحقاق المكافأة"),
            ("AdditionalSalaryStartDate", "تاريخ بدء الراتب الإضافي"), ("EndOfServiceCompute", "احتساب مكافأة نهاية الخدمة"),
            ("CalcPreviousSalaries", "احتساب الرواتب السابقة"), ("StopSalaryCalc", "إيقاف احتساب الراتب") })
        {
            var property = typeof(EmployeeFinancialInfo).GetProperty(key)!;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            financial.Add(new(key, label, "compensation", InputTypeFor(type), "",
                ReadOnly: key is "GosiProfileId" or "TaxProfileId" or "GosiBaseMode" or "TaxBaseMode"));
        }
        var payment = sections.Single(s => s.Key == "payment").Fields;
        Replace(payment, "Currency", f => f with { InputType = "select", Options = string.Join('\n', SmartAttendance.Web.Pages.Employees.FinancialInfoModel.Currencies) });
        Replace(payment, "PaymentMethod", f => f with { InputType = "select", Options = string.Join('\n', SmartAttendance.Web.Pages.Employees.FinancialInfoModel.PaymentMethods) });
        foreach (var (key, label) in new (string, string)[] {
            ("Iban", "IBAN"), ("BankBranch", "فرع البنك"), ("UnitNo", "رقم الوحدة"),
            ("CardNo", "رقم البطاقة"), ("MxpAccount", "حساب Mxp"), ("BankCommitment", "التزام بنكي") })
            payment.Add(new(key, label, "compensation", key == "BankCommitment" ? "checkbox" : "text", ""));
        return sections;
    }

    private static void Replace(List<UpdateField> fields, string key, Func<UpdateField, UpdateField> replace)
    {
        var index = fields.FindIndex(f => f.Key == key);
        fields[index] = replace(fields[index]);
    }

    private static string InputTypeFor(Type type) => type == typeof(bool) ? "checkbox" :
        type == typeof(DateOnly) ? "date" : type == typeof(decimal) || type == typeof(int) ? "number" : "text";

    public static string ProfileValue(object? value) => value switch
    {
        null => "", DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        decimal amount => amount.ToString("0.####", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    public static bool TryProfileValue(Type propertyType, string value, out object? result)
    {
        result = null;
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (type == typeof(string)) { result = value; return true; }
        if (string.IsNullOrEmpty(value)) return Nullable.GetUnderlyingType(propertyType) != null;
        if (type == typeof(bool) && bool.TryParse(value, out var flag)) { result = flag; return true; }
        if (type == typeof(DateOnly) && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) { result = date; return true; }
        if (type == typeof(int) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 0) { result = number; return true; }
        if (type == typeof(decimal) && decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount) && amount >= 0) { result = amount; return true; }
        return false;
    }

    public static PropertyInfo? ProfileProperty(UpdateField field) => field.ReadOnly ? null :
        field.Target == "employee" ? typeof(Employee).GetProperty(field.Key) :
        field.Target == "compensation" ? typeof(EmployeeFinancialInfo).GetProperty(field.Key) : null;

    private async Task<List<UpdateSection>> WithProfileLookupsAsync(List<UpdateSection> sections)
    {
        foreach (var (key, lookup) in new[] { ("WorkType", "worktypes"), ("JobGrade", "grades"),
                     ("Religion", "religions"), ("SponsorName", "sponsors"), ("ContractType", "contracttypes") })
        {
            var options = await SmartAttendance.Web.Infrastructure.Hrms.HrLookups.ValuesAsync(_dbContext, lookup);
            foreach (var section in sections)
            {
                var index = section.Fields.FindIndex(f => f.Key == key);
                if (index >= 0) section.Fields[index] = section.Fields[index] with { InputType = "select", Options = string.Join('\n', options) };
            }
        }
        return sections;
    }

    private async Task ReadProfileValuesAsync(int employeeId, Dictionary<string, string> values)
    {
        var employee = await _dbContext.Employees.AsNoTracking().SingleAsync(e => e.Id == employeeId);
        var actor = _actor ?? throw new InvalidOperationException("Employee access must be authorized first.");
        CanViewFinancial = await _permissions.CanAccessEmployeeAsync(actor.SystemUserId,
            SmartAttendance.Application.Common.Security.PeoplePermissionCodes.ViewCompensation, employeeId,
            SmartAttendance.Web.Infrastructure.Security.PeopleCompatibilityAccess.IsAllowed(actor.Role,
                SmartAttendance.Application.Common.Security.PeoplePermissionCodes.ViewCompensation), HttpContext.RequestAborted);
        var financial = CanViewFinancial ? await _dbContext.EmployeeFinancialInfos.AsNoTracking()
            .SingleOrDefaultAsync(f => f.EmployeeId == employeeId) : null;
        if (CanViewFinancial)
        {
            var allowances = await _dbContext.EmployeeAllowances.AsNoTracking().Where(a => a.EmployeeId == employeeId).ToListAsync();
            values["Allowances"] = ProfileValue(allowances.Where(a => a.IsActiveOn(DateOnly.FromDateTime(DateTime.Today))).Sum(a => a.Amount));
        }
        foreach (var field in BuildFieldDictionary().Values)
        {
            var entity = field.Target == "employee" ? (object)employee : financial;
            var type = field.Target == "employee" ? typeof(Employee) : typeof(EmployeeFinancialInfo);
            if (field.Target is "employee" or "compensation" && type.GetProperty(field.Key) is { } property)
                values[field.Key] = ProfileValue(entity == null ? null : property.GetValue(entity));
        }
    }

    private async Task ApplyProfileFieldAsync(int employeeId, UpdateField field, string value)
    {
        var property = ProfileProperty(field) ?? throw new InvalidOperationException("Unsupported profile field.");
        if (!TryProfileValue(property.PropertyType, value, out var parsed))
            throw new InvalidOperationException("Invalid profile field value.");
        if (field.Target == "employee")
        {
            var employee = await _dbContext.Employees.SingleAsync(e => e.Id == employeeId);
            property.SetValue(employee, parsed);
            if (field.Key == "PositionId")
                employee.Position = parsed is int id ? await _dbContext.HrJobPositions.Where(p => p.Id == id).Select(p => p.ArabicName).SingleAsync() : null;
            employee.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var financial = await _dbContext.EmployeeFinancialInfos.SingleOrDefaultAsync(f => f.EmployeeId == employeeId);
            if (financial == null) { financial = new() { EmployeeId = employeeId }; _dbContext.EmployeeFinancialInfos.Add(financial); }
            property.SetValue(financial, parsed);
            financial.UpdatedAt = DateTime.UtcNow;
        }
        await _dbContext.SaveChangesAsync();
    }
}
