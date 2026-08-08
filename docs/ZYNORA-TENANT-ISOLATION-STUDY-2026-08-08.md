# ZYNORA — دراسة عزل الشركات والأداء قبل الإنتاج (المراحل 0–8 و28)

**التاريخ:** 2026-08-08 · **الحالة:** 📋 **دراسة فقط — لم يُعدَّل سطر كود واحد**
**الفرع:** `claude/smartattendance-local-rebuild-wftwb3`

> كل ادّعاء بهذا المستند مرفق بموضعه من الكود. ما لم أقِسه لم أدّعِه، وما لم
> أُثبته سمّيته **اشتباهاً لم يُتحقَّق منه** لا نتيجة.

---

## 0) الحكم المبكّر — اقرأ هذا أولاً

النظام **يملك بنية عزل صحيحة ومصمَّمة جيداً، ولا يستعملها إلا 8 صفحات من أصل
عشرات.** المشكلة ليست غياب التصميم — بل أن التصميم **مكتبة لا يستدعيها أحد**
خارج الرواتب والمستندات.

### 🔴 تصحيح — قِستُ التوزيع فتبيّن أن تقديري الأول كان خاطئاً

كتبتُ بالنسخة الأولى من هذا المستند أن الثغرات «ليست تسريباً جارياً» استناداً
إلى `docs/COMPANY-ID-MIGRATION-PLAN.md` (2026-07-31: «شركتان، الثانية صفر
موظفين»). **قِستُ الأمر بنفسي على نسخة من قاعدة الإنتاج فظهر غير ذلك:**

```sql
SELECT CompanyId, COUNT(*), SUM(CASE WHEN IsActive=1 THEN 1 ELSE 0 END)
FROM Employees WHERE ISNULL(IsDeleted,0)=0 GROUP BY CompanyId;
```

| الشركة | موظفون | نشطون |
|---|---|---|
| 1 | 1,023 | 996 |
| 2 | 894 | 874 |
| 3 | 877 | 854 |

**ثلاث شركات — كلها مأهولة وكلها نشطة. تعدّد المستأجرين يعمل بالإنتاج الآن.**
(وثيقة يوليو صارت بائتة؛ 2,794 موظفاً موزّعين لا 1,357 بشركة واحدة.)

⟹ الثغرات المُثبتة أدناه **قابلة للاستغلال اليوم لا غداً**. تحديداً:

- مستخدم الشركة (1) يضغط «تحديث الحضور» ⟹ **تُحذف يوميات 1,728 موظفاً
  بالشركتين (2) و(3)** وتُعاد بناؤها بمناوبةٍ اختارها هو.
- القراءات والإشعارات تعبر الشركات الثلاث.

هذه ليست «جاهزية إنتاج» — هذه **حالة قائمة تحتاج إصلاحاً**.

> ✅ الرواتب استثناء مثبَت: دفعة المسير الحيّة `2026-8-1` كانت **874 موظفاً** —
> مطابقة تماماً لعدد نشطي الشركة (2) وحدها. عزل `PayrollRunStore` يعمل فعلياً.

---

## المرحلة 0 — تقرير فهم النظام

### Solution structure

| المشروع | الدور |
|---|---|
| `SmartAttendance.Domain` | الكيانات (`Employee` وغيرها) |
| `SmartAttendance.Application` | `PeopleDataScope` · `IEffectiveScopeService` |
| `SmartAttendance.Infrastructure` | `ApplicationDbContext` + **40 هجرة EF** |
| `SmartAttendance.Web` | الصفحات + **~110 متجراً بـSQL خام** تحت `Infrastructure/Hrms` |
| `SmartAttendance.API` · `MobileApp` | REST بتوكن Bearer |
| `SmartAttendance.Tests` | 90 ملف اختبار · 1299 اختباراً |
| `SmartAttendance.E2E` | موجود |

### مصدر الحقيقة — ثلاث آليات مخطط متعايشة ⚠️

1. **هجرات EF Core** — 40 هجرة بـ`SmartAttendance.Infrastructure/Migrations`.
2. **`SqlSchemaMigrator.ApplyAsync`** — هجرات محكومة بجدول `__SchemaMigrations`،
   تعمل **مرة واحدة عند الإقلاع** (`Program.cs:447`)، ومحميّة بـ**`sp_getapplock`**
   (`SqlSchemaMigrator.cs:1134`) ⟵ سباق النسختين معالَج فعلاً.
3. **`EnsureAsync` بكل متجر** — شفاء ذاتي يُنشئ الجدول عند أول استعمال.

**الخلاصة:** لا يوجد مصدر حقيقة واحد. الثلاثة تعمل معاً، وأيّها ينشئ جدولاً
بعينه يُعرف بالقراءة فقط. (المرحلة 19 — يحتاج سياسة معلنة.)

### Authentication / Authorization

```
Cookie أو Bearer
  ↓ SessionSecurityValidator (SecurityStamp — إبطال الجلسات البائتة)
  ↓ RoleSecurityMiddleware
  ↓ PageCatalog / RoleRouteCatalog  (صلاحية الصفحة: "Attendance.DayAttendance")
  ↓ IEffectiveScopeService → PeopleDataScope  (نطاق الموظفين)
```

### Company/Tenant flow — **موجود ومصمَّم جيداً**

| المكوّن | الملف | التقييم |
|---|---|---|
| `CompanyScope` | `Security/CompanyScope.cs` | **مغلق الفشل بالتصميم**: لا مُنشئ عامّ يعطي «مسموح بكل شيء» صدفةً · `DeniedAll` عند أي شكّ · `ToSqlPredicate` يعطي `1=0` لا يُهمل الشرط |
| `CompanyScopeProvider` | نفس الملف | يبني النطاق من `IEffectiveScopeService` — **لا يخترع مصدر حقيقة ثانياً** |
| `EmployeeCompanyGuard` | `Security/EmployeeCompanyGuard.cs` | يتحقّق من ملكية `employeeId` قبل أي عملية |
| `CompanySelectionContext` | `CompanyContext/` | الشركة الفعّالة بكوكي — **يتحقّق من `allowedCompanyIds` فلا يُزوَّر** |

> ✅ الإجابة على سؤالك «هل يوجد Active Company Context؟» — **نعم**، ومصمَّم
> بشكل صحيح، وغير قابل للتزوير عبر الكوكي (`CompanySelectionContext.cs:24-38`).

### 🔴 لكن — من يستهلك هذا التصميم؟

```
ICompanyScopeProvider مستهلَك بـ 8 صفحات فقط:
  Documents/View · EmployeeDocuments · LeaveBalances/Adjust
  Payroll/{FinancialRequests, Loans, RunDetail, Runs, TerminationSettlement}

EmployeeCompanyGuard مستهلَك بـ 6 صفحات فقط (نفس القائمة تقريباً)

CompanyScope كوسيط بمتاجر Hrms: متجر واحد من ~110
  → PayrollRunStore.cs
```

**صفر صفحة حضور. صفر متجر حضور.**

### بنية الحضور — `DayAttendances` بلا `CompanyId`

`DayAttendanceStore.cs:151-167`:

```sql
CREATE TABLE DayAttendances (
    Id, EmployeeId, WorkDate, ShiftTypeId, DayKind,
    CheckIn, CheckOut, LateHours, EarlyLeaveHours,
    WorkedHours, Status, IsAnalyzed, AnalyzedAt
);   -- لا CompanyId
```

⟹ العزل **لا يمكن** أن يكون عمود فلترة مباشراً؛ لا بدّ من الوصل بـ`Employees`.

**وهذا ممكن الآن:** `Employee.CompanyId` **موجود ومملوء**
(`Domain/Entities/Employee.cs:131` · `SqlSchemaMigrator.cs:63-82` مع
`IX_Employees_CompanyId`)، والتشخيص أثبت **صفر موظف بلا فرع صالح**.

> ⚠️ **تصحيح مستند قائم:** `docs/COMPANY-ISOLATION-AUDIT.md` (2026-07-31) يقول
> «كيان `Employee` لا يحمل `CompanyId` إطلاقاً» ويبني عليه أن العزل البنيوي
> «خارج النطاق». **هذا لم يعد صحيحاً** — العمود أُضيف والبيانات نظيفة.
> العائق الذي بُرِّر به التأجيل **زال**.

---

## المراحل 3–4 — تتبّع كامل لكل شبهة (Entry Point ← SQL)

### 🔴 P0-1 — تحليل الحضور بلا نطاق شركة · **مُثبَت**

```
POST /DayAttendance  (handler: تحديث الحضور)
  ↓ RoleSecurityMiddleware ✅ يتحقّق من صلاحية الصفحة فقط
  ↓ Pages/DayAttendance/Index.cshtml.cs:325
      ⟵ صفر إشارة لـCompanyScope بالملف كله (فُحص بالـgrep)
  ↓ DayAttendanceStore.AnalyzeMonthAsync(dbContext, year, month, shiftTypeId)
      ⟵ التوقيع لا يحمل شركة إطلاقاً
  ↓ DayAttendanceStore.cs:246
```

```csharp
var employees = await dbContext.Employees.AsNoTracking()
    .Where(e => e.IsActive)          // ← كل الشركات
    .ToListAsync();
```

```
  ↓ DayAttendanceStore.cs:280  — AttendanceRecords بلا شرط شركة
  ↓ ShiftTypeStore · EmployeeShiftTypeStore · ShiftOverrideStore ·
    RosterStore · PunchSemanticStore  — **كلها بلا شركة**
```

**النتيجة:** مطابقة للشبهة حرفياً. ولاحظ أن فلترة `Employees` وحدها لن تكفي —
ستة مصادر مدخلات أخرى تدخل بيانات شركات أخرى للتحليل (المرحلة 12 محقّة).

### 🔴 P0-2 — حذف يوميات كل الشركات · **مُثبَت — الأخطر**

`DayAttendanceStore.cs:404-411`:

```sql
DELETE FROM DayAttendances WHERE WorkDate >= @From AND WorkDate <= @To;
```

بلا أي شرط شركة أو موظف. داخل معاملة تعيد البناء بعدها.

**سيناريو الضرر:** مستخدم الشركة (أ) يضغط «تحديث الحضور» ⟹ **تُحذف يوميات
الشركة (ب) كاملةً للفترة** ثم يُعاد بناؤها بمناوبة افتراضية اختارها هو.
هذا ليس تسريب قراءة — هذا **تدمير بيانات عابر للشركات**، وينتقل أثره للرواتب
لأن `EmployeeMonthAttendance` يُبنى من `DayAttendances`.

### 🔴 P0-3 — تعديل يوم حضور بلا إثبات ملكية · **مُثبَت**

`DayAttendanceStore.cs:569`:

```csharp
public static async Task<bool> UpdateDayAsync(
    ApplicationDbContext dbContext, int employeeId, DateOnly date, ...)
```

`employeeId` يأتي من المتصفّح. الاستعلام:
`... WHERE EmployeeId=@Emp AND WorkDate=@Date` — بلا التحقّق من أن الموظف ضمن
نطاق المستخدم. `EmployeeCompanyGuard` **موجود ولا يُستدعى هنا**.

### 🔴 P0-4 — قراءة الحضور بلا نطاق · **مُثبَت**

`DayAttendanceStore.ListRangeAsync` (سطر 1014) — الاستعلام (سطر 1047+):

```sql
FROM DayAttendances d
INNER JOIN Employees e ON e.Id = d.EmployeeId
LEFT JOIN ShiftTypes s ON s.Id = d.ShiftTypeId
WHERE d.WorkDate >= @From AND d.WorkDate <= @To
ORDER BY e.EmployeeNo, d.WorkDate;
```

الوصل بـ`Employees` **قائم بالفعل** ⟹ إضافة `AND (شرط CompanyScope على e.CompanyId)`
تكلفتها سطر واحد. تُستهلك من: `/DayAttendance` · `/AttendanceViewer` · **وزرّ
الإشعار**.

### 🔴 P0-5 — الإشعارات تتبع نفس النطاق المكسور · **مُثبَت**

`Pages/DayAttendance/Index.cshtml.cs:355` (`OnPostNotifyAsync`) ← سطر 367 يستدعي
`ListRangeAsync` نفسها ⟹ **يُرسل لموظفي كل الشركات**.

> ⚠️ **ملاحظة صدق:** يوجد `SmartAttendance.Tests/DayAttendanceNotifyScopeTests.cs`
> واسمه يوحي بأن النطاق مُختبَر. قرأتُه: **يختبر تطابق فلتر الحالة بين العرض
> والزرّ، لا عزل الشركات.** الاسم مضلِّل ولا يحرس ما نبحث عنه.

### 🟠 P1-1 — الترقيم بالذاكرة لا بالـSQL · **مُثبَت**

`Pages/DayAttendance/Index.cshtml.cs:169-192`:

```csharp
var all = await DayAttendanceStore.ListRangeAsync(...);   // كل الصفوف
PresentCount = all.Count(...); LateCount = all.Count(...); // عدّ بالذاكرة
var view = ApplyStatusFilter(all).OrderByDescending(...).ToList();
Rows = view.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();  // 50
```

بالمقياس المستهدف (10,000 موظف × 30 يوماً) = **~300,000 صفّاً محمّلاً لعرض 50**.
وكل صفّ يحمل **أربعة استعلامات `EXISTS` مترابطة** (`AttendanceRecords` ·
`LeaveRequests` · `SelfServiceRequests` · فحص `AnalyzedAt`) لحساب عمود `IsStale`.

### 🟠 P1-2 — Attendance Viewer: نفس النمط مضاعفاً · **مُثبَت**

`Pages/AttendanceViewer/Index.cshtml.cs`:

```
:130  ListRangeAsync(...)            ← كل الصفوف، بلا شركة
:142  matchIds = ... ToListAsync()   ← فلترة الموظفين بالذاكرة
:147  .GroupBy(r => (EmployeeId,...))← تجميع بـC#
:164  employees.Skip(...).Take(20)   ← لعرض 20 موظفاً
:181-193 Departments/Branches/Positions/Employees ← **lookups بلا شركة**
```

مطابق حرفياً لما وصفته بالمرحلة 7. والـlookups تسريب هيكل تنظيمي (المرحلة 15).

### 🔴 P0-6 — التطوير والإنتاج على **نفس قاعدة البيانات** · **مُثبَت**

```
appsettings.json             → Server=localhost;Database=SmartAttendance
appsettings.Development.json → Server=localhost;Database=SmartAttendance
```

**نصّان متطابقان.** لا Staging إطلاقاً. ⟹ `dotnet run` بالتطوير يكتب على قاعدة
الإنتاج، و`SqlSchemaMigrator` يعمل عند كل إقلاع ⟹ **هجرة تلقائية من التطوير
للإنتاج** — وهو ما تمنعه المرحلة 18 صراحةً.

هذا **ليس اشتباهاً**: حدث فعلياً بهذه الجلسة — أنشأتُ ثابت `MinimumWage` للفحص
الحيّ على المنفذ 5092 فكُتب بقاعدة الإنتاج، وحذفتُه بعدها.

---

## المرحلة 5 — التصنيف

### P0 — حاجز إنتاج (قبل تشغيل الشركة الثانية)

| # | الثغرة | الموضع |
|---|---|---|
| P0-1 | تحليل الحضور يقرأ كل الشركات | `DayAttendanceStore.cs:246,280` |
| P0-2 | **حذف يوميات كل الشركات** | `DayAttendanceStore.cs:406` |
| P0-3 | تعديل يوم بلا إثبات ملكية | `DayAttendanceStore.cs:569` |
| P0-4 | قراءة الحضور بلا نطاق | `DayAttendanceStore.cs:1047` |
| P0-5 | إشعارات عابرة للشركات | `DayAttendance/Index.cshtml.cs:367` |
| P0-6 | تطوير وإنتاج على نفس القاعدة | `appsettings*.json` |

### P1 — عالية

| # | المشكلة |
|---|---|
| P1-1 | ترقيم `/DayAttendance` بالذاكرة (~300 ألف صفّ لعرض 50) |
| P1-2 | `/AttendanceViewer` تحميل + `GroupBy` بالذاكرة لعرض 20 |
| P1-3 | lookups (أقسام/فروع/مسميات/موظفون) بلا نطاق شركة |
| P1-4 | `DayAttendances` بلا فهرس على `WorkDate` (الفهرس الوحيد `(EmployeeId, WorkDate)` — والاستعلام يبدأ بـ`WorkDate`) |
| P1-5 | خدمتا الخلفية (`NotificationDispatcherService` · `NotificationRuleGeneratorService`) لم أتتبّع نطاقهما بعد |

### P2 — متوسطة / دين تقني

| # | المشكلة |
|---|---|
| P2-1 | ثلاث آليات مخطط متعايشة بلا سياسة معلنة |
| P2-2 | `docs/COMPANY-ISOLATION-AUDIT.md` **بائت** ويضلّل بحجّة زالت |
| P2-3 | اسم `DayAttendanceNotifyScopeTests` يوحي بحراسة عزل لا يحرسها |
| P2-4 | لا مؤشّر «الشركة الفعّالة» بواجهة صفحات الحضور |

### اشتباهات **لم أتحقّق منها بعد** (لا أصنّفها)

`MonthAttendanceStore` · `WeekAttendanceStore` · `AttendanceRecords` (شاشة
السجلات) · `RecommendationStore` · `LoanStore` · `FinancialRequestStore` ·
`AcknowledgmentStore` · `ContractRegisterStore` · `DocumentTemplateStore` ·
`RosterStore` · `MissingPunchRequestStore` · واجهات الـAPI والموبايل ·
مسارات الاستيراد/التصدير.

---

## المرحلة 28 — التصميم المقترح (للمراجعة قبل التنفيذ)

### Root Cause واحد لا ستّة

> **العزل مصمَّم كـ«خدمة اختيارية تستدعيها الصفحة»، لا كقيد تفرضه طبقة الوصول.**

فأي صفحة جديدة تنسى الاستدعاء ⟹ ثغرة. وقد نُسي بكل صفحات الحضور.

### مبدأ التصميم: اجعل النسيان مستحيلاً لا مذموماً

**لن أفعل** `.Where(x => x.CompanyId == id)` بعشرات الملفات (المرحلة 8 تمنعه،
وأنا أوافق).

**البناء فوق القائم لا بجواره:** `CompanyScope` + `EmployeeCompanyGuard` تصميمهما
صحيح — المطلوب **تغيير نقطة الفرض**، من الصفحة إلى **توقيع المتجر**:

```
AnalyzeMonthAsync(db, year, month, shiftTypeId)
        ↓ يصير
AnalyzeMonthAsync(db, CompanyScope scope, year, month, shiftTypeId)
```

بجعل `CompanyScope` **وسيطاً إلزامياً** (لا اختيارياً بقيمة افتراضية)، **المترجم
نفسه يمنع النسيان** — كل نداء قائم يتوقّف عن البناء حتى يمرّر نطاقاً. هذا هو
الفرق بين مراجعة بشرية وضمان بنيوي، وهو نفس النمط الذي اتُّبع بـ`PayrollRunStore`.

### طبقات الدفاع المستهدفة (المرحلة 10)

```
Authentication → RoleSecurityMiddleware → CompanyScopeProvider
   → توقيع المتجر (إلزامي، يفرضه المترجم)
   → EmployeeCompanyGuard قبل كل كتابة بمعرّف من المتصفح
   → ToSqlPredicate على e.CompanyId داخل الاستعلام
```

### الأداء (المرحلتان 6–7)

نقل العدّ والفلترة والترقيم للـSQL:
`COUNT` مجمَّع بـ`GROUP BY Status` · `OFFSET/FETCH` · وبالـViewer:
صفحة الموظفين أولاً (20) ثم `WHERE EmployeeId IN (...)` ⟹ ~600 صفّاً بدل 300,000.

⚠️ عمود `IsStale` بأربعة `EXISTS` هو المرشّح الأول للاختناق — يحتاج قياساً
بخطة تنفيذ فعلية قبل أي قرار (لن أخمّن).

### خطة الأمواج المقترحة

| الموجة | المحتوى | المخاطرة |
|---|---|---|
| 0 | **فصل قاعدة التطوير** (P0-6) — قبل أي كود آخر | منخفضة · تُزيل خطر إتلاف الإنتاج أثناء بقية العمل |
| 1 | اختبارات عزل **حمراء أولاً** بشركتين | لا مخاطرة |
| 2 | `CompanyScope` وسيطاً إلزامياً بقراءات الحضور | متوسطة (يكسر البناء عمداً) |
| 3 | كتابات الحضور: الحذف والتحليل والتعديل + الحارس | **عالية — تمسّ أرقام الحضور** |
| 4 | ترقيم SQL لـ`/DayAttendance` و`/AttendanceViewer` | متوسطة |
| 5 | المتاجر الباقية + الـlookups | متوسطة |
| 6 | اختبار إقلاع حقيقي + نسختين متزامنتين | منخفضة |
| 7 | قياس الأداء قبل/بعد بأرقام مقيسة | لا مخاطرة |

### ضمان عدم الانحدار (المرحلتان 30–31)

الشركة الأولى بها **1357 موظفاً والثانية صفر** ⟹ اختبار الانحدار حاسم وبسيط:
**تشغيل تحليل الحضور ومسير الرواتب قبل التغيير وبعده على نفس الفترة يجب أن
يُنتج صفوفاً متطابقة بايتاً ببايت.** أي فرق = انحدار، لا تحسين.

### المخاطر

1. **الموجة 3 تمسّ المال** — تعديل مسار الحذف/التحليل قد يغيّر أرقام الحضور
   فالرواتب. الحماية: مقارنة قبل/بعد إلزامية.
2. **`CompanyId` قد يكون `NULL`** بصفوف تاريخية. `CompanyScope.Allows` يقرّر
   بالفعل أن الأدمن وحده يراها — قرار موثّق، ويجب ألا يُنقض بالتنفيذ.
3. **كسر البناء متعمَّد** بالموجة 2 — مقصود لا عرَض.

---

## سجلّ التنفيذ

### ✅ الموجة 0 — فصل قاعدة التطوير (منفَّذة ومتحقَّق منها)

**ما وجدته أثناء التنفيذ وغيّر الخطة:** تعديل `appsettings.Development.json`
وحده **كان سيكون إصلاحاً كاذباً**. الحارس رفض الإقلاع وكشف السبب الحقيقي:

```
متغيّر بيئة على مستوى المستخدم:
ConnectionStrings__DefaultConnection = Server=localhost;Database=SmartAttendance
```

وهو **يتجاوز كل ملفات appsettings**. ولأن خدمة الإنتاج (`ZynoraPortalServer`)
تعمل بنفس المستخدم `Lenovo`، فحذف هذا المتغيّر **يعيد توجيه الإنتاج نفسه**.
لذلك تُرك كما هو، ويُتجاوَز على **مستوى العملية المحلية وحدها** عبر
`launchSettings.json`.

| الخطوة | الحالة |
|---|---|
| استرجاع `SmartAttendance_Dev` من نسخة 2026-08-08 | ✅ 2,794 موظفاً · 177,060 يومية — نسخة مطابقة |
| `appsettings.Development.json` ⟵ `_Dev` | ✅ |
| تجاوز متغيّر المستخدم بـ`launchSettings.json` | ✅ |
| `EnvironmentDatabaseGuard` يرفض الإقلاع قبل المهاجر | ✅ 11 اختباراً |
| إقلاع فعليّ + `/health/ready` | ✅ **200** |
| متغيّر بيئة الإنتاج | ✅ فارغ ⟹ Production ⟹ الحارس لا يمسّه |

**دليل أن الحارس ليس زينة:** أُطلق التطبيق وهو يشير لقاعدة الإنتاج فرفض الإقلاع
برسالة تشرح السبب — لا تحذيراً بسجلّ.

### ✅ الإصلاح العاجل — P0-2: الحذف العابر للشركات بتحليل الحضور (منفَّذ)

بقرار محمد: عولجت هذه الثغرة وحدها أولاً لأنها الوحيدة التي **تُتلف** بيانات
لا تقرأها فقط.

- `AnalyzeMonthAsync` صار يتطلّب `CompanyScope` **إلزامياً بلا قيمة افتراضية** —
  المترجم يمنع النسيان، لا مراجعة بشرية.
- الحذف والبناء **متلازمان بنفس النطاق** (الفهرس الفريد `(EmployeeId, WorkDate)`
  يفرض ذلك — عزل أحدهما دون الآخر يفقد بيانات أو يصطدم بمفتاح مكرّر).
- غير المقيَّد يبقى على **الأمر الأصلي حرفياً** لا صيغة مكافئة — يضمن بقاء
  اليوميات اليتيمة (موظف محذوف نهائياً) ضمن حذف الأدمن كما كانت.
- المستدعيان: `/DayAttendance` يمرّر نطاق المستخدم من `ICompanyScopeProvider`
  (نفس محرّك الرواتب)، وإعادة التحليل التلقائية بعد الموافقات تمرّر **شركة
  الموظف المستهدَف** — وموظف بلا شركة ⟹ `DeniedAll` (مغلق الفشل).

**الإثبات الحيّ على `SmartAttendance_Dev`** (معاملة مُتراجَع عنها):

| القياس | النتيجة |
|---|---|
| حذف بنطاق الشركة (2) — يوليو | **27,094** = صفوف الشركة (2) بالضبط |
| الشركة (1) | 30,876 — لم تُمسّ |
| الشركة (3) | 26,474 — لم تُمسّ |
| بعد ROLLBACK | الأعداد الثلاثة عادت كاملة |

وقبل الإصلاح كان الدليل على التشغيل غير المعزول صريحاً: `AnalyzedAt` لكل
الشركات الثلاث يحمل **نفس اللحظة** (2026-08-07 19:20:38) — تحليل واحد دهسها معاً.

1,321 اختباراً أخضر + 6 جديدة تحرس الشرط المولَّد.

### ✅ الموجتان 2–3 — عزل القراءات والتعديل والإشعارات (منفَّذة · `481ccff`)

- `ListRangeAsync`/`ListAsync`: `CompanyScope` إلزامي — الشرط على وصل `Employees`
  القائم. غير المقيَّد يبقى على نصّ الاستعلام حرفياً.
- `UpdateDayAsync` (P0-3): `EmployeeCompanyGuard` على المسار — `employeeId`
  القادم من المتصفّح لم يعد يُقبل بلا إثبات ملكية.
- زرّ الإشعار (P0-5) يقرأ بنفس نطاق الشاشة.
- محرّكا القواعد الفترية والاقتراحات + مصدرا الحضور بالتقارير — كلها تتطلّب
  نطاقاً (التقارير: `null` ⟹ صفر صفوف لا كل الشركات).
- تسع صفحات تحقن `ICompanyScopeProvider`. المترجم فرض التغطية.

### ✅ الموجة 4 — الترقيم بالـSQL (منفَّذة)

**‏`/DayAttendance`:** كانت تحمّل المدى كاملاً ثم تعدّ وترشّح وترتّب وتقصّ 50
بالذاكرة — **والبحث نفسه كان بالذاكرة بعد التحميل**. الآن: تجميعة حالات +
عدّاد بائتة مجموعي + `OFFSET/FETCH`، والبحث شرط SQL.

**‏`/AttendanceViewer`:** صفحة العشرين موظفاً تُحسم بالـSQL أولاً ثم تُقرأ
يومياتهم وحدها.

**القياسات (على `SmartAttendance_Dev` — فترة يوليو، 84,444 يومية، 3 شركات):**

| القياس | قبل | بعد |
|---|---|---|
| صفوف منقولة للتطبيق — DayAttendance | 84,444 | **50** |
| صفوف منقولة — Viewer | 84,444 | **620** (20 موظفاً × 31) |
| حساب «البائتة» للفترة (خادمياً) | 2,517ms (EXISTS لكل صف) | **473ms** (وصلات مسبقة التجميع — نفس الناتج: 3) |
| استعلام صفحة الـViewer | — | 78ms |

> **اكتشاف قياس مهم:** عنق الزجاجة لم يكن نقل الصفوف وحده بل **حساب `IsStale`
> بثلاثة `EXISTS` مترابطة لكل صف**. القياس الأول للصيغة المجمّعة الجديدة كان
> **أبطأ** من القديم الظاهري (2,938ms مقابل 79ms) — لأن قياس القديم بـ`COUNT(*)`
> ترك المحسّن يتجاهل حساب البائتة أصلاً. أعيد القياس بإجبار الحساب: القديم
> الحقيقي 2,517ms. ثم أُبدل العدّ المترابط بوصلات مسبقة التجميع ⟹ 473ms
> بنفس الناتج. حساب الصفّ الواحد بقي لصفوف الصفحة الخمسين فقط.

`ListForEmployeeAsync` (الموبايل) غير مقيَّد عمداً — المسار ذاتيّ والمعرّف من
التوكن. تعبير البائتة استُخرج لمصدر واحد (`StaleCaseSqlAsync`) فلا تفترق دلالته
بين الشاشات.

### الباقي صراحةً

- **الموجة 1** (اختبارات عزل تكاملية بشركتين ضدّ قاعدة حقيقية) — الحارس الآن
  اختبارات وحدات على الشرط المولَّد + إثباتات SQL يدوية موثَّقة، لا E2E.
- المتاجر غير المتتبَّعة (القائمة أعلاه) — لم تُفحص بعد، ولا أدّعي غير ذلك.
- فهرس `DayAttendances(WorkDate)` — مؤجَّل: يحتاج قياس خطة تنفيذ قبل الإضافة.
- اختبار الإقلاع المزدوج (نسختان متزامنتان) — `sp_getapplock` موجود بالكود
  ولم يُختبر تكاملياً.

---

## ما أحتاجه منك قبل التنفيذ

1. **موافقة على المبدأ**: `CompanyScope` وسيطاً **إلزامياً** بتوقيع متاجر الحضور
   (يكسر البناء عمداً حتى يُمرَّر النطاق) — أم تفضّل نمطاً أخفّ؟
2. **الموجة 0 أولاً؟** فصل قاعدة التطوير عن الإنتاج قبل أي كود. أرشّحه بشدّة:
   بقية العمل يعني تشغيل تحليلات وحذف يوميات — وهي **حالياً تكتب على الإنتاج**.
3. **هل أُكمل التتبّع** للمتاجر التي لم أتحقّق منها بعد (القائمة أعلاه) قبل
   التنفيذ، أم أبدأ بالحضور وأتتبّع الباقي بالتوازي؟

**لم أعدّل أي ملف. لا كود، لا هجرة، لا `WHERE CompanyId` — كما طلبت.**
