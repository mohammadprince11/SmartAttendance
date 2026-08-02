using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

// اسمٌ مستعار لازم: صنف `ViolationTypePickerItem` فيه خاصية `PenaltyAction`
// تحجب الصنفَ العامّ ذا الاسم نفسه.
using Hrms = SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Violations;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<ViolationCaseRow> Items { get; private set; } = new();

    public List<EmployeePickerItem> EmployeeOptions { get; private set; } = new();

    public List<ViolationCategoryPickerItem> CategoryOptions { get; private set; } = new();

    public List<ViolationTypePickerItem> ViolationOptions { get; private set; } = new();

    public int TotalCount => Items.Count;

    public bool OpenCreateModal { get; private set; }

    [BindProperty]
    public CreateViolationInput Input { get; set; } = new();

    /// <summary>
    /// فلتر رد الموظف: all | pending (بانتظار الرد) | replied (تم الرد).
    ///
    /// ⚠️ <c>string?</c> **عمداً**: خاصيةٌ غير قابلة للعدم ومربوطة بالـGET يعدّها
    /// المُقيِّد «مطلوبة» حين لا يُمرَّر الفلتر بالرابط، فيرتدّ <c>ModelState</c>
    /// غير صالح **بمجرّد فتح الصفحة**. وهو ما كان يُبقي شريط «يرجى مراجعة الحقول
    /// المطلوبة» أحمرَ دائماً بالصفحة القديمة بلا سببٍ حقيقيّ حتى تعوّد المستخدم
    /// على تجاهله — وهو أسوأ ما يصيب رسالة خطأ.
    /// (<c>[ValidateNever]</c> جُرِّب أولاً فلم يُسقط الشرط؛ القابلية للعدم أسقطته.)
    /// وهذه فلاتر عرضٍ لا مدخلات: لا شيء فيها يُتحقَّق منه أصلاً.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? Reply { get; set; } = "all";

    /// <summary>
    /// التبويب المفتوح: work (ما ينتظرك) · log (السجلّ) · create (تسجيل مخالفة).
    /// يعبر إعادة التوجيه بعد الحفظ فلا تقفز الشاشة لأوّل تبويب.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; } = "work";

    public int PendingReplyCount => Items.Count(i => i.EmployeeReplyStatus == "Pending");

    // ---------------------------------------------------- الإجراءات التأديبية
    //
    // كانت شاشةً مستقلّة (`/Violations/Actions`) فيُخرجك فتحُها من المخالفات إلى
    // صفحةٍ أخرى وتفقد سياقك. صارت تبويباً هنا: **نُقل استعلامها ولم يُكرَّر**،
    // والشاشة القديمة تعيد التوجيه إلى هذا التبويب.
    //
    // الحالة (قابلة للطعن · نهائية · ساقطة) **مشتقّة من التهيئة لا مخزَّنة**:
    // تغيير مدّة الطعن أو الإسقاط بصفحة الإعدادات يعيد تصنيف كل صفٍّ فوراً بلا
    // تعديل بيانات. ولذلك تُصفّى بالذاكرة بعد القراءة لا بالاستعلام.

    public List<ActionsModel.ActionRow> DisciplinaryActions { get; private set; } = new();

    public int ObjectionDays { get; private set; }

    public int DropMonths { get; private set; }

    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>مرشّح حالة الإجراء: All · Open (قابل للطعن) · Final · Dropped.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ActionState { get; set; } = "All";

    public int OpenForObjection => DisciplinaryActions.Count(row =>
        ViolationConfigPolicy.CanObject(row.NotifiedOn, ObjectionDays, Today));

    public int DroppedActions => DisciplinaryActions.Count(row =>
        ViolationConfigPolicy.IsDropped(row.DecidedOn, DropMonths, Today));

    public decimal ActionsDeductions => DisciplinaryActions.Sum(row => row.DeductionAmount);

    public string ActionStatusOf(ActionsModel.ActionRow row) =>
        ViolationConfigPolicy.StatusLabel(row.NotifiedOn, row.DecidedOn, ObjectionDays, DropMonths, Today);

    /// <summary>
    /// تهيئة المخالفات وقوالب رسائلها — تُقرأ من `DisciplinaryConfigStore` نفسه
    /// الذي تقرأ منه صفحة الإعدادات، فلا نسختان تفترقان.
    ///
    /// ⚠️ **العرض هنا والكتابة هناك**: نماذج هذين التبويبين تُرسل إلى معالجات
    /// `/DisciplinaryRules` بـ`asp-page`، ولا يُنسخ منطق حفظٍ يمسّ سلّم الجزاءات.
    /// </summary>
    public DisciplinaryConfigStore.ConfigView Config { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Input.EventDate = DateTime.Today;
        await LoadPageDataAsync();
        await LoadDisciplinaryActionsAsync();
        Config = await DisciplinaryConfigStore.LoadAsync(_db);
    }

    private async Task LoadDisciplinaryActionsAsync()
    {
        ObjectionDays = int.TryParse(await DisciplinarySettingAsync(ViolationConfigPolicy.KeyObjectionDays), out var days) ? days : 0;
        DropMonths = int.TryParse(await DisciplinarySettingAsync(ViolationConfigPolicy.KeyDropMonths), out var months) ? months : 0;

        var all = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT c.Id, ISNULL(c.ReferenceNo, N'') AS ReferenceNo,
       ISNULL(e.FullName, N'') AS EmployeeName, ISNULL(e.EmployeeNo, N'') AS EmployeeNo,
       ISNULL(c.ViolationTitle, N'') AS ViolationTitle, c.FinalPenaltyAction,
       ISNULL(c.FinancialImpactType, N'None') AS FinancialImpactType,
       ISNULL(c.DeductionAmount, 0) AS DeductionAmount,
       c.EventDate, c.NotifiedOn, c.DecidedOn, c.LetterIssuedAt
FROM EmployeeViolationCases c
LEFT JOIN Employees e ON e.Id = c.EmployeeId
WHERE ISNULL(c.IsDeleted, 0) = 0
  AND (c.FinalPenaltyAction IS NOT NULL AND LTRIM(RTRIM(c.FinalPenaltyAction)) <> N'')
ORDER BY c.EventDate DESC, c.Id DESC;
""",
            _ => { },
            reader => new ActionsModel.ActionRow(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "ReferenceNo"),
                HrmsDatabase.GetString(reader, "EmployeeName"),
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "ViolationTitle"),
                HrmsDatabase.GetString(reader, "FinalPenaltyAction"),
                HrmsDatabase.GetString(reader, "FinancialImpactType"),
                HrmsDatabase.GetNullableDecimal(reader, "DeductionAmount") ?? 0,
                HrmsDatabase.GetDateOnly(reader, "EventDate") ?? default,
                HrmsDatabase.GetDateOnly(reader, "NotifiedOn"),
                HrmsDatabase.GetDateOnly(reader, "DecidedOn"),
                HrmsDatabase.GetDateTime(reader, "LetterIssuedAt")));

        DisciplinaryActions = ActionState switch
        {
            "Open" => all.Where(row => ViolationConfigPolicy.CanObject(row.NotifiedOn, ObjectionDays, Today)).ToList(),
            "Dropped" => all.Where(row => ViolationConfigPolicy.IsDropped(row.DecidedOn, DropMonths, Today)).ToList(),
            "Final" => all.Where(row =>
                !ViolationConfigPolicy.CanObject(row.NotifiedOn, ObjectionDays, Today)
                && !ViolationConfigPolicy.IsDropped(row.DecidedOn, DropMonths, Today)
                && row.DecidedOn is not null).ToList(),
            _ => all
        };
    }

    private async Task<string> DisciplinarySettingAsync(string key) =>
        await HrmsDatabase.ScalarAsync<string>(
            _db,
            "SELECT TOP 1 [Value] FROM DisciplinarySettings WHERE [Key] = @Key;",
            command => HrmsDatabase.AddParameter(command, "@Key", key)) ?? "0";

    private async Task<int> DisciplinaryIntSettingAsync(string key, int fallback) =>
        int.TryParse(await DisciplinarySettingAsync(key), out var value) ? value : fallback;

    /// <summary>
    /// تنبيهات «هام» باللائحة — تُعرض بعد التسجيل ولا تمنعه.
    ///
    /// **تنبيهٌ لا منع** عمداً: نصّ اللائحة يجعل عدم التجديد والإنهاء المباشر
    /// **قراراً إدارياً**، والنظام يقوله ولا يوقّعه. منعُ التسجيل هنا كان سيمنع
    /// توثيق المخالفة أصلاً، وهو عكس المقصود.
    /// </summary>
    private async Task<string?> BuildEscalationNoticeAsync(int employeeId, string categoryName)
    {
        if (!DisciplinaryPolicyRules.CountsForEscalation(categoryName))
        {
            return null;
        }

        var threshold = await DisciplinaryIntSettingAsync("NonRenewalThreshold", 0);
        var notices = new List<string>();

        // عدد جزاءات الفئتين (ب) و(ج) — نافذة العقد يقاربها العام الحالي.
        var count = await ScalarAsync<int>(
            """
SELECT COUNT(1) FROM EmployeeViolationCases
WHERE EmployeeId = @Id AND ISNULL(IsDeleted,0) = 0
  AND (ViolationCategory LIKE N'ب%' OR ViolationCategory LIKE N'ج%')
  AND EventDate >= @Since;
""",
            command =>
            {
                Add(command, "@Id", employeeId);
                Add(command, "@Since", new DateTime(DateTime.Today.Year, 1, 1));
            });

        if (DisciplinaryPolicyRules.ReachedNonRenewal(count, threshold))
        {
            notices.Add($"بلغ الموظف {count} جزاءات من الفئتين (ب) و(ج) — اللائحة تنصّ على عدم تجديد العقد عند {threshold} فأكثر.");
        }

        // إنذار بالفصل ما زال سارياً؟ السريان من تاريخ الإجراء ومدّته بقاعدته.
        var activeFinalWarning = await ScalarAsync<int>(
            """
SELECT COUNT(1)
FROM EmployeeViolationCases v
INNER JOIN DisciplinaryPenaltyRules r ON r.Id = v.PenaltyRuleId
WHERE v.EmployeeId = @Id AND ISNULL(v.IsDeleted,0) = 0
  AND r.ActionType = N'FinalWarning'
  AND DATEADD(MONTH, ISNULL(r.DropEvery, 12), v.EventDate) >= @Today;
""",
            command =>
            {
                Add(command, "@Id", employeeId);
                Add(command, "@Today", DateTime.Today);
            });

        if (DisciplinaryPolicyRules.TriggersImmediateTermination(activeFinalWarning > 0, categoryName))
        {
            notices.Add("على الموظف إنذار بالفصل ما زال سارياً — اللائحة تنصّ على إنهاء التعاقد مباشرة عند مخالفةٍ من الفئتين (ب) أو (ج).");
        }

        return notices.Count == 0 ? null : string.Join(" · ", notices);
    }

    /// <summary>مخالفات محجوزة على اعتماد اللجنة — الوجه الظاهر لبوّابة الاعتماد.</summary>
    public IReadOnlyList<ViolationCaseRow> AwaitingApproval =>
        Items.Where(i => i.Status == "بانتظار الاعتماد").ToList();

    /// <summary>
    /// اعتماد المخالفة بعد قرار اللجنة.
    ///
    /// يسجّل **من** اعتمد ومتى: قرارٌ يرتّب خصماً على راتب موظف لا يصحّ أن يبقى
    /// بلا صاحب. ولا يلمس المبالغ ولا العقوبة — يرفع الحالة فقط.
    /// </summary>
    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        await ViolationCaseSchema.EnsureAsync(_db);
        await ExecuteAsync(
            """
UPDATE EmployeeViolationCases
SET Status = N'موافق عليه',
    ApprovedBy = @By,
    ApprovedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND ISNULL(IsDeleted, 0) = 0 AND Status = N'بانتظار الاعتماد';
""",
            command =>
            {
                Add(command, "@Id", id);
                Add(command, "@By", User?.Identity?.Name ?? "—");
            });

        // الاعتماد هو لحظة «اتخاذ الإجراء» بمسار اللجنة — فيخرج الكتاب هنا.
        await DisciplinaryLetterStore.IssueAsync(_db, id, User?.Identity?.Name ?? "قسم الموارد البشرية");

        TempData["SuccessMessage"] = "اعتُمدت المخالفة، وصدر كتاب العقوبة، وسُجّل من اعتمدها وتاريخه.";
        return RedirectToPage(new { Reply, Tab });
    }

    /// <summary>طلب رد الموظف على المخالفة (حق الدفاع) — يظهر له ببوابته وإشعار.</summary>
    public async Task<IActionResult> OnPostRequestReplyAsync(int id)
    {
        await ViolationCaseSchema.EnsureAsync(_db);
        await ExecuteAsync(
            """
UPDATE EmployeeViolationCases
SET EmployeeReplyStatus = N'Pending', ReplyRequestedAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND ISNULL(IsDeleted, 0) = 0;

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
SELECT N'مطلوب ردك على مخالفة',
       N'سُجّلت بحقك مخالفة بالرقم المرجعي ' + v.ReferenceNo + N' — يرجى الرد عليها من بوابة الموظف.',
       'Employee', '/EmployeePortal?tab=requests'
FROM EmployeeViolationCases v WHERE v.Id = @Id;
""",
            command => Add(command, "@Id", id));

        TempData["SuccessMessage"] = "تم طلب رد الموظف — ستظهر المخالفة ببوابته للرد عليها.";
        return RedirectToPage(new { Reply, Tab });
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await ViolationCaseSchema.EnsureAsync(_db);

        if (Input.EmployeeId <= 0)
        {
            ModelState.AddModelError("Input.EmployeeId", "اختر الموظف بالكود أو الاسم.");
        }

        if (Input.CategoryId <= 0)
        {
            ModelState.AddModelError("Input.CategoryId", "اختر فئة المخالفة أولاً.");
        }

        if (Input.ViolationTypeId <= 0)
        {
            ModelState.AddModelError("Input.ViolationTypeId", "اختر نوع المخالفة.");
        }

        if (Input.EventDate == default)
        {
            ModelState.AddModelError("Input.EventDate", "حدد تاريخ الحدث.");
        }

        var employeeExists = await ScalarAsync<int>(
            "SELECT COUNT(1) FROM Employees WHERE Id = @Id AND ISNULL(IsDeleted, 0) = 0;",
            command => Add(command, "@Id", Input.EmployeeId));

        if (employeeExists == 0 && Input.EmployeeId > 0)
        {
            ModelState.AddModelError("Input.EmployeeId", "الموظف المحدد غير موجود.");
        }

        var selectedRule = Input.ViolationTypeId > 0
            ? await GetViolationRuleAsync(Input.ViolationTypeId)
            : null;

        if (selectedRule == null && Input.ViolationTypeId > 0)
        {
            ModelState.AddModelError("Input.ViolationTypeId", "نوع المخالفة المحدد غير موجود.");
        }

        if (selectedRule != null && Input.CategoryId > 0 && selectedRule.CategoryId != Input.CategoryId)
        {
            ModelState.AddModelError("Input.ViolationTypeId", "نوع المخالفة لا يتبع الفئة المختارة.");
        }

        // 🔒 معايير استحقاق المخالفة.
        //
        // مخالفةٌ مشروطة بفئة موظفين لا تُسجَّل على غيرهم: «التأخّر» لا معنى له لمن
        // عقده «عن بعد»، و«الزيّ الرسمي» لا يخصّ الإداريين.
        //
        // ⚠️ **مَنعٌ لا إخفاء.** كان بالوسع ترشيح المنسدلة فتختفي المخالفة ممن لا
        // يطابق — لكنّ الاختفاء يترك المسجِّل يبحث عن بندٍ يراه بلائحة الشركة ولا
        // يجده بالشاشة، بلا سبب معروض. والرسالة الصريحة تعلّمه القاعدة؛ والصمت
        // يجعله يسجّل تحت بندٍ آخر خطأً.
        if (selectedRule != null && Input.EmployeeId > 0)
        {
            var eligibility = HrConditions.Deserialize(selectedRule.ConditionsJson);

            if (!eligibility.IsEmpty)
            {
                var rows = await HrConditionFacts.LoadAsync(_db, Input.EmployeeId);
                var employee = rows.FirstOrDefault();

                // التاريخ المرجعي **تاريخ الحدث** لا اليوم: مخالفةٌ تُسجَّل بأثر رجعيّ
                // تُقاس بشروط الموظف حين وقعت، لا بشروطه بعد نقلٍ أو ترقية.
                var asOf = Input.EventDate == default
                    ? DateOnly.FromDateTime(DateTime.Today)
                    : DateOnly.FromDateTime(Input.EventDate);

                var matches = employee != null
                    && HrConditions.Matches(eligibility, HrConditionFacts.Build(employee, asOf), matchWhenEmpty: true);

                if (!matches)
                {
                    ModelState.AddModelError(
                        "Input.ViolationTypeId",
                        $"هذه المخالفة مشروطة بـ«{HrConditions.Describe(eligibility)}» والموظف المحدّد لا يطابقها.");
                }
            }
        }

        // 🔒 مادة 6 من اللائحة: تقادم الاتهام.
        //
        // «لا يجوز اتهام الموظف بمخالفة مضى على علم الشركة بها أكثر من ثلاثين
        // يوماً». كانت مادّةً بالوثيقة لا تمنع تسجيلاً بتاريخٍ قبل سنة. والمنع
        // هنا **يقيّد ولا يُحرّر**: أسوأ ما يفعله ردّ تسجيلٍ متأخّر، لا إصدار
        // عقوبةٍ ساقطةٍ بالتقادم.
        var staleDays = await DisciplinaryIntSettingAsync("StaleAccusationDays", 0);

        if (Input.EventDate != default
            && DisciplinaryPolicyRules.IsStale(
                DateOnly.FromDateTime(Input.EventDate), Today, staleDays))
        {
            ModelState.AddModelError(
                "Input.EventDate",
                $"مضى أكثر من {staleDays} يوماً على تاريخ الحدث — لا يجوز الاتهام بمخالفة متقادمة (مادة 6 من اللائحة).");
        }

        if (!ModelState.IsValid)
        {
            OpenCreateModal = true;
            await LoadPageDataAsync();
            return Page();
        }

        // 🔒 بوّابة اعتماد اللجنة.
        //
        // راية «تتطلّب موافقة اللجنة» بالتهيئة كانت تُحفظ **ولا تفعل شيئاً**: يستطيع
        // المسجِّل أن يعتمد المخالفة بنفسه لحظة إنشائها. وراية تَعِد بضبطٍ غير موجود
        // أسوأ من غيابها — يطمئنّ إليها من يقرأ الإعدادات وهي لا تمنع شيئاً.
        //
        // فحين تكون مفعّلة **لا تُقفَل المخالفة بالإنشاء**: تُحجز على «بانتظار
        // الاعتماد» مهما اختار المسجِّل، ولا تُعتمد إلا بإجراء منفصل يسجّل من اعتمد
        // ومتى. البوّابة **تقيّد ولا تُحرّر**: أسوأ ما قد تفعله تأخير اعتمادٍ، لا
        // إصدار عقوبةٍ بغير وجه.
        var config = await DisciplinaryConfigStore.LoadSettingsAsync(_db);
        var heldForCommittee = false;

        if (config.RequiresCommitteeApproval
            && (Input.Status == "موافق عليه" || Input.ActionStatus == "تم اتخاذ الإجراء"))
        {
            Input.Status = "بانتظار الاعتماد";
            Input.ActionStatus = "بانتظار الإجراء";
            heldForCommittee = true;
        }

        var penaltyAction = string.IsNullOrWhiteSpace(Input.FinalPenaltyAction)
            ? selectedRule!.PenaltyAction
            : Input.FinalPenaltyAction.Trim();

        var referenceNo = await GenerateReferenceNoAsync();

        await ExecuteAsync(
            """
INSERT INTO EmployeeViolationCases
(ReferenceNo, EmployeeId, ViolationTypeId, PenaltyRuleId, ViolationCategory, ViolationTitle,
 EventDate, Source, ActionStatus, Status, ProposedAction, FinalPenaltyAction,
 FinancialImpactType, FinancialImpactValue, DeductionAmount, Notes, CreatedAt, IsDeleted)
VALUES
(@ReferenceNo, @EmployeeId, @ViolationTypeId, @PenaltyRuleId, @ViolationCategory, @ViolationTitle,
 @EventDate, @Source, @ActionStatus, @Status, @ProposedAction, @FinalPenaltyAction,
 @FinancialImpactType, @FinancialImpactValue, @DeductionAmount, @Notes, SYSUTCDATETIME(), 0);
""",
            command =>
            {
                Add(command, "@ReferenceNo", referenceNo);
                Add(command, "@EmployeeId", Input.EmployeeId);
                Add(command, "@ViolationTypeId", Input.ViolationTypeId);
                Add(command, "@PenaltyRuleId", selectedRule!.PenaltyRuleId > 0 ? selectedRule.PenaltyRuleId : DBNull.Value);
                Add(command, "@ViolationCategory", selectedRule.CategoryName);
                Add(command, "@ViolationTitle", selectedRule.ViolationName);
                Add(command, "@EventDate", Input.EventDate.Date);
                Add(command, "@Source", string.IsNullOrWhiteSpace(Input.Source) ? "مباشر" : Input.Source.Trim());
                Add(command, "@ActionStatus", string.IsNullOrWhiteSpace(Input.ActionStatus) ? "بانتظار الإجراء" : Input.ActionStatus.Trim());
                Add(command, "@Status", string.IsNullOrWhiteSpace(Input.Status) ? "مسودة" : Input.Status.Trim());
                Add(command, "@ProposedAction", string.IsNullOrWhiteSpace(Input.ProposedAction) ? DBNull.Value : Input.ProposedAction.Trim());
                Add(command, "@FinalPenaltyAction", string.IsNullOrWhiteSpace(penaltyAction) ? DBNull.Value : penaltyAction);
                Add(command, "@FinancialImpactType", string.IsNullOrWhiteSpace(Input.FinancialImpactType) ? selectedRule.FinancialImpactType : Input.FinancialImpactType.Trim());
                Add(command, "@FinancialImpactValue", Input.FinancialImpactValue < 0 ? 0 : Input.FinancialImpactValue);
                Add(command, "@DeductionAmount", Input.DeductionAmount < 0 ? 0 : Input.DeductionAmount);
                Add(command, "@Notes", string.IsNullOrWhiteSpace(Input.Notes) ? DBNull.Value : Input.Notes.Trim());
            });

        // كتاب العقوبة يخرج لحظة اتخاذ الإجراء لا قبله: مخالفةٌ ما زالت مسوّدة أو
        // محجوزة على اللجنة لا كتاب لها بعد — وإصداره مبكراً يُبلّغ الموظف بعقوبةٍ
        // قد لا تُقرّ. والمحجوزة يخرج كتابها عند الاعتماد.
        var actionTaken = !heldForCommittee
            && (Input.ActionStatus == "تم اتخاذ الإجراء" || Input.Status == "موافق عليه");

        if (actionTaken)
        {
            var caseId = await ScalarAsync<int>(
                "SELECT TOP 1 Id FROM EmployeeViolationCases WHERE ReferenceNo = @Ref ORDER BY Id DESC;",
                command => Add(command, "@Ref", referenceNo));

            if (caseId > 0)
            {
                await DisciplinaryLetterStore.IssueAsync(_db, caseId, User?.Identity?.Name ?? "قسم الموارد البشرية");
            }
        }

        var notices = new List<string>();

        var escalation = await BuildEscalationNoticeAsync(Input.EmployeeId, selectedRule.CategoryName);
        if (!string.IsNullOrWhiteSpace(escalation))
        {
            notices.Add(escalation);
        }

        // مادة 6/ج: «لا يجوز فرض جزاء إلا بعد إشعار الموظف والتحقيق معه تحريرياً،
        // ويُكتفى بالشفهي بالمخالفات البسيطة».
        //
        // **تنبيهٌ لا منع**: نصّ المادة يجيز الشفهيّ بالبسيط، ومنعُ التسجيل كان
        // سيوقف توثيق المخالفة نفسها. والقاعدة معطّلة افتراضياً حتى تشغّلها الشركة.
        if (DisciplinaryPolicyRules.RequiresInvestigation(
                selectedRule.ActionType,
                enforce: bool.TryParse(await DisciplinarySettingAsync("RequireInvestigation"), out var enforce) && enforce)
            && Input.ActionStatus == "تم اتخاذ الإجراء")
        {
            notices.Add($"جزاءٌ جسيم ({PenaltyAction.Label(selectedRule.ActionType)}) — اللائحة تشترط التحقيق مع الموظف قبل فرضه (مادة 6). اطلب ردّه من زرّ «طلب ردّ الموظف».");
        }

        if (notices.Count > 0)
        {
            TempData["PolicyNotice"] = string.Join(" · ", notices);
        }

        TempData["SuccessMessage"] = heldForCommittee
            ? $"سُجّلت المخالفة {referenceNo} بحالة «بانتظار الاعتماد» — التهيئة تشترط موافقة اللجنة قبل نفاذها."
            : actionTaken
                ? $"تم تسجيل المخالفة {referenceNo} وصدر كتاب العقوبة."
                : $"تم تسجيل المخالفة بنجاح بالرقم المرجعي {referenceNo}.";
        return RedirectToPage();
    }

    private async Task LoadPageDataAsync()
    {
        await ViolationCaseSchema.EnsureAsync(_db);

        EmployeeOptions = await QueryAsync(
            """
SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName
FROM Employees
WHERE ISNULL(IsDeleted, 0) = 0 AND ISNULL(IsActive, 1) = 1
ORDER BY FullName;
""",
            reader => new EmployeePickerItem
            {
                Id = ToInt(reader["Id"]),
                EmployeeNo = ToStringValue(reader["EmployeeNo"]),
                FullName = ToStringValue(reader["FullName"])
            });

        CategoryOptions = await LoadCategoryOptionsAsync();
        ViolationOptions = await LoadViolationOptionsAsync();
        Items = await LoadViolationRowsAsync();
    }

    private async Task<List<ViolationCategoryPickerItem>> LoadCategoryOptionsAsync()
    {
        var exists = await ScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID('DisciplinaryViolationCategories', 'U') IS NOT NULL THEN 1 ELSE 0 END;");

        if (exists == 0)
        {
            return new List<ViolationCategoryPickerItem>();
        }

        return await QueryAsync(
            """
SELECT Id, Name
FROM DisciplinaryViolationCategories
WHERE ISNULL(IsActive, 1) = 1
ORDER BY ISNULL(DisplayOrder, 999), Name;
""",
            reader => new ViolationCategoryPickerItem
            {
                Id = ToInt(reader["Id"]),
                Name = ToStringValue(reader["Name"])
            });
    }

    private async Task<List<ViolationTypePickerItem>> LoadViolationOptionsAsync()
    {
        var exists = await ScalarAsync<int>(
            """
SELECT CASE WHEN OBJECT_ID('DisciplinaryViolationTypes', 'U') IS NOT NULL
          AND OBJECT_ID('DisciplinaryViolationCategories', 'U') IS NOT NULL
          THEN 1 ELSE 0 END;
""");

        if (exists == 0)
        {
            return new List<ViolationTypePickerItem>();
        }

        var hasPenaltyTable = await ScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID('DisciplinaryPenaltyRules', 'U') IS NOT NULL THEN 1 ELSE 0 END;");

        // العمود يُضاف بـ`DisciplinarySchema` وهذه الشاشة تقرأ بلا `Ensure`، فقاعدةٌ
        // لم تمرّ بعد على شاشة اللائحة تفتقده — والاستعلام يجب أن يُنقص عموداً لا أن
        // يُسقط تسجيل المخالفات كلّه.
        var hasConditions = await ScalarAsync<int>(
            "SELECT CASE WHEN COL_LENGTH('DisciplinaryViolationTypes','ConditionsJson') IS NOT NULL THEN 1 ELSE 0 END;");

        var conditionsColumn = hasConditions == 1
            ? "ISNULL(t.ConditionsJson, N'')"
            : "N''";

        var sql = hasPenaltyTable == 1
            ? $"""
SELECT
    t.Id,
    t.CategoryId,
    ISNULL(c.Name, N'') AS CategoryName,
    t.Name AS ViolationName,
    {conditionsColumn} AS ConditionsJson,
    ISNULL(p.Id, 0) AS PenaltyRuleId,
    ISNULL(p.PenaltyAction, N'') AS PenaltyAction,
    ISNULL(p.FinancialImpactType, N'None') AS FinancialImpactType,
    ISNULL(p.FinancialValue, 0) AS FinancialValue,
    ISNULL(p.ActionType, N'VerbalWarning') AS ActionType
FROM DisciplinaryViolationTypes t
LEFT JOIN DisciplinaryViolationCategories c ON c.Id = t.CategoryId
OUTER APPLY
(
    SELECT TOP 1 r.Id, r.PenaltyAction, r.FinancialImpactType, r.FinancialValue, r.ActionType
    FROM DisciplinaryPenaltyRules r
    WHERE r.ViolationTypeId = t.Id
      AND ISNULL(r.IsActive, 1) = 1
    ORDER BY r.OccurrenceFrom, r.Id
) p
WHERE ISNULL(t.IsActive, 1) = 1
ORDER BY ISNULL(c.DisplayOrder, 999), c.Name, t.Name;
"""
            : $"""
SELECT
    t.Id,
    t.CategoryId,
    ISNULL(c.Name, N'') AS CategoryName,
    t.Name AS ViolationName,
    {conditionsColumn} AS ConditionsJson,
    0 AS PenaltyRuleId,
    N'' AS PenaltyAction,
    N'None' AS FinancialImpactType,
    CAST(0 AS decimal(18,2)) AS FinancialValue,
    N'VerbalWarning' AS ActionType
FROM DisciplinaryViolationTypes t
LEFT JOIN DisciplinaryViolationCategories c ON c.Id = t.CategoryId
WHERE ISNULL(t.IsActive, 1) = 1
ORDER BY ISNULL(c.DisplayOrder, 999), c.Name, t.Name;
""";

        return await QueryAsync(sql, reader => new ViolationTypePickerItem
        {
            Id = ToInt(reader["Id"]),
            CategoryId = ToInt(reader["CategoryId"]),
            CategoryName = ToStringValue(reader["CategoryName"]),
            ViolationName = ToStringValue(reader["ViolationName"]),
            ConditionsJson = ToStringValue(reader["ConditionsJson"]),
            PenaltyRuleId = ToInt(reader["PenaltyRuleId"]),
            PenaltyAction = ToStringValue(reader["PenaltyAction"]),
            FinancialImpactType = ToStringValue(reader["FinancialImpactType"], "None"),
            FinancialValue = ToDecimal(reader["FinancialValue"]),
            ActionType = ToStringValue(reader["ActionType"], "VerbalWarning")
        });
    }

    private async Task<List<ViolationCaseRow>> LoadViolationRowsAsync()
    {
        return await QueryAsync(
            """
SELECT
    v.Id,
    v.ReferenceNo,
    v.EmployeeId,
    ISNULL(e.EmployeeNo, N'') AS EmployeeCode,
    ISNULL(e.FullName, N'') AS EmployeeName,
    ISNULL(e.Position, N'-') AS Position,
    ISNULL(d.Name, N'-') AS DepartmentName,
    ISNULL(b.Name, N'-') AS BranchName,
    ISNULL(v.ViolationCategory, N'') AS ViolationCategory,
    ISNULL(v.ViolationTitle, N'') AS ViolationTitle,
    v.EventDate,
    ISNULL(v.Source, N'') AS Source,
    ISNULL(v.ActionStatus, N'') AS ActionStatus,
    ISNULL(v.Status, N'') AS Status,
    ISNULL(v.FinalPenaltyAction, ISNULL(v.ProposedAction, N'')) AS FinalPenaltyAction,
    ISNULL(v.FinancialImpactType, N'None') AS FinancialImpactType,
    ISNULL(v.FinancialImpactValue, 0) AS FinancialImpactValue,
    ISNULL(v.DeductionAmount, 0) AS DeductionAmount,
    ISNULL(v.Notes, N'') AS Notes,
    ISNULL(v.EmployeeReplyStatus, N'NotRequested') AS EmployeeReplyStatus,
    ISNULL(v.EmployeeReply, N'') AS EmployeeReply,
    v.EmployeeRepliedAt,
    v.LetterIssuedAt
FROM EmployeeViolationCases v
LEFT JOIN Employees e ON e.Id = v.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = d.BranchId
WHERE ISNULL(v.IsDeleted, 0) = 0
  AND (@Reply = 'all'
       OR (@Reply = 'pending' AND ISNULL(v.EmployeeReplyStatus, N'NotRequested') = N'Pending')
       OR (@Reply = 'replied' AND ISNULL(v.EmployeeReplyStatus, N'NotRequested') = N'Replied'))
ORDER BY v.EventDate DESC, v.Id DESC;
""",
            reader => new ViolationCaseRow
            {
                Id = ToInt(reader["Id"]),
                ReferenceNo = ToStringValue(reader["ReferenceNo"]),
                EmployeeId = ToInt(reader["EmployeeId"]),
                EmployeeCode = ToStringValue(reader["EmployeeCode"]),
                EmployeeName = ToStringValue(reader["EmployeeName"]),
                Position = ToStringValue(reader["Position"], "-"),
                Branch = ToStringValue(reader["BranchName"], "-"),
                Department = ToStringValue(reader["DepartmentName"], "-"),
                ViolationCategory = ToStringValue(reader["ViolationCategory"]),
                ViolationTitle = ToStringValue(reader["ViolationTitle"]),
                EventDate = ToDate(reader["EventDate"]),
                Source = ToStringValue(reader["Source"]),
                ActionStatus = ToStringValue(reader["ActionStatus"]),
                Status = ToStringValue(reader["Status"]),
                FinalPenaltyAction = ToStringValue(reader["FinalPenaltyAction"]),
                FinancialImpactType = ToStringValue(reader["FinancialImpactType"], "None"),
                FinancialImpactValue = ToDecimal(reader["FinancialImpactValue"]),
                DeductionAmount = ToDecimal(reader["DeductionAmount"]),
                Notes = ToStringValue(reader["Notes"]),
                EmployeeReplyStatus = ToStringValue(reader["EmployeeReplyStatus"], "NotRequested"),
                EmployeeReply = ToStringValue(reader["EmployeeReply"]),
                EmployeeRepliedAt = reader["EmployeeRepliedAt"] as DateTime?,
                LetterIssuedAt = reader["LetterIssuedAt"] as DateTime?
            },
            command => Add(command, "@Reply", string.IsNullOrWhiteSpace(Reply) ? "all" : Reply));
    }

    private async Task<ViolationTypePickerItem?> GetViolationRuleAsync(int violationTypeId)
    {
        return (await LoadViolationOptionsAsync()).FirstOrDefault(x => x.Id == violationTypeId);
    }

    private async Task<string> GenerateReferenceNoAsync()
    {
        var prefix = $"VC{DateTime.Today:yy}-";
        var count = await ScalarAsync<int>(
            "SELECT COUNT(1) FROM EmployeeViolationCases WHERE ReferenceNo LIKE @Prefix;",
            command => Add(command, "@Prefix", prefix + "%"));

        return $"{prefix}{count + 1:000}";
    }

    private async Task ExecuteAsync(string sql, Action<IDbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<T> ScalarAsync<T>(string sql, Action<IDbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            var value = await command.ExecuteScalarAsync();

            if (value == null || value == DBNull.Value)
            {
                return default!;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<T>> QueryAsync<T>(string sql, Func<IDataRecord, T> map, Action<IDbCommand>? configure = null)
    {
        var result = new List<T>();
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(map(reader));
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    private static void Add(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static int ToInt(object value) => value == DBNull.Value ? 0 : Convert.ToInt32(value);

    private static decimal ToDecimal(object value) => value == DBNull.Value ? 0 : Convert.ToDecimal(value);

    private static DateTime ToDate(object value) => value == DBNull.Value ? DateTime.Today : Convert.ToDateTime(value);

    private static string ToStringValue(object value, string fallback = "") => value == DBNull.Value ? fallback : Convert.ToString(value) ?? fallback;
}

public sealed class CreateViolationInput
{
    [Required(ErrorMessage = "اختر الموظف بالكود أو الاسم.")]
    [Display(Name = "الموظف")]
    public int EmployeeId { get; set; }

    [Required(ErrorMessage = "اختر فئة المخالفة.")]
    [Display(Name = "الفئة")]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "اختر نوع المخالفة.")]
    [Display(Name = "نوع المخالفة")]
    public int ViolationTypeId { get; set; }

    [Required(ErrorMessage = "حدد تاريخ الحدث.")]
    [DataType(DataType.Date)]
    [Display(Name = "تاريخ الحدث")]
    public DateTime EventDate { get; set; } = DateTime.Today;

    [Required]
    [StringLength(50)]
    [Display(Name = "المصدر")]
    public string Source { get; set; } = "مباشر";

    [Required]
    [StringLength(100)]
    [Display(Name = "حالة الإجراء")]
    public string ActionStatus { get; set; } = "بانتظار الإجراء";

    [Required]
    [StringLength(100)]
    [Display(Name = "الحالة")]
    public string Status { get; set; } = "مسودة";

    [StringLength(300)]
    [Display(Name = "العقوبة")]
    public string? FinalPenaltyAction { get; set; }

    [StringLength(40)]
    [Display(Name = "نوع أثر اللائحة")]
    public string FinancialImpactType { get; set; } = "None";

    [Range(0, 999999999)]
    [Display(Name = "قيمة أثر اللائحة")]
    public decimal FinancialImpactValue { get; set; }

    [Range(0, 999999999)]
    [Display(Name = "مبلغ الخصم بالدينار")]
    public decimal DeductionAmount { get; set; }

    [StringLength(300)]
    [Display(Name = "الإجراء المقترح")]
    public string? ProposedAction { get; set; }

    [StringLength(1000)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public sealed class EmployeePickerItem
{
    public int Id { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    public string SearchText => $"{EmployeeNo} - {FullName}";
}

public sealed class ViolationCategoryPickerItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class ViolationTypePickerItem
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string ViolationName { get; set; } = string.Empty;
    public int PenaltyRuleId { get; set; }
    public string PenaltyAction { get; set; } = string.Empty;
    public string FinancialImpactType { get; set; } = "None";
    public decimal FinancialValue { get; set; }

    /// <summary>معايير استحقاق المخالفة. فارغ ⟹ تنطبق على الجميع.</summary>
    public string ConditionsJson { get; set; } = string.Empty;

    /// <summary>
    /// نوع إجراء الدرجة الأولى — يقرّر أيلزم التحقيق قبل فرضه (مادة 6/ج).
    ///
    /// ⚠️ الاسم مؤهَّل بالمساحة: خاصية `PenaltyAction` أعلاه بهذا الصنف نفسه
    /// تحجب الصنفَ العامّ ذا الاسم نفسه.
    /// </summary>
    public string ActionType { get; set; } = Hrms.PenaltyAction.VerbalWarning;
}

public sealed class ViolationCaseRow
{
    public int Id { get; set; }
    public string ReferenceNo { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string ViolationCategory { get; set; } = string.Empty;
    public string ViolationTitle { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ActionStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FinalPenaltyAction { get; set; } = string.Empty;
    public string FinancialImpactType { get; set; } = "None";
    public decimal FinancialImpactValue { get; set; }
    public decimal DeductionAmount { get; set; }
    public string Notes { get; set; } = string.Empty;
    /// <summary>تاريخ إصدار كتاب العقوبة — فارغ ⟹ لم يصدر بعد.</summary>
    public DateTime? LetterIssuedAt { get; set; }

    public string EmployeeReplyStatus { get; set; } = "NotRequested";
    public string EmployeeReply { get; set; } = string.Empty;
    public DateTime? EmployeeRepliedAt { get; set; }

    public string ReplyStatusText => EmployeeReplyStatus switch
    {
        "Pending" => "بانتظار رد الموظف",
        "Replied" => "تم الرد",
        _ => "—"
    };

    public string Avatar => string.IsNullOrWhiteSpace(EmployeeName) ? "؟" : EmployeeName.Trim()[0].ToString();

    public string StatusCss => Status switch
    {
        "موافق عليه" => "ok",
        "تم اتخاذ الإجراء" => "done",
        "بانتظار الاعتماد" => "pending",
        "مرفوض" => "rejected",
        _ => "draft"
    };

    public string DisplayFinancialImpact => FinancialImpactType switch
    {
        "Days" => $"{FinancialImpactValue:0.##} يوم حسب اللائحة",
        "Hours" => $"{FinancialImpactValue:0.##} ساعة حسب اللائحة",
        "Amount" => $"{FinancialImpactValue:0.##} مبلغ ثابت حسب اللائحة",
        _ => "لا يوجد"
    };
}
