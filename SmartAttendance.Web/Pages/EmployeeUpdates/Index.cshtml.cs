using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeeUpdates;

/// <summary>
/// دفتر حركات الموظف (نمط كيان): تعديلات البيانات كسجلات Transaction بمرجع
/// وتاريخ تنفيذ وحالة، بدل التعديل المباشر — الأساس لقفل الرواتب لاحقاً.
/// </summary>
public partial class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPermissionAuthorizationService _permissions;
    private readonly IEffectiveScopeService _effectiveScopeService;

    public IndexModel(
        ApplicationDbContext dbContext,
        IPermissionAuthorizationService permissions,
        IEffectiveScopeService effectiveScopeService)
    {
        _dbContext = dbContext;
        _permissions = permissions;
        _effectiveScopeService = effectiveScopeService;
    }

    /// <summary>حقول الإسناد الوظيفيّ — تغييرها يتطلّب ChangeAssignment (نظير P0-7).</summary>
    private static readonly HashSet<string> AssignmentFieldKeys =
        new(StringComparer.OrdinalIgnoreCase) { "BranchId", "DepartmentId", "DirectManagerId", "PositionId", "Position" };

    public string Tab { get; private set; } = "stage";
    public string ActiveSectionKey { get; private set; } = "employee-info";
    public int SelectedEmployeeId { get; private set; }
    public UpdateEmployee SelectedEmployee { get; private set; } = UpdateEmployee.Empty;
    public List<UpdateEmployee> Employees { get; private set; } = new();
    public List<DepartmentOption> Departments { get; private set; } = new();
    public List<string> PositionOptions { get; private set; } = new(); // ZYNORA_FIX14G_LOOKUP_PROPERTIES
    public List<string> NationalityOptions { get; private set; } = new();
    public List<EmployeeLookupOption> ManagerOptions { get; private set; } = new();
    public List<UpdateSection> Sections { get; private set; } = BuildSections();
    public UpdateSection ActiveSection => Sections.FirstOrDefault(x => x.Key == ActiveSectionKey) ?? Sections[0];
    public List<UpdateField> CurrentFields => ActiveSection.Fields;
    public List<UpdateSection> StageBlocks => BuildStageBlocks(Sections);
    public Dictionary<string, string> CurrentValues { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<UpdateBatch> OpenBatches { get; private set; } = new();
    public List<UpdateBatch> HistoryBatches { get; private set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public string Initials => GetInitials(SelectedEmployee.FullName);

    private ActorScope? _actor;

    /// <summary>هوية الفاعل ونطاقه (قواعد ViewDirectory ∩ أدوار الوصول).</summary>
    private sealed record ActorScope(
        int SystemUserId,
        string Role,
        bool IsAdmin,
        PeopleDataScope DirectoryScope,
        PeopleDataScope AccessRoleScope,
        bool Unrestricted);

    private async Task<ActorScope> ResolveActorScopeAsync()
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);
        var isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        var directoryScope = await _permissions.GetPeopleDataScopeAsync(
            systemUserId,
            PeoplePermissionCodes.ViewDirectory,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewDirectory),
            HttpContext.RequestAborted);

        var accessRoleScope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId, isAdmin, HttpContext.RequestAborted);

        var unrestricted =
            directoryScope.IsUnrestricted && !directoryScope.HasAnyDenial &&
            accessRoleScope.IsUnrestricted && !accessRoleScope.HasAnyDenial;

        return _actor = new ActorScope(
            systemUserId, role, isAdmin, directoryScope, accessRoleScope, unrestricted);
    }

    private Task<bool> CanViewAsync(ActorScope actor) =>
        _permissions.HasPermissionAsync(
            actor.SystemUserId,
            PeoplePermissionCodes.ViewDirectory,
            PeopleCompatibilityAccess.IsAllowed(actor.Role, PeoplePermissionCodes.ViewDirectory),
            HttpContext.RequestAborted);

    /// <summary>النطاق الفعّال على موظفٍ بعينه: قواعد (ViewProfile) ∩ أدوار الوصول.</summary>
    private async Task<bool> CanAccessEmployeeAsync(ActorScope actor, int employeeId)
    {
        if (employeeId <= 0)
        {
            return false;
        }

        var rulesAllows = await _permissions.CanAccessEmployeeAsync(
            actor.SystemUserId,
            PeoplePermissionCodes.ViewProfile,
            employeeId,
            PeopleCompatibilityAccess.IsAllowed(actor.Role, PeoplePermissionCodes.ViewProfile),
            HttpContext.RequestAborted);

        if (!rulesAllows)
        {
            return false;
        }

        if (actor.AccessRoleScope.IsUnrestricted && !actor.AccessRoleScope.HasAnyDenial)
        {
            return true;
        }

        var location = await LoadEmployeeLocationAsync(employeeId);
        return location is not null &&
               actor.AccessRoleScope.AllowsEmployee(
                   employeeId, location.Value.CompanyId, location.Value.BranchId, location.Value.DepartmentId);
    }

    private Task<bool> CanEditCompensationAsync(ActorScope actor, int employeeId) =>
        _permissions.CanAccessEmployeeAsync(
            actor.SystemUserId,
            PeoplePermissionCodes.EditCompensation,
            employeeId,
            PeopleCompatibilityAccess.IsAllowed(actor.Role, PeoplePermissionCodes.EditCompensation),
            HttpContext.RequestAborted);

    private Task<bool> CanEditProfileAsync(ActorScope actor, int employeeId) =>
        _permissions.CanAccessEmployeeAsync(actor.SystemUserId, PeoplePermissionCodes.Edit, employeeId,
            PeopleCompatibilityAccess.IsAllowed(actor.Role, PeoplePermissionCodes.Edit), HttpContext.RequestAborted);

    private Task<bool> CanChangeAssignmentAsync(ActorScope actor, int employeeId) =>
        _permissions.CanAccessEmployeeAsync(
            actor.SystemUserId,
            PeoplePermissionCodes.ChangeAssignment,
            employeeId,
            PeopleCompatibilityAccess.IsAllowed(actor.Role, PeoplePermissionCodes.ChangeAssignment),
            HttpContext.RequestAborted);

    private async Task<(int CompanyId, int BranchId, int DepartmentId)?> LoadEmployeeLocationAsync(int employeeId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    ISNULL(b.CompanyId, 0)    AS CompanyId,
    ISNULL(e.BranchId, 0)     AS BranchId,
    ISNULL(e.DepartmentId, 0) AS DepartmentId
FROM Employees e
LEFT JOIN Branches b ON b.Id = e.BranchId
WHERE e.Id = @Id AND ISNULL(e.IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", employeeId),
            reader => (
                CompanyId: HrmsDatabase.GetInt(reader, "CompanyId"),
                BranchId: HrmsDatabase.GetInt(reader, "BranchId"),
                DepartmentId: HrmsDatabase.GetInt(reader, "DepartmentId")));

        return rows.Count > 0 ? rows[0] : null;
    }

    public static int ResolveExplicitEmployeeSelection(int? employeeId, bool employeeSelected) =>
        employeeSelected ? Math.Max(0, employeeId.GetValueOrDefault()) : 0;

    public async Task<IActionResult> OnGetAsync(int? employeeId, string? tab, string? section, bool employeeSelected = false)
    {
        var actor = await ResolveActorScopeAsync();
        if (!await CanViewAsync(actor))
        {
            return Forbid();
        }

        // A legacy/bookmarked employeeId alone is not a picker choice.
        // This marker is navigation state only; authorization is still mandatory.
        employeeId = ResolveExplicitEmployeeSelection(employeeId, employeeSelected);
        if (employeeId.GetValueOrDefault() > 0 && !await CanAccessEmployeeAsync(actor, employeeId.GetValueOrDefault()))
        {
            return Forbid();
        }

        await LoadPageAsync(employeeId, tab, section);
        return Page();
    }

    // ZYNORA_FIX14B_STAGE_METHOD_START
    public async Task<IActionResult> OnPostStageAsync(
        int employeeId, string sectionKey, DateTime? effectiveDate, string? note, bool isRetroactive = false)
    {
        // تخويل: عرضٌ + الموظف المستهدَف ضمن النطاق. بلا هذا كان أي واصلٍ للصفحة
        // يُنشئ حركةً لأي موظفٍ بأي شركة (تُطبَّق عند القفل).
        var actor = await ResolveActorScopeAsync();
        if (!await CanViewAsync(actor) || !await CanAccessEmployeeAsync(actor, employeeId) || !await CanEditProfileAsync(actor, employeeId))
        {
            return Forbid();
        }

        // قدرتان تُفلتِران الحقول الحسّاسة عند الترحيل فلا تدخل الحركة أصلاً: الراتب
        // يتطلّب EditCompensation، والإسناد الوظيفيّ يتطلّب ChangeAssignment (نظير P0-7).
        var canEditCompensation = await CanEditCompensationAsync(actor, employeeId);
        var canChangeAssignment = await CanChangeAssignmentAsync(actor, employeeId);

        await EmployeeUpdateSchema.EnsureAsync(_dbContext);
        await EnsureMovementColumnsAsync();

        var sections = await BuildSectionsWithDynamicFieldsAsync();
        var stageBlocks = BuildStageBlocks(sections);
        var stagedFields = stageBlocks
            .SelectMany(block => block.Fields)
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var current = await BuildCurrentValuesAsync(employeeId);

        var changes = new List<UpdateChange>();
        foreach (var field in stagedFields)
        {
            // إسقاط الحقول التي لا يملك المستخدم صلاحية تطبيقها — فلا تُرحَّل مضلِّلةً.
            if (IsFinancialField(field) && (!canEditCompensation || !CanViewFinancial))
            {
                continue;
            }

            if (AssignmentFieldKeys.Contains(field.Key) && !canChangeAssignment)
            {
                continue;
            }

            if (field.ReadOnly || !Request.Form.ContainsKey(field.Key)) continue;
            var oldValue = NormalizeValue(current.GetValueOrDefault(field.Key, string.Empty));
            var newValue = NormalizeValue(Request.Form[field.Key].FirstOrDefault() ?? string.Empty);
            if (field.Target is "custom" or "financial-custom" && field.InputType == "checkbox" && newValue == "false") newValue = "";

            if (!oldValue.Equals(newValue, StringComparison.OrdinalIgnoreCase))
            {
                if (!ValidStoredFieldValue(field, newValue))
                {
                    StatusMessage = $"قيمة غير صالحة لحقل «{field.Label}». لم تُحفظ الحركة.";
                    return RedirectToPage(new { employeeId, employeeSelected = true, tab = "stage" });
                }
                changes.Add(new UpdateChange
                {
                    FieldKey = field.Key,
                    FieldLabel = field.Label,
                    OldValue = oldValue,
                    NewValue = newValue
                });
            }
        }

        if (!await ValidProfileAssignmentAsync(employeeId, changes))
        {
            StatusMessage = "الارتباط الوظيفي غير صالح ضمن نطاق الموظف.";
            return RedirectToPage(new { employeeId, employeeSelected = true, tab = "stage" });
        }
        if (changes.Count == 0)
        {
            StatusMessage = "\u0644\u0627 \u062A\u0648\u062C\u062F \u062A\u063A\u064A\u064A\u0631\u0627\u062A \u0644\u0625\u0646\u0634\u0627\u0621 \u062D\u0631\u0643\u0629.";
            return RedirectToPage(new { employeeId, employeeSelected = true, tab = "stage", section = "employee-master" });
        }

        var requestedBy = User.Identity?.Name ?? "System";
        var resolvedEffectiveDate = (effectiveDate ?? DateTime.Today).Date;
        var sectionName = "\u062A\u062D\u062F\u064A\u062B \u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0645\u0648\u0638\u0641";

        await using var stageTransaction = await _dbContext.Database.BeginTransactionAsync();
        var batchId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO EmployeeUpdateBatches
(EmployeeId, SectionKey, SectionName, Status, RequestedBy, RequestedAt, EffectiveDate, Note, IsRetroactive)
VALUES
(@EmployeeId, 'employee-master', @SectionName, 'Open', @RequestedBy, SYSUTCDATETIME(), @EffectiveDate, @Note, @IsRetroactive);

SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@SectionName", sectionName);
                HrmsDatabase.AddParameter(command, "@RequestedBy", requestedBy);
                HrmsDatabase.AddParameter(command, "@EffectiveDate", resolvedEffectiveDate);
                HrmsDatabase.AddParameter(command, "@Note", note ?? string.Empty);
                // علَمٌ صريح لا استنتاج من التاريخ: سريانٌ بالماضي قد يكون تصحيح
                // خطأ إدخال (بلا أثر مالي) أو قراراً بأثر رجعي (يستوجب إعادة احتساب).
                HrmsDatabase.AddParameter(command, "@IsRetroactive", isRetroactive);
            });

        foreach (var change in changes)
        {
            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                """
INSERT INTO EmployeeUpdateChanges
(BatchId, FieldKey, FieldLabel, OldValue, NewValue)
VALUES
(@BatchId, @FieldKey, @FieldLabel, @OldValue, @NewValue);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@BatchId", batchId);
                    HrmsDatabase.AddParameter(command, "@FieldKey", change.FieldKey);
                    HrmsDatabase.AddParameter(command, "@FieldLabel", change.FieldLabel);
                    HrmsDatabase.AddParameter(command, "@OldValue", change.OldValue);
                    HrmsDatabase.AddParameter(command, "@NewValue", change.NewValue);
                });
        }

        StatusMessage = $"\u062A\u0645 \u0625\u0646\u0634\u0627\u0621 \u062D\u0631\u0643\u0629 \u063A\u064A\u0631 \u0645\u0642\u0641\u0644\u0629 EU{DateTime.UtcNow:yy}-{batchId}. \u062A\u0627\u0631\u064A\u062E \u0627\u0644\u0633\u0631\u064A\u0627\u0646: {resolvedEffectiveDate:dd/MM/yyyy}.";
        await stageTransaction.CommitAsync();
        return RedirectToPage(new { employeeId, employeeSelected = true, tab = "confirm", section = "employee-master" });
    }
    // ZYNORA_FIX14B_STAGE_METHOD_END
    public async Task<IActionResult> OnPostLockAsync(int batchId, int employeeId)
    {
        await EmployeeUpdateSchema.EnsureAsync(_dbContext);
        await EnsureMovementColumnsAsync();

        var batch = await LoadSingleBatchAsync(batchId);
        if (batch is null || !batch.Status.Equals("Open", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "الحركة غير موجودة أو تم قفلها مسبقاً.";
            return RedirectToPage(new { employeeId, employeeSelected = true, tab = "confirm" });
        }

        // تخويل عند **الكتابة** (الحاسم): الموظف الحقيقيّ للحركة ضمن النطاق. القفل هو
        // اللحظة التي تُطبَّق فيها التغييرات على ملف الموظف — فيُفحص هنا لا عند العرض.
        var actor = await ResolveActorScopeAsync();
        if (!await CanViewAsync(actor) || !await CanAccessEmployeeAsync(actor, batch.EmployeeId) || !await CanEditProfileAsync(actor, batch.EmployeeId))
        {
            return Forbid();
        }

        var canEditCompensation = await CanEditCompensationAsync(actor, batch.EmployeeId);
        var canChangeAssignment = await CanChangeAssignmentAsync(actor, batch.EmployeeId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var status = await HrmsDatabase.ScalarAsync<string>(_dbContext,
            "SELECT Status FROM EmployeeUpdateBatches WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", batchId));
        if (status != "Open") return RedirectToPage(new { employeeId, employeeSelected = true, tab = "history" });
        var definitions = (await BuildSectionsWithDynamicFieldsAsync()).SelectMany(s => s.Fields)
            .GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var current = await BuildCurrentValuesAsync(batch.EmployeeId);
        foreach (var change in batch.Changes)
        {
            if (!definitions.TryGetValue(change.FieldKey, out var field) || field.ReadOnly ||
                !ValidStoredFieldValue(field, change.NewValue) ||
                !NormalizeValue(current.GetValueOrDefault(change.FieldKey, "")).Equals(NormalizeValue(change.OldValue), StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "الحركة قديمة أو تتعارض مع بيانات الملف الحالية. أنشئ حركة جديدة؛ لم تُطبَّق تغييرات.";
                return RedirectToPage(new { employeeId, employeeSelected = true, tab = "confirm" });
            }
            if ((IsFinancialField(field) && (!canEditCompensation || !CanViewFinancial)) ||
                (AssignmentFieldKeys.Contains(field.Key) && !canChangeAssignment)) return Forbid();
        }
        if (!await ValidProfileAssignmentAsync(batch.EmployeeId, batch.Changes))
        {
            StatusMessage = "تعذر تطبيق الارتباط الوظيفي ضمن النطاق الحالي؛ لم تُطبَّق تغييرات.";
            return RedirectToPage(new { employeeId, employeeSelected = true, tab = "confirm" });
        }
        foreach (var change in batch.Changes)
        {
            var field = definitions[change.FieldKey];
            if (field.Target is "employee" or "compensation")
                await ApplyProfileFieldAsync(batch.EmployeeId, field, change.NewValue);
            else
                await ApplyCustomFieldAsync(batch.EmployeeId, change.FieldKey, change.FieldLabel, change.NewValue);
        }

        var lockedBy = User.Identity?.Name ?? "System";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE EmployeeUpdateBatches
SET Status = 'Locked',
    LockedBy = @LockedBy,
    LockedAt = SYSUTCDATETIME()
WHERE Id = @BatchId AND Status = 'Open';
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@LockedBy", lockedBy);
                HrmsDatabase.AddParameter(command, "@BatchId", batchId);
            });

        await transaction.CommitAsync();
        StatusMessage = $"تم قفل الحركة رقم {batchId} وتطبيق التغييرات على ملف الموظف.";
        return RedirectToPage(new { employeeId, employeeSelected = true, tab = "history" });
    }

    public async Task<IActionResult> OnPostDeleteOpenAsync(int batchId, int employeeId)
    {
        await EmployeeUpdateSchema.EnsureAsync(_dbContext);
        await EnsureMovementColumnsAsync();

        // حتى حذف حركةٍ غير مقفلة يُقيَّد بموظفٍ ضمن نطاق المستخدم — لا عبث بحركات
        // موظفي شركاتٍ أخرى. الحركة غير الموجودة تُعامَل بصمت (لا كشف وجود).
        var actor = await ResolveActorScopeAsync();
        var target = await LoadSingleBatchAsync(batchId);
        if (target is null ||
            !await CanViewAsync(actor) ||
            !await CanAccessEmployeeAsync(actor, target.EmployeeId))
        {
            StatusMessage = "الحركة غير موجودة أو خارج نطاق صلاحياتك.";
            return RedirectToPage(new { employeeId, employeeSelected = true, tab = "confirm" });
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
DELETE FROM EmployeeUpdateChanges
WHERE BatchId IN (SELECT Id FROM EmployeeUpdateBatches WHERE Id = @BatchId AND Status = 'Open');

DELETE FROM EmployeeUpdateBatches
WHERE Id = @BatchId AND Status = 'Open';
""",
            command => HrmsDatabase.AddParameter(command, "@BatchId", batchId));

        StatusMessage = "تم حذف الحركة غير المقفلة.";
        return RedirectToPage(new { employeeId, employeeSelected = true, tab = "confirm" });
    }

    // ZYNORA_FIX14B_MOVEMENT_COLUMNS_START
    // Compatibility shim. EffectiveDate is owned by migration 20260826-21.
    private Task EnsureMovementColumnsAsync() => Task.CompletedTask;
    // ZYNORA_FIX14B_MOVEMENT_COLUMNS_END
    private async Task LoadPageAsync(int? employeeId, string? tab, string? section)
    {
        await EmployeeUpdateSchema.EnsureAsync(_dbContext);
        await EnsureMovementColumnsAsync();

        Sections = await BuildSectionsWithDynamicFieldsAsync(); // ZYNORA_FIX14A_LOAD_DYNAMIC_SECTIONS
Tab = NormalizeTab(tab);
        ActiveSectionKey = NormalizeSection(section);

        // No automatic employee selection. The shared picker submits an explicit ID.
        SelectedEmployeeId = Math.Max(0, employeeId.GetValueOrDefault());
        if (SelectedEmployeeId <= 0)
        {
            return;
        }

        SelectedEmployee = await LoadEmployeeAsync(SelectedEmployeeId) ?? UpdateEmployee.Empty;
        if (SelectedEmployee.Id <= 0)
        {
            SelectedEmployeeId = 0;
            return;
        }

        await LoadProfileAssignmentOptionsAsync(SelectedEmployeeId);
        Departments = ProfileDepartments;
        NationalityOptions = await LoadNationalityOptionsAsync();
        ManagerOptions = await LoadActiveManagersAsync(SelectedEmployeeId);
        CurrentValues = await BuildCurrentValuesAsync(SelectedEmployeeId);
        OpenBatches = await LoadBatchesAsync(SelectedEmployeeId, "Open");
        HistoryBatches = await LoadBatchesAsync(SelectedEmployeeId, "Locked");
        if (!CanViewFinancial)
        {
            var sensitive = Sections.SelectMany(s => s.Fields).Where(IsFinancialField).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Sections = Sections.Select(s => s with { Fields = s.Fields.Where(f => !IsFinancialField(f)).ToList() }).ToList();
            foreach (var batch in OpenBatches.Concat(HistoryBatches)) batch.Changes.RemoveAll(c => sensitive.Contains(c.FieldKey));
        }
    }

    private async Task<List<UpdateEmployee>> LoadEmployeesAsync()
    {
        // النطاق (قواعد ∩ أدوار الوصول) يُطبَّق **داخل SQL قبل الحدّ** لا صفّاً-صفّاً
        // بعده — نفس مسار قائمة الأشخاص/المنتقي (P0-1 · Task 3). قبل ذلك كان الحدّ
        // يسبق النطاق ثمّ يُرشَّح بالذاكرة، فالمقيَّد قد يخسر مخوّلين خلف أوّل خمسمئة،
        // ويُحمَّل خمسمئة صفٍّ لترشيحها بالذاكرة على 10k+ موظف.
        var query = _dbContext.Employees
            .AsNoTracking()
            .Where(e => !e.IsDeleted);

        if (_actor is not null && !_actor.Unrestricted)
        {
            query = query
                .ApplyPeopleDataScope(_actor.DirectoryScope)
                .ApplyPeopleDataScope(_actor.AccessRoleScope);
        }

        return await query
            .OrderBy(e => e.FullName)
            .Take(500)
            .Select(e => new UpdateEmployee
            {
                Id = e.Id,
                EmployeeNo = e.EmployeeNo ?? string.Empty,
                FullName = e.FullName ?? string.Empty,
                Position = e.Position ?? string.Empty,
                DepartmentName = e.Department.Name ?? string.Empty,
                BranchName = e.Department.Branch == null
                    ? string.Empty
                    : e.Department.Branch.Name ?? string.Empty
            })
            .ToListAsync(HttpContext.RequestAborted);
    }

    private async Task<UpdateEmployee?> LoadEmployeeAsync(int employeeId)
    {
        var list = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    e.Id,
    ISNULL(e.EmployeeNo, '') AS EmployeeNo,
    ISNULL(e.FullName, '') AS FullName,
    ISNULL(e.Position, '') AS Position,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
WHERE e.Id = @EmployeeId;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new UpdateEmployee
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName")
            });

        return list.FirstOrDefault();
    }

    private async Task<List<DepartmentOption>> LoadDepartmentsAsync()
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT d.Id, ISNULL(d.Name, '') AS Name, ISNULL(b.Name, '') AS BranchName
FROM Departments d
LEFT JOIN Branches b ON d.BranchId = b.Id
ORDER BY b.Name, d.Name;
""",
            null,
            reader => new DepartmentOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = $"{HrmsDatabase.GetString(reader, "Name")} - {HrmsDatabase.GetString(reader, "BranchName")}".Trim(' ', '-')
            });
    }

    // ZYNORA_FIX14G_LOOKUP_METHODS_START
    private async Task<List<string>> LoadPositionOptionsAsync(string? currentPosition)
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
CREATE TABLE #PositionOptions
(
    [Name] nvarchar(400) COLLATE DATABASE_DEFAULT NOT NULL
);

IF OBJECT_ID(N'dbo.HrJobPositions', N'U') IS NOT NULL
BEGIN
    INSERT INTO #PositionOptions ([Name])
    SELECT DISTINCT LTRIM(RTRIM([ArabicName]))
    FROM [dbo].[HrJobPositions]
    WHERE LTRIM(RTRIM(ISNULL([ArabicName], N''))) <> N''
      AND ISNULL([IsActive], 1) = 1;
END;

IF OBJECT_ID(N'dbo.JobPositions', N'U') IS NOT NULL
BEGIN
    INSERT INTO #PositionOptions ([Name])
    SELECT DISTINCT LTRIM(RTRIM(j.[Name]))
    FROM [dbo].[JobPositions] j
    WHERE LTRIM(RTRIM(ISNULL(j.[Name], N''))) <> N''
      AND ISNULL(j.[IsActive], 1) = 1
      AND NOT EXISTS
      (
          SELECT 1
          FROM #PositionOptions existing
          WHERE existing.[Name] = LTRIM(RTRIM(j.[Name]))
      );
END;

IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
BEGIN
    INSERT INTO #PositionOptions ([Name])
    SELECT DISTINCT LTRIM(RTRIM(e.[Position]))
    FROM [dbo].[Employees] e
    WHERE LTRIM(RTRIM(ISNULL(e.[Position], N''))) <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM #PositionOptions existing
          WHERE existing.[Name] = LTRIM(RTRIM(e.[Position]))
      );
END;

IF LTRIM(RTRIM(ISNULL(@CurrentPosition, N''))) <> N''
   AND NOT EXISTS
   (
       SELECT 1
       FROM #PositionOptions existing
       WHERE existing.[Name] = LTRIM(RTRIM(@CurrentPosition))
   )
BEGIN
    INSERT INTO #PositionOptions ([Name])
    VALUES (LTRIM(RTRIM(@CurrentPosition)));
END;

SELECT [Name]
FROM #PositionOptions
ORDER BY [Name];

DROP TABLE #PositionOptions;
""",
            command => HrmsDatabase.AddParameter(command, "@CurrentPosition", currentPosition ?? string.Empty),
            reader => HrmsDatabase.GetString(reader, "Name"));
    }

    private async Task<List<string>> LoadNationalityOptionsAsync()
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
CREATE TABLE #NationalityOptions
(
    [Name] nvarchar(400) COLLATE DATABASE_DEFAULT NOT NULL
);

IF OBJECT_ID(N'dbo.Nationalities', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Nationalities', N'Name') IS NOT NULL
BEGIN
    INSERT INTO #NationalityOptions ([Name])
    EXEC(N'SELECT DISTINCT LTRIM(RTRIM([Name])) AS [Name] FROM [dbo].[Nationalities] WHERE LTRIM(RTRIM(ISNULL([Name], N''''))) <> N''''');
END;

IF OBJECT_ID(N'dbo.HrNationalities', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.HrNationalities', N'Name') IS NOT NULL
BEGIN
    INSERT INTO #NationalityOptions ([Name])
    EXEC(N'SELECT DISTINCT LTRIM(RTRIM([Name])) AS [Name] FROM [dbo].[HrNationalities] WHERE LTRIM(RTRIM(ISNULL([Name], N''''))) <> N''''');
END;

IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
BEGIN
    INSERT INTO #NationalityOptions ([Name])
    SELECT DISTINCT LTRIM(RTRIM(e.[Nationality]))
    FROM [dbo].[Employees] e
    WHERE LTRIM(RTRIM(ISNULL(e.[Nationality], N''))) <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM #NationalityOptions existing
          WHERE existing.[Name] = LTRIM(RTRIM(e.[Nationality]))
      );
END;

SELECT [Name]
FROM #NationalityOptions
GROUP BY [Name]
ORDER BY [Name];

DROP TABLE #NationalityOptions;
""",
            null,
            reader => HrmsDatabase.GetString(reader, "Name"));
    }

    private async Task<List<EmployeeLookupOption>> LoadActiveManagersAsync(int currentEmployeeId)
    {
        // المدراء المرشَّحون محصورون بشركة الموظف المُحدَّد (نظير Edit.LoadManagersAsync) —
        // منتقٍ عامّ يكشف أسماء/أرقام موظفي شركةٍ أخرى للمستخدم المقيَّد. بلا موظفٍ محدَّد
        // (@EmployeeId=0) لا شركة تُطابَق فتعود القائمة فارغة (المدير يُنتقى بعد اختيار الموظف).
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 500
    e.Id,
    CONCAT(ISNULL(e.FullName, ''), N' - ', ISNULL(e.EmployeeNo, '')) AS Name
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsActive = 1
  AND ISNULL(e.IsDeleted, 0) = 0
  AND e.Id <> @EmployeeId
  AND b.CompanyId = (
      SELECT b2.CompanyId FROM Employees e2
      INNER JOIN Branches b2 ON e2.BranchId = b2.Id
      WHERE e2.Id = @EmployeeId)
ORDER BY e.FullName, e.EmployeeNo;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", currentEmployeeId),
            reader => new EmployeeLookupOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name").Trim(' ', '-')
            });
    }
    // ZYNORA_FIX14G_LOOKUP_METHODS_END
    private async Task<Dictionary<string, string>> BuildCurrentValuesAsync(int employeeId)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await ReadProfileValuesAsync(employeeId, values);

        var customRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT FieldKey, ISNULL(FieldValue, '') AS FieldValue
FROM EmployeeCustomFields
WHERE EmployeeId = @EmployeeId;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader =>
            {
                var customKey = HrmsDatabase.GetString(reader, "FieldKey");
                if (!values.ContainsKey(customKey))
                {
                    values[customKey] = HrmsDatabase.GetString(reader, "FieldValue");
                } // ZYNORA_FIX14G_SAFE_CUSTOM_VALUES
                return true;
            });

        foreach (var field in BuildFieldDictionary().Values)
        {
            if (!values.ContainsKey(field.Key))
            {
                values[field.Key] = string.Empty;
            }
        }

        return values;
    }

    private async Task<List<UpdateBatch>> LoadBatchesAsync(int employeeId, string status)
    {
        var batches = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 50
    Id,
    EmployeeId,
    SectionKey,
    SectionName,
    Status,
    ISNULL(RequestedBy, '') AS RequestedBy,
    RequestedAt,
    ISNULL(LockedBy, '') AS LockedBy,
    LockedAt,
    ISNULL(Note, '') AS Note,
    ISNULL(EffectiveDate, CAST(RequestedAt AS date)) AS EffectiveDate,
    ISNULL(IsRetroactive, 0) AS IsRetroactive
FROM EmployeeUpdateBatches
WHERE EmployeeId = @EmployeeId AND Status = @Status
ORDER BY ISNULL(LockedAt, RequestedAt) DESC, Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Status", status);
            },
            reader => new UpdateBatch
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                SectionKey = HrmsDatabase.GetString(reader, "SectionKey"),
                SectionName = HrmsDatabase.GetString(reader, "SectionName"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                RequestedBy = HrmsDatabase.GetString(reader, "RequestedBy"),
                RequestedAt = HrmsDatabase.GetDateTime(reader, "RequestedAt"),
                EffectiveDate = HrmsDatabase.GetDateTime(reader, "EffectiveDate"),
                LockedBy = HrmsDatabase.GetString(reader, "LockedBy"),
                LockedAt = HrmsDatabase.GetDateTime(reader, "LockedAt"),
                Note = HrmsDatabase.GetString(reader, "Note"),
                IsRetroactive = HrmsDatabase.GetBool(reader, "IsRetroactive")
            });

        foreach (var batch in batches)
        {
            batch.Changes = await LoadChangesAsync(batch.Id);
        }

        return batches;
    }

    private async Task<UpdateBatch?> LoadSingleBatchAsync(int batchId)
    {
        var batches = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    Id,
    EmployeeId,
    SectionKey,
    SectionName,
    Status,
    ISNULL(RequestedBy, '') AS RequestedBy,
    RequestedAt,
    ISNULL(LockedBy, '') AS LockedBy,
    LockedAt,
    ISNULL(Note, '') AS Note,
    ISNULL(EffectiveDate, CAST(RequestedAt AS date)) AS EffectiveDate,
    ISNULL(IsRetroactive, 0) AS IsRetroactive
FROM EmployeeUpdateBatches
WHERE Id = @BatchId;
""",
            command => HrmsDatabase.AddParameter(command, "@BatchId", batchId),
            reader => new UpdateBatch
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                SectionKey = HrmsDatabase.GetString(reader, "SectionKey"),
                SectionName = HrmsDatabase.GetString(reader, "SectionName"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                RequestedBy = HrmsDatabase.GetString(reader, "RequestedBy"),
                RequestedAt = HrmsDatabase.GetDateTime(reader, "RequestedAt"),
                EffectiveDate = HrmsDatabase.GetDateTime(reader, "EffectiveDate"),
                LockedBy = HrmsDatabase.GetString(reader, "LockedBy"),
                LockedAt = HrmsDatabase.GetDateTime(reader, "LockedAt"),
                Note = HrmsDatabase.GetString(reader, "Note"),
                IsRetroactive = HrmsDatabase.GetBool(reader, "IsRetroactive")
            });

        var batch = batches.FirstOrDefault();
        if (batch is not null)
        {
            batch.Changes = await LoadChangesAsync(batch.Id);
        }

        return batch;
    }

    private async Task<List<UpdateChange>> LoadChangesAsync(int batchId)
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Id, BatchId, FieldKey, FieldLabel, ISNULL(OldValue, '') AS OldValue, ISNULL(NewValue, '') AS NewValue
FROM EmployeeUpdateChanges
WHERE BatchId = @BatchId
ORDER BY Id;
""",
            command => HrmsDatabase.AddParameter(command, "@BatchId", batchId),
            reader => new UpdateChange
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                BatchId = HrmsDatabase.GetInt(reader, "BatchId"),
                FieldKey = HrmsDatabase.GetString(reader, "FieldKey"),
                FieldLabel = HrmsDatabase.GetString(reader, "FieldLabel"),
                OldValue = HrmsDatabase.GetString(reader, "OldValue"),
                NewValue = HrmsDatabase.GetString(reader, "NewValue")
            });
    }

    private async Task ApplyCustomFieldAsync(int employeeId, string fieldKey, string fieldLabel, string value)
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF EXISTS (SELECT 1 FROM EmployeeCustomFields WHERE EmployeeId = @EmployeeId AND FieldKey = @FieldKey)
BEGIN
    UPDATE EmployeeCustomFields
    SET FieldLabel = @FieldLabel,
        FieldValue = @FieldValue,
        UpdatedAt = SYSUTCDATETIME()
    WHERE EmployeeId = @EmployeeId AND FieldKey = @FieldKey;
END
ELSE
BEGIN
    INSERT INTO EmployeeCustomFields (EmployeeId, FieldKey, FieldLabel, FieldValue, UpdatedAt)
    VALUES (@EmployeeId, @FieldKey, @FieldLabel, @FieldValue, SYSUTCDATETIME());
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@FieldKey", fieldKey);
                HrmsDatabase.AddParameter(command, "@FieldLabel", fieldLabel);
                HrmsDatabase.AddParameter(command, "@FieldValue", value ?? string.Empty);
            });
    }

    public string FieldValue(string key) => CurrentValues.TryGetValue(key, out var value) ? value : string.Empty;

    public string FieldDisplayValue(string key, string? value)
    {
        value ??= string.Empty;
        if (key.Equals("IsActive", StringComparison.OrdinalIgnoreCase))
        {
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "فعال" : "غير فعال";
        }

        if (Sections.SelectMany(section => section.Fields).Any(field =>
                field.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && field.InputType == "checkbox"))
        {
            return bool.TryParse(value, out var enabled) ? (enabled ? "نعم" : "لا") : "-";
        }

        if (key.Equals("DepartmentId", StringComparison.OrdinalIgnoreCase))
        {
            return Departments.FirstOrDefault(x => x.Id.ToString() == value)?.Name ?? "-";
        }

        if (key.Equals("DirectManagerId", StringComparison.OrdinalIgnoreCase)) // ZYNORA_FIX14G_DISPLAY_MANAGER
        {
            return ManagerOptions.FirstOrDefault(x => x.Id.ToString() == value)?.Name ?? "-";
        }

        if (key == "BranchId") return ProfileBranches.FirstOrDefault(x => x.Id.ToString() == value)?.Name ?? "-";
        if (key == "PositionId") return ProfilePositions.FirstOrDefault(x => x.Id.ToString() == value)?.Name ?? "-";
        if (key is "Gender" or "MaritalStatus") return OptionLabel(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        if (key is "BasicSalary" or "Allowances" or "Deductions" && decimal.TryParse(value, out var d))
        {
            return d.ToString("N0");
        }

        return value;
    }

    public string DisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
    public string DisplayDateTime(DateTime? value) => value.HasValue ? value.Value.ToString("dd/MM/yyyy HH:mm") : "-";
    public string DisplayDate(DateTime? value) => value.HasValue ? value.Value.ToString("dd/MM/yyyy") : "-";

    private static string ToInputDate(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd") : string.Empty;

    private static string ToDecimalString(System.Data.Common.DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) return string.Empty;
        var value = Convert.ToDecimal(reader.GetValue(ordinal));
        return value == 0 ? string.Empty : value.ToString("0.##");
    }

    private static string NormalizeValue(string? value) => (value ?? string.Empty).Trim();

    private string NormalizeTab(string? tab)
    {
        return tab?.ToLowerInvariant() switch
        {
            "confirm" => "confirm",
            "history" => "history",
            _ => "stage"
        };
    }

    private string NormalizeSection(string? section)
    {
        if (string.Equals(section, "basic", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(section, "recruitment", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(section, "contact", StringComparison.OrdinalIgnoreCase))
        {
            return "employee-info";
        }

        if (!string.IsNullOrWhiteSpace(section) && Sections.Any(x => x.Key.Equals(section, StringComparison.OrdinalIgnoreCase)))
        {
            return section;
        }

        return Sections[0].Key;
    }

    private string GetInitials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "م";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][0].ToString();
        return $"{parts[0][0]}{parts[^1][0]}";
    }

    private static List<UpdateSection> BuildSections()
    {
        return UseProfileFields(new List<UpdateSection>
        {
            new("employee-info", "معلومات الموظف", "البيانات الأساسية والتوظيف والاتصال في حركة واحدة: الاسم، المنصب، القسم، المدير، الهاتف، البريد",
                new()
                {
                    new("FullName", "اسم الموظف", "employee", "text", "مثال: محمد علي"),
                    new("EmployeeNo", "الرقم الوظيفي", "employee", "text", "مثال: EMP-001"),
                    new("NationalId", "رقم الهوية / البطاقة", "employee", "text", "مثال: 123456"),
                    new("BirthDate", "تاريخ الميلاد", "employee", "date", ""),
                    new("Position", "\u0627\u0644\u0645\u0646\u0635\u0628", "employee", "select-position", ""),
                    new("DepartmentId", "القسم / الفرع", "employee", "select-department", ""),
                    new("DirectManagerId", "\u0627\u0644\u0645\u062f\u064a\u0631 \u0627\u0644\u0645\u0628\u0627\u0634\u0631", "employee", "select-manager", ""),
                    new("HireDate", "تاريخ المباشرة", "employee", "date", ""),
                    new("Phone", "رقم الهاتف", "employee", "text", "مثال: 0770xxxxxxx"),
                    new("Email", "البريد الإلكتروني", "employee", "text", "name@company.com"),
                    new("IsActive", "حالة الموظف", "employee", "select-active", "")
                }),

            new("attendance", "معلومات الحضور", "الشفت، نظام الدوام، السماحية، ساعات العمل",
                new()
                {
                    new("ShiftName", "الشفت", "custom", "text", "مثال: Morning Shift"),
                    new("AttendanceRule", "نظام الدوام", "custom", "text", "مثال: HQ 7h / Site 9h"),
                    new("GraceMinutes", "سماحية التأخير بالدقائق", "custom", "number", "مثال: 10"),
                    new("WorkHours", "ساعات العمل اليومية", "custom", "number", "مثال: 9")
                }),

            new("financial", "المعلومات المالية", "الراتب الأساسي والبدلات والاستقطاعات",
                new()
                {
                    new("BasicSalary", "الراتب الأساسي", "compensation", "number", "مثال: 750000"),
                    new("Allowances", "البدلات", "compensation", "number", "مثال: 50000"),
                    new("Deductions", "الاستقطاعات الثابتة", "compensation", "number", "مثال: 0")
                }),

            new("payment", "معلومات الدفع", "طريقة الدفع، البنك، الحساب، العملة",
                new()
                {
                    new("PaymentMethod", "طريقة الدفع", "compensation", "text", "Cash / Bank / Card"),
                    new("BankName", "اسم البنك", "compensation", "text", "مثال: مصرف الرافدين"),
                    new("BankAccount", "رقم الحساب", "compensation", "text", "مثال: IQ..."),
                    new("Currency", "العملة", "compensation", "text", "IQD / USD")
                }),

            new("extra", "حقول إضافية", "أي معلومات إضافية تحتاجها الشركة",
                new()
                {
                    new("ContractType", "نوع العقد", "custom", "text", "دوام كامل / جزئي / مؤقت"),
                    new("Nationality", "\u0627\u0644\u062c\u0646\u0633\u064a\u0629", "employee", "select-nationality", ""),
                    new("Accommodation", "السكن", "custom", "text", "داخلي / خارجي"),
                    new("EmergencyContact", "جهة اتصال للطوارئ", "custom", "text", "الاسم والرقم")
                })
        });
    }

    // ZYNORA_FIX14A_DYNAMIC_PROFILE_FIELDS_METHOD_START
    private async Task<List<UpdateSection>> BuildSectionsWithDynamicFieldsAsync()
    {
        var sections = BuildSections();
        var existingKeys = sections
            .SelectMany(section => section.Fields)
            .Select(field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var definitions = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
IF OBJECT_ID(N'[dbo].[EmployeeProfileFieldDefinitions]', N'U') IS NULL
BEGIN
    SELECT
        CAST('' AS nvarchar(80)) AS SectionKey,
        CAST('' AS nvarchar(120)) AS FieldKey,
        CAST('' AS nvarchar(150)) AS FieldLabel,
        CAST('text' AS nvarchar(40)) AS FieldType,
        CAST('' AS nvarchar(max)) AS FieldOptions,
        CAST(0 AS bit) AS IsRequired,
        CAST(0 AS int) AS SortOrder
    WHERE 1 = 0;
END
ELSE
BEGIN
    SELECT
        ISNULL(SectionKey, '') AS SectionKey,
        ISNULL(FieldKey, '') AS FieldKey,
        ISNULL(FieldLabel, '') AS FieldLabel,
        ISNULL(FieldType, 'text') AS FieldType,
        ISNULL(FieldOptions, '') AS FieldOptions,
        ISNULL(IsRequired, 0) AS IsRequired,
        ISNULL(SortOrder, 0) AS SortOrder
    FROM EmployeeProfileFieldDefinitions
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
END
""",
            null,
            reader => new DynamicUpdateFieldDefinition
            {
                SectionKey = HrmsDatabase.GetString(reader, "SectionKey"),
                FieldKey = HrmsDatabase.GetString(reader, "FieldKey"),
                FieldLabel = HrmsDatabase.GetString(reader, "FieldLabel"),
                FieldType = HrmsDatabase.GetString(reader, "FieldType"),
                FieldOptions = HrmsDatabase.GetString(reader, "FieldOptions"),
                IsRequired = HrmsDatabase.GetBool(reader, "IsRequired"),
                SortOrder = HrmsDatabase.GetInt(reader, "SortOrder")
            });

        foreach (var group in definitions
            .Where(field => !string.IsNullOrWhiteSpace(field.FieldKey))
            .Where(field => !existingKeys.Contains(field.FieldKey))
            .GroupBy(field => NormalizeProfileSectionKey(field.SectionKey), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => ProfileSectionOrder(group.Key)))
        {
            var fields = group
                .OrderBy(field => field.SortOrder)
                .ThenBy(field => field.FieldLabel)
                .Select(field => new UpdateField(
                    field.FieldKey,
                    string.IsNullOrWhiteSpace(field.FieldLabel) ? field.FieldKey : field.FieldLabel,
                    group.Key == "financial" ? "financial-custom" : "custom",
                    NormalizeDynamicFieldInputType(field.FieldType),
                    string.Empty, field.FieldOptions, field.IsRequired))
                .ToList();

            if (fields.Count > 0)
            {
                sections.Add(new UpdateSection(
                    "profile-" + group.Key,
                    ProfileSectionName(group.Key),
                    ProfileSectionDescription(group.Key),
                    fields));
            }
        }

        return await WithProfileLookupsAsync(sections);
    }

    private static string NormalizeProfileSectionKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "basic" => "basic",
            "personal" => "personal",
            "job" => "job",
            "financial" => "financial",
            "additional" => "additional",
            _ => "additional"
        };
    }

    private static int ProfileSectionOrder(string key)
    {
        return key switch
        {
            "basic" => 10,
            "personal" => 20,
            "job" => 30,
            "financial" => 40,
            "additional" => 50,
            _ => 99
        };
    }

    private static string ProfileSectionName(string key)
    {
        return key switch
        {
            "basic" => "\u0627\u0644\u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629",
            "personal" => "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0634\u062E\u0635\u064A\u0629",
            "job" => "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0648\u0638\u064A\u0641\u064A\u0629",
            "financial" => "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0645\u0627\u0644\u064A\u0629",
            _ => "\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0625\u0636\u0627\u0641\u064A\u0629"
        };
    }

    private static string ProfileSectionDescription(string key)
    {
        return key switch
        {
            "basic" => "\u062D\u0642\u0648\u0644 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u064A\u0629 \u0636\u0645\u0646 \u0627\u0644\u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629",
            "personal" => "\u062D\u0642\u0648\u0644 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u064A\u0629 \u0636\u0645\u0646 \u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0634\u062E\u0635\u064A\u0629",
            "job" => "\u062D\u0642\u0648\u0644 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u064A\u0629 \u0636\u0645\u0646 \u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0648\u0638\u064A\u0641\u064A\u0629",
            "financial" => "\u062D\u0642\u0648\u0644 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u064A\u0629 \u0636\u0645\u0646 \u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0645\u0627\u0644\u064A\u0629",
            _ => "\u062D\u0642\u0648\u0644 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u064A\u0629 \u0627\u0644\u0625\u0636\u0627\u0641\u064A\u0629"
        };
    }

    private static string NormalizeDynamicFieldInputType(string? type)
    {
        return (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "number" => "number",
            "date" => "date",
            "textarea" => "textarea",
            "select" => "select",
            "checkbox" => "checkbox",
            _ => "text"
        };
    }
    // ZYNORA_FIX14A_DYNAMIC_PROFILE_FIELDS_METHOD_END
    // ZYNORA_FIX14C_STAGE_BLOCKS_START
    private static List<UpdateSection> BuildStageBlocks(List<UpdateSection> sections)
    {
        var result = new List<UpdateSection>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        UpdateSection? FindSection(string key) =>
            sections.FirstOrDefault(section => section.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        void AddField(List<UpdateField> target, UpdateField? field)
        {
            if (field is null)
            {
                return;
            }

            if (used.Add(field.Key))
            {
                target.Add(field);
            }
        }

        void AddByKeys(List<UpdateField> target, string sectionKey, params string[] keys)
        {
            var source = FindSection(sectionKey);
            if (source is null)
            {
                return;
            }

            foreach (var key in keys)
            {
                AddField(target, source.Fields.FirstOrDefault(field => field.Key.Equals(key, StringComparison.OrdinalIgnoreCase)));
            }
        }

        void AddFromSection(List<UpdateField> target, string sectionKey)
        {
            var source = FindSection(sectionKey);
            if (source is null)
            {
                return;
            }

            foreach (var field in source.Fields)
            {
                AddField(target, field);
            }
        }

        var basic = new List<UpdateField>();
        AddByKeys(basic, "employee-info", "EmployeeNo", "FullName", "NationalId", "BirthDate", "IsActive");
        AddFromSection(basic, "profile-basic");
        if (basic.Count > 0)
        {
            result.Add(new UpdateSection(
                "stage-basic",
                "\u0627\u0644\u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629",
                "\u0627\u0644\u062D\u0642\u0648\u0644 \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629 \u0645\u0646 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641.",
                basic));
        }

        var personal = new List<UpdateField>();
        AddByKeys(personal, "employee-info", "Phone", "Email", "Gender", "MaritalStatus", "Country", "IsCitizen", "PassportNo", "SponsorName", "Religion", "MotherCountry", "MotherCity", "PersonalEmail", "PhoneExtension");
        AddByKeys(personal, "extra", "Nationality", "Accommodation", "EmergencyContact");
        AddFromSection(personal, "profile-personal");
        if (personal.Count > 0)
        {
            result.Add(new UpdateSection(
                "stage-personal",
                "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0634\u062E\u0635\u064A\u0629",
                "\u062D\u0642\u0648\u0644 \u0627\u0644\u062A\u0648\u0627\u0635\u0644 \u0648\u0627\u0644\u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0634\u062E\u0635\u064A\u0629.",
                personal));
        }

        var job = new List<UpdateField>();
        AddByKeys(job, "employee-info", "BranchId", "DepartmentId", "PositionId", "DirectManagerId", "HireDate", "JoiningDate", "WorkType", "JobGrade", "ContractEndDate");
        AddByKeys(job, "extra", "ContractType");
        AddFromSection(job, "profile-job");
        if (job.Count > 0)
        {
            result.Add(new UpdateSection(
                "stage-job",
                "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0648\u0638\u064A\u0641\u064A\u0629",
                "\u0627\u0644\u0645\u0646\u0635\u0628 \u0648\u0627\u0644\u0642\u0633\u0645 \u0648\u062A\u0641\u0627\u0635\u064A\u0644 \u0627\u0644\u062A\u0648\u0638\u064A\u0641.",
                job));
        }

        var financial = new List<UpdateField>();
        AddFromSection(financial, "financial");
        AddFromSection(financial, "payment");
        AddFromSection(financial, "profile-financial");
        if (financial.Count > 0)
        {
            result.Add(new UpdateSection(
                "stage-financial",
                "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0645\u0627\u0644\u064A\u0629",
                "\u0627\u0644\u0631\u0627\u062A\u0628 \u0648\u0627\u0644\u062F\u0641\u0639 \u0648\u0627\u0644\u0628\u062F\u0644\u0627\u062A \u0648\u0627\u0644\u0627\u0633\u062A\u0642\u0637\u0627\u0639\u0627\u062A.",
                financial));
        }

        var additional = new List<UpdateField>();
        AddFromSection(additional, "profile-additional");
        AddFromSection(additional, "extra");

        foreach (var section in sections)
        {
            foreach (var field in section.Fields)
            {
                AddField(additional, field);
            }
        }

        if (additional.Count > 0)
        {
            result.Add(new UpdateSection(
                "stage-additional",
                "\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0625\u0636\u0627\u0641\u064A\u0629",
                "\u0623\u064A \u062D\u0642\u0648\u0644 \u0623\u062E\u0631\u0649 \u0645\u0631\u062A\u0628\u0637\u0629 \u0628\u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641.",
                additional));
        }

        if (result.Count == 0)
        {
            var fallback = FindSection("employee-info") ?? sections.First();
            result.Add(fallback);
        }

        return result;
    }
    // ZYNORA_FIX14C_STAGE_BLOCKS_END
    private Dictionary<string, UpdateField> BuildFieldDictionary()
    {
        return BuildSections()
            .SelectMany(x => x.Fields)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    // ZYNORA_FIX14A_DYNAMIC_PROFILE_FIELD_RECORD_START
    private sealed class DynamicUpdateFieldDefinition
    {
        public string SectionKey { get; set; } = string.Empty;
        public string FieldKey { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string FieldType { get; set; } = "text";
        public int SortOrder { get; set; }
        public string FieldOptions { get; set; } = "";
        public bool IsRequired { get; set; }
    }
    // ZYNORA_FIX14A_DYNAMIC_PROFILE_FIELD_RECORD_END
    public record UpdateSection(string Key, string Name, string Description, List<UpdateField> Fields);
    public record UpdateField(string Key, string Label, string Target, string InputType, string Placeholder,
        string Options = "", bool IsRequired = false, bool ReadOnly = false);

    public class UpdateEmployee
    {
        public static UpdateEmployee Empty => new() { FullName = "لا يوجد موظف", EmployeeNo = "-", Position = "-", DepartmentName = "-", BranchName = "-" };
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
    }

    public class DepartmentOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    // ZYNORA_FIX14G_LOOKUP_RECORD_START
    public class EmployeeLookupOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
    // ZYNORA_FIX14G_LOOKUP_RECORD_END

    public class UpdateBatch
    {
        public int Id { get; set; }

        /// <summary>
        /// رقم مرجعي مقروء بنمط كيان (EU26-147): البادئة + سنة الطلب + المعرّف.
        /// مشتق للعرض لا مخزَّن — المعرّف يضمن الثبات والفرادة بلا عمود جديد.
        /// </summary>
        public string RefNo => $"EU{RequestedAt:yy}-{Id}";

        public int EmployeeId { get; set; }
        public string SectionKey { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string RequestedBy { get; set; } = string.Empty;
        public DateTime? RequestedAt { get; set; }
        public DateTime? EffectiveDate { get; set; }

        /// <summary>
        /// حركة **بأثر رجعي** — قرارٌ يستوجب إعادة احتساب ما مضى، تمييزاً عن مجرّد
        /// تصحيح خطأ إدخال بتاريخٍ ماضٍ. الفرق يقرّر هل يُعاد حساب رواتب أم لا.
        /// </summary>
        public bool IsRetroactive { get; set; }

        public string LockedBy { get; set; } = string.Empty;
        public DateTime? LockedAt { get; set; }
        public string Note { get; set; } = string.Empty;
        public List<UpdateChange> Changes { get; set; } = new();
    }

    public class UpdateChange
    {
        public int Id { get; set; }
        public int BatchId { get; set; }
        public string FieldKey { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
    }
}

