using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.MasterDataImports.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Imports;

/// <summary>
/// محرك الاستيراد الشامل للموظفين: يقرأ ملفات Excel/CSV ويؤسس الموظفين مع
/// فروعهم وأقسامهم ومناصبهم دفعة واحدة (إنشاء المراجع الناقصة تلقائياً).
/// يُستخدم من نافذة الاستيراد بـ/Employees — أثقل ملف بالمشروع، عدّل بحذر.
/// </summary>
public sealed class EmployeeBootstrapImportEngine
{
    public const long MaxFileBytes = 10L * 1024L * 1024L;
    public const int MaxRows = 10000;

    public static IReadOnlyList<string> RequiredColumnNames { get; } =
        new[]
        {
            "EmployeeNo",
            "FullName",
            "CompanyName",
            "WorkLocationName",
            "DepartmentName",
            "PositionName",
            "HireDate"
        };

    private static readonly EmployeeTemplateColumn[] BaseColumns =
    {
        new("EmployeeNo", true, EmployeeTemplateColumnKind.Text, 16, "رقم الموظف"),
        new("FullName", true, EmployeeTemplateColumnKind.Text, 30, "الاسم الكامل"),
        new("CompanyName", true, EmployeeTemplateColumnKind.Text, 28, "اسم الشركة"),
        new("CompanyCode", false, EmployeeTemplateColumnKind.Text, 16, "رمز الشركة"),
        new("WorkLocationName", true, EmployeeTemplateColumnKind.Text, 26, "اسم موقع العمل"),
        new("WorkLocationCode", false, EmployeeTemplateColumnKind.Text, 18, "رمز موقع العمل"),
        new("DepartmentName", true, EmployeeTemplateColumnKind.Text, 24, "اسم القسم"),
        new("DepartmentCode", false, EmployeeTemplateColumnKind.Text, 17, "رمز القسم"),
        new("PositionName", true, EmployeeTemplateColumnKind.Text, 28, "المسمى الوظيفي"),
        new("PositionCode", false, EmployeeTemplateColumnKind.Text, 16, "رمز المسمى الوظيفي"),
        new("HireDate", true, EmployeeTemplateColumnKind.Date, 15, "تاريخ التعيين"),
        new("NationalId", false, EmployeeTemplateColumnKind.Text, 20, "رقم الهوية الوطنية"),
        new("Phone", false, EmployeeTemplateColumnKind.Text, 18, "رقم الهاتف"),
        new("Email", false, EmployeeTemplateColumnKind.Text, 28, "البريد الإلكتروني"),
        new("BirthDate", false, EmployeeTemplateColumnKind.Date, 15, "تاريخ الميلاد"),
        new("Gender", false, EmployeeTemplateColumnKind.Text, 14, "الجنس"),
        new("MaritalStatus", false, EmployeeTemplateColumnKind.Text, 17, "الحالة الاجتماعية"),
        new("Nationality", false, EmployeeTemplateColumnKind.Text, 18, "الجنسية"),
        new("Country", false, EmployeeTemplateColumnKind.Text, 18, "البلد"),
        new("ContractType", false, EmployeeTemplateColumnKind.Text, 18, "نوع العقد"),
        new("ContractEndDate", false, EmployeeTemplateColumnKind.Date, 17, "تاريخ انتهاء العقد"),
        new("EmploymentStatus", false, EmployeeTemplateColumnKind.Text, 18, "الحالة الوظيفية"),
        new("IsActive", false, EmployeeTemplateColumnKind.Text, 13, "فعال"),
        new("DirectManagerEmployeeNo", false, EmployeeTemplateColumnKind.Text, 24,
            "رقم الموظف للمدير المباشر"),
        // الراتب الأساسي يسكن EmployeeFinancialInfos لا Employees. الأعمدة حتى
        // BasicSalary هي كتلة القالب القديمة الثابتة؛ أي توسعة جديدة تُلحَق بعدها
        // حتى لا ينزاح ترتيب ملفٍ معبّأ سابقاً.
        new("BasicSalary", false, EmployeeTemplateColumnKind.Text, 16, "الراتب الأساسي"),
        new("FirstName", false, EmployeeTemplateColumnKind.Text, 18, "الاسم الأول (عربي)"),
        new("SecondName", false, EmployeeTemplateColumnKind.Text, 18, "الاسم الثاني (عربي)"),
        new("ThirdName", false, EmployeeTemplateColumnKind.Text, 18, "الاسم الثالث (عربي)"),
        new("LastName", false, EmployeeTemplateColumnKind.Text, 18, "اللقب (عربي)"),
        new("FirstNameEn", false, EmployeeTemplateColumnKind.Text, 18, "الاسم الأول (إنجليزي)"),
        new("SecondNameEn", false, EmployeeTemplateColumnKind.Text, 18, "الاسم الثاني (إنجليزي)"),
        new("ThirdNameEn", false, EmployeeTemplateColumnKind.Text, 18, "الاسم الثالث (إنجليزي)"),
        new("LastNameEn", false, EmployeeTemplateColumnKind.Text, 18, "اللقب (إنجليزي)"),
        new("IsCitizen", false, EmployeeTemplateColumnKind.Text, 13, "مواطن"),
        new("PassportNo", false, EmployeeTemplateColumnKind.Text, 18, "رقم جواز السفر"),
        new("SponsorName", false, EmployeeTemplateColumnKind.Text, 20, "اسم الكفيل"),
        new("Religion", false, EmployeeTemplateColumnKind.Text, 15, "الديانة"),
        new("MotherCountry", false, EmployeeTemplateColumnKind.Text, 18, "البلد الأم"),
        new("MotherCity", false, EmployeeTemplateColumnKind.Text, 18, "المدينة الأم"),
        new("JoiningDate", false, EmployeeTemplateColumnKind.Date, 18, "تاريخ المباشرة الفعلية"),
        new("WorkType", false, EmployeeTemplateColumnKind.Text, 16, "نوع الدوام"),
        new("JobGrade", false, EmployeeTemplateColumnKind.Text, 18, "الدرجة الوظيفية"),
        new("PhoneExtension", false, EmployeeTemplateColumnKind.Text, 15, "امتداد الهاتف"),
        new("PersonalEmail", false, EmployeeTemplateColumnKind.Text, 28, "البريد الشخصي")
    };

    private static readonly LocalizedTemplateField[] LocalizedTemplateFields =
    {
        new("FirstName", "الاسم الأول", true, 18),
        new("SecondName", "الاسم الثاني", false, 18),
        new("ThirdName", "الاسم الثالث", false, 18),
        new("LastName", "اللقب", true, 18),
        new("CompanyName", "اسم الشركة", true, 28),
        new("WorkLocationName", "اسم موقع العمل", true, 26),
        new("DepartmentName", "اسم القسم", true, 24),
        new("PositionName", "المسمى الوظيفي", true, 28)
    };

    private static readonly HashSet<string> ReplacedByLocalizedColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "FullName",
            "FirstName",
            "SecondName",
            "ThirdName",
            "LastName",
            "FirstNameEn",
            "SecondNameEn",
            "ThirdNameEn",
            "LastNameEn",
            "CompanyName",
            "WorkLocationName",
            "DepartmentName",
            "PositionName"
        };

    private readonly ApplicationDbContext _dbContext;

    public EmployeeBootstrapImportEngine(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private async Task<List<TemplateLanguage>> LoadTemplateLanguagesAsync(
        int? companyId)
    {
        var query = _dbContext.CompanyLanguages
            .AsNoTracking()
            .Where(item => item.IsActive && !item.IsDeleted);

        if (companyId.HasValue && companyId.Value > 0)
        {
            query = query.Where(item => item.CompanyId == companyId.Value);
        }

        var configured = await query
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.EnglishName)
            .Select(item => new TemplateLanguage(
                item.CultureCode,
                item.NativeName,
                item.IsDefault,
                item.IsRequired))
            .ToListAsync();

        // عند عدم اختيار شركة نستخدم اتحاد لغات الشركات، لا صفاً مكرراً لكل شركة.
        configured = configured
            .GroupBy(item => item.CultureCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new TemplateLanguage(
                group.Key,
                group.Select(item => item.NativeName)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key,
                group.Any(item => item.IsDefault),
                group.Any(item => item.IsRequired)))
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.CultureCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (configured.Count == 0)
        {
            configured.Add(new TemplateLanguage(
                "ar-IQ",
                "العربية",
                true,
                true));
        }

        if (!configured.Any(item => item.IsDefault))
        {
            configured[0] = configured[0] with { IsDefault = true };
        }

        return configured;
    }

    private static List<EmployeeTemplateColumn> BuildLocalizedTemplateColumns(
        IReadOnlyList<TemplateLanguage> languages)
    {
        var result = new List<EmployeeTemplateColumn>();
        var insertedNames = false;

        foreach (var column in BaseColumns)
        {
            if (column.Name.Equals("FullName", StringComparison.OrdinalIgnoreCase))
            {
                AddLocalizedColumns(result, languages,
                    LocalizedTemplateFields.Where(field =>
                        field.Name is "FirstName" or "SecondName" or "ThirdName" or "LastName"));
                insertedNames = true;
                continue;
            }

            if (ReplacedByLocalizedColumns.Contains(column.Name))
            {
                var field = LocalizedTemplateFields.FirstOrDefault(item =>
                    item.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase));

                if (field is not null &&
                    field.Name is not ("FirstName" or "SecondName" or "ThirdName" or "LastName"))
                {
                    AddLocalizedColumns(result, languages, new[] { field });
                }

                continue;
            }

            result.Add(column);
        }

        if (!insertedNames)
        {
            AddLocalizedColumns(result, languages,
                LocalizedTemplateFields.Where(field =>
                    field.Name is "FirstName" or "SecondName" or "ThirdName" or "LastName"));
        }

        return result;
    }

    private static void AddLocalizedColumns(
        ICollection<EmployeeTemplateColumn> target,
        IReadOnlyList<TemplateLanguage> languages,
        IEnumerable<LocalizedTemplateField> fields)
    {
        foreach (var language in languages)
        {
            foreach (var field in fields)
            {
                var name = BuildLocalizedColumnName(field.Name, language.CultureCode);
                var displayName = $"{field.ArabicLabel} [{language.CultureCode}]";
                target.Add(new EmployeeTemplateColumn(
                    name,
                    field.Required && language.IsRequired,
                    EmployeeTemplateColumnKind.Text,
                    field.Width,
                    displayName));
            }
        }
    }

    private static string BuildLocalizedColumnName(string fieldName, string cultureCode) =>
        $"{fieldName}[{NormalizeCultureCode(cultureCode)}]";

    private static string NormalizeCultureCode(string cultureCode)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureCode.Trim()).Name;
        }
        catch (CultureNotFoundException)
        {
            return cultureCode.Trim();
        }
    }

    public async Task<List<string>> GetTemplateColumnsAsync(int? companyId = null)
    {
        var columns = BuildLocalizedTemplateColumns(
                await LoadTemplateLanguagesAsync(companyId))
            .Select(column => column.Name)
            .ToList();

        var dynamicFields = await LoadDynamicFieldDefinitionsAsync();

        foreach (var field in dynamicFields)
        {
            columns.Add(BuildDynamicHeader(field, columns));
        }

        return columns;
    }

    public async Task<List<string>> GetRequiredTemplateColumnsAsync(int? companyId = null)
    {
        var localizedColumns = BuildLocalizedTemplateColumns(
            await LoadTemplateLanguagesAsync(companyId));
        var required = localizedColumns
            .Where(column => column.Required)
            .Select(column => column.Name)
            .ToList();
        var usedHeaders = new HashSet<string>(
            localizedColumns.Select(column => column.Name),
            StringComparer.OrdinalIgnoreCase);
        var dynamicFields =
            await LoadDynamicFieldDefinitionsAsync();

        foreach (var field in dynamicFields)
        {
            var header = BuildDynamicHeader(
                field,
                usedHeaders);
            usedHeaders.Add(header);

            if (field.IsRequired)
            {
                required.Add(header);
            }
        }

        return required;
    }

    /// <summary>
    /// يبني قالب الاستيراد. مع <paramref name="includeData"/> يخرج القالب
    /// معبّأً بالموظفين الحاليين ليُعدَّل ويُعاد استيراده.
    /// </summary>
    /// <summary>
    /// نطاق التصدير (P0-4): <see cref="AllowedEmployeeIds"/> = <c>null</c> يعني غير
    /// مقيَّد (يُصدَّر الجميع)؛ خلافه يقصر الصفوف على المعرّفات المسموح بها فقط. و
    /// <see cref="IncludeSalary"/> يحكم إخراج عمود الراتب — يُفرَّغ لمن لا يملك
    /// People.ViewCompensation فلا يتسرّب التعويض عبر «تصدير البيانات».
    /// </summary>
    public sealed record TemplateExportScope(
        IReadOnlySet<int>? AllowedEmployeeIds,
        bool IncludeSalary);

    /// <summary>
    /// نطاق الاستيراد (P0 — جانب الكتابة). قبل هذا كان <see cref="ImportAsync"/> محروساً
    /// بصلاحية Import العالمية فقط بلا أي فحص نطاق صفّاً-صفّاً، فأمكن:
    /// (1) تحديث موظفٍ بشركةٍ أخرى بمطابقة EmployeeNo عالميّاً — بل نقله عابراً
    /// للشركات متخطّياً حارس ChangeAssignment؛ (2) إنشاء شركات/فروع/أقسام/مناصب
    /// خارج النطاق؛ (3) كتابة الرواتب بلا EditCompensation.
    ///
    /// <para>القرار (فرضٌ صارم): بنطاقٍ مقيَّد يُسمح فقط بإنشاء/تحديث موظفٍ ضمن بنيةٍ
    /// <b>قائمة</b> وداخل نطاق المستخدم — لا إنشاء بنية جديدة (يتطلّب نطاقاً عاماً)،
    /// ولا لمس موظفٍ خارج النطاق. والراتب لا يُكتب إلا مع <see cref="CanEditCompensation"/>.</para>
    ///
    /// <para><see cref="IsUnrestricted"/> (أدمن/عام) يُبقي السلوك التأسيسي كاملاً.
    /// النطاقان يُقاطَعان (AND) بنفس <see cref="PeopleDataScope.AllowsEmployee"/>.</para>
    /// </summary>
    public sealed record ImportScope(
        bool IsUnrestricted,
        PeopleDataScope DirectoryScope,
        PeopleDataScope AccessRoleScope,
        bool CanEditCompensation)
    {
        /// <summary>نطاقٌ عام (أدمن): استيرادٌ تأسيسيٌّ كامل بلا قيود صفّية.</summary>
        public static ImportScope Unrestricted(bool canEditCompensation = true) =>
            new(true,
                PeopleDataScope.Unrestricted(),
                PeopleDataScope.Unrestricted(),
                canEditCompensation);
    }

    public async Task<byte[]> BuildTemplateWorkbookAsync(
        bool includeData = false,
        TemplateExportScope? exportScope = null,
        int? companyId = null)
    {
        await EmployeeProfileDynamicFields.EnsureSchemaAsync(_dbContext);

        var languages = await LoadTemplateLanguagesAsync(companyId);
        var columns = BuildLocalizedTemplateColumns(languages);
        var dynamicFields = await LoadDynamicFieldDefinitionsAsync();
        var usedHeaders = new HashSet<string>(
            columns.Select(column => column.Name),
            StringComparer.OrdinalIgnoreCase);
        var dynamicHeadersByKey =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var field in dynamicFields)
        {
            var header = BuildDynamicHeader(field, usedHeaders);
            usedHeaders.Add(header);
            dynamicHeadersByKey[field.FieldKey] = header;
            columns.Add(
                new EmployeeTemplateColumn(
                    header,
                    field.IsRequired,
                    EmployeeTemplateColumnKind.Custom,
                    24));
        }

        var references = await LoadTemplateReferencesAsync();

        var dataRows = includeData
            ? await LoadTemplateDataRowsAsync(
                columns,
                dynamicHeadersByKey,
                exportScope)
            : new List<List<string>>();

        return BuildWorkbook(columns, references, dataRows);
    }

    public async Task<MasterDataImportPreviewViewModel> PreviewAsync(
        string filePath,
        string token,
        string originalFileName,
        int previewLimit)
    {
        // المعاينة غير مستعملة بمسار الاستيراد بخطوة واحدة؛ تُبقى غير مقيَّدة.
        var plan = await BuildPlanAsync(filePath, ImportScope.Unrestricted());

        return new MasterDataImportPreviewViewModel
        {
            Token = token,
            FileName = originalFileName,
            ImportType = "Employees",
            TotalRows = plan.Rows.Count,
            ReadyCount = plan.Rows.Count(row => row.CanImport),
            ErrorCount = plan.Rows.Count(row => !row.CanImport),
            CreateCount = plan.Rows.Count(row =>
                row.CanImport &&
                row.EmployeeAction == "Create"),
            UpdateCount = plan.Rows.Count(row =>
                row.CanImport &&
                row.EmployeeAction == "Update"),
            PreviewLimit = previewLimit,
            Rows = plan.Rows
                .OrderBy(row => row.CanImport)
                .ThenBy(row => row.RowNumber)
                .Take(previewLimit)
                .Select(ToPreviewRow)
                .ToList()
        };
    }

    public async Task<MasterDataImportResultViewModel> ImportAsync(
        string filePath,
        string originalFileName,
        ImportScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        await EmployeeProfileDynamicFields.EnsureSchemaAsync(_dbContext);

        var plan = await BuildPlanAsync(filePath, scope);
        var validRows = plan.Rows
            .Where(row => row.CanImport)
            .OrderBy(row => row.RowNumber)
            .ToList();

        if (validRows.Count == 0)
        {
            return new MasterDataImportResultViewModel
            {
                CreatedCount = 0,
                UpdatedCount = 0,
                SkippedCount = plan.Rows.Count,
                ErrorCount = plan.Rows.Count,
                Message = BuildImportFailureMessage(
                    plan.Rows.Select(row => row.Errors))
            };
        }

        var dynamicDefinitions =
            await LoadDynamicFieldDefinitionsAsync();

        var structureCounts = new BootstrapStructureCounts();
        var createdEmployees = 0;
        var updatedEmployees = 0;
        var dynamicValues = 0;

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        try
        {
            var companies = await _dbContext.Companies
                .Where(company => !company.IsDeleted)
                .ToListAsync();

            var branches = await _dbContext.Branches
                .Where(branch => !branch.IsDeleted)
                .ToListAsync();

            var departments = await _dbContext.Departments
                .Where(department => !department.IsDeleted)
                .ToListAsync();

            var employees = await _dbContext.Employees
                .Where(employee => !employee.IsDeleted)
                .ToListAsync();

            var positions = await LoadPositionsAsync();
            var companyCodes = new HashSet<string>(
                companies.Select(company => NormalizeCode(company.Code)),
                StringComparer.OrdinalIgnoreCase);
            var branchCodes = new HashSet<string>(
                branches.Select(branch => NormalizeCode(branch.Code)),
                StringComparer.OrdinalIgnoreCase);
            var departmentCodes = new HashSet<string>(
                departments.Select(department => NormalizeCode(department.Code)),
                StringComparer.OrdinalIgnoreCase);
            var positionCodes = new HashSet<string>(
                positions
                    .Select(position => NormalizeCode(position.Code))
                    .Where(code => !string.IsNullOrWhiteSpace(code)),
                StringComparer.OrdinalIgnoreCase);

            var resolvedRows = new List<ResolvedEmployeeImportRow>();

            foreach (var row in validRows)
            {
                var company = await EnsureCompanyAsync(
                    row,
                    companies,
                    companyCodes,
                    structureCounts);

                var branch = await EnsureBranchAsync(
                    row,
                    company,
                    branches,
                    branchCodes,
                    structureCounts);

                var department = await EnsureDepartmentAsync(
                    row,
                    company,
                    departments,
                    departmentCodes,
                    structureCounts);

                var position = await EnsurePositionAsync(
                    row,
                    company,
                    department,
                    positions,
                    positionCodes,
                    structureCounts);

                var employee = employees.FirstOrDefault(item =>
                    Same(item.EmployeeNo, row.EmployeeNo));

                var created = employee == null;
                employee ??= new Employee();

                employee.EmployeeNo = NormalizeIdentifier(row.EmployeeNo);
                employee.FullName = row.FullName.Trim();
                employee.BranchId = branch.Id;
                employee.DepartmentId = department.Id;
                employee.PositionId = position.Id;
                employee.Position = position.Name;
                employee.HireDate = row.HireDate!.Value;

                ApplyOptional(
                    row.NationalId,
                    created,
                    value => employee.NationalId = value);
                ApplyOptional(
                    row.Phone,
                    created,
                    value => employee.Phone = value);
                ApplyOptional(
                    row.Email,
                    created,
                    value => employee.Email = value);
                ApplyOptional(
                    row.Country,
                    created,
                    value => employee.Country = value);
                ApplyOptional(
                    row.Nationality,
                    created,
                    value => employee.Nationality = value);
                ApplyOptional(
                    row.Gender,
                    created,
                    value => employee.Gender = value);
                ApplyOptional(
                    row.MaritalStatus,
                    created,
                    value => employee.MaritalStatus = value);
                ApplyOptional(row.FirstName, created, value => employee.FirstName = value);
                ApplyOptional(row.SecondName, created, value => employee.SecondName = value);
                ApplyOptional(row.ThirdName, created, value => employee.ThirdName = value);
                ApplyOptional(row.LastName, created, value => employee.LastName = value);
                ApplyOptional(row.FirstNameEn, created, value => employee.FirstNameEn = value);
                ApplyOptional(row.SecondNameEn, created, value => employee.SecondNameEn = value);
                ApplyOptional(row.ThirdNameEn, created, value => employee.ThirdNameEn = value);
                ApplyOptional(row.LastNameEn, created, value => employee.LastNameEn = value);
                ApplyOptional(row.PassportNo, created, value => employee.PassportNo = value);
                ApplyOptional(row.SponsorName, created, value => employee.SponsorName = value);
                ApplyOptional(row.Religion, created, value => employee.Religion = value);
                ApplyOptional(row.MotherCountry, created, value => employee.MotherCountry = value);
                ApplyOptional(row.MotherCity, created, value => employee.MotherCity = value);
                ApplyOptional(row.WorkType, created, value => employee.WorkType = value);
                ApplyOptional(row.JobGrade, created, value => employee.JobGrade = value);
                ApplyOptional(row.PhoneExtension, created, value => employee.PhoneExtension = value);
                ApplyOptional(row.PersonalEmail, created, value => employee.PersonalEmail = value);

                if (row.BirthDate.HasValue)
                {
                    employee.BirthDate = row.BirthDate;
                }

                if (row.JoiningDate.HasValue)
                {
                    employee.JoiningDate = row.JoiningDate;
                }

                if (row.IsCitizen.HasValue)
                {
                    employee.IsCitizen = row.IsCitizen.Value;
                }

                if (row.IsActive.HasValue)
                {
                    employee.IsActive = row.IsActive.Value;
                }
                else if (created)
                {
                    employee.IsActive = true;
                }

                if (created)
                {
                    await _dbContext.Employees.AddAsync(employee);
                    employees.Add(employee);
                    createdEmployees++;
                }
                else
                {
                    updatedEmployees++;
                }

                resolvedRows.Add(
                    new ResolvedEmployeeImportRow(
                        row,
                        employee,
                        created,
                        company.Id,
                        branch.Id,
                        department.Id,
                        position.Id));
            }

            await _dbContext.SaveChangesAsync();

            var employeesByNo = employees
                .Where(employee => employee.Id > 0)
                .GroupBy(
                    employee => NormalizeKey(employee.EmployeeNo),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var resolved in resolvedRows)
            {
                await SaveImportedLocalizedValuesAsync(resolved);

                await UpdateExtendedEmployeeColumnsAsync(
                    resolved.Employee.Id,
                    resolved.Plan,
                    resolved.Created);

                if (!string.IsNullOrWhiteSpace(
                        resolved.Plan.DirectManagerEmployeeNo))
                {
                    var managerKey = NormalizeKey(
                        resolved.Plan.DirectManagerEmployeeNo);

                    if (employeesByNo.TryGetValue(
                            managerKey,
                            out var manager))
                    {
                        await ExecuteSqlAsync(
                            """
                            UPDATE dbo.Employees
                            SET DirectManagerId = @DirectManagerId,
                                UpdatedAt = SYSUTCDATETIME()
                            WHERE Id = @EmployeeId;
                            """,
                            command =>
                            {
                                AddParameter(
                                    command,
                                    "@DirectManagerId",
                                    manager.Id);
                                AddParameter(
                                    command,
                                    "@EmployeeId",
                                    resolved.Employee.Id);
                            });
                    }
                }

                // الراتب لا يُكتب إلا مع EditCompensation — وإلا فصلاحية Import تصبح
                // باباً خلفيّاً لكتابة التعويض (مطابقةً لحارس FinancialInfo).
                if (scope.CanEditCompensation)
                {
                    await SaveBasicSalaryAsync(
                        resolved.Employee.Id,
                        resolved.Plan.BasicSalary);
                }

                dynamicValues +=
                    await SaveDynamicFieldsAsync(
                        resolved.Employee.Id,
                        resolved.Plan.Values,
                        dynamicDefinitions);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        var skipped = plan.Rows.Count - validRows.Count;

        return new MasterDataImportResultViewModel
        {
            CreatedCount = createdEmployees,
            UpdatedCount = updatedEmployees,
            SkippedCount = skipped,
            ErrorCount = skipped,
            Message =
                $"اكتمل استيراد الملف {originalFileName}. " +
                $"الموظفون المضافون: {createdEmployees}، " +
                $"الموظفون المحدّثون: {updatedEmployees}، " +
                $"الشركات المضافة: {structureCounts.Companies}، " +
                $"مواقع العمل المضافة: {structureCounts.Branches}، " +
                $"الأقسام المضافة: {structureCounts.Departments}، " +
                $"المناصب المضافة: {structureCounts.Positions}، " +
                $"القيم المخصصة المحفوظة: {dynamicValues}، " +
                $"الصفوف المتخطاة: {skipped}."
        };
    }

    public static string BuildImportFailureMessage(
        IEnumerable<IEnumerable<string>> rowErrors)
    {
        ArgumentNullException.ThrowIfNull(rowErrors);

        var rows = rowErrors
            .Select(errors => errors?
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .Select(LocalizeImportError)
                .ToList() ?? new List<string>())
            .ToList();

        if (rows.Count == 0)
        {
            return "لم يعثر الملف على صفوف موظفين قابلة للاستيراد.";
        }

        var commonReasons = rows
            .SelectMany(errors => errors)
            .GroupBy(error => error, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(group => $"{group.Key} ({group.Count()} صف)")
            .ToList();

        var message =
            $"لم يتم استيراد أي موظف. الصفوف المرفوضة: {rows.Count}.";

        return commonReasons.Count == 0
            ? message
            : $"{message} الأسباب الأكثر تكراراً: " +
              string.Join("، ", commonReasons) + ".";
    }

    private static string LocalizeImportError(string error)
    {
        const string requiredSuffix = " is required.";

        if (error.EndsWith(requiredSuffix, StringComparison.Ordinal))
        {
            var fieldName = error[..^requiredSuffix.Length];
            var field = BaseColumns.FirstOrDefault(column =>
                column.Name.Equals(
                    fieldName,
                    StringComparison.OrdinalIgnoreCase));

            if (field != null)
            {
                return $"{field.DisplayName ?? field.Name} مطلوب";
            }
        }

        const string customFieldPrefix =
            "Required custom field is missing: ";

        if (error.StartsWith(
                customFieldPrefix,
                StringComparison.Ordinal))
        {
            return "الحقل المخصص المطلوب غير معبأ: " +
                   error[customFieldPrefix.Length..];
        }

        return error;
    }

    private async Task<EmployeeBootstrapPlan> BuildPlanAsync(
        string filePath,
        ImportScope scope)
    {
        var file = ReadFile(filePath);
        ValidateHeaders(file.Headers);

        if (file.Rows.Count > MaxRows)
        {
            throw new InvalidOperationException(
                $"The file contains more than {MaxRows} rows.");
        }

        var dataRows = file.Rows
            .Where(HasInputData)
            .ToList();

        var snapshot = await LoadSnapshotAsync();
        var defaultCultures = await LoadDefaultCulturesAsync();
        var requiredCultures = await LoadRequiredCulturesAsync();
        var dynamicDefinitions =
            await LoadDynamicFieldDefinitionsAsync();
        var plans = new List<EmployeeBootstrapRowPlan>();
        var seenEmployeeNumbers = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in dataRows)
        {
            var plan = BuildRowPlan(
                row,
                snapshot,
                seenEmployeeNumbers,
                dynamicDefinitions,
                defaultCultures,
                requiredCultures);

            plans.Add(plan);
        }

        var availableManagers = new HashSet<string>(
            snapshot.EmployeeNumbers,
            StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans.Where(item => item.CanImport))
        {
            availableManagers.Add(
                NormalizeKey(plan.EmployeeNo));
        }

        foreach (var plan in plans)
        {
            if (string.IsNullOrWhiteSpace(
                    plan.DirectManagerEmployeeNo))
            {
                continue;
            }

            if (Same(
                    plan.EmployeeNo,
                    plan.DirectManagerEmployeeNo))
            {
                plan.Errors.Add(
                    "The employee cannot be their own direct manager.");
                continue;
            }

            if (!availableManagers.Contains(
                    NormalizeKey(
                        plan.DirectManagerEmployeeNo)))
            {
                plan.Errors.Add(
                    $"Direct manager employee number was not found: " +
                    $"{plan.DirectManagerEmployeeNo}");
            }
        }

        await ApplyImportScopeAsync(plans, scope);

        return new EmployeeBootstrapPlan(plans);
    }

    /// <summary>
    /// فرض نطاق الاستيراد صفّاً-صفّاً (فرضٌ صارم). النطاق العام لا يتأثّر. للمقيَّد:
    /// (1) لا إنشاء بنية جديدة (شركة/فرع/قسم/منصب) — كلّها يجب أن تكون قائمة؛
    /// (2) وجهة الصفّ (شركة/فرع/قسم) ضمن نطاق المستخدم؛
    /// (3) عند تحديث موظفٍ قائم، يجب أن يكون هو نفسه ضمن النطاق — يسدّ اختطاف موظفٍ
    ///     بشركةٍ أخرى بمطابقة EmployeeNo عالميّاً (منع النقل العابر للشركات).
    /// الصفوف المخالفة تُعلَّم بخطأٍ فتُتخطّى بآلية التخطّي القائمة نفسها.
    /// </summary>
    private async Task ApplyImportScopeAsync(
        List<EmployeeBootstrapRowPlan> plans,
        ImportScope scope)
    {
        if (scope.IsUnrestricted)
        {
            return;
        }

        // الوجهة (شركة/فرع/قسم) تُقيَّم موقعيّاً — لا معرّف موظفٍ بعد للصفّ المُنشأ.
        bool LocationAllowed(int companyId, int branchId, int departmentId) =>
            scope.DirectoryScope.AllowsLocation(companyId, branchId, departmentId) &&
            scope.AccessRoleScope.AllowsLocation(companyId, branchId, departmentId);

        // الموظف القائم يُقيَّم بمعرّفه الحقيقي (يحترم بُعدَي الموظف/الذات).
        bool EmployeeAllowed(int employeeId, int companyId, int branchId, int departmentId) =>
            scope.DirectoryScope.AllowsEmployee(employeeId, companyId, branchId, departmentId) &&
            scope.AccessRoleScope.AllowsEmployee(employeeId, companyId, branchId, departmentId);

        // الموظفون القائمون بهويّة كاملة — لفحص «الموجود داخل النطاق» بدقّة بدل
        // الاكتفاء بوجهة الملف (التي قد تُحاول نقله). الشركة تُشتقّ عبر الفرع.
        var existingByNo = (await QueryAsync(
                """
                SELECT e.Id, e.EmployeeNo, b.CompanyId, e.BranchId,
                       ISNULL(e.DepartmentId, 0) AS DepartmentId
                FROM dbo.Employees e
                INNER JOIN dbo.Branches b ON b.Id = e.BranchId
                WHERE e.IsDeleted = 0;
                """,
                command => { },
                reader => new
                {
                    Id = GetInt32(reader, "Id"),
                    EmployeeNo = GetString(reader, "EmployeeNo"),
                    CompanyId = GetInt32(reader, "CompanyId"),
                    BranchId = GetInt32(reader, "BranchId"),
                    DepartmentId = GetInt32(reader, "DepartmentId")
                }))
            .GroupBy(item => NormalizeKey(item.EmployeeNo), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            if (!plan.CanImport || plan.Company is null)
            {
                continue;   // أصلاً مخطئ أو بلا شركة محسومة.
            }

            var anyStructurePlanned =
                plan.Company.IsPlanned ||
                plan.Branch is null || plan.Branch.IsPlanned ||
                plan.Department is null || plan.Department.IsPlanned ||
                plan.Position is null || plan.Position.IsPlanned;

            var targetAllowed = !anyStructurePlanned &&
                LocationAllowed(plan.Company.Id, plan.Branch!.Id, plan.Department!.Id);

            var isUpdate = plan.EmployeeAction == "Update";
            var existingFound = existingByNo.TryGetValue(
                NormalizeKey(plan.EmployeeNo), out var current);
            var existingAllowed = existingFound &&
                EmployeeAllowed(current!.Id, current.CompanyId, current.BranchId, current.DepartmentId);

            var error = EvaluateRowScope(
                scope.IsUnrestricted,
                anyStructurePlanned,
                targetAllowed,
                isUpdate,
                existingFound,
                existingAllowed);

            if (error is not null)
            {
                plan.Errors.Add(error);
            }
        }
    }

    // رسائل رفض النطاق — ثوابت ليشترك فيها الفرض والاختبار.
    public const string ImportStructureScopeError =
        "الاستيراد بنطاق مقيَّد لا يُنشئ شركاتٍ أو فروعاً أو أقساماً أو مناصب جديدة — يتطلّب صلاحيةً عامة.";

    public const string ImportDestinationScopeError =
        "وجهة هذا الصفّ (الشركة/الفرع/القسم) خارج نطاق صلاحياتك.";

    public const string ImportHijackScopeError =
        "لا تملك صلاحيةً على الموظف المراد تحديثه (خارج نطاقك).";

    /// <summary>
    /// قرار فرض النطاق لصفٍّ واحد (نقيّ، بلا قاعدة بيانات) — يُرجع رسالة الخطأ أو
    /// <c>null</c> إن مرّ الصفّ. عامٌّ ⟹ يمرّ دائماً. مقيَّدٌ: يُرفض إنشاء بنية جديدة،
    /// ثم وجهةٌ خارج النطاق، ثم اختطاف موظفٍ قائمٍ خارج النطاق (تحديث فقط).
    /// </summary>
    public static string? EvaluateRowScope(
        bool isUnrestricted,
        bool anyStructurePlanned,
        bool targetAllowed,
        bool isUpdate,
        bool existingFound,
        bool existingAllowed)
    {
        if (isUnrestricted)
        {
            return null;
        }

        if (anyStructurePlanned)
        {
            return ImportStructureScopeError;
        }

        if (!targetAllowed)
        {
            return ImportDestinationScopeError;
        }

        if (isUpdate && existingFound && !existingAllowed)
        {
            return ImportHijackScopeError;
        }

        return null;
    }

    private EmployeeBootstrapRowPlan BuildRowPlan(
        ParsedImportRow row,
        BootstrapSnapshot snapshot,
        HashSet<string> seenEmployeeNumbers,
        IReadOnlyList<DynamicFieldDefinition> dynamicDefinitions,
        IReadOnlyDictionary<string, string> defaultCultures,
        IReadOnlyDictionary<string, IReadOnlyList<string>> requiredCultures)
    {
        var preferredCulture = ResolvePreferredCulture(row.Values, defaultCultures);
        var firstName = GetPreferredLocalizedValue(row.Values, "FirstName", preferredCulture);
        var secondName = GetPreferredLocalizedValue(row.Values, "SecondName", preferredCulture);
        var thirdName = GetPreferredLocalizedValue(row.Values, "ThirdName", preferredCulture);
        var lastName = GetPreferredLocalizedValue(row.Values, "LastName", preferredCulture);
        var composedName = ComposeFullName(firstName, secondName, thirdName, lastName);

        var plan = new EmployeeBootstrapRowPlan
        {
            RowNumber = row.RowNumber,
            Values = row.Values,
            EmployeeNo = GetValue(row.Values, "EmployeeNo"),
            FullName = FirstNonBlank(GetValue(row.Values, "FullName"), composedName),
            CompanyName = FirstNonBlank(
                GetPreferredLocalizedValue(row.Values, "CompanyName", preferredCulture),
                GetValue(row.Values, "Company")),
            CompanyCode = GetValue(
                row.Values,
                "CompanyCode"),
            WorkLocationName = FirstNonBlank(
                GetPreferredLocalizedValue(row.Values, "WorkLocationName", preferredCulture),
                GetValue(row.Values, "BranchName", "WorkLocation")),
            WorkLocationCode = GetValue(
                row.Values,
                "WorkLocationCode",
                "BranchCode"),
            DepartmentName = GetPreferredLocalizedValue(
                row.Values,
                "DepartmentName",
                preferredCulture),
            DepartmentCode = GetValue(
                row.Values,
                "DepartmentCode"),
            PositionName = FirstNonBlank(
                GetPreferredLocalizedValue(row.Values, "PositionName", preferredCulture),
                GetValue(row.Values, "Position")),
            PositionCode = GetValue(
                row.Values,
                "PositionCode"),
            NationalId = GetValue(
                row.Values,
                "NationalId"),
            Phone = GetValue(
                row.Values,
                "Phone"),
            Email = GetValue(
                row.Values,
                "Email"),
            Country = GetValue(
                row.Values,
                "Country"),
            Nationality = GetValue(
                row.Values,
                "Nationality"),
            Gender = GetValue(
                row.Values,
                "Gender"),
            MaritalStatus = GetValue(
                row.Values,
                "MaritalStatus"),
            ContractType = GetValue(
                row.Values,
                "ContractType"),
            EmploymentStatus = GetValue(
                row.Values,
                "EmploymentStatus"),
            DirectManagerEmployeeNo = GetValue(
                row.Values,
                "DirectManagerEmployeeNo"),
            FirstName = firstName,
            SecondName = secondName,
            ThirdName = thirdName,
            LastName = lastName,
            FirstNameEn = FirstNonBlank(
                GetValue(row.Values, "FirstNameEn"),
                GetPreferredLocalizedValue(row.Values, "FirstName", "en-US")),
            SecondNameEn = FirstNonBlank(
                GetValue(row.Values, "SecondNameEn"),
                GetPreferredLocalizedValue(row.Values, "SecondName", "en-US")),
            ThirdNameEn = FirstNonBlank(
                GetValue(row.Values, "ThirdNameEn"),
                GetPreferredLocalizedValue(row.Values, "ThirdName", "en-US")),
            LastNameEn = FirstNonBlank(
                GetValue(row.Values, "LastNameEn"),
                GetPreferredLocalizedValue(row.Values, "LastName", "en-US")),
            PassportNo = GetValue(row.Values, "PassportNo"),
            SponsorName = GetValue(row.Values, "SponsorName"),
            Religion = GetValue(row.Values, "Religion"),
            MotherCountry = GetValue(row.Values, "MotherCountry"),
            MotherCity = GetValue(row.Values, "MotherCity"),
            WorkType = GetValue(row.Values, "WorkType"),
            JobGrade = GetValue(row.Values, "JobGrade"),
            PhoneExtension = GetValue(row.Values, "PhoneExtension"),
            PersonalEmail = GetValue(row.Values, "PersonalEmail")
        };

        plan.LocalizedValues.AddRange(ParseLocalizedValues(row.Values));
        ValidateRequiredLocalizedValues(plan, requiredCultures);

        Require(plan.EmployeeNo, "EmployeeNo", plan.Errors);
        Require(plan.FullName, "FullName", plan.Errors);
        Require(plan.CompanyName, "CompanyName", plan.Errors);
        Require(
            plan.WorkLocationName,
            "WorkLocationName",
            plan.Errors);
        Require(
            plan.DepartmentName,
            "DepartmentName",
            plan.Errors);
        Require(
            plan.PositionName,
            "PositionName",
            plan.Errors);

        foreach (var definition in dynamicDefinitions
                     .Where(item => item.IsRequired))
        {
            var matchingValue = row.Values
                .Where(pair => TryResolveDynamicHeader(
                    pair.Key,
                    dynamicDefinitions,
                    out var resolved) &&
                    Same(
                        resolved.FieldKey,
                        definition.FieldKey))
                .Select(pair => pair.Value)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(matchingValue))
            {
                plan.Errors.Add(
                    $"Required custom field is missing: " +
                    $"{definition.FieldLabel}");
            }
        }

        ValidateLength(
            plan.EmployeeNo,
            50,
            "EmployeeNo",
            plan.Errors);
        ValidateLength(
            plan.FullName,
            200,
            "FullName",
            plan.Errors);
        ValidateLength(
            plan.CompanyName,
            200,
            "CompanyName",
            plan.Errors);
        ValidateLength(
            plan.WorkLocationName,
            200,
            "WorkLocationName",
            plan.Errors);
        ValidateLength(
            plan.DepartmentName,
            200,
            "DepartmentName",
            plan.Errors);
        ValidateLength(
            plan.PositionName,
            150,
            "PositionName",
            plan.Errors);

        foreach (var (value, maximum, field) in new (string Value, int Maximum, string Field)[]
                 {
                     (plan.FirstName, 100, "FirstName"),
                     (plan.SecondName, 100, "SecondName"),
                     (plan.ThirdName, 100, "ThirdName"),
                     (plan.LastName, 100, "LastName"),
                     (plan.FirstNameEn, 100, "FirstNameEn"),
                     (plan.SecondNameEn, 100, "SecondNameEn"),
                     (plan.ThirdNameEn, 100, "ThirdNameEn"),
                     (plan.LastNameEn, 100, "LastNameEn"),
                     (plan.PassportNo, 50, "PassportNo"),
                     (plan.SponsorName, 150, "SponsorName"),
                     (plan.Religion, 50, "Religion"),
                     (plan.MotherCountry, 100, "MotherCountry"),
                     (plan.MotherCity, 100, "MotherCity"),
                     (plan.WorkType, 50, "WorkType"),
                     (plan.JobGrade, 100, "JobGrade"),
                     (plan.PhoneExtension, 20, "PhoneExtension"),
                     (plan.PersonalEmail, 200, "PersonalEmail")
                 })
        {
            ValidateLength(value, maximum, field, plan.Errors);
        }

        var hireDateText = GetValue(
            row.Values,
            "HireDate");

        if (!TryParseDate(
                hireDateText,
                out var hireDate))
        {
            plan.Errors.Add(
                $"Invalid HireDate: {hireDateText}");
        }
        else
        {
            plan.HireDate = hireDate;
        }

        var birthDateText = GetValue(
            row.Values,
            "BirthDate");

        if (!string.IsNullOrWhiteSpace(birthDateText))
        {
            if (!TryParseDate(
                    birthDateText,
                    out var birthDate))
            {
                plan.Errors.Add(
                    $"Invalid BirthDate: {birthDateText}");
            }
            else
            {
                plan.BirthDate = birthDate;
            }
        }

        var joiningDateText = GetValue(row.Values, "JoiningDate");
        if (!string.IsNullOrWhiteSpace(joiningDateText))
        {
            if (!TryParseDate(joiningDateText, out var joiningDate))
            {
                plan.Errors.Add($"Invalid JoiningDate: {joiningDateText}");
            }
            else
            {
                plan.JoiningDate = joiningDate;
            }
        }

        var isCitizenText = GetValue(row.Values, "IsCitizen");
        if (!string.IsNullOrWhiteSpace(isCitizenText))
        {
            if (!TryParseBoolean(isCitizenText, out var isCitizen))
            {
                plan.Errors.Add($"Invalid IsCitizen value: {isCitizenText}");
            }
            else
            {
                plan.IsCitizen = isCitizen;
            }
        }

        var contractEndDateText = GetValue(
            row.Values,
            "ContractEndDate");

        if (!string.IsNullOrWhiteSpace(
                contractEndDateText))
        {
            if (!TryParseDate(
                    contractEndDateText,
                    out var contractEndDate))
            {
                plan.Errors.Add(
                    $"Invalid ContractEndDate: " +
                    $"{contractEndDateText}");
            }
            else
            {
                plan.ContractEndDate =
                    contractEndDate;
            }
        }

        var basicSalaryText = GetValue(
            row.Values,
            "BasicSalary");

        if (!string.IsNullOrWhiteSpace(basicSalaryText))
        {
            if (!ImportAmountParser.TryParse(
                    basicSalaryText,
                    out var basicSalary))
            {
                plan.Errors.Add(
                    $"Invalid BasicSalary: {basicSalaryText}");
            }
            else if (basicSalary < 0)
            {
                plan.Errors.Add(
                    $"BasicSalary must not be negative: " +
                    $"{basicSalaryText}");
            }
            else
            {
                plan.BasicSalary = basicSalary;
            }
        }

        var isActiveText = GetValue(
            row.Values,
            "IsActive");

        if (!string.IsNullOrWhiteSpace(isActiveText))
        {
            if (!TryParseBoolean(
                    isActiveText,
                    out var isActive))
            {
                plan.Errors.Add(
                    $"Invalid IsActive value: {isActiveText}");
            }
            else
            {
                plan.IsActive = isActive;
            }
        }

        if (!string.IsNullOrWhiteSpace(plan.Email) &&
            !LooksLikeEmail(plan.Email))
        {
            plan.Errors.Add(
                $"Invalid Email: {plan.Email}");
        }

        if (!string.IsNullOrWhiteSpace(plan.EmployeeNo))
        {
            var employeeKey =
                NormalizeKey(plan.EmployeeNo);

            if (!seenEmployeeNumbers.Add(employeeKey))
            {
                plan.Errors.Add(
                    "Duplicate EmployeeNo inside the same file.");
            }
        }

        var company = ResolveCompany(
            plan,
            snapshot);

        if (company != null)
        {
            plan.Company = company;

            var branch = ResolveBranch(
                plan,
                company,
                snapshot);

            if (branch != null)
            {
                plan.Branch = branch;
            }

            var department = ResolveDepartment(
                plan,
                company,
                snapshot);

            if (department != null)
            {
                plan.Department = department;
            }

            var position = ResolvePosition(
                plan,
                company,
                department,
                snapshot);

            if (position != null)
            {
                plan.Position = position;
            }
        }

        plan.EmployeeAction =
            snapshot.EmployeeNumbers.Contains(
                NormalizeKey(plan.EmployeeNo))
                ? "Update"
                : "Create";

        if (plan.EmployeeAction == "Update")
        {
            plan.Messages.Add(
                "Employee exists and will be updated. " +
                "Blank optional cells keep their current values.");
        }
        else
        {
            plan.Messages.Add(
                "Employee will be created.");
        }

        return plan;
    }

    private static CompanyReference? ResolveCompany(
        EmployeeBootstrapRowPlan plan,
        BootstrapSnapshot snapshot)
    {
        var byCode = string.IsNullOrWhiteSpace(
                plan.CompanyCode)
            ? null
            : snapshot.Companies.FirstOrDefault(
                item => Same(
                    item.Code,
                    plan.CompanyCode));

        var byName = snapshot.Companies
            .Where(item => Same(
                item.Name,
                plan.CompanyName))
            .ToList();

        if (byCode != null &&
            !Same(byCode.Name, plan.CompanyName))
        {
            plan.Errors.Add(
                $"CompanyCode {plan.CompanyCode} belongs to " +
                $"'{byCode.Name}', not '{plan.CompanyName}'.");
            return null;
        }

        if (byCode == null && byName.Count > 1)
        {
            plan.Errors.Add(
                $"Company name is ambiguous: {plan.CompanyName}. " +
                "Use CompanyCode.");
            return null;
        }

        var selected = byCode ?? byName.FirstOrDefault();

        if (selected != null)
        {
            return selected;
        }

        if (!string.IsNullOrWhiteSpace(plan.CompanyCode) &&
            snapshot.Companies.Any(item =>
                Same(item.Code, plan.CompanyCode)))
        {
            plan.Errors.Add(
                $"CompanyCode already exists: {plan.CompanyCode}");
            return null;
        }

        var planned = new CompanyReference(
            0,
            plan.CompanyName.Trim(),
            NormalizeCode(plan.CompanyCode),
            true);

        snapshot.Companies.Add(planned);
        plan.Messages.Add(
            $"Company '{planned.Name}' does not exist " +
            "and will be created automatically.");

        return planned;
    }

    private static BranchReference? ResolveBranch(
        EmployeeBootstrapRowPlan plan,
        CompanyReference company,
        BootstrapSnapshot snapshot)
    {
        var byCode = string.IsNullOrWhiteSpace(
                plan.WorkLocationCode)
            ? null
            : snapshot.Branches.FirstOrDefault(
                item => Same(
                    item.Code,
                    plan.WorkLocationCode));

        if (byCode != null &&
            !Same(byCode.CompanyKey, company.Key))
        {
            plan.Errors.Add(
                $"WorkLocationCode {plan.WorkLocationCode} " +
                "belongs to another company.");
            return null;
        }

        var byName = snapshot.Branches
            .Where(item =>
                Same(item.CompanyKey, company.Key) &&
                Same(item.Name, plan.WorkLocationName))
            .ToList();

        if (byCode != null &&
            !Same(byCode.Name, plan.WorkLocationName))
        {
            plan.Errors.Add(
                $"WorkLocationCode {plan.WorkLocationCode} " +
                $"belongs to '{byCode.Name}', not " +
                $"'{plan.WorkLocationName}'.");
            return null;
        }

        var selected = byCode ?? byName.FirstOrDefault();

        if (selected != null)
        {
            return selected;
        }

        var planned = new BranchReference(
            0,
            company.Key,
            plan.WorkLocationName.Trim(),
            NormalizeCode(plan.WorkLocationCode),
            true);

        snapshot.Branches.Add(planned);
        plan.Messages.Add(
            $"Work location '{planned.Name}' does not exist " +
            "and will be created automatically.");

        return planned;
    }

    private static DepartmentReference? ResolveDepartment(
        EmployeeBootstrapRowPlan plan,
        CompanyReference company,
        BootstrapSnapshot snapshot)
    {
        var byCode = string.IsNullOrWhiteSpace(
                plan.DepartmentCode)
            ? null
            : snapshot.Departments.FirstOrDefault(
                item => Same(
                    item.Code,
                    plan.DepartmentCode));

        if (byCode != null &&
            !Same(byCode.CompanyKey, company.Key))
        {
            plan.Errors.Add(
                $"DepartmentCode {plan.DepartmentCode} " +
                "belongs to another company.");
            return null;
        }

        var byName = snapshot.Departments
            .Where(item =>
                Same(item.CompanyKey, company.Key) &&
                Same(item.Name, plan.DepartmentName))
            .ToList();

        if (byCode != null &&
            !Same(byCode.Name, plan.DepartmentName))
        {
            plan.Errors.Add(
                $"DepartmentCode {plan.DepartmentCode} " +
                $"belongs to '{byCode.Name}', not " +
                $"'{plan.DepartmentName}'.");
            return null;
        }

        var selected = byCode ?? byName.FirstOrDefault();

        if (selected != null)
        {
            return selected;
        }

        var planned = new DepartmentReference(
            0,
            company.Key,
            plan.DepartmentName.Trim(),
            NormalizeCode(plan.DepartmentCode),
            true);

        snapshot.Departments.Add(planned);
        plan.Messages.Add(
            $"Department '{planned.Name}' does not exist " +
            "and will be created automatically.");

        return planned;
    }

    private static PositionReference? ResolvePosition(
        EmployeeBootstrapRowPlan plan,
        CompanyReference company,
        DepartmentReference? department,
        BootstrapSnapshot snapshot)
    {
        var byCode = string.IsNullOrWhiteSpace(
                plan.PositionCode)
            ? null
            : snapshot.Positions.FirstOrDefault(
                item =>
                    Same(item.CompanyKey, company.Key) &&
                    Same(item.Code, plan.PositionCode));

        var byName = snapshot.Positions
            .Where(item =>
                Same(item.CompanyKey, company.Key) &&
                Same(item.Name, plan.PositionName))
            .ToList();

        if (byCode != null &&
            !Same(byCode.Name, plan.PositionName))
        {
            plan.Errors.Add(
                $"PositionCode {plan.PositionCode} belongs to " +
                $"'{byCode.Name}', not '{plan.PositionName}'.");
            return null;
        }

        var selected = byCode ?? byName.FirstOrDefault();

        if (selected != null)
        {
            if (department != null &&
                selected.IsPlanned &&
                !string.IsNullOrWhiteSpace(
                    selected.DepartmentKey) &&
                !Same(
                    selected.DepartmentKey,
                    department.Key))
            {
                plan.Errors.Add(
                    $"Position '{plan.PositionName}' is used for " +
                    "more than one department in the same file. " +
                    "Use distinct position names.");
            }

            return selected;
        }

        var planned = new PositionReference(
            0,
            company.Key,
            department?.Key ?? string.Empty,
            plan.PositionName.Trim(),
            NormalizeCode(plan.PositionCode),
            true);

        snapshot.Positions.Add(planned);
        plan.Messages.Add(
            $"Position '{planned.Name}' does not exist " +
            "and will be created automatically.");

        return planned;
    }

    private async Task<BootstrapSnapshot> LoadSnapshotAsync()
    {
        var companies = await _dbContext.Companies
            .AsNoTracking()
            .Where(company => !company.IsDeleted)
            .Select(company => new CompanyReference(
                company.Id,
                company.Name,
                company.Code,
                false))
            .ToListAsync();

        var companyKeys = companies.ToDictionary(
            company => company.Id,
            company => company.Key);

        var branches = await _dbContext.Branches
            .AsNoTracking()
            .Where(branch => !branch.IsDeleted)
            .Select(branch => new
            {
                branch.Id,
                branch.CompanyId,
                branch.Name,
                branch.Code
            })
            .ToListAsync();

        var branchReferences = branches
            .Where(branch =>
                companyKeys.ContainsKey(branch.CompanyId))
            .Select(branch => new BranchReference(
                branch.Id,
                companyKeys[branch.CompanyId],
                branch.Name,
                branch.Code,
                false))
            .ToList();

        var departments = await _dbContext.Departments
            .AsNoTracking()
            .Where(department => !department.IsDeleted)
            .Select(department => new
            {
                department.Id,
                department.CompanyId,
                department.Name,
                department.Code
            })
            .ToListAsync();

        var departmentReferences = departments
            .Where(department =>
                companyKeys.ContainsKey(department.CompanyId))
            .Select(department => new DepartmentReference(
                department.Id,
                companyKeys[department.CompanyId],
                department.Name,
                department.Code,
                false))
            .ToList();

        var positions = await LoadPositionsAsync();

        var positionReferences = positions
            .Where(position =>
                companyKeys.ContainsKey(position.CompanyId))
            .Select(position => new PositionReference(
                position.Id,
                companyKeys[position.CompanyId],
                position.DepartmentId.HasValue
                    ? $"id:{position.DepartmentId.Value}"
                    : string.Empty,
                position.Name,
                position.Code,
                false))
            .ToList();

        var employeeNumbers = await _dbContext.Employees
            .AsNoTracking()
            .Where(employee => !employee.IsDeleted)
            .Select(employee => employee.EmployeeNo)
            .ToListAsync();

        return new BootstrapSnapshot(
            companies,
            branchReferences,
            departmentReferences,
            positionReferences,
            employeeNumbers
                .Select(NormalizeKey)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase));
    }

    private async Task<Company> EnsureCompanyAsync(
        EmployeeBootstrapRowPlan row,
        List<Company> companies,
        HashSet<string> usedCodes,
        BootstrapStructureCounts counts)
    {
        var company = ResolveExistingCompany(
            companies,
            row.CompanyName,
            row.CompanyCode);

        if (company != null)
        {
            company.IsActive = true;
            return company;
        }

        var code = SelectCode(
            row.CompanyCode,
            "COMP",
            usedCodes);

        company = new Company
        {
            Name = row.CompanyName.Trim(),
            Code = code,
            IsActive = true
        };

        await _dbContext.Companies.AddAsync(company);
        await _dbContext.SaveChangesAsync();

        companies.Add(company);
        usedCodes.Add(NormalizeCode(code));
        counts.Companies++;

        return company;
    }

    private async Task<Branch> EnsureBranchAsync(
        EmployeeBootstrapRowPlan row,
        Company company,
        List<Branch> branches,
        HashSet<string> usedCodes,
        BootstrapStructureCounts counts)
    {
        var branch = branches.FirstOrDefault(item =>
            item.CompanyId == company.Id &&
            (
                Same(item.Name, row.WorkLocationName) ||
                (
                    !string.IsNullOrWhiteSpace(
                        row.WorkLocationCode) &&
                    Same(
                        item.Code,
                        row.WorkLocationCode)
                )
            ));

        if (branch != null)
        {
            branch.IsActive = true;
            return branch;
        }

        var code = SelectCode(
            row.WorkLocationCode,
            "BR",
            usedCodes);

        branch = new Branch
        {
            CompanyId = company.Id,
            Name = row.WorkLocationName.Trim(),
            Code = code,
            IsActive = true
        };

        await _dbContext.Branches.AddAsync(branch);
        await _dbContext.SaveChangesAsync();

        branches.Add(branch);
        usedCodes.Add(NormalizeCode(code));
        counts.Branches++;

        return branch;
    }

    private async Task<Department> EnsureDepartmentAsync(
        EmployeeBootstrapRowPlan row,
        Company company,
        List<Department> departments,
        HashSet<string> usedCodes,
        BootstrapStructureCounts counts)
    {
        var department = departments.FirstOrDefault(item =>
            item.CompanyId == company.Id &&
            (
                Same(item.Name, row.DepartmentName) ||
                (
                    !string.IsNullOrWhiteSpace(
                        row.DepartmentCode) &&
                    Same(
                        item.Code,
                        row.DepartmentCode)
                )
            ));

        if (department != null)
        {
            department.IsActive = true;
            return department;
        }

        var code = SelectCode(
            row.DepartmentCode,
            "DEP",
            usedCodes);

        department = new Department
        {
            CompanyId = company.Id,
            BranchId = null,
            Name = row.DepartmentName.Trim(),
            Code = code,
            IsActive = true
        };

        await _dbContext.Departments.AddAsync(department);
        await _dbContext.SaveChangesAsync();

        departments.Add(department);
        usedCodes.Add(NormalizeCode(code));
        counts.Departments++;

        return department;
    }

    private async Task<PositionRow> EnsurePositionAsync(
        EmployeeBootstrapRowPlan row,
        Company company,
        Department department,
        List<PositionRow> positions,
        HashSet<string> usedCodes,
        BootstrapStructureCounts counts)
    {
        var position = positions.FirstOrDefault(item =>
            item.CompanyId == company.Id &&
            (
                Same(item.Name, row.PositionName) ||
                (
                    !string.IsNullOrWhiteSpace(
                        row.PositionCode) &&
                    Same(
                        item.Code,
                        row.PositionCode)
                )
            ));

        if (position != null)
        {
            await ExecuteSqlAsync(
                """
                UPDATE dbo.HrJobPositions
                SET IsActive = 1,
                    DepartmentId =
                        CASE
                            WHEN DepartmentId IS NULL
                            THEN @DepartmentId
                            ELSE DepartmentId
                        END,
                    UpdatedAt = SYSDATETIME()
                WHERE Id = @Id;
                """,
                command =>
                {
                    AddParameter(
                        command,
                        "@DepartmentId",
                        department.Id);
                    AddParameter(
                        command,
                        "@Id",
                        position.Id);
                });

            return position;
        }

        var code = SelectCode(
            row.PositionCode,
            "POS",
            usedCodes);

        var id = await ExecuteScalarIntAsync(
            """
            INSERT INTO dbo.HrJobPositions
            (
                CompanyId,
                ArabicName,
                JobCode,
                DepartmentId,
                IsActive,
                CreatedAt
            )
            VALUES
            (
                @CompanyId,
                @ArabicName,
                @JobCode,
                @DepartmentId,
                1,
                SYSDATETIME()
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            command =>
            {
                AddParameter(
                    command,
                    "@CompanyId",
                    company.Id);
                AddParameter(
                    command,
                    "@ArabicName",
                    row.PositionName.Trim());
                AddParameter(
                    command,
                    "@JobCode",
                    code);
                AddParameter(
                    command,
                    "@DepartmentId",
                    department.Id);
            });

        position = new PositionRow(
            id,
            company.Id,
            department.Id,
            row.PositionName.Trim(),
            code);

        positions.Add(position);
        usedCodes.Add(NormalizeCode(code));
        counts.Positions++;

        return position;
    }

    private async Task UpdateExtendedEmployeeColumnsAsync(
        int employeeId,
        EmployeeBootstrapRowPlan row,
        bool created)
    {
        var hasContractType =
            !string.IsNullOrWhiteSpace(row.ContractType);
        var hasContractEndDate =
            row.ContractEndDate.HasValue;
        var hasEmploymentStatus =
            !string.IsNullOrWhiteSpace(
                row.EmploymentStatus);

        await ExecuteSqlAsync(
            """
            UPDATE dbo.Employees
            SET ContractType =
                    CASE
                        WHEN @HasContractType = 1
                        THEN @ContractType
                        ELSE ContractType
                    END,
                ContractEndDate =
                    CASE
                        WHEN @HasContractEndDate = 1
                        THEN @ContractEndDate
                        ELSE ContractEndDate
                    END,
                EmploymentStatus =
                    CASE
                        WHEN @HasEmploymentStatus = 1
                        THEN @EmploymentStatus
                        WHEN @Created = 1
                        THEN N'Active'
                        ELSE EmploymentStatus
                    END,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @EmployeeId;
            """,
            command =>
            {
                AddParameter(
                    command,
                    "@HasContractType",
                    hasContractType);
                AddParameter(
                    command,
                    "@ContractType",
                    NullIfBlank(row.ContractType));
                AddParameter(
                    command,
                    "@HasContractEndDate",
                    hasContractEndDate);
                AddParameter(
                    command,
                    "@ContractEndDate",
                    row.ContractEndDate);
                AddParameter(
                    command,
                    "@HasEmploymentStatus",
                    hasEmploymentStatus);
                AddParameter(
                    command,
                    "@EmploymentStatus",
                    NullIfBlank(
                        row.EmploymentStatus));
                AddParameter(
                    command,
                    "@Created",
                    created);
                AddParameter(
                    command,
                    "@EmployeeId",
                    employeeId);
            });
    }

    /// <summary>
    /// يكتب الراتب الأساسي بملف الموظف المالي (<c>EmployeeFinancialInfos</c>).
    ///
    /// خليّة فارغة ⟹ **لا تغيير** — الاستيراد لا يمسح راتباً مُدخلاً بالنظام
    /// لمجرد أن العمود تُرك فارغاً بالملف، وهذا يطابق سلوك بقية الأعمدة الاختيارية.
    /// idempotent: تحديث للصفّ القائم وإدراج لغير الموجود.
    /// </summary>
    private async Task SaveBasicSalaryAsync(
        int employeeId,
        decimal? basicSalary)
    {
        if (basicSalary is null || employeeId <= 0)
        {
            return;
        }

        await EmployeeFinancialInfoSchema.EnsureAsync(_dbContext);

        await ExecuteSqlAsync(
            """
            UPDATE dbo.EmployeeFinancialInfos
            SET BasicSalary = @BasicSalary,
                UpdatedAt = SYSUTCDATETIME()
            WHERE EmployeeId = @EmployeeId
              AND ISNULL(IsDeleted, 0) = 0;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.EmployeeFinancialInfos
                    (EmployeeId, BasicSalary, CreatedAt, IsDeleted)
                VALUES
                    (@EmployeeId, @BasicSalary, SYSUTCDATETIME(), 0);
            END;
            """,
            command =>
            {
                AddParameter(
                    command,
                    "@EmployeeId",
                    employeeId);
                AddParameter(
                    command,
                    "@BasicSalary",
                    basicSalary.Value);
            });
    }

    private async Task<int> SaveDynamicFieldsAsync(
        int employeeId,
        Dictionary<string, string> values,
        List<DynamicFieldDefinition> definitions)
    {
        var saved = 0;

        foreach (var pair in values)
        {
            if (!TryResolveDynamicHeader(
                    pair.Key,
                    definitions,
                    out var definition))
            {
                continue;
            }

            var value = pair.Value?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            await ExecuteSqlAsync(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.EmployeeCustomFields
                    WHERE EmployeeId = @EmployeeId
                      AND FieldKey = @FieldKey
                )
                BEGIN
                    UPDATE dbo.EmployeeCustomFields
                    SET FieldLabel = @FieldLabel,
                        FieldValue = @FieldValue,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE EmployeeId = @EmployeeId
                      AND FieldKey = @FieldKey;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.EmployeeCustomFields
                    (
                        EmployeeId,
                        FieldKey,
                        FieldLabel,
                        FieldValue,
                        UpdatedAt
                    )
                    VALUES
                    (
                        @EmployeeId,
                        @FieldKey,
                        @FieldLabel,
                        @FieldValue,
                        SYSUTCDATETIME()
                    );
                END;
                """,
                command =>
                {
                    AddParameter(
                        command,
                        "@EmployeeId",
                        employeeId);
                    AddParameter(
                        command,
                        "@FieldKey",
                        definition.FieldKey);
                    AddParameter(
                        command,
                        "@FieldLabel",
                        definition.FieldLabel);
                    AddParameter(
                        command,
                        "@FieldValue",
                        value);
                });

            saved++;
        }

        return saved;
    }

    private async Task<TemplateReferenceData> LoadTemplateReferencesAsync()
    {
        var companies = await _dbContext.Companies
            .AsNoTracking()
            .Where(company =>
                !company.IsDeleted &&
                company.IsActive)
            .OrderBy(company => company.Name)
            .Select(company => new TemplateReferenceRow(
                company.Name,
                company.Code,
                string.Empty))
            .ToListAsync();

        var branches = await (
                from branch in _dbContext.Branches.AsNoTracking()
                join company in _dbContext.Companies.AsNoTracking()
                    on branch.CompanyId equals company.Id
                where
                    !branch.IsDeleted &&
                    branch.IsActive &&
                    !company.IsDeleted
                orderby company.Name, branch.Name
                select new TemplateReferenceRow(
                    branch.Name,
                    branch.Code,
                    company.Name))
            .ToListAsync();

        var departments = await (
                from department in _dbContext.Departments.AsNoTracking()
                join company in _dbContext.Companies.AsNoTracking()
                    on department.CompanyId equals company.Id
                where
                    !department.IsDeleted &&
                    department.IsActive &&
                    !company.IsDeleted
                orderby company.Name, department.Name
                select new TemplateReferenceRow(
                    department.Name,
                    department.Code,
                    company.Name))
            .ToListAsync();

        var positions = await LoadPositionsAsync();

        var companyNames = await _dbContext.Companies
            .AsNoTracking()
            .Where(company => !company.IsDeleted)
            .ToDictionaryAsync(
                company => company.Id,
                company => company.Name);

        var positionReferences = positions
            .Where(position =>
                companyNames.ContainsKey(
                    position.CompanyId))
            .OrderBy(position =>
                companyNames[position.CompanyId])
            .ThenBy(position => position.Name)
            .Select(position => new TemplateReferenceRow(
                position.Name,
                position.Code,
                companyNames[position.CompanyId]))
            .ToList();

        return new TemplateReferenceData(
            companies,
            branches,
            departments,
            positionReferences);
    }

    /// <summary>
    /// يقرأ الموظفين الحاليين بترتيب أعمدة القالب نفسه — كل صفٍّ جاهزٌ
    /// لإعادة الاستيراد كما هو. محدودٌ بـ<see cref="MaxRows"/> لأن الاستيراد
    /// يرفض ما زاد عنها.
    /// </summary>
    /// <summary>
    /// P0-4 — يحسم معرّفات الموظفين المسموح بها بتقاطع نطاق القواعد مع نطاق أدوار
    /// الوصول (نفس <see cref="PeopleDataScope.AllowsEmployee"/> لصفحة السرد)، لتمريرها
    /// كـ <c>AllowedEmployeeIds</c> عند تصدير البيانات فلا يتسرّب موظفو شركةٍ أخرى.
    /// </summary>
    public async Task<IReadOnlySet<int>> ResolveAllowedEmployeeIdsAsync(
        PeopleDataScope directoryScope,
        PeopleDataScope accessRoleScope)
    {
        var rows = await QueryAsync(
            """
            SELECT e.Id, b.CompanyId, e.BranchId, ISNULL(e.DepartmentId, 0) AS DepartmentId
            FROM dbo.Employees e
            INNER JOIN dbo.Branches b ON b.Id = e.BranchId
            WHERE e.IsDeleted = 0;
            """,
            command => { },
            reader => new
            {
                Id = GetInt32(reader, "Id"),
                CompanyId = GetInt32(reader, "CompanyId"),
                BranchId = GetInt32(reader, "BranchId"),
                DepartmentId = GetInt32(reader, "DepartmentId")
            });

        var allowed = new HashSet<int>();
        foreach (var r in rows)
        {
            if (directoryScope.AllowsEmployee(r.Id, r.CompanyId, r.BranchId, r.DepartmentId) &&
                accessRoleScope.AllowsEmployee(r.Id, r.CompanyId, r.BranchId, r.DepartmentId))
            {
                allowed.Add(r.Id);
            }
        }

        return allowed;
    }

    private async Task<List<List<string>>> LoadTemplateDataRowsAsync(
        IReadOnlyList<EmployeeTemplateColumn> columns,
        IReadOnlyDictionary<string, string> dynamicHeadersByKey,
        TemplateExportScope? exportScope = null)
    {
        var positions = await LoadPositionsAsync();
        var positionCodes = positions
            .GroupBy(position => position.Id)
            .ToDictionary(
                group => group.Key,
                group => group.First().Code);

        // P0-4 — قصر الصفوف على نطاق المستخدم: null = غير مقيَّد؛ مجموعةٌ فارغة =
        // لا صفوف. القيم أعداد صحيحة (معرّفات) فلا حقن. وعمود الراتب يُفرَّغ لمن لا
        // يملك ViewCompensation فلا يتسرّب التعويض عبر التصدير.
        var allowedIds = exportScope?.AllowedEmployeeIds;
        if (allowedIds is { Count: 0 })
        {
            return new List<List<string>>();
        }

        var scopeFilter = allowedIds is null
            ? string.Empty
            : $" AND e.Id IN ({string.Join(",", allowedIds)})";

        var includeSalary = exportScope?.IncludeSalary ?? true;
        var basicSalaryExpr = includeSalary
            ? """
                ISNULL(CONVERT(varchar(30), (
                    SELECT TOP 1 fi.BasicSalary
                    FROM dbo.EmployeeFinancialInfos AS fi
                    WHERE fi.EmployeeId = e.Id
                      AND ISNULL(fi.IsDeleted, 0) = 0
                    ORDER BY fi.Id DESC)), N'')
              """
            : "N''";

        var employees = await QueryAsync(
            $"""
            SELECT TOP (@MaxRows)
                e.Id,
                b.CompanyId,
                e.BranchId,
                ISNULL(e.DepartmentId, 0) AS DepartmentId,
                e.PositionId,
                e.EmployeeNo,
                e.FullName,
                ISNULL(c.Name, N'') AS CompanyName,
                ISNULL(c.Code, N'') AS CompanyCode,
                ISNULL(b.Name, N'') AS WorkLocationName,
                ISNULL(b.Code, N'') AS WorkLocationCode,
                ISNULL(d.Name, N'') AS DepartmentName,
                ISNULL(d.Code, N'') AS DepartmentCode,
                ISNULL(e.Position, N'') AS PositionName,
                CONVERT(varchar(10), e.HireDate, 23) AS HireDate,
                CONVERT(varchar(10), e.BirthDate, 23) AS BirthDate,
                CONVERT(varchar(10), e.ContractEndDate, 23)
                    AS ContractEndDate,
                ISNULL(e.NationalId, N'') AS NationalId,
                ISNULL(e.Phone, N'') AS Phone,
                ISNULL(e.Email, N'') AS Email,
                ISNULL(e.Gender, N'') AS Gender,
                ISNULL(e.MaritalStatus, N'') AS MaritalStatus,
                ISNULL(e.Nationality, N'') AS Nationality,
                ISNULL(e.Country, N'') AS Country,
                ISNULL(e.ContractType, N'') AS ContractType,
                ISNULL(e.EmploymentStatus, N'') AS EmploymentStatus,
                ISNULL(e.FirstName, N'') AS FirstName,
                ISNULL(e.SecondName, N'') AS SecondName,
                ISNULL(e.ThirdName, N'') AS ThirdName,
                ISNULL(e.LastName, N'') AS LastName,
                ISNULL(e.FirstNameEn, N'') AS FirstNameEn,
                ISNULL(e.SecondNameEn, N'') AS SecondNameEn,
                ISNULL(e.ThirdNameEn, N'') AS ThirdNameEn,
                ISNULL(e.LastNameEn, N'') AS LastNameEn,
                CASE WHEN e.IsCitizen = 1 THEN 'true' ELSE 'false' END AS IsCitizen,
                ISNULL(e.PassportNo, N'') AS PassportNo,
                ISNULL(e.SponsorName, N'') AS SponsorName,
                ISNULL(e.Religion, N'') AS Religion,
                ISNULL(e.MotherCountry, N'') AS MotherCountry,
                ISNULL(e.MotherCity, N'') AS MotherCity,
                CONVERT(varchar(10), e.JoiningDate, 23) AS JoiningDate,
                ISNULL(e.WorkType, N'') AS WorkType,
                ISNULL(e.JobGrade, N'') AS JobGrade,
                ISNULL(e.PhoneExtension, N'') AS PhoneExtension,
                ISNULL(e.PersonalEmail, N'') AS PersonalEmail,
                {basicSalaryExpr} AS BasicSalary,
                CASE
                    WHEN e.IsActive = 1 THEN 'true'
                    ELSE 'false'
                END AS IsActive,
                ISNULL(m.EmployeeNo, N'') AS DirectManagerEmployeeNo
            FROM dbo.Employees e
            LEFT JOIN dbo.Branches b
                ON b.Id = e.BranchId
            LEFT JOIN dbo.Companies c
                ON c.Id = b.CompanyId
            LEFT JOIN dbo.Departments d
                ON d.Id = e.DepartmentId
            LEFT JOIN dbo.Employees m
                ON m.Id = e.DirectManagerId
            WHERE e.IsDeleted = 0{scopeFilter}
            ORDER BY e.EmployeeNo;
            """,
            command => AddParameter(command, "@MaxRows", MaxRows),
            reader => new TemplateEmployeeRow(
                GetInt32(reader, "Id"),
                GetInt32(reader, "CompanyId"),
                GetInt32(reader, "BranchId"),
                GetInt32(reader, "DepartmentId"),
                GetNullableInt32(reader, "PositionId"),
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["EmployeeNo"] =
                        GetString(reader, "EmployeeNo"),
                    ["FullName"] =
                        GetString(reader, "FullName"),
                    ["CompanyName"] =
                        GetString(reader, "CompanyName"),
                    ["CompanyCode"] =
                        GetString(reader, "CompanyCode"),
                    ["WorkLocationName"] =
                        GetString(reader, "WorkLocationName"),
                    ["WorkLocationCode"] =
                        GetString(reader, "WorkLocationCode"),
                    ["DepartmentName"] =
                        GetString(reader, "DepartmentName"),
                    ["DepartmentCode"] =
                        GetString(reader, "DepartmentCode"),
                    ["PositionName"] =
                        GetString(reader, "PositionName"),
                    ["HireDate"] =
                        GetString(reader, "HireDate"),
                    ["BirthDate"] =
                        GetString(reader, "BirthDate"),
                    ["ContractEndDate"] =
                        GetString(reader, "ContractEndDate"),
                    ["NationalId"] =
                        GetString(reader, "NationalId"),
                    ["Phone"] = GetString(reader, "Phone"),
                    ["Email"] = GetString(reader, "Email"),
                    ["Gender"] = GetString(reader, "Gender"),
                    ["MaritalStatus"] =
                        GetString(reader, "MaritalStatus"),
                    ["Nationality"] =
                        GetString(reader, "Nationality"),
                    ["Country"] = GetString(reader, "Country"),
                    ["ContractType"] =
                        GetString(reader, "ContractType"),
                    ["EmploymentStatus"] =
                        GetString(reader, "EmploymentStatus"),
                    ["IsActive"] =
                        GetString(reader, "IsActive"),
                    ["DirectManagerEmployeeNo"] =
                        GetString(
                            reader,
                            "DirectManagerEmployeeNo"),
                    ["BasicSalary"] =
                        GetString(reader, "BasicSalary"),
                    ["FirstName"] = GetString(reader, "FirstName"),
                    ["SecondName"] = GetString(reader, "SecondName"),
                    ["ThirdName"] = GetString(reader, "ThirdName"),
                    ["LastName"] = GetString(reader, "LastName"),
                    ["FirstNameEn"] = GetString(reader, "FirstNameEn"),
                    ["SecondNameEn"] = GetString(reader, "SecondNameEn"),
                    ["ThirdNameEn"] = GetString(reader, "ThirdNameEn"),
                    ["LastNameEn"] = GetString(reader, "LastNameEn"),
                    ["IsCitizen"] = GetString(reader, "IsCitizen"),
                    ["PassportNo"] = GetString(reader, "PassportNo"),
                    ["SponsorName"] = GetString(reader, "SponsorName"),
                    ["Religion"] = GetString(reader, "Religion"),
                    ["MotherCountry"] = GetString(reader, "MotherCountry"),
                    ["MotherCity"] = GetString(reader, "MotherCity"),
                    ["JoiningDate"] = GetString(reader, "JoiningDate"),
                    ["WorkType"] = GetString(reader, "WorkType"),
                    ["JobGrade"] = GetString(reader, "JobGrade"),
                    ["PhoneExtension"] = GetString(reader, "PhoneExtension"),
                    ["PersonalEmail"] = GetString(reader, "PersonalEmail")
                }));

        if (employees.Count == 0)
        {
            return new List<List<string>>();
        }

        if (dynamicHeadersByKey.Count > 0)
        {
            await FillCustomFieldValuesAsync(
                employees,
                dynamicHeadersByKey);
        }

        await FillLocalizedTemplateValuesAsync(employees, columns);

        var rows = new List<List<string>>(employees.Count);

        foreach (var employee in employees)
        {
            if (employee.PositionId.HasValue &&
                positionCodes.TryGetValue(
                    employee.PositionId.Value,
                    out var positionCode))
            {
                employee.Values["PositionCode"] = positionCode;
            }

            rows.Add(
                columns
                    .Select(column =>
                        employee.Values.TryGetValue(
                            column.Name,
                            out var value)
                            ? value
                            : string.Empty)
                    .ToList());
        }

        return rows;
    }

    private async Task FillCustomFieldValuesAsync(
        List<TemplateEmployeeRow> employees,
        IReadOnlyDictionary<string, string> dynamicHeadersByKey)
    {
        var employeesById = employees
            .GroupBy(employee => employee.Id)
            .ToDictionary(
                group => group.Key,
                group => group.First());

        var values = await QueryAsync(
            """
            SELECT
                EmployeeId,
                FieldKey,
                ISNULL(FieldValue, N'') AS FieldValue
            FROM dbo.EmployeeCustomFields;
            """,
            command => { },
            reader => (
                EmployeeId: GetInt32(reader, "EmployeeId"),
                FieldKey: GetString(reader, "FieldKey"),
                FieldValue: GetString(reader, "FieldValue")));

        foreach (var value in values)
        {
            if (!employeesById.TryGetValue(
                    value.EmployeeId,
                    out var employee) ||
                !dynamicHeadersByKey.TryGetValue(
                    value.FieldKey,
                    out var header))
            {
                continue;
            }

            employee.Values[header] = value.FieldValue;
        }
    }

    private async Task FillLocalizedTemplateValuesAsync(
        List<TemplateEmployeeRow> employees,
        IReadOnlyList<EmployeeTemplateColumn> columns)
    {
        var localizedColumns = new List<LocalizedTemplateColumn>();
        foreach (var column in columns)
        {
            if (TryParseLocalizedHeader(
                    column.Name,
                    out var fieldName,
                    out var cultureCode))
            {
                localizedColumns.Add(new LocalizedTemplateColumn(
                    column.Name,
                    fieldName,
                    cultureCode));
            }
        }

        if (localizedColumns.Count == 0 || employees.Count == 0)
        {
            return;
        }

        var companyIds = employees.Select(item => item.CompanyId).Distinct().ToArray();
        var defaults = await _dbContext.CompanyLanguages
            .AsNoTracking()
            .Where(item =>
                companyIds.Contains(item.CompanyId) &&
                item.IsActive &&
                item.IsDefault &&
                !item.IsDeleted)
            .ToDictionaryAsync(
                item => item.CompanyId,
                item => NormalizeCultureCode(item.CultureCode));

        var translations = await _dbContext.LocalizedEntityValues
            .AsNoTracking()
            .Where(item =>
                companyIds.Contains(item.CompanyId) &&
                !item.IsDeleted &&
                (item.EntityType == "Employee" ||
                 item.EntityType == "Company" ||
                 item.EntityType == "Branch" ||
                 item.EntityType == "Department" ||
                 item.EntityType == "Position"))
            .ToListAsync();

        foreach (var employee in employees)
        {
            foreach (var column in localizedColumns)
            {
                var (entityType, entityId, localizedField, fallbackKey) = column.FieldName switch
                {
                    "CompanyName" => ("Company", employee.CompanyId, "Name", "CompanyName"),
                    "WorkLocationName" => ("Branch", employee.BranchId, "Name", "WorkLocationName"),
                    "DepartmentName" => ("Department", employee.DepartmentId, "Name", "DepartmentName"),
                    "PositionName" => ("Position", employee.PositionId ?? 0, "Name", "PositionName"),
                    _ => ("Employee", employee.Id, column.FieldName, column.FieldName)
                };

                var value = translations.FirstOrDefault(item =>
                    item.CompanyId == employee.CompanyId &&
                    item.EntityType == entityType &&
                    item.EntityId == entityId &&
                    item.FieldName.Equals(localizedField, StringComparison.OrdinalIgnoreCase) &&
                    item.CultureCode.Equals(column.CultureCode, StringComparison.OrdinalIgnoreCase))?.Value;

                if (string.IsNullOrWhiteSpace(value) &&
                    defaults.TryGetValue(employee.CompanyId, out var defaultCulture) &&
                    defaultCulture.Equals(column.CultureCode, StringComparison.OrdinalIgnoreCase))
                {
                    employee.Values.TryGetValue(fallbackKey, out value);
                }

                if (string.IsNullOrWhiteSpace(value) &&
                    column.CultureCode.StartsWith("en", StringComparison.OrdinalIgnoreCase) &&
                    employee.Values.TryGetValue(fallbackKey + "En", out var legacyEnglish))
                {
                    value = legacyEnglish;
                }

                employee.Values[column.Name] = value ?? string.Empty;
            }
        }
    }

    private async Task<List<DynamicFieldDefinition>>
        LoadDynamicFieldDefinitionsAsync()
    {
        await EmployeeProfileDynamicFields.EnsureSchemaAsync(
            _dbContext);

        return await QueryAsync(
            """
            SELECT
                FieldKey,
                FieldLabel,
                IsRequired,
                SectionKey,
                SortOrder
            FROM dbo.EmployeeProfileFieldDefinitions
            WHERE IsActive = 1
            ORDER BY
                CASE SectionKey
                    WHEN 'basic' THEN 10
                    WHEN 'personal' THEN 20
                    WHEN 'job' THEN 30
                    WHEN 'financial' THEN 40
                    WHEN 'additional' THEN 50
                    ELSE 99
                END,
                SortOrder,
                Id;
            """,
            command => { },
            reader => new DynamicFieldDefinition(
                GetString(reader, "FieldKey"),
                GetString(reader, "FieldLabel"),
                GetBoolean(reader, "IsRequired"),
                GetString(reader, "SectionKey"),
                GetInt32(reader, "SortOrder")));
    }

    private async Task<List<PositionRow>> LoadPositionsAsync()
    {
        return await QueryAsync(
            """
            IF OBJECT_ID(N'dbo.HrJobPositions', N'U') IS NULL
            BEGIN
                SELECT
                    CAST(0 AS int) AS Id,
                    CAST(0 AS int) AS CompanyId,
                    CAST(NULL AS int) AS DepartmentId,
                    CAST(N'' AS nvarchar(400)) AS ArabicName,
                    CAST(N'' AS nvarchar(160)) AS JobCode
                WHERE 1 = 0;
            END
            ELSE
            BEGIN
                SELECT
                    Id,
                    CompanyId,
                    DepartmentId,
                    ArabicName,
                    ISNULL(JobCode, N'') AS JobCode
                FROM dbo.HrJobPositions
                WHERE IsActive = 1;
            END;
            """,
            command => { },
            reader => new PositionRow(
                GetInt32(reader, "Id"),
                GetInt32(reader, "CompanyId"),
                GetNullableInt32(
                    reader,
                    "DepartmentId"),
                GetString(reader, "ArabicName"),
                GetString(reader, "JobCode")));
    }

    private static MasterDataImportPreviewRowViewModel ToPreviewRow(
        EmployeeBootstrapRowPlan row)
    {
        var messages = row.CanImport
            ? row.Messages
            : row.Errors;

        return new MasterDataImportPreviewRowViewModel
        {
            RowNumber = row.RowNumber,
            Key = row.EmployeeNo,
            Action = row.CanImport
                ? row.EmployeeAction
                : "-",
            Status = row.CanImport
                ? "Ready"
                : "Error",
            Message = string.Join(" ", messages),
            CanImport = row.CanImport,
            Values = row.Values
        };
    }

    private static bool HasInputData(ParsedImportRow row)
    {
        foreach (var pair in row.Values)
        {
            if (IsReferenceHeader(pair.Key))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReferenceHeader(string header)
    {
        return NormalizeHeader(header)
            .StartsWith(
                "ref",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateHeaders(
        IReadOnlyList<string> headers)
    {
        var normalized = headers
            .Select(NormalizeHeader)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        bool HasAny(params string[] names)
        {
            return names.Any(name =>
                normalized.Contains(NormalizeHeader(name)) ||
                headers.Any(header =>
                    TryParseLocalizedHeader(header, out var fieldName, out _) &&
                    fieldName.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }

        var missing = new List<string>();

        if (!HasAny("EmployeeNo"))
        {
            missing.Add("EmployeeNo");
        }

        if (!HasAny("FullName") &&
            !(HasAny("FirstName") && HasAny("LastName")))
        {
            missing.Add("FirstName/LastName");
        }

        if (!HasAny("CompanyName", "CompanyCode"))
        {
            missing.Add("CompanyName");
        }

        if (!HasAny(
                "WorkLocationName",
                "WorkLocationCode",
                "BranchName",
                "BranchCode",
                "WorkLocation"))
        {
            missing.Add("WorkLocationName");
        }

        if (!HasAny(
                "DepartmentName",
                "DepartmentCode"))
        {
            missing.Add("DepartmentName");
        }

        if (!HasAny("PositionName", "Position"))
        {
            missing.Add("PositionName");
        }

        if (!HasAny("HireDate"))
        {
            missing.Add("HireDate");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Missing required columns: " +
                string.Join(", ", missing));
        }
    }

    private static ParsedImportFile ReadFile(string filePath)
    {
        var extension = Path
            .GetExtension(filePath)
            .ToLowerInvariant();

        return extension switch
        {
            ".xlsx" => ReadXlsx(filePath),
            ".csv" => ReadCsv(filePath),
            _ => throw new InvalidOperationException(
                "Unsupported file type. Upload .xlsx or .csv.")
        };
    }

    private static ParsedImportFile ReadXlsx(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var sharedStrings =
            ReadSharedStrings(archive);
        var worksheetPath =
            GetFirstWorksheetPath(archive);
        var entry = archive.GetEntry(worksheetPath)
            ?? throw new InvalidOperationException(
                "Worksheet not found inside Excel file.");

        XNamespace ns =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var rows = document
            .Descendants(ns + "sheetData")
            .Elements(ns + "row")
            .ToList();

        if (rows.Count == 0)
        {
            return new ParsedImportFile();
        }

        var headerCells = ReadRowCells(
            rows[0],
            sharedStrings,
            ns);

        var headers = headerCells
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .ToList();

        var result = new ParsedImportFile
        {
            Headers = headers
        };

        foreach (var row in rows.Skip(1))
        {
            var rowNumber = int.TryParse(
                row.Attribute("r")?.Value,
                out var parsedRowNumber)
                ? parsedRowNumber
                : result.Rows.Count + 2;

            var cells = ReadRowCells(
                row,
                sharedStrings,
                ns);

            if (cells.Count == 0)
            {
                continue;
            }

            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            for (var index = 0;
                 index < headers.Count;
                 index++)
            {
                values[headers[index]] =
                    cells.TryGetValue(
                        index,
                        out var value)
                        ? value
                        : string.Empty;
            }

            result.Rows.Add(
                new ParsedImportRow(
                    rowNumber,
                    values));
        }

        return result;
    }

    private static ParsedImportFile ReadCsv(string filePath)
    {
        var lines = File.ReadAllLines(
            filePath,
            Encoding.UTF8);

        if (lines.Length == 0)
        {
            return new ParsedImportFile();
        }

        var headers = SplitDelimitedLine(
            lines[0],
            ',');

        var result = new ParsedImportFile
        {
            Headers = headers
        };

        for (var index = 1;
             index < lines.Length;
             index++)
        {
            var cells = SplitDelimitedLine(
                lines[index],
                ',');

            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            for (var headerIndex = 0;
                 headerIndex < headers.Count;
                 headerIndex++)
            {
                values[headers[headerIndex]] =
                    headerIndex < cells.Count
                        ? cells[headerIndex]
                        : string.Empty;
            }

            result.Rows.Add(
                new ParsedImportRow(
                    index + 1,
                    values));
        }

        return result;
    }

    private static Dictionary<int, string> ReadRowCells(
        XElement row,
        IReadOnlyList<string> sharedStrings,
        XNamespace ns)
    {
        var values = new Dictionary<int, string>();

        foreach (var cell in row.Elements(ns + "c"))
        {
            var reference =
                cell.Attribute("r")?.Value ??
                string.Empty;
            var columnIndex =
                GetColumnIndex(reference);

            if (columnIndex < 0)
            {
                continue;
            }

            var type =
                cell.Attribute("t")?.Value;
            var rawValue =
                cell.Element(ns + "v")?.Value ??
                string.Empty;

            string value;

            if (type == "s" &&
                int.TryParse(
                    rawValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var sharedStringIndex) &&
                sharedStringIndex >= 0 &&
                sharedStringIndex <
                    sharedStrings.Count)
            {
                value =
                    sharedStrings[sharedStringIndex];
            }
            else if (type == "inlineStr")
            {
                value = string.Concat(
                    cell.Descendants(ns + "t")
                        .Select(text => text.Value));
            }
            else
            {
                value = rawValue;
            }

            values[columnIndex] =
                value?.Trim() ?? string.Empty;
        }

        return values;
    }

    private static List<string> ReadSharedStrings(
        ZipArchive archive)
    {
        var entry = archive.GetEntry(
            "xl/sharedStrings.xml");

        if (entry == null)
        {
            return new List<string>();
        }

        XNamespace ns =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        return document
            .Descendants(ns + "si")
            .Select(item => string.Concat(
                item.Descendants(ns + "t")
                    .Select(text => text.Value)))
            .ToList();
    }

    private static string GetFirstWorksheetPath(
        ZipArchive archive)
    {
        XNamespace mainNs =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookEntry = archive.GetEntry(
            "xl/workbook.xml")
            ?? throw new InvalidOperationException(
                "workbook.xml not found.");

        var relationshipsEntry = archive.GetEntry(
            "xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException(
                "workbook relationships not found.");

        using var workbookStream =
            workbookEntry.Open();
        var workbookDocument =
            XDocument.Load(workbookStream);

        var firstSheet = workbookDocument
            .Descendants(mainNs + "sheet")
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No sheets found in workbook.");

        var relationshipId =
            firstSheet.Attribute(relNs + "id")
                ?.Value;

        using var relationshipsStream =
            relationshipsEntry.Open();
        var relationshipsDocument =
            XDocument.Load(relationshipsStream);

        var relationship =
            relationshipsDocument
                .Descendants(
                    packageRelNs + "Relationship")
                .FirstOrDefault(item =>
                    item.Attribute("Id")?.Value ==
                    relationshipId)
            ?? throw new InvalidOperationException(
                "Worksheet relationship not found.");

        var target =
            relationship.Attribute("Target")?.Value
            ?? throw new InvalidOperationException(
                "Worksheet target not found.");

        if (target.StartsWith(
                "/",
                StringComparison.Ordinal))
        {
            return target.TrimStart('/');
        }

        return "xl/" + target.TrimStart('/');
    }

    private static int GetColumnIndex(
        string reference)
    {
        var letters = new string(
            reference
                .TakeWhile(char.IsLetter)
                .ToArray());

        if (string.IsNullOrWhiteSpace(letters))
        {
            return -1;
        }

        var index = 0;

        foreach (var letter in letters
                     .ToUpperInvariant())
        {
            index *= 26;
            index += letter - 'A' + 1;
        }

        return index - 1;
    }

    private static List<string> SplitDelimitedLine(
        string line,
        char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var index = 0;
             index < line.Length;
             index++)
        {
            var character = line[index];

            if (character == '"')
            {
                if (insideQuotes &&
                    index + 1 < line.Length &&
                    line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (
                character == delimiter &&
                !insideQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private static byte[] BuildWorkbook(
        IReadOnlyList<EmployeeTemplateColumn> dataColumns,
        TemplateReferenceData references,
        IReadOnlyList<IReadOnlyList<string>> dataRows)
    {
        var referenceColumns =
            BuildReferenceColumns(references);
        var firstReferenceColumn =
            dataColumns.Count + 3;
        var totalColumns =
            firstReferenceColumn +
            referenceColumns.Count - 1;
        var maxReferenceRows =
            referenceColumns.Count == 0
                ? 1
                : referenceColumns.Max(column =>
                    column.Values.Count) + 1;
        var maxRow = Math.Max(
            Math.Max(5000, maxReferenceRows),
            dataRows.Count + 1);

        using var memory = new MemoryStream();

        using (var archive = new ZipArchive(
                   memory,
                   ZipArchiveMode.Create,
                   true))
        {
            AddZipEntry(
                archive,
                "[Content_Types].xml",
                BuildContentTypesXml());
            AddZipEntry(
                archive,
                "_rels/.rels",
                BuildRootRelationshipsXml());
            AddZipEntry(
                archive,
                "xl/workbook.xml",
                BuildWorkbookXml());
            AddZipEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                BuildWorkbookRelationshipsXml());
            AddZipEntry(
                archive,
                "xl/styles.xml",
                BuildStylesXml());
            AddZipEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                BuildWorksheetXml(
                    dataColumns,
                    referenceColumns,
                    dataRows,
                    firstReferenceColumn,
                    totalColumns,
                    maxReferenceRows,
                    maxRow));
        }

        return memory.ToArray();
    }

    private static List<ReferenceColumn> BuildReferenceColumns(
        TemplateReferenceData references)
    {
        return new List<ReferenceColumn>
        {
            new(
                "Ref Company Name",
                DisplayName: "اسم الشركة المرجعي",
                Values: references.Companies
                    .Select(item => item.Name)
                    .ToList()),
            new(
                "Ref Company Code",
                DisplayName: "رمز الشركة المرجعي",
                Values: references.Companies
                    .Select(item => item.Code)
                    .ToList()),
            new(
                "Ref Work Location Name",
                DisplayName: "اسم موقع العمل المرجعي",
                Values: references.Branches
                    .Select(item => item.Name)
                    .ToList()),
            new(
                "Ref Work Location Code",
                DisplayName: "رمز موقع العمل المرجعي",
                Values: references.Branches
                    .Select(item => item.Code)
                    .ToList()),
            new(
                "Ref Work Location Company",
                DisplayName: "شركة موقع العمل المرجعية",
                Values: references.Branches
                    .Select(item => item.Company)
                    .ToList()),
            new(
                "Ref Department Name",
                DisplayName: "اسم القسم المرجعي",
                Values: references.Departments
                    .Select(item => item.Name)
                    .ToList()),
            new(
                "Ref Department Code",
                DisplayName: "رمز القسم المرجعي",
                Values: references.Departments
                    .Select(item => item.Code)
                    .ToList()),
            new(
                "Ref Department Company",
                DisplayName: "شركة القسم المرجعية",
                Values: references.Departments
                    .Select(item => item.Company)
                    .ToList()),
            new(
                "Ref Position Name",
                DisplayName: "اسم المسمى الوظيفي المرجعي",
                Values: references.Positions
                    .Select(item => item.Name)
                    .ToList()),
            new(
                "Ref Position Code",
                DisplayName: "رمز المسمى الوظيفي المرجعي",
                Values: references.Positions
                    .Select(item => item.Code)
                    .ToList()),
            new(
                "Ref Position Company",
                DisplayName: "شركة المسمى الوظيفي المرجعية",
                Values: references.Positions
                    .Select(item => item.Company)
                    .ToList())
        };
    }

    private static string BuildContentTypesXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>";
    }

    private static string BuildRootRelationshipsXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";
    }

    private static string BuildWorkbookXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Employee Information\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
            "</workbook>";
    }

    private static string BuildWorkbookRelationshipsXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";
    }

    private static string BuildStylesXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd\"/></numFmts>" +
            "<fonts count=\"5\">" +
            "<font><sz val=\"11\"/><name val=\"Aptos\"/></font>" +
            "<font><b/><color rgb=\"FF9F1D2D\"/><sz val=\"11\"/><name val=\"Aptos\"/></font>" +
            "<font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"11\"/><name val=\"Aptos\"/></font>" +
            "<font><b/><color rgb=\"FF7A4E00\"/><sz val=\"11\"/><name val=\"Aptos\"/></font>" +
            "<font><b/><color rgb=\"FF23364D\"/><sz val=\"11\"/><name val=\"Aptos\"/></font>" +
            "</fonts>" +
            "<fills count=\"6\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFDE2E2\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF0B3048\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFF1CC\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE8EEF5\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"2\">" +
            "<border><left/><right/><top/><bottom/><diagonal/></border>" +
            "<border>" +
            "<left style=\"thin\"><color rgb=\"FFB9C7D6\"/></left>" +
            "<right style=\"thin\"><color rgb=\"FFB9C7D6\"/></right>" +
            "<top style=\"thin\"><color rgb=\"FFB9C7D6\"/></top>" +
            "<bottom style=\"thin\"><color rgb=\"FFB9C7D6\"/></bottom>" +
            "<diagonal/>" +
            "</border>" +
            "</borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"7\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"5\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
            "</cellXfs>" +
            "</styleSheet>";
    }

    private static string BuildWorksheetXml(
        IReadOnlyList<EmployeeTemplateColumn> dataColumns,
        IReadOnlyList<ReferenceColumn> referenceColumns,
        IReadOnlyList<IReadOnlyList<string>> dataRows,
        int firstReferenceColumn,
        int totalColumns,
        int maxReferenceRows,
        int maxRow)
    {
        var builder = new StringBuilder();
        var dataEndColumn = dataColumns.Count;
        var lastBodyRow = Math.Max(
            maxReferenceRows,
            dataRows.Count + 1);
        var dataEndReference =
            GetCellReference(
                Math.Max(1, dataRows.Count + 1),
                dataEndColumn);
        var totalEndReference =
            GetCellReference(
                Math.Max(1, lastBodyRow),
                totalColumns);

        builder.Append(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append(
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        builder.Append("<dimension ref=\"A1:");
        builder.Append(totalEndReference);
        builder.Append("\"/>");
        builder.Append(
            "<sheetViews><sheetView workbookViewId=\"0\" rightToLeft=\"1\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        builder.Append(
            "<sheetFormatPr defaultRowHeight=\"20\"/>");
        builder.Append("<cols>");

        for (var index = 0;
             index < dataColumns.Count;
             index++)
        {
            builder.Append("<col min=\"");
            builder.Append(index + 1);
            builder.Append("\" max=\"");
            builder.Append(index + 1);
            builder.Append("\" width=\"");
            builder.Append(
                dataColumns[index].Width.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append("\" style=\"");
            builder.Append(
                dataColumns[index].Kind ==
                EmployeeTemplateColumnKind.Date
                    ? 6
                    : 5);
            builder.Append("\" customWidth=\"1\"/>");
        }

        builder.Append("<col min=\"");
        builder.Append(firstReferenceColumn);
        builder.Append("\" max=\"");
        builder.Append(totalColumns);
        builder.Append(
            "\" width=\"24\" style=\"5\" customWidth=\"1\"/>");
        builder.Append("</cols>");
        builder.Append("<sheetData>");
        builder.Append("<row r=\"1\" ht=\"34\" customHeight=\"1\">");

        for (var index = 0;
             index < dataColumns.Count;
             index++)
        {
            var column = dataColumns[index];
            var style = column.Kind ==
                EmployeeTemplateColumnKind.Custom
                ? 3
                : column.Required
                    ? 1
                    : 2;
            var header = column.Required
                ? column.Header + " *"
                : column.Header;

            builder.Append(
                BuildInlineCell(
                    1,
                    index + 1,
                    header,
                    style));
        }

        for (var index = 0;
             index < referenceColumns.Count;
             index++)
        {
            builder.Append(
                BuildInlineCell(
                    1,
                    firstReferenceColumn + index,
                    referenceColumns[index].Caption,
                    4));
        }

        builder.Append("</row>");

        for (var row = 2;
             row <= lastBodyRow;
             row++)
        {
            builder.Append("<row r=\"");
            builder.Append(row);
            builder.Append("\">");

            var dataRowIndex = row - 2;

            if (dataRowIndex < dataRows.Count)
            {
                var dataRow = dataRows[dataRowIndex];

                for (var index = 0;
                     index < dataColumns.Count &&
                     index < dataRow.Count;
                     index++)
                {
                    if (string.IsNullOrEmpty(dataRow[index]))
                    {
                        continue;
                    }

                    builder.Append(
                        BuildInlineCell(
                            row,
                            index + 1,
                            dataRow[index],
                            5));
                }
            }

            for (var index = 0;
                 index < referenceColumns.Count;
                 index++)
            {
                var values =
                    referenceColumns[index].Values;
                var valueIndex = row - 2;

                if (valueIndex < values.Count)
                {
                    builder.Append(
                        BuildInlineCell(
                            row,
                            firstReferenceColumn + index,
                            values[valueIndex],
                            5));
                }
            }

            builder.Append("</row>");
        }

        builder.Append("</sheetData>");
        builder.Append("<autoFilter ref=\"A1:");
        builder.Append(dataEndReference);
        builder.Append("\"/>");

        var validations = BuildDataValidations(
            dataColumns,
            referenceColumns,
            firstReferenceColumn,
            maxRow);

        builder.Append(validations);
        builder.Append("</worksheet>");

        return builder.ToString();
    }

    private static string BuildDataValidations(
        IReadOnlyList<EmployeeTemplateColumn> dataColumns,
        IReadOnlyList<ReferenceColumn> referenceColumns,
        int firstReferenceColumn,
        int maxRow)
    {
        var referenceMap = referenceColumns
            .Select((column, index) => new
            {
                column.Header,
                ColumnNumber =
                    firstReferenceColumn + index,
                LastRow = Math.Max(
                    2,
                    column.Values.Count + 1)
            })
            .ToDictionary(
                item => item.Header,
                item => item,
                StringComparer.OrdinalIgnoreCase);

        var mappings = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["CompanyName"] = "Ref Company Name",
            ["CompanyCode"] = "Ref Company Code",
            ["WorkLocationName"] =
                "Ref Work Location Name",
            ["WorkLocationCode"] =
                "Ref Work Location Code",
            ["DepartmentName"] =
                "Ref Department Name",
            ["DepartmentCode"] =
                "Ref Department Code",
            ["PositionName"] =
                "Ref Position Name",
            ["PositionCode"] =
                "Ref Position Code"
        };

        var items = new List<string>();

        for (var index = 0;
             index < dataColumns.Count;
             index++)
        {
            var column = dataColumns[index];
            var mappingKey = column.Name;
            if (TryParseLocalizedHeader(
                    column.Name,
                    out var localizedField,
                    out _))
            {
                mappingKey = localizedField;
            }

            if (!mappings.TryGetValue(
                    mappingKey,
                    out var referenceHeader) ||
                !referenceMap.TryGetValue(
                    referenceHeader,
                    out var reference))
            {
                continue;
            }

            var targetColumn =
                GetColumnName(index + 1);
            var referenceColumn =
                GetColumnName(
                    reference.ColumnNumber);
            var formula =
                $"${referenceColumn}$2:" +
                $"${referenceColumn}$" +
                $"{reference.LastRow}";
            var sqref =
                $"{targetColumn}2:" +
                $"{targetColumn}{maxRow}";

            items.Add(
                "<dataValidation type=\"list\" allowBlank=\"1\" showErrorMessage=\"0\" showInputMessage=\"1\" " +
                $"sqref=\"{sqref}\" promptTitle=\"ZYNORA reference\" prompt=\"Select an existing value or type a new value to create it automatically.\">" +
                $"<formula1>{formula}</formula1>" +
                "</dataValidation>");
        }

        if (items.Count == 0)
        {
            return string.Empty;
        }

        return
            $"<dataValidations count=\"{items.Count}\">" +
            string.Concat(items) +
            "</dataValidations>";
    }

    private static string BuildInlineCell(
        int row,
        int column,
        string value,
        int style)
    {
        return
            $"<c r=\"{GetCellReference(row, column)}\" " +
            $"s=\"{style}\" t=\"inlineStr\"><is><t>" +
            $"{Xml(value)}</t></is></c>";
    }

    private static string GetCellReference(
        int row,
        int column)
    {
        return GetColumnName(column) +
               row.ToString(
                   CultureInfo.InvariantCulture);
    }

    private static string GetColumnName(int column)
    {
        var result = string.Empty;
        var dividend = column;

        while (dividend > 0)
        {
            var modulo =
                (dividend - 1) % 26;
            result =
                Convert.ToChar(65 + modulo) +
                result;
            dividend =
                (dividend - modulo) / 26;
        }

        return result;
    }

    private static string Xml(string? value)
    {
        return System.Security.SecurityElement
            .Escape(value ?? string.Empty) ??
            string.Empty;
    }

    private static void AddZipEntry(
        ZipArchive archive,
        string entryName,
        string content)
    {
        var entry = archive.CreateEntry(
            entryName,
            CompressionLevel.Fastest);

        using var stream = entry.Open();
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false));

        writer.Write(content);
    }

    private async Task ExecuteSqlAsync(
        string sql,
        Action<DbCommand> configure)
    {
        var connection =
            _dbContext.Database.GetDbConnection();
        var shouldClose =
            connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command =
                connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction =
                _dbContext.Database.CurrentTransaction
                    ?.GetDbTransaction();
            configure(command);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose &&
                _dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<int> ExecuteScalarIntAsync(
        string sql,
        Action<DbCommand> configure)
    {
        var connection =
            _dbContext.Database.GetDbConnection();
        var shouldClose =
            connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command =
                connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction =
                _dbContext.Database.CurrentTransaction
                    ?.GetDbTransaction();
            configure(command);
            var value =
                await command.ExecuteScalarAsync();

            return Convert.ToInt32(
                value,
                CultureInfo.InvariantCulture);
        }
        finally
        {
            if (shouldClose &&
                _dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<T>> QueryAsync<T>(
        string sql,
        Action<DbCommand> configure,
        Func<DbDataReader, T> map)
    {
        var result = new List<T>();
        var connection =
            _dbContext.Database.GetDbConnection();
        var shouldClose =
            connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command =
                connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction =
                _dbContext.Database.CurrentTransaction
                    ?.GetDbTransaction();
            configure(command);

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(map(reader));
            }
        }
        finally
        {
            if (shouldClose &&
                _dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    private static Company? ResolveExistingCompany(
        IReadOnlyList<Company> companies,
        string name,
        string code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var byCode = companies.FirstOrDefault(
                company => Same(company.Code, code));

            if (byCode != null)
            {
                return byCode;
            }
        }

        return companies.FirstOrDefault(
            company => Same(company.Name, name));
    }

    private static string SelectCode(
        string requestedCode,
        string prefix,
        HashSet<string> usedCodes)
    {
        var normalizedRequested =
            NormalizeCode(requestedCode);

        if (!string.IsNullOrWhiteSpace(
                normalizedRequested) &&
            !usedCodes.Contains(
                normalizedRequested))
        {
            return normalizedRequested;
        }

        return GenerateShortCode(
            prefix,
            usedCodes);
    }

    private static string GenerateShortCode(
        string prefix,
        HashSet<string> usedCodes)
    {
        for (var number = 1;
             number < 1000000;
             number++)
        {
            var candidate =
                $"{prefix}-{number:000}";

            if (!usedCodes.Contains(candidate))
            {
                return candidate;
            }
        }

        return
            $"{prefix}-" +
            Guid.NewGuid()
                .ToString("N")
                .Substring(0, 8)
                .ToUpperInvariant();
    }

    private static void ApplyOptional(
        string value,
        bool created,
        Action<string?> apply)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            apply(value.Trim());
        }
        else if (created)
        {
            apply(null);
        }
    }

    private static void Require(
        string value,
        string field,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
        }
    }

    private static void ValidateLength(
        string value,
        int maxLength,
        string field,
        ICollection<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            value.Trim().Length > maxLength)
        {
            errors.Add(
                $"{field} exceeds the maximum length " +
                $"of {maxLength} characters.");
        }
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string> values,
        params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var normalizedAlias =
                NormalizeHeader(alias);

            foreach (var pair in values)
            {
                if (NormalizeHeader(pair.Key) ==
                    normalizedAlias)
                {
                    return pair.Value?.Trim() ??
                           string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private async Task SaveImportedLocalizedValuesAsync(
        ResolvedEmployeeImportRow resolved)
    {
        if (resolved.Plan.LocalizedValues.Count == 0)
        {
            return;
        }

        var activeCultures = (await _dbContext.CompanyLanguages
                .AsNoTracking()
                .Where(item =>
                    item.CompanyId == resolved.CompanyId &&
                    item.IsActive &&
                    !item.IsDeleted)
                .Select(item => item.CultureCode)
                .ToListAsync())
            .Select(NormalizeCultureCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (activeCultures.Count == 0)
        {
            return;
        }

        var values = resolved.Plan.LocalizedValues
            .Where(item =>
                activeCultures.Contains(item.CultureCode) &&
                !string.IsNullOrWhiteSpace(item.Value))
            .ToList();

        await UpsertLocalizedEntityAsync(
            resolved.CompanyId,
            "Company",
            resolved.CompanyId,
            values.Where(item => item.FieldName == "CompanyName")
                .Select(item => item with { FieldName = "Name" }));

        await UpsertLocalizedEntityAsync(
            resolved.CompanyId,
            "Branch",
            resolved.BranchId,
            values.Where(item => item.FieldName == "WorkLocationName")
                .Select(item => item with { FieldName = "Name" }));

        await UpsertLocalizedEntityAsync(
            resolved.CompanyId,
            "Department",
            resolved.DepartmentId,
            values.Where(item => item.FieldName == "DepartmentName")
                .Select(item => item with { FieldName = "Name" }));

        await UpsertLocalizedEntityAsync(
            resolved.CompanyId,
            "Position",
            resolved.PositionId,
            values.Where(item => item.FieldName == "PositionName")
                .Select(item => item with { FieldName = "Name" }));

        var employeeValues = values
            .Where(item => item.FieldName is
                "FirstName" or "SecondName" or "ThirdName" or "LastName")
            .ToList();

        foreach (var culture in employeeValues
                     .Select(item => item.CultureCode)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var cultureValues = employeeValues
                .Where(item => item.CultureCode.Equals(
                    culture,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            var fullName = ComposeFullName(
                cultureValues.FirstOrDefault(item => item.FieldName == "FirstName")?.Value ?? string.Empty,
                cultureValues.FirstOrDefault(item => item.FieldName == "SecondName")?.Value ?? string.Empty,
                cultureValues.FirstOrDefault(item => item.FieldName == "ThirdName")?.Value ?? string.Empty,
                cultureValues.FirstOrDefault(item => item.FieldName == "LastName")?.Value ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                employeeValues.Add(new LocalizedCellValue("FullName", culture, fullName));
            }
        }

        await UpsertLocalizedEntityAsync(
            resolved.CompanyId,
            "Employee",
            resolved.Employee.Id,
            employeeValues);
    }

    private async Task UpsertLocalizedEntityAsync(
        int companyId,
        string entityType,
        int entityId,
        IEnumerable<LocalizedCellValue> candidateValues)
    {
        var values = candidateValues
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .GroupBy(
                item => $"{item.FieldName}\u001f{item.CultureCode}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        if (values.Count == 0)
        {
            return;
        }

        var fields = values.Select(item => item.FieldName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var cultures = values.Select(item => item.CultureCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existing = await _dbContext.LocalizedEntityValues
            .Where(item =>
                item.CompanyId == companyId &&
                item.EntityType == entityType &&
                item.EntityId == entityId &&
                fields.Contains(item.FieldName) &&
                cultures.Contains(item.CultureCode))
            .ToListAsync();

        foreach (var value in values)
        {
            var row = existing.FirstOrDefault(item =>
                item.FieldName.Equals(value.FieldName, StringComparison.OrdinalIgnoreCase) &&
                item.CultureCode.Equals(value.CultureCode, StringComparison.OrdinalIgnoreCase));

            if (row is null)
            {
                _dbContext.LocalizedEntityValues.Add(new LocalizedEntityValue
                {
                    CompanyId = companyId,
                    EntityType = entityType,
                    EntityId = entityId,
                    FieldName = value.FieldName,
                    CultureCode = value.CultureCode,
                    Value = value.Value.Trim(),
                    TranslationStatus = "Import"
                });
            }
            else
            {
                row.Value = value.Value.Trim();
                row.TranslationStatus = "Import";
                row.IsDeleted = false;
                row.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task<Dictionary<string, string>> LoadDefaultCulturesAsync()
    {
        var rows = await (
            from company in _dbContext.Companies.AsNoTracking()
            join language in _dbContext.CompanyLanguages.AsNoTracking()
                on company.Id equals language.CompanyId
            where !company.IsDeleted &&
                  language.IsActive &&
                  language.IsDefault &&
                  !language.IsDeleted
            select new
            {
                company.Code,
                company.Name,
                language.CultureCode
            }).ToListAsync();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var culture = NormalizeCultureCode(row.CultureCode);
            if (!string.IsNullOrWhiteSpace(row.Code))
            {
                result[$"code:{NormalizeKey(row.Code)}"] = culture;
            }

            if (!string.IsNullOrWhiteSpace(row.Name))
            {
                result[$"name:{NormalizeKey(row.Name)}"] = culture;
            }
        }

        return result;
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> LoadRequiredCulturesAsync()
    {
        var rows = await (
            from company in _dbContext.Companies.AsNoTracking()
            join language in _dbContext.CompanyLanguages.AsNoTracking()
                on company.Id equals language.CompanyId
            where !company.IsDeleted && language.IsActive && language.IsRequired && !language.IsDeleted
            select new { company.Code, company.Name, language.CultureCode }).ToListAsync();

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in rows.GroupBy(row => NormalizeKey(row.Code)))
        {
            result[$"code:{group.Key}"] = group.Select(row => NormalizeCultureCode(row.CultureCode))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        foreach (var group in rows.GroupBy(row => NormalizeKey(row.Name)))
        {
            result[$"name:{group.Key}"] = group.Select(row => NormalizeCultureCode(row.CultureCode))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        return result;
    }

    private static void ValidateRequiredLocalizedValues(
        EmployeeBootstrapRowPlan plan,
        IReadOnlyDictionary<string, IReadOnlyList<string>> requirements)
    {
        IReadOnlyList<string>? cultures = null;
        if (!string.IsNullOrWhiteSpace(plan.CompanyCode))
            requirements.TryGetValue($"code:{NormalizeKey(plan.CompanyCode)}", out cultures);
        if (cultures is null && !string.IsNullOrWhiteSpace(plan.CompanyName))
            requirements.TryGetValue($"name:{NormalizeKey(plan.CompanyName)}", out cultures);
        if (cultures is null || cultures.Count == 0) return;

        var requiredFields = new[]
        {
            "FirstName", "LastName", "CompanyName", "WorkLocationName", "DepartmentName", "PositionName"
        };

        foreach (var culture in cultures)
        {
            foreach (var field in requiredFields)
            {
                var present = plan.LocalizedValues.Any(item =>
                    item.FieldName.Equals(field, StringComparison.OrdinalIgnoreCase) &&
                    item.CultureCode.Equals(culture, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Value));

                // الملفات القديمة تبقى مدعومة: القيمة غير الموسومة تغطي اللغة الأساسية فقط.
                if (!present &&
                    plan.LocalizedValues.Count == 0 &&
                    !string.IsNullOrWhiteSpace(GetLegacyPlanValue(plan, field)))
                {
                    present = true;
                }

                if (!present)
                {
                    plan.Errors.Add($"{field} [{culture}] is required.");
                }
            }
        }
    }

    private static string GetLegacyPlanValue(EmployeeBootstrapRowPlan plan, string fieldName) => fieldName switch
    {
        "FirstName" => FirstNonBlank(plan.FirstName, plan.FullName),
        "LastName" => FirstNonBlank(plan.LastName, plan.FullName),
        "CompanyName" => plan.CompanyName,
        "WorkLocationName" => plan.WorkLocationName,
        "DepartmentName" => plan.DepartmentName,
        "PositionName" => plan.PositionName,
        _ => string.Empty
    };

    private static string? ResolvePreferredCulture(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string> configuredDefaults)
    {
        var companyCode = GetValue(values, "CompanyCode");
        if (!string.IsNullOrWhiteSpace(companyCode) &&
            configuredDefaults.TryGetValue(
                $"code:{NormalizeKey(companyCode)}",
                out var byCode))
        {
            return byCode;
        }

        var companyName = GetValue(values, "CompanyName", "Company");
        if (!string.IsNullOrWhiteSpace(companyName) &&
            configuredDefaults.TryGetValue(
                $"name:{NormalizeKey(companyName)}",
                out var byName))
        {
            return byName;
        }

        foreach (var localizedName in ParseLocalizedValues(values)
                     .Where(item => item.FieldName == "CompanyName" && !string.IsNullOrWhiteSpace(item.Value)))
        {
            if (configuredDefaults.TryGetValue(
                    $"name:{NormalizeKey(localizedName.Value)}",
                    out var byLocalizedName))
            {
                return byLocalizedName;
            }
        }

        return null;
    }

    private static string GetPreferredLocalizedValue(
        IReadOnlyDictionary<string, string> values,
        string fieldName,
        string? preferredCulture)
    {
        var legacy = GetValue(values, fieldName);
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            return legacy;
        }

        var localized = ParseLocalizedValues(values)
            .Where(item => item.FieldName.Equals(
                fieldName,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferredCulture))
        {
            var preferred = localized.FirstOrDefault(item =>
                item.CultureCode.Equals(
                    NormalizeCultureCode(preferredCulture),
                    StringComparison.OrdinalIgnoreCase));
            if (preferred is not null && !string.IsNullOrWhiteSpace(preferred.Value))
            {
                return preferred.Value;
            }
        }

        var arabic = localized.FirstOrDefault(item =>
            item.CultureCode.StartsWith("ar", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.Value));

        return arabic?.Value ??
               localized.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Value))?.Value ??
               string.Empty;
    }

    private static List<LocalizedCellValue> ParseLocalizedValues(
        IReadOnlyDictionary<string, string> values)
    {
        var result = new List<LocalizedCellValue>();

        foreach (var pair in values)
        {
            if (!TryParseLocalizedHeader(
                    pair.Key,
                    out var fieldName,
                    out var cultureCode) ||
                !LocalizedTemplateFields.Any(field =>
                    field.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(new LocalizedCellValue(
                fieldName,
                cultureCode,
                pair.Value?.Trim() ?? string.Empty));
        }

        return result;
    }

    public static bool TryParseLocalizedHeader(
        string? header,
        out string fieldName,
        out string cultureCode)
    {
        fieldName = string.Empty;
        cultureCode = string.Empty;

        var value = (header ?? string.Empty).Replace("*", string.Empty).Trim();
        var start = value.LastIndexOf('[');
        var end = value.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return false;
        }

        var culture = value[(start + 1)..end].Trim();
        try
        {
            cultureCode = CultureInfo.GetCultureInfo(culture).Name;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }

        var normalizedField = ImportHeaderAliases.Canonicalize(value[..start]);
        var definition = LocalizedTemplateFields.FirstOrDefault(field =>
            ImportHeaderAliases.Canonicalize(field.Name) == normalizedField ||
            ImportHeaderAliases.Canonicalize(field.ArabicLabel) == normalizedField);

        if (definition is null)
        {
            return false;
        }

        fieldName = definition.Name;
        return true;
    }

    private static string ComposeFullName(params string[] parts) =>
        string.Join(" ", parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim()));

    private static string FirstNonBlank(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool TryParseBoolean(
        string value,
        out bool result)
    {
        var normalized =
            NormalizeKey(value);

        if (normalized is
            "1" or
            "yes" or
            "y" or
            "true" or
            "active" or
            "فعال" or
            "نعم")
        {
            result = true;
            return true;
        }

        if (normalized is
            "0" or
            "no" or
            "n" or
            "false" or
            "inactive" or
            "غير فعال" or
            "لا")
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryParseDate(
        string value,
        out DateOnly date)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var formats = new[]
        {
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "dd/MM/yyyy",
            "d/M/yyyy"
        };

        if (DateTime.TryParseExact(
                value.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exact))
        {
            date = DateOnly.FromDateTime(exact);
            return true;
        }

        if (double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var numericDate) &&
            numericDate > 20000 &&
            numericDate < 90000)
        {
            date = DateOnly.FromDateTime(
                DateTime.FromOADate(
                    numericDate));
            return true;
        }

        return false;
    }

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        var dot = value.LastIndexOf('.');

        return at > 0 &&
               dot > at + 1 &&
               dot < value.Length - 1;
    }

    private static bool TryResolveDynamicHeader(
        string header,
        IReadOnlyList<DynamicFieldDefinition> definitions,
        out DynamicFieldDefinition definition)
    {
        definition = DynamicFieldDefinition.Empty;
        var value = header
            .Replace('\u00A0', ' ')
            .Trim();

        if (!value.StartsWith(
                "Custom:",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = value
            .Substring("Custom:".Length)
            .Trim();

        var bracketKey =
            ExtractBracketKey(value);

        if (!string.IsNullOrWhiteSpace(bracketKey))
        {
            var byKey = definitions.FirstOrDefault(item =>
                Same(item.FieldKey, bracketKey));

            if (byKey != null)
            {
                definition = byKey;
                return true;
            }

            value = RemoveBracketKey(value);
        }

        var byLabel = definitions.FirstOrDefault(item =>
            Same(item.FieldLabel, value));

        if (byLabel != null)
        {
            definition = byLabel;
            return true;
        }

        var byFieldKey = definitions.FirstOrDefault(item =>
            Same(item.FieldKey, value));

        if (byFieldKey != null)
        {
            definition = byFieldKey;
            return true;
        }

        return false;
    }

    private static string BuildDynamicHeader(
        DynamicFieldDefinition field,
        IEnumerable<string> usedHeaders)
    {
        var label = string.IsNullOrWhiteSpace(
                field.FieldLabel)
            ? field.FieldKey
            : field.FieldLabel.Trim();
        var header = $"Custom: {label}";
        var used = usedHeaders.ToHashSet(
            StringComparer.OrdinalIgnoreCase);

        if (used.Contains(header))
        {
            header =
                $"Custom: {label} [{field.FieldKey}]";
        }

        return header;
    }

    private static string ExtractBracketKey(
        string value)
    {
        var start = value.LastIndexOf('[');
        var end = value.LastIndexOf(']');

        if (start >= 0 && end > start)
        {
            return value
                .Substring(
                    start + 1,
                    end - start - 1)
                .Trim();
        }

        return string.Empty;
    }

    private static string RemoveBracketKey(
        string value)
    {
        var start = value.LastIndexOf('[');
        var end = value.LastIndexOf(']');

        if (start >= 0 && end > start)
        {
            return value
                .Remove(
                    start,
                    end - start + 1)
                .Trim();
        }

        return value.Trim();
    }

    /// <summary>
    /// تطبيع الترويسة **مع ترجمة العربية للاسم المعياري**. نقطة واحدة يمرّ بها
    /// كل مقارنة ترويسة بالمحرك (<c>ValidateHeaders</c> · <c>GetValue</c> ·
    /// <c>IsReferenceHeader</c>)، فقبول القالب المترجَم يسري عليها كلها معاً.
    /// </summary>
    private static string NormalizeHeader(
        string? value)
    {
        return ImportHeaderAliases.Canonicalize(value);
    }

    private static string NormalizeKey(
        string? value)
    {
        var cleaned =
            (value ?? string.Empty)
                .Replace('\u00A0', ' ')
                .Trim();

        while (cleaned.Contains("  "))
        {
            cleaned = cleaned.Replace("  ", " ");
        }

        return cleaned.ToLowerInvariant();
    }

    private static string NormalizeIdentifier(
        string? value)
    {
        var cleaned =
            (value ?? string.Empty)
                .Replace('\u00A0', ' ')
                .Trim();

        while (cleaned.Contains("  "))
        {
            cleaned =
                cleaned.Replace("  ", " ");
        }

        return cleaned;
    }

    private static string NormalizeCode(
        string? value)
    {
        var cleaned =
            (value ?? string.Empty)
                .Replace('\u00A0', ' ')
                .Trim();

        while (cleaned.Contains(" -"))
        {
            cleaned =
                cleaned.Replace(" -", "-");
        }

        while (cleaned.Contains("- "))
        {
            cleaned =
                cleaned.Replace("- ", "-");
        }

        while (cleaned.Contains("  "))
        {
            cleaned =
                cleaned.Replace("  ", " ");
        }

        return cleaned.ToUpperInvariant();
    }

    private static bool Same(
        string? left,
        string? right)
    {
        return string.Equals(
            NormalizeKey(left),
            NormalizeKey(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? NullIfBlank(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object? value)
    {
        var parameter =
            command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value =
            value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static int GetInt32(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        return reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(
                reader.GetValue(ordinal),
                CultureInfo.InvariantCulture);
    }

    private static int? GetNullableInt32(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(
                reader.GetValue(ordinal),
                CultureInfo.InvariantCulture);
    }

    private static string GetString(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        return reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(
                  reader.GetValue(ordinal),
                  CultureInfo.InvariantCulture) ??
              string.Empty;
    }

    private static bool GetBoolean(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        return !reader.IsDBNull(ordinal) &&
               Convert.ToBoolean(
                   reader.GetValue(ordinal),
                   CultureInfo.InvariantCulture);
    }

    private sealed class EmployeeBootstrapPlan
    {
        public EmployeeBootstrapPlan(
            List<EmployeeBootstrapRowPlan> rows)
        {
            Rows = rows;
        }

        public List<EmployeeBootstrapRowPlan> Rows { get; }
    }

    private sealed class EmployeeBootstrapRowPlan
    {
        public int RowNumber { get; set; }
        public Dictionary<string, string> Values { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyCode { get; set; } = string.Empty;
        public string WorkLocationName { get; set; } = string.Empty;
        public string WorkLocationCode { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string DepartmentCode { get; set; } = string.Empty;
        public string PositionName { get; set; } = string.Empty;
        public string PositionCode { get; set; } = string.Empty;
        public DateOnly? HireDate { get; set; }
        public string NationalId { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateOnly? BirthDate { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string MaritalStatus { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string ContractType { get; set; } = string.Empty;
        public DateOnly? ContractEndDate { get; set; }
        public string EmploymentStatus { get; set; } = string.Empty;
        public bool? IsActive { get; set; }
        public string DirectManagerEmployeeNo { get; set; } = string.Empty;
        public decimal? BasicSalary { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string SecondName { get; set; } = string.Empty;
        public string ThirdName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FirstNameEn { get; set; } = string.Empty;
        public string SecondNameEn { get; set; } = string.Empty;
        public string ThirdNameEn { get; set; } = string.Empty;
        public string LastNameEn { get; set; } = string.Empty;
        public bool? IsCitizen { get; set; }
        public string PassportNo { get; set; } = string.Empty;
        public string SponsorName { get; set; } = string.Empty;
        public string Religion { get; set; } = string.Empty;
        public string MotherCountry { get; set; } = string.Empty;
        public string MotherCity { get; set; } = string.Empty;
        public DateOnly? JoiningDate { get; set; }
        public string WorkType { get; set; } = string.Empty;
        public string JobGrade { get; set; } = string.Empty;
        public string PhoneExtension { get; set; } = string.Empty;
        public string PersonalEmail { get; set; } = string.Empty;
        public List<LocalizedCellValue> LocalizedValues { get; } = new();
        public string EmployeeAction { get; set; } = "Create";
        public CompanyReference? Company { get; set; }
        public BranchReference? Branch { get; set; }
        public DepartmentReference? Department { get; set; }
        public PositionReference? Position { get; set; }
        public List<string> Messages { get; } = new();
        public List<string> Errors { get; } = new();
        public bool CanImport => Errors.Count == 0;
    }

    private sealed class BootstrapSnapshot
    {
        public BootstrapSnapshot(
            List<CompanyReference> companies,
            List<BranchReference> branches,
            List<DepartmentReference> departments,
            List<PositionReference> positions,
            HashSet<string> employeeNumbers)
        {
            Companies = companies;
            Branches = branches;
            Departments = departments;
            Positions = positions;
            EmployeeNumbers = employeeNumbers;
        }

        public List<CompanyReference> Companies { get; }
        public List<BranchReference> Branches { get; }
        public List<DepartmentReference> Departments { get; }
        public List<PositionReference> Positions { get; }
        public HashSet<string> EmployeeNumbers { get; }
    }

    private sealed class CompanyReference
    {
        public CompanyReference(
            int id,
            string name,
            string code,
            bool isPlanned)
        {
            Id = id;
            Name = name;
            Code = code;
            IsPlanned = isPlanned;
            Key = id > 0
                ? $"id:{id}"
                : $"new-company:{NormalizeKey(name)}";
        }

        public int Id { get; }
        public string Name { get; }
        public string Code { get; }
        public bool IsPlanned { get; }
        public string Key { get; }
    }

    private sealed class BranchReference
    {
        public BranchReference(
            int id,
            string companyKey,
            string name,
            string code,
            bool isPlanned)
        {
            Id = id;
            CompanyKey = companyKey;
            Name = name;
            Code = code;
            IsPlanned = isPlanned;
            Key = id > 0
                ? $"id:{id}"
                : $"new-branch:{companyKey}:{NormalizeKey(name)}";
        }

        public int Id { get; }
        public string CompanyKey { get; }
        public string Name { get; }
        public string Code { get; }
        public bool IsPlanned { get; }
        public string Key { get; }
    }

    private sealed class DepartmentReference
    {
        public DepartmentReference(
            int id,
            string companyKey,
            string name,
            string code,
            bool isPlanned)
        {
            Id = id;
            CompanyKey = companyKey;
            Name = name;
            Code = code;
            IsPlanned = isPlanned;
            Key = id > 0
                ? $"id:{id}"
                : $"new-department:{companyKey}:{NormalizeKey(name)}";
        }

        public int Id { get; }
        public string CompanyKey { get; }
        public string Name { get; }
        public string Code { get; }
        public bool IsPlanned { get; }
        public string Key { get; }
    }

    private sealed class PositionReference
    {
        public PositionReference(
            int id,
            string companyKey,
            string departmentKey,
            string name,
            string code,
            bool isPlanned)
        {
            Id = id;
            CompanyKey = companyKey;
            DepartmentKey = departmentKey;
            Name = name;
            Code = code;
            IsPlanned = isPlanned;
            Key = id > 0
                ? $"id:{id}"
                : $"new-position:{companyKey}:{NormalizeKey(name)}";
        }

        public int Id { get; }
        public string CompanyKey { get; }
        public string DepartmentKey { get; }
        public string Name { get; }
        public string Code { get; }
        public bool IsPlanned { get; }
        public string Key { get; }
    }

    private sealed record PositionRow(
        int Id,
        int CompanyId,
        int? DepartmentId,
        string Name,
        string Code);

    private sealed record ParsedImportRow(
        int RowNumber,
        Dictionary<string, string> Values);

    private sealed class ParsedImportFile
    {
        public List<string> Headers { get; set; } = new();
        public List<ParsedImportRow> Rows { get; set; } = new();
    }

    private sealed record ResolvedEmployeeImportRow(
        EmployeeBootstrapRowPlan Plan,
        Employee Employee,
        bool Created,
        int CompanyId,
        int BranchId,
        int DepartmentId,
        int PositionId);

    private sealed record DynamicFieldDefinition(
        string FieldKey,
        string FieldLabel,
        bool IsRequired,
        string SectionKey,
        int SortOrder)
    {
        public static DynamicFieldDefinition Empty { get; } =
            new(string.Empty, string.Empty, false, string.Empty, 0);
    }

    private sealed record TemplateReferenceRow(
        string Name,
        string Code,
        string Company);

    private sealed record TemplateEmployeeRow(
        int Id,
        int CompanyId,
        int BranchId,
        int DepartmentId,
        int? PositionId,
        Dictionary<string, string> Values);

    private sealed record TemplateLanguage(
        string CultureCode,
        string NativeName,
        bool IsDefault,
        bool IsRequired);

    private sealed record LocalizedTemplateField(
        string Name,
        string ArabicLabel,
        bool Required,
        double Width);

    private sealed record LocalizedCellValue(
        string FieldName,
        string CultureCode,
        string Value);

    private sealed record LocalizedTemplateColumn(
        string Name,
        string FieldName,
        string CultureCode);

    private sealed record TemplateReferenceData(
        List<TemplateReferenceRow> Companies,
        List<TemplateReferenceRow> Branches,
        List<TemplateReferenceRow> Departments,
        List<TemplateReferenceRow> Positions);

    /// <summary>
    /// <paramref name="Header"/> مفتاح إنجليزي تُبنى عليه صيغ التحقق من الصحة،
    /// و<paramref name="DisplayName"/> نصّ الترويسة العربي المعروض.
    /// </summary>
    private sealed record ReferenceColumn(
        string Header,
        List<string> Values,
        string? DisplayName = null)
    {
        public string Caption => DisplayName ?? Header;
    }

    /// <summary>
    /// <paramref name="Name"/> هو **مفتاح المطابقة** المعياري (إنجليزي) الذي
    /// تُبنى عليه القراءة والتحقق وصيغ التحقق من الصحة.
    /// <paramref name="DisplayName"/> هو **نصّ الترويسة المعروض** بالملف — عربي
    /// لأن النظام عربيّ. فصلهما يمنع أن تكسر الترجمةُ المطابقةَ.
    /// </summary>
    private sealed record EmployeeTemplateColumn(
        string Name,
        bool Required,
        EmployeeTemplateColumnKind Kind,
        double Width,
        string? DisplayName = null)
    {
        public string Header => DisplayName ?? Name;
    }

    private enum EmployeeTemplateColumnKind
    {
        Text,
        Date,
        Custom
    }

    private sealed class BootstrapStructureCounts
    {
        public int Companies { get; set; }
        public int Branches { get; set; }
        public int Departments { get; set; }
        public int Positions { get; set; }
    }
}
