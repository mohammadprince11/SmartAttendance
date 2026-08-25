using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>موديل partial رندر الحقول المخصصة داخل سلايدات الملف 360°.</summary>
public sealed class EntityCustomFieldsPartialModel
{
    public string EntityKey { get; set; } = string.Empty;
    public List<EntityCustomFields.FieldDefinition> Fields { get; set; } = new();
    /// <summary>required بالمتصفح — يُعطَّل بسلايد السجلات المشترك (JS يديره حسب النوع الظاهر).</summary>
    public bool ApplyRequired { get; set; } = true;
    public bool StartHidden { get; set; }
}

/// <summary>
/// Structured 360° data that lives inside the employee profile page but isn't a
/// file upload: the family / dependents list shown as a card inside the
/// "ملفات الموظف" tab. Separate partial so the large main ProfileModel stays
/// untouched. Uses the employee id in <c>Id</c>.
/// </summary>
public partial class ProfileModel
{
    public List<EmployeeDependent> Dependents { get; set; } = new();
    public List<EmployeeFileRecord> FileRecords { get; set; } = new();
    public EmployeeFinancialInfo? FinancialInfo { get; set; }

    // ---- العلاوات (نمط كيان: عنصر راتب + مبلغ + نطاق + حالة مشتقة) ----
    public List<EmployeeAllowance> Allowances { get; set; } = new();
    public List<SalaryItemStore.SalaryItem> SalaryItemOptions { get; set; } = new();
    public decimal ActiveAllowancesTotal { get; set; }

    /// <summary>الملفان الماليان المطبَّقان فعلاً على هذا الموظف — كانا محسوبين وخفيّين.</summary>
    public PayrollProfileResolver.Resolution GosiChoice { get; set; } = PayrollProfileResolver.NoProfile;
    public PayrollProfileResolver.Resolution TaxChoice { get; set; } = PayrollProfileResolver.NoProfile;

    /// <summary>وعاء الضمان المحتسَب اليوم — رقمٌ كان يُحسب بالمسير ولا يُعرض بالملف.</summary>
    public decimal GosiBase { get; set; }

    // ---- العقود (نمط كيان: متعددة، التجديد صف جديد) ----
    public List<EmployeeContract> Contracts { get; set; } = new();
    public List<string> ContractTypeOptions { get; set; } = new();

    // ---- دفتر استحقاقات الإجازة (نمط كيان: سابق/استحقاق/مستخدم/حالي) ----
    public sealed class LeaveLedgerRow
    {
        public SmartAttendance.Domain.Enums.LeaveType Type { get; set; }
        public decimal CarriedOver { get; set; }
        public decimal Entitled { get; set; }
        public decimal Used { get; set; }
        public decimal Current => CarriedOver + Entitled - Used; // السالب مسموح مثل كيان
    }

    public int LeaveLedgerYear { get; set; } = DateTime.Today.Year;
    public List<LeaveLedgerRow> LeaveLedger { get; set; } = new();

    // ---- الحقول المخصصة لكل كيان (الداينمك مرحلة 2) ----
    public Dictionary<string, List<EntityCustomFields.FieldDefinition>> CustomFieldDefs { get; set; } = new();
    public Dictionary<string, Dictionary<int, Dictionary<string, string>>> CustomFieldValues { get; set; } = new();

    public List<EntityCustomFields.FieldDefinition> CustomFieldsOf(string entityKey) =>
        CustomFieldDefs.TryGetValue(entityKey, out var defs) ? defs : new();

    public int DependentCount => Dependents.Count;
    public int SupportedCount => Dependents.Count(d => d.IsDependent);
    public int EmergencyContactCount => Dependents.Count(d => d.IsEmergencyContact);

    public IEnumerable<EmployeeFileRecord> RecordsOf(EmployeeRecordType type) =>
        FileRecords.Where(r => r.RecordType == type);

    [TempData] public string? PanelSuccess { get; set; }
    [TempData] public string? PanelError { get; set; }

    /// <summary>
    /// الحقول الحساسة (نمط أدوار كيان): رؤية الراتب والعلاوات محصورة بالأدوار
    /// المعرّفة بإعداد Sensitive.SalaryRoles (الافتراضي Admin وHR Manager).
    /// </summary>
    public bool CanViewSalary { get; set; }

    private async Task LoadSalaryVisibilityAsync()
    {
        var allowedRoles = await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.GetAsync(
            _dbContext, "Sensitive.SalaryRoles", "Admin,HR Manager");
        var role = PeopleAccessContext.GetRole(HttpContext);
        var roleAllowed = allowedRoles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(role, StringComparer.OrdinalIgnoreCase);

        // التعويض بيانٌ ماليّ حسّاس: بجانب قائمة الأدوار القديمة (توافقية)، نُكرم منح
        // People.ViewCompensation صريحاً بأدوار الوصول ضمن نطاق هذا الموظف — فالرؤية
        // تصير صلاحيةً من الدرجة الأولى لا مجرّد قائمة أدوار عامة. راجع تقرير أمان People.
        if (roleAllowed)
        {
            CanViewSalary = true;
            return;
        }

        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;

        // إضافيّ (أدوار الوصول — الحقول الحساسة): من يحمل دوراً يمنح حقل «الراتب»
        // يراه أيضاً. OR فلا يُسحب من أحدٍ يراه اليوم.
        CanViewSalary =
            await CanAccessAsync(systemUserId, role, PeoplePermissionCodes.ViewCompensation, Id) ||
            await SmartAttendance.Web.Infrastructure.Security.AccessRoleStore.HasSensitiveFieldAsync(
                _dbContext, systemUserId, SmartAttendance.Web.Infrastructure.Security.SensitiveFieldCatalog.Salary);
    }

    [BindProperty] public DependentInput Dependent { get; set; } = new();
    [BindProperty] public FileRecordInput RecordInput { get; set; } = new();
    [BindProperty] public IFormFile? RecordAttachment { get; set; }
    [BindProperty] public AllowanceInput Allowance { get; set; } = new();
    [BindProperty] public IFormFile? AllowanceAttachment { get; set; }
    [BindProperty] public ContractInput Contract { get; set; } = new();
    [BindProperty] public IFormFile? ContractAttachment { get; set; }

    public async Task LoadPanelsAsync()
    {
        await LoadSalaryVisibilityAsync();
        await EmployeeDependentSchema.EnsureAsync(_dbContext);
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        await EmployeeFinancialInfoSchema.EnsureAsync(_dbContext);

        Dependents = await _dbContext.EmployeeDependents.AsNoTracking()
            .Where(d => d.EmployeeId == Id)
            .OrderBy(d => d.Relation).ThenBy(d => d.Name).ToListAsync();

        FileRecords = await _dbContext.EmployeeFileRecords.AsNoTracking()
            .Where(r => r.EmployeeId == Id)
            .OrderByDescending(r => r.ToDate).ThenByDescending(r => r.Id).ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (CanViewSalary)
        {
            FinancialInfo = await _dbContext.EmployeeFinancialInfos.AsNoTracking()
                .FirstOrDefaultAsync(f => f.EmployeeId == Id);

            await EmployeeAllowanceSchema.EnsureAsync(_dbContext);
            Allowances = await _dbContext.EmployeeAllowances.AsNoTracking()
                .Where(a => a.EmployeeId == Id)
                .OrderByDescending(a => a.FromDate).ToListAsync();

            ActiveAllowancesTotal = Allowances.Where(a => a.IsActiveOn(today)).Sum(a => a.Amount);
            await LoadFinancialProfilesAsync(today);
            SalaryItemOptions = await SalaryItemStore.ActiveIncomeItemsAsync(_dbContext);
        }

        await HrLookups.EnsureSchemaAsync(_dbContext);

        ContractTypeOptions = await HrLookups.ValuesAsync(_dbContext, "contracttypes");

        await EmployeeContractSchema.EnsureAsync(_dbContext);
        Contracts = await _dbContext.EmployeeContracts.AsNoTracking()
            .Where(c => c.EmployeeId == Id)
            .OrderByDescending(c => c.IsCurrent).ThenByDescending(c => c.FromDate).ToListAsync();

        await LoadLeaveLedgerAsync();

        // الحقول المخصصة: التعريفات + قيم كل سجلات هذا الموظف دفعة واحدة.
        CustomFieldDefs = await EntityCustomFields.DefinitionsByEntityAsync(_dbContext);
        var recordIds = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dependent"] = Dependents.Select(d => d.Id).ToList(),
            ["Contract"] = Contracts.Select(c => c.Id).ToList(),
            ["Allowance"] = Allowances.Select(a => a.Id).ToList(),
        };
        foreach (var group in FileRecords.GroupBy(r => r.RecordType))
        {
            recordIds[group.Key.ToString()] = group.Select(r => r.Id).ToList();
        }
        CustomFieldValues = await EntityCustomFields.ValuesByEntityAsync(_dbContext, recordIds);
    }

    // نفس منطق صفحة /LeaveBalances: المنح من LeaveBalance (أو افتراضي السياسة)،
    // والاستخدام يُشتق من طلبات الإجازة المعتمدة المتداخلة مع السنة.
    /// <summary>
    /// يحسم ملفَّي الضريبة والضمان المطبَّقين على هذا الموظف ويحسب وعاء الضمان
    /// **للعرض فقط** بنفس مركِّب المسير — لا صيغة ثانية هنا. بكيان «راتب الضمان
    /// الاجتماعي» رقمٌ ظاهر بالبطاقة، وعندنا كان يُحتسب بالمسير ولا يراه أحد.
    /// </summary>
    private async Task LoadFinancialProfilesAsync(DateOnly today)
    {
        var taxProfiles = await PayrollConfigStore.ListTaxProfilesAsync(_dbContext);
        var gosiProfiles = await PayrollConfigStore.ListGosiProfilesAsync(_dbContext);

        var rows = await HrConditionFacts.LoadAsync(_dbContext, Id);
        var facts = rows.Count > 0
            ? HrConditionFacts.Build(rows[0], today)
            : new Dictionary<string, HrConditions.Fact>();

        TaxChoice = PayrollProfileResolver.Resolve(
            FinancialInfo?.TaxProfileId, PayrollConfigStore.Candidates(taxProfiles), facts);
        GosiChoice = PayrollProfileResolver.Resolve(
            FinancialInfo?.GosiProfileId, PayrollConfigStore.Candidates(gosiProfiles), facts);

        var basic = FinancialInfo?.BasicSalary ?? 0;
        var members = await SalaryBaseStore.MembersAsync(
            _dbContext, SalaryBaseComposer.GosiBaseKey, GosiChoice.ProfileId ?? 0);

        GosiBase = SalaryBaseComposer.Compose(
            new SalaryBaseComposer.Amounts
            {
                Basic = basic,
                Allowances = ActiveAllowancesTotal,
                Gross = basic + ActiveAllowancesTotal
            },
            members);
    }

    private async Task LoadLeaveLedgerAsync()
    {
        await LeaveBalanceSchema.EnsureAsync(_dbContext);

        var year = LeaveLedgerYear;
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var trackedTypes = SmartAttendance.Domain.Leave.IraqiLeavePolicy.TrackedTypes.ToList();

        var overrides = await _dbContext.LeaveBalances.AsNoTracking()
            .Where(b => b.EmployeeId == Id && b.Year == year)
            .ToListAsync();

        var requests = await _dbContext.LeaveRequests.AsNoTracking()
            .Where(r => r.EmployeeId == Id
                     && r.Status == SmartAttendance.Domain.Enums.LeaveStatus.Approved
                     && trackedTypes.Contains(r.LeaveType)
                     && r.FromDate <= yearEnd
                     && r.ToDate >= yearStart)
            .Select(r => new { r.LeaveType, r.FromDate, r.ToDate })
            .ToListAsync();

        LeaveLedger = trackedTypes.Select(type =>
        {
            var stored = overrides.FirstOrDefault(b => b.LeaveType == type);
            var used = requests.Where(r => r.LeaveType == type).Sum(r =>
            {
                var start = r.FromDate > yearStart ? r.FromDate : yearStart;
                var end = r.ToDate < yearEnd ? r.ToDate : yearEnd;
                var days = end.DayNumber - start.DayNumber + 1;
                return days > 0 ? days : 0;
            });

            return new LeaveLedgerRow
            {
                Type = type,
                CarriedOver = stored?.CarriedOverDays ?? 0,
                Entitled = stored?.EntitledDays
                    ?? SmartAttendance.Domain.Leave.IraqiLeavePolicy.GetDefaultEntitlement(type) ?? 0,
                Used = used
            };
        }).ToList();
    }

    private IActionResult BackToFiles() => RedirectToPage("./Profile", null, new { Id }, "profile-files");

    private static string? CleanText(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public async Task<IActionResult> OnPostSaveDependentAsync()
    {
        await EmployeeDependentSchema.EnsureAsync(_dbContext);
        if (!await _dbContext.Employees.AnyAsync(e => e.Id == Id && !e.IsDeleted)) return NotFound();
        if (string.IsNullOrWhiteSpace(Dependent.Name)) { PanelError = "اسم المعال مطلوب."; return BackToFiles(); }

        var user = User.Identity?.Name ?? "System";
        var now = DateTime.UtcNow;
        var e = Dependent.Id > 0
            ? await _dbContext.EmployeeDependents.FirstOrDefaultAsync(d => d.Id == Dependent.Id && d.EmployeeId == Id)
            : null;
        if (e == null) { e = new EmployeeDependent { EmployeeId = Id, CreatedAt = now, CreatedBy = user }; _dbContext.EmployeeDependents.Add(e); }
        else { e.UpdatedAt = now; e.UpdatedBy = user; }

        e.Relation = Dependent.Relation;
        e.Name = Dependent.Name.Trim();
        e.NameOther = CleanText(Dependent.NameOther);
        e.BirthDate = Dependent.BirthDate;
        e.MarriageDate = Dependent.MarriageDate;
        e.Religion = CleanText(Dependent.Religion);
        e.Nationality = CleanText(Dependent.Nationality);
        e.NationalId = CleanText(Dependent.NationalId);
        e.PassportNo = CleanText(Dependent.PassportNo);
        e.IsCitizen = Dependent.IsCitizen;
        e.ResidencyNo = CleanText(Dependent.ResidencyNo);
        e.Gender = CleanText(Dependent.Gender);
        e.IsStudent = Dependent.IsStudent;
        e.MaritalStatus = CleanText(Dependent.MaritalStatus);
        e.IsEmergencyContact = Dependent.IsEmergencyContact;
        e.IsSpecialNeeds = Dependent.IsSpecialNeeds;
        e.IsWorking = Dependent.IsWorking;
        e.IsDependent = Dependent.IsDependent;
        e.MobilePhone = CleanText(Dependent.MobilePhone);
        e.CompanyName = CleanText(Dependent.CompanyName);
        e.Note = CleanText(Dependent.Note);

        await _dbContext.SaveChangesAsync();
        await EntityCustomFields.SaveValuesFromFormAsync(_dbContext, "Dependent", e.Id, Request.Form);
        PanelSuccess = "تم حفظ المعال.";
        return BackToFiles();
    }

    public async Task<IActionResult> OnPostDeleteDependentAsync(int recordId)
    {
        var e = await _dbContext.EmployeeDependents.FirstOrDefaultAsync(d => d.Id == recordId && d.EmployeeId == Id);
        if (e != null)
        {
            e.IsDeleted = true;
            e.UpdatedAt = DateTime.UtcNow;
            e.UpdatedBy = User.Identity?.Name ?? "System";
            await _dbContext.SaveChangesAsync();
            await EntityCustomFields.DeleteValuesAsync(_dbContext, "Dependent", recordId);
            PanelSuccess = "تم الحذف.";
        }
        return BackToFiles();
    }

    // ---- Structured file records (education/experience/certificate/training/medical) ----
    public async Task<IActionResult> OnPostSaveRecordAsync()
    {
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        if (!await _dbContext.Employees.AnyAsync(e => e.Id == Id && !e.IsDeleted)) return NotFound();
        if (string.IsNullOrWhiteSpace(RecordInput.Title)) { PanelError = "العنوان مطلوب."; return BackToFiles(); }

        var user = User.Identity?.Name ?? "System";
        var now = DateTime.UtcNow;
        var r = RecordInput.Id > 0
            ? await _dbContext.EmployeeFileRecords.FirstOrDefaultAsync(x => x.Id == RecordInput.Id && x.EmployeeId == Id)
            : null;
        if (r == null) { r = new EmployeeFileRecord { EmployeeId = Id, CreatedAt = now, CreatedBy = user }; _dbContext.EmployeeFileRecords.Add(r); }
        else { r.UpdatedAt = now; r.UpdatedBy = user; }

        r.RecordType = RecordInput.RecordType;
        r.Title = RecordInput.Title.Trim();
        r.Subtitle = CleanText(RecordInput.Subtitle);
        r.Country = CleanText(RecordInput.Country);
        r.RefNo = CleanText(RecordInput.RefNo);
        r.FromDate = RecordInput.FromDate;
        r.ToDate = RecordInput.ToDate;
        r.Amount = RecordInput.Amount;
        r.IsCurrent = RecordInput.IsCurrent;
        r.IsReturned = RecordInput.IsReturned;
        r.ReturnDate = RecordInput.ReturnDate;
        r.Gpa = CleanText(RecordInput.Gpa);
        r.RefContactName = CleanText(RecordInput.RefContactName);
        r.RefContactPosition = CleanText(RecordInput.RefContactPosition);
        r.RefContactPhone = CleanText(RecordInput.RefContactPhone);
        r.RefContactNote = CleanText(RecordInput.RefContactNote);
        r.Note = CleanText(RecordInput.Note);

        var (name, path) = await SaveRecordFileAsync(RecordAttachment);
        if (path != null) { r.AttachmentName = name; r.AttachmentPath = path; }

        await _dbContext.SaveChangesAsync();
        await EntityCustomFields.SaveValuesFromFormAsync(_dbContext, r.RecordType.ToString(), r.Id, Request.Form);
        PanelSuccess = "تم حفظ السجل.";
        return BackToFiles();
    }

    public async Task<IActionResult> OnPostDeleteRecordAsync(int recordId)
    {
        var r = await _dbContext.EmployeeFileRecords.FirstOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == Id);
        if (r != null)
        {
            r.IsDeleted = true; r.UpdatedAt = DateTime.UtcNow; r.UpdatedBy = User.Identity?.Name ?? "System";
            await _dbContext.SaveChangesAsync();
            await EntityCustomFields.DeleteValuesAsync(_dbContext, r.RecordType.ToString(), recordId);
            PanelSuccess = "تم الحذف.";
        }
        return BackToFiles();
    }

    private static readonly string[] AllowedRecordFileExtensions =
        { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".doc", ".docx", ".xls", ".xlsx" };

    // المرحلة 6: سجلات الموظف (إنذارات/قرارات) خارج wwwroot والقراءة عبر /files.
    private async Task<(string? name, string? path)> SaveRecordFileAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0) return (null, null);

        var stored = await _protectedFiles.SaveAsync(
            file, Id, "record", HttpContext.RequestAborted);

        return stored is null
            ? (null, null)
            : (Path.GetFileName(file.FileName), stored);
    }

    // ---- العلاوات: حفظ (إضافة/تعديل) وحذف — نفس نمط المعالين ----
    public async Task<IActionResult> OnPostSaveAllowanceAsync()
    {
        if (!await HasEmployeeActionPermissionAsync(PeoplePermissionCodes.EditCompensation, Id))
            return Forbid();

        await EmployeeAllowanceSchema.EnsureAsync(_dbContext);
        if (!await _dbContext.Employees.AnyAsync(e => e.Id == Id && !e.IsDeleted)) return NotFound();
        var salaryItem = Allowance.SalaryItemId > 0
            ? (await SalaryItemStore.ActiveIncomeItemsAsync(_dbContext))
                .SingleOrDefault(x => x.Id == Allowance.SalaryItemId)
            : null;
        if (salaryItem == null) { PanelError = "عنصر الراتب غير موجود أو غير نشط."; return BackToFiles(); }
        if (Allowance.FromDate == default) { PanelError = "تاريخ بداية العلاوة مطلوب."; return BackToFiles(); }
        if (Allowance.ToDate.HasValue && Allowance.ToDate.Value < Allowance.FromDate)
        { PanelError = "تاريخ نهاية العلاوة قبل بدايتها."; return BackToFiles(); }

        var user = User.Identity?.Name ?? "System";
        var now = DateTime.UtcNow;
        var a = Allowance.Id > 0
            ? await _dbContext.EmployeeAllowances.FirstOrDefaultAsync(x => x.Id == Allowance.Id && x.EmployeeId == Id)
            : null;
        if (a == null) { a = new EmployeeAllowance { EmployeeId = Id, CreatedAt = now, CreatedBy = user }; _dbContext.EmployeeAllowances.Add(a); }
        else { a.UpdatedAt = now; a.UpdatedBy = user; }

        a.SalaryItemId = salaryItem.Id;
        a.ItemName = salaryItem.Name;
        a.Amount = Allowance.Amount;
        a.FromDate = Allowance.FromDate;
        a.ToDate = Allowance.ToDate;
        a.EndAfterDate = Allowance.EndAfterDate;
        a.Note = CleanText(Allowance.Note);

        var (name, path) = await SaveRecordFileAsync(AllowanceAttachment);
        if (path != null) { a.AttachmentName = name; a.AttachmentPath = path; }

        await _dbContext.SaveChangesAsync();
        await EntityCustomFields.SaveValuesFromFormAsync(_dbContext, "Allowance", a.Id, Request.Form);
        PanelSuccess = "تم حفظ العلاوة.";
        return BackToFiles();
    }

    public async Task<IActionResult> OnPostDeleteAllowanceAsync(int recordId)
    {
        if (!await HasEmployeeActionPermissionAsync(PeoplePermissionCodes.EditCompensation, Id))
            return Forbid();

        var a = await _dbContext.EmployeeAllowances.FirstOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == Id);
        if (a != null)
        {
            a.IsDeleted = true;
            a.UpdatedAt = DateTime.UtcNow;
            a.UpdatedBy = User.Identity?.Name ?? "System";
            await _dbContext.SaveChangesAsync();
            PanelSuccess = "تم حذف العلاوة.";
        }
        return BackToFiles();
    }

    public class AllowanceInput
    {
        public int Id { get; set; }
        public int SalaryItemId { get; set; }
        public decimal Amount { get; set; }
        public DateOnly FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public bool EndAfterDate { get; set; }
        public string? Note { get; set; }
    }

    // ---- العقود: حفظ (إضافة/تعديل) وحذف — التجديد يُدخل كصف جديد ----
    public async Task<IActionResult> OnPostSaveContractAsync()
    {
        await EmployeeContractSchema.EnsureAsync(_dbContext);
        if (!await _dbContext.Employees.AnyAsync(e => e.Id == Id && !e.IsDeleted)) return NotFound();
        if (string.IsNullOrWhiteSpace(Contract.ContractType)) { PanelError = "نوع العقد مطلوب."; return BackToFiles(); }
        if (Contract.FromDate == default) { PanelError = "تاريخ بداية العقد مطلوب."; return BackToFiles(); }
        if (Contract.ToDate.HasValue && Contract.ToDate.Value < Contract.FromDate)
        { PanelError = "تاريخ نهاية العقد قبل بدايته."; return BackToFiles(); }

        var user = User.Identity?.Name ?? "System";
        var contractType = Contract.ContractType.Trim();
        var contractNo = CleanText(Contract.ContractNo);
        var note = CleanText(Contract.Note);
        var (attachmentName, attachmentPath) = await SaveRecordFileAsync(ContractAttachment);

        // المحرّك الرسمي الوحيد لكتابة العقود = ContractRegisterStore. هذه الشاشة سطح
        // إدخالٍ يوجّه الكتابة إليه: فلا يتكرّر ثابت «العقد الحالي الواحد» ولا يُفقَد
        // نسب renew/extend كما كان بالمحرّك الثاني السابق.
        int contractId;
        if (Contract.Id > 0)
        {
            // العقد يجب أن يخصّ هذا الموظف — معرّفٌ من النموذج لا يُعدَّل عقد موظفٍ آخر.
            if (!await _dbContext.EmployeeContracts
                    .AnyAsync(x => x.Id == Contract.Id && x.EmployeeId == Id && !x.IsDeleted))
            { PanelError = "العقد غير موجود."; return BackToFiles(); }

            await ContractRegisterStore.UpdateContractAsync(
                _dbContext, Contract.Id, contractNo, contractType, Contract.FromDate, Contract.ToDate,
                note, user, makeCurrent: Contract.IsCurrent,
                attachmentName: attachmentName, attachmentPath: attachmentPath);
            contractId = Contract.Id;
        }
        else
        {
            contractId = await ContractRegisterStore.AddContractAsync(
                _dbContext, Id, contractNo, contractType, Contract.FromDate, Contract.ToDate,
                makeCurrent: Contract.IsCurrent, previousContractId: null, movementKind: null,
                effectiveDate: null, note: note, createdBy: user,
                attachmentName: attachmentName, attachmentPath: attachmentPath);
        }

        // مزامنة حقول العقد المختصرة بكيان الموظف (نوع العقد الحالي وانتهاؤه) — تغذّي التنبيهات والتقارير.
        if (Contract.IsCurrent)
        {
            await _dbContext.Employees
                .Where(e => e.Id == Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.ContractType, contractType)
                    .SetProperty(e => e.ContractEndDate, Contract.ToDate));
        }

        await EntityCustomFields.SaveValuesFromFormAsync(_dbContext, "Contract", contractId, Request.Form);
        PanelSuccess = "تم حفظ العقد.";
        return BackToFiles();
    }

    public async Task<IActionResult> OnPostDeleteContractAsync(int recordId)
    {
        // المسار القانونيّ الوحيد لحذف العقود = ContractRegisterStore (كالإضافة والتعديل):
        // نطاقٌ + تحقّق ملكية العقد لهذا الموظف + ثابت العقد الحاليّ الواحد + مزامنة الحقول
        // المسطّحة + معاملة + idempotency. لا كتابة EF مباشرة هنا (لا محرّك حذفٍ ثانٍ).
        var ok = await ContractRegisterStore.DeleteContractAsync(
            _dbContext,
            await _companyScope.GetAsync(HttpContext.RequestAborted),
            recordId,
            User.Identity?.Name ?? "System",
            expectedEmployeeId: Id);

        if (ok) { PanelSuccess = "تم حذف العقد."; }
        else { PanelError = "تعذّر حذف العقد (غير موجود أو خارج نطاقك)."; }

        return BackToFiles();
    }

    public class ContractInput
    {
        public int Id { get; set; }
        public string? ContractNo { get; set; }
        public string ContractType { get; set; } = string.Empty;
        public DateOnly FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public bool IsCurrent { get; set; }
        public string? Note { get; set; }
    }

    public string LeaveTypeText(SmartAttendance.Domain.Enums.LeaveType type) => type switch
    {
        SmartAttendance.Domain.Enums.LeaveType.Annual => "إجازة سنوية",
        SmartAttendance.Domain.Enums.LeaveType.Sick => "إجازة مرضية",
        _ => type.ToString()
    };

    public string RelationText(DependentRelation relation) => relation switch
    {
        DependentRelation.Spouse => "شريك",
        DependentRelation.Son => "إبن",
        DependentRelation.Daughter => "بنت",
        DependentRelation.Relative => "قريب",
        _ => relation.ToString()
    };

    public class FileRecordInput
    {
        public int Id { get; set; }
        public EmployeeRecordType RecordType { get; set; } = EmployeeRecordType.Education;
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Country { get; set; }
        public string? RefNo { get; set; }
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public decimal? Amount { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsReturned { get; set; }
        public DateOnly? ReturnDate { get; set; }
        public string? Gpa { get; set; }
        public string? RefContactName { get; set; }
        public string? RefContactPosition { get; set; }
        public string? RefContactPhone { get; set; }
        public string? RefContactNote { get; set; }
        public string? Note { get; set; }
    }

    public class DependentInput
    {
        public int Id { get; set; }
        public DependentRelation Relation { get; set; } = DependentRelation.Spouse;
        public string Name { get; set; } = string.Empty;
        public string? NameOther { get; set; }
        public DateOnly? BirthDate { get; set; }
        public DateOnly? MarriageDate { get; set; }
        public string? Religion { get; set; }
        public string? Nationality { get; set; }
        public string? NationalId { get; set; }
        public string? PassportNo { get; set; }
        public bool IsCitizen { get; set; }
        public string? ResidencyNo { get; set; }
        public string? Gender { get; set; }
        public bool IsStudent { get; set; }
        public string? MaritalStatus { get; set; }
        public bool IsEmergencyContact { get; set; }
        public bool IsSpecialNeeds { get; set; }
        public bool IsWorking { get; set; }
        public bool IsDependent { get; set; }
        public string? MobilePhone { get; set; }
        public string? CompanyName { get; set; }
        public string? Note { get; set; }
    }
}
