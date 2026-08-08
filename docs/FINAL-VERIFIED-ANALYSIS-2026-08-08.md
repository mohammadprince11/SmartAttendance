# FINAL VERIFIED ANALYSIS — ZYNORA Multi-Tenant Isolation & Production Hardening

> تاريخ: 2026-08-08 · الفرع: `claude/smartattendance-local-rebuild-wftwb3`
> **مصدر الحقيقة: الكود التنفيذي الحالي على هذا الفرع** (لا الوثائق ولا التقارير).
> الفرع متقدّم على `main` بموجات العزل 0–5 + إصلاحات الأداء. هذا التحليل يُعيد
> التحقّق من الكود كما هو الآن، ويكشف ثغرات **لم تُغلَق** رغم الموجات السابقة.
>
> **الحالة: تحليل فقط — لم يُعدَّل أي ملف تطبيق.** لا كود قبل موافقتك على الترتيب.

---

## 1) البنية الحالية للعزل (Verified from code)

| الطبقة | الآلية الفعلية | الموضع |
|---|---|---|
| Authentication | ASP.NET Identity cookie · `User.Identity` | البنية القائمة |
| Company Scope | `IEffectiveScopeService` ⟵ `CompanyScopeProvider` ⟵ `ICompanyScopeProvider.GetAsync()` | `Infrastructure/Security` |
| نموذج النطاق | `CompanyScope`: `Unrestricted` (أدمن) · `ForCompanies(ids)` (مقيَّد بواحدة أو أكثر) · `DeniedAll` (مغلق الفشل) | `CompanyScope.cs` |
| فرض القائمة | `EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId")` ⟶ `e.CompanyId IN (...)` / `1=1` / `1=0` | `EmployeeCompanyGuard.cs` |
| فرض الصفّ | `CanAccessEmployeeAsync` · `CanAccessOwnedRowAsync(table, idCol, id, scope)` عبر وصل `Employees` | `EmployeeCompanyGuard.cs` |

**النموذج الحقيقي مؤكَّد:** مستخدم قد يُخوَّل بشركة واحدة أو أكثر؛ الأدمن `Unrestricted`؛
غياب النطاق ⟹ `DeniedAll` (مغلق الفشل). **ثلاث شركات مأهولة بالإنتاج** (1,023 / 894 / 877).

---

## 2) السبب الجذري (Root Cause) — بنيويّ لا نقطيّ

> **`CompanyScope` موجود، لكنه اختياريٌّ بتوقيع الوصول للبيانات.**
> المتاجر (Stores) تسمح بالوصول غير المقيَّد افتراضياً؛ حدّ المستأجر يُفرَض حيث
> **تذكّر المطوّر** أن يمرّر النطاق أو يستدعي الحارس — لا كقيدٍ يفرضه المترجم.

الدليل: عمليات القراءة/السرد عُزلت بجعل `CompanyScope` **وسيطاً إلزامياً** (نجح: المترجم
يمنع النسيان). لكن عمليات **التعديل بمعرّفٍ من المتصفح** (`OnPost*(int id)`) ما زالت
تنادي دوالّ متجرٍ **بلا وسيط نطاق وبلا حارس**، فالعزل فيها متروك لانضباط الصفحة —
وهو غير متّسق فعلاً (بعض الصفحات تحرس، وكثير لا).

---

## 3) الـFindings — مؤكَّدة/مرفوضة مع الأدلّة والثقة

### ✅ مُغلقة سابقاً وأُعيد التحقّق منها من الكود الحالي

| # | Finding | الدليل الحالي | الحالة | ثقة |
|---|---|---|---|---|
| A | `AnalyzeMonthAsync` حذف عابر للشركات | `DayAttendanceStore.cs:442` — الحذف بنطاق إلزامي (`DELETE d ... EXISTS ... scope`)، الأدمن على الأمر الأصلي حرفياً | **مُغلقة** (`775cd49`) | 95% |
| B | `ListRangeAsync` قراءة بلا نطاق | `:1432` — `companyClause` من `scope.ToSqlPredicate` · وسيط إلزامي | **مُغلقة** | 95% |
| C | تعديل الحضور بتلاعب `EmployeeId` | `AttendanceViewer:213` — الحارس داخل `UpdateDayAsync` | **مُغلقة** | 90% |
| D | إشعارات الحضور تعبر الشركات | `/DayAttendance` notify يبني القائمة بـ`ListRangeAsync(scope)` | **مُغلقة** (P0-5) | 88% |
| E | لوحة الحضور تحسب من كل الشركات | `AttendanceDashboard:81` — `ListRangeAsync(scope)` | **مُغلقة (عزلاً)** — لكن أداءً: تحمّل الشهر كاملاً (انظر P1-perf) | 85% |
| J | إرسال الإقرارات يعبر الشركات | `AcknowledgmentStore:269` — `scope.Allows(row.CompanyId)` · `LoadAssignments:170` مرشَّح | **مُغلقة** — الجمهور محصور بنطاق المُرسِل | 85% |

### 🔴 مؤكَّدة ومفتوحة الآن (لم تُغلَق)

| # | Finding | الدليل | التصنيف | ثقة |
|---|---|---|---|---|
| **G-1** | `LoanStore.PostDueInstallmentsAsync` (`:332`) يختار الأقساط المستحقّة عبر **كل الشركات** (`INNER JOIN EmployeeLoans` بلا مرشِّح) ويُنشئ اقتطاعات مسير لها. الصفحة `Loans:127` تناديها **بلا نطاق**. | مستخدم مقيَّد يضغط «ترحيل الأقساط» ⟹ خصومات رواتب لموظفي شركات أخرى | **P0 — Cross-Tenant Payroll Modification** | 95% |
| **G-2** | `Loans.OnPostDeleteAsync(id)` (`:118`) ⟶ `DeleteAsync(_db, id)` **بلا حارس ملكية** | حذف قرض شركة أخرى بمعرّف مصنوع | **P0 — Cross-Tenant Write** | 90% |
| **G-3** | `Loans.OnGetScheduleAsync(id)` (`:134`) ⟶ `InstallmentsAsync(_db, id)` **بلا حارس** | كشف جدول أقساط (بيانات مالية) لقرض شركة أخرى | **P1 — Cross-Tenant Read** | 90% |
| **H-1** | `FinancialRequests.OnPostApproveAsync(id)` (`:90`) ⟶ `ApprovalWorkflowEngine.ApproveAsync` (بلا وسيط نطاق) ثم `ApplyIfFinancialAsync` — **لا حارس ملكية بالصفحة** | اعتماد طلب مالي لشركة أخرى ⟹ أثر مالي (قرض/بدل/زيادة) على موظفها | **P0 — Cross-Tenant Financial Effect** | 80% *(ملاحظة: قد يقيّده إسناد خطوة الموافقة جزئياً — يلزم دليل إضافي؛ العلاج حارس صريح بأي حال)* |
| **H-2** | `FinancialRequests.OnPostRejectAsync(id)` (`:107`) بلا حارس | رفض/إبطال طلب شركة أخرى | **P1** | 80% |
| **H-3** | `FinancialRequests.OnPostSubmitAsync` (`:83`) ⟶ `SubmitAsync(_db, detail, employeeId, ...)` مع `employeeId` من النموذج **بلا فحص ملكية** (المنتقي مقيَّد لكن POST المصنوع يتجاوزه) | إنشاء طلب مالي لموظف شركة أخرى (الأثر عند الاعتماد) | **P1 — Cross-Tenant Create** | 85% |
| **I-1** | `Movements.OnPostLockAsync(id)` (`:113`) ⟶ `LockMovementAsync(_db, id, ...)` بلا حارس | قفل حركة عقد لشركة أخرى | **P1** | 90% |
| **I-2** | `Movements.OnPostDeleteAsync(id)` (`:120`) ⟶ `DeleteMovementAsync(_db, id)` بلا حارس | حذف حركة عقد لشركة أخرى | **P0/P1 — Cross-Tenant Modification** | 90% |

### 🟡 أداء (مؤكَّدة)

| # | Finding | الدليل | تصنيف | ثقة |
|---|---|---|---|---|
| F-1 | `/DayAttendance` و`/AttendanceViewer` رُقّما بالـSQL (`PageRangeAsync`/`PageViewerEmployeesAsync`) | **مُغلقة** (الموجة 4) | — | 90% |
| F-2 | `AttendanceDashboard` يحمّل يوميات الشهر كاملةً (`ListRangeAsync`) ثم يعدّ/يجمع بالـC# | `AttendanceDashboard:81–107` | **P1 — SQL Aggregation ناقص** | 88% |
| F-3 | تبويب المعالجة والتقارير الواسعة كانت تحسب «البائتة» المترابطة بلا داعٍ | **مُغلقة** (`d151682`,`3a2e98f`) — قياس: 5,154ms⟶0 | — | 95% |

### ⚪ مرفوضة كثغرة عزل

| # | Finding | القرار | ثقة |
|---|---|---|---|
| K | Document Templates | **لا CompanyId على القوالب ⟹ قوالب عامة (Platform-level)**. الوثائق المُصدَرة per-employee وتُقيَّد عبر الموظف. **لا تُضَف عزلاً بلا سبب** (المرحلة 6/K). | 75% *(يلزم تأكيد أين تُسرَد الوثائق المُصدَرة)* |

### 🧪 بنية تحتية للاختبار (مفتوحة)

| # | Finding | الحالة | تصنيف |
|---|---|---|---|
| T-1 | اختبارات تكامل بشركتين (User A ⟶ بيانات B عبر تلاعب المعرّف) | **غير موجودة** — الحارس الحالي اختبارات وحدة على المُسنَد + إثباتات SQL يدوية | P1 |
| T-2 | اختبار إقلاع/هجرة على قاعدة فارغة + قفل موزَّع لنسختين | **غير موجود** | P1 |
| C-1 | Concurrency/Idempotency لـ`PostDueInstallmentsAsync`: لا معاملة تغلّف الحلقة ولا قفل — ضغطتان متزامنتان/نسختان قد تختاران القسط نفسه قبل وسمه ⟹ **خصم مكرّر** | مؤكَّد بالكود (`:360–396`) | P1 — Financial Idempotency |

---

## 4) تحليل التحليل (Challenge) — محاولة دحض نتائجي

- **هل ثمّة Global Query Filter يعزل ضمنياً؟** لا. الوصول عبر `HrmsDatabase.QueryAsync` بـSQL خام لا EF query filters؛ لا middleware يحقن `CompanyId`. الأدلّة أعلاه من SQL مباشر.
- **هل يقيّد إسناد خطوة الموافقة H-1 ضمنياً؟** محتمل جزئياً (المُعتمِد يجب أن يكون على مسار الموافقة)، لكنه **تخويلٌ بالدور لا عزلُ مستأجر**؛ ولا يغطّي مساراً بشركة واحدة يشترك مُعتمِدوه. لذا أبقيتُ P0 بثقة 80% مع طلب دليل إضافي قبل البناء عليه.
- **هل المنتقي المقيَّد يكفي لـH-3/G؟** لا — القائمة المنسدلة ليست حدّاً أمنياً؛ POST مصنوع يتجاوزها (المرحلة 13).
- **هل الأدمن يتعطّل بأي إصلاح مقترح؟** لا: نمط الحارس يعيد `true` فوراً لـ`Unrestricted`، والسرد يصبح `1=1`.
- **هل `DeniedAll` قد ينقلب لغير مقيَّد؟** لا: كل الدوالّ تفحص `IsDeniedAll` أولاً وتعيد فارغاً/ترفض.

---

## 5) البنية المقترحة (Design) — تجعل التجاوز استثناءً صريحاً

**المبدأ:** ما فُعِل بالقراءات يُعمَّم على التعديل بمعرّف: **الحارس إلزاميّ عند كل
`OnPost*(int id)` ماليّ/عابر لموظف**، ونقطة الفرض المفضّلة **طبقة المتجر** (لا الصفحة)
حتى لا يُنسى:

- `PostDueInstallmentsAsync(db, **scope**, year, month, user)` — يضيف
  `AND {ListFilter(scope,"e.CompanyId")}` على وصل `EmployeeLoans⟶Employees`.
- `DeleteAsync`/`SetStatusAsync`/`InstallmentsAsync(db, **scope**, id)` — حارس
  `CanAccessOwnedRowAsync(Tables.EmployeeLoans, ...)` داخل المتجر (fail-closed).
- `ApprovalWorkflowEngine.ApproveAsync/RejectAsync(db, **scope**, id, ...)` أو حارس
  بالصفحة على ملكية الطلب (`Tables.SelfServiceRequests`) قبل الاستدعاء.
- `SubmitAsync(db, **scope**, detail, employeeId, ...)` — `CanAccessEmployeeAsync`.
- `LockMovementAsync`/`DeleteMovementAsync(db, **scope**, id)` — حارس على
  `Tables.EmployeeContracts` (موجود بالكتالوج).
- إضافة `Tables.EmployeeContracts` مستعملة؛ لا جداول جديدة مطلوبة.

**قاعدة Fail-Closed (المرحلة 7):** كل دالّة مُعدَّلة: `scope==DeniedAll ⟹ لا فعل`؛
تعذّر إثبات الملكية ⟹ رفض؛ الاستثناء الوحيد `Unrestricted` صريح.

**بدائل نوقشت ورُفضت للآن:** Global EF query filters (المتاجر SQL خام لا EF) ·
CompanyId على كل جدول (تكرار وهجرات واسعة؛ وصل `Employees` كافٍ ومُثبَت) ·
scoped repository جديد (Big-Bang؛ ممنوع بالمرحلة 20). النمط الحاليّ أصغر وقابل للعكس.

---

## 6) الأثر

- **الأمن:** إغلاق 3 مسارات P0 (خصم أقساط · اعتماد طلب مالي · حذف عقد) + 5 مسارات P1.
- **صحّة الرواتب/البيانات:** **صفر تغيير صيغة** — الإصلاحات ترشيحُ نطاقٍ وحرّاسُ ملكية فقط؛ الأدمن سلوكه حرفيّ. (المرحلتان 16/20).
- **الأداء:** P1-F2 (تجميع لوحة الحضور بالـSQL) يحسّن ولا يغيّر ناتجاً — مصدر الحقيقة يبقى `DayAttendances`.
- **الهجرة/الإنتاج:** لا هجرة ولا وصول إنتاج ضمن هذه المهمة. Dev منفصل (`_Dev` + `EnvironmentDatabaseGuard`)؛ Staging غير موجود بعد.
- **مخاطر الإنتاج:** الثغرات المفتوحة قابلة للاستغلال الآن (3 شركات مأهولة).

---

## 7) ملفات ستُعدَّل (عند الموافقة) / يُمنع تعديلها

**ستُعدَّل (متاجر + صفحاتها فقط):**
`LoanStore.cs` · `Pages/Payroll/Loans.cshtml.cs` · `FinancialRequestStore.cs` (Submit) ·
`Pages/Payroll/FinancialRequests.cshtml.cs` · `ContractRegisterStore.cs` (Lock/Delete) ·
`Pages/Contracts/Movements.cshtml.cs` · (P1) `AttendanceDashboard` تجميع SQL ·
`ApprovalWorkflowEngine.cs` أو حارس الصفحة · اختبارات جديدة.

**يُمنع تعديلها:** كل ملفّ صيغة/حساب رواتب (`SalaryFormulaEvaluator`, `PayrollRun*`
الحسابية) · منطق الإجازات/الإضافي/التأخير/الجزاءات/التقريب · التنقّل/الواجهة/الوركفلو.

---

## 8) الاختبارات المطلوبة (Red-first، المرحلة 18)

لكل P0/P1 عزل: اختبار **يفشل على الكود الحالي** ثم يُخضَّر:
مستخدم شركة A ⟶ (قسط/حذف/جدول) قرض B · اعتماد/رفض/إنشاء طلب مالي B · قفل/حذف حركة عقد B
⟹ يجب: رفض/صفر صفوف متأثّرة/فارغ. + اختبار idempotency لـ`PostDue` (نداءان ⟹ خصم واحد).
+ (P1) اختبار تكامل بشركتين E2E (T-1) واختبار إقلاع مزدوج (T-2).

---

## 9) ترتيب التنفيذ المقترح (عند الموافقة)

1. **P0** عزل ترحيل أقساط القروض + idempotency (`PostDueInstallmentsAsync`).
2. **P0** حرّاس الملكية على القروض (Delete · Schedule) والعقود (Lock · Delete movement).
3. **P0** عزل اعتماد/رفض/إنشاء الطلبات المالية.
4. **P1** تجميع لوحة الحضور بالـSQL.
5. **P1** اختبارات التكامل بشركتين + الإقلاع المزدوج.
6. **P2** CSRF (تصنيف نقاط النهاية أولاً — لا `AutoValidateAntiforgeryToken` أعمى).

كل مرحلة: Build · Unit · Integration · Security · Regression — ثم كوميت مستقل. **بلا نشر.**

---

## 10) شرط الثقة

كل P0 هنا ≥ 80%. الوحيد عند الحدّ (**H-1 = 80%**) يستوجب دليلاً إضافياً على سلوك
`ApprovalWorkflowEngine` قبل الاعتماد الكامل — لكن العلاج (حارس ملكية صريح) صحيح بأي حال.
`K` (قوالب الوثائق) و`T-2` دون التنفيذ حتى يُجمع دليل إضافي.

---

## 11) تصنيف CSRF (P2 — نُفِّذ التصنيف، لا حاجة لتغيير)

**القاعدة**: `AutoValidateAntiforgeryToken` الأعمى **خطأ** هنا — كان سيكسر واجهات
الموبايل (توكن Bearer، لا كوكي، فلا سطح CSRF أصلاً). التصنيف نقطةً نقطة:

| السطح | المصادقة | الطلبات | الحكم |
|---|---|---|---|
| **صفحات Razor** (كل الباك-أوفيس المُعدِّل) | كوكي | POST | ✅ **محميّ**: إطار Razor يتحقّق تلقائياً، وJS يرسل `RequestVerificationToken` (مؤكَّد بعدّة صفحات: Runs · أجراس الإشعارات · القوالب) |
| `Api/MeController` · `Api/AuthController` | Bearer (`ApiTokenAuthHandler`) | POST | ✅ **N/A بنيوياً**: لا اعتماد كوكي ضمنيّ ⟹ لا CSRF |
| `Api/WebAuthnController` | كوكي | POST تسجيل/بصمة | ✅ **مُخفَّف بالبروتوكول**: مراسم WebAuthn مربوطة بالأصل (origin) بحكم واجهة المتصفح؛ ومساراها قبل-المصادقة `AllowAnonymous` |
| `PushController` (`/push/*`) | كوكي | POST اشتراك/إلغاء | ⚠️ **خطورة مهملة**: حمولة الاشتراك يولّدها متصفّح الضحية بمفتاح الخادم من نفس الأصل، فلا يستطيع موقعٌ مهاجمٌ حقن اشتراكٍ ذي قيمة؛ أسوأ أثرٍ إزعاجُ إشعارات. لا يستحق كسر الاشتراك الصامت بـpwa.js |
| `EmployeeFilesController` | كوكي | **GET فقط** (تنزيل/ملف) | ✅ **N/A**: لا تعديل |

**الخلاصة**: السطح المُعدِّل الحقيقي (صفحات Razor) محميّ بالإطار افتراضياً؛ وواجهات
الـAPI محصّنة بنيوياً بتوكن Bearer. لا فجوة CSRF قابلة للتنفيذ، وأيّ مرشّح عام كان
سيضرّ. القرار: **لا تغيير** — والتصنيف نفسه هو المُخرَج.

---

## 12) حالة التنفيذ (الموجة 6 — 2026-08-08)

| البند | الكوميت | الحالة |
|---|---|---|
| P0 عزل ترحيل الأقساط + idempotency + مسار المسير | `c08ac43` | ✅ |
| P0 حرّاس القروض (حفظ/اعتماد/حذف/جدول) والعقود (قفل/حذف حركة) | `c08ac43` | ✅ |
| P0 عزل اعتماد/رفض/إنشاء الطلبات المالية (المحرّك المشترك + شاشتا الطلبات والموافقات) | `c08ac43` | ✅ |
| P1 تجميع لوحة الحضور بالـSQL | `cff7dd6` | ✅ (تحقّق حيّ على `_Dev`) |
| P1 اختبارات التكامل بشركتين + قفل الإقلاع المزدوج | `547380e` | ✅ (تمرّان على `_Dev`، تخطٍّ آليّ إن غاب SQL) |
| P2 CSRF | — | ✅ تصنيف: لا تغيير |

**1338 اختباراً. بلا نشر.** الباقي دون الحدّ (`K` · `T-2`) مؤجَّل لدليلٍ إضافي.
