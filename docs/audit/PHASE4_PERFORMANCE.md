# PHASE 4 — PERFORMANCE, SCALABILITY & RELIABILITY

```text
PHASE 4 BASELINE
Repository:   github.com/mohammadprince11/SmartAttendance
Branch:       main
Commit SHA:   b10d4b0
Environment:  Production (Windows) — SQL Server Express · net10.0 · ASP.NET Core Razor Pages
Deployment:   عملية واحدة خلف Cloudflare Tunnel · مهمة مجدولة بحلقة run-server
Audit Date:   2026-08-11
```

## PERFORMANCE AUDIT LIMITATIONS (إلزامي — يُقرأ أولاً)

1. **Phase 2 مكتملة 14/1158 ملفاً** — أي وحدة لم تُقرأ لا يُدّعى تدقيق أدائها.
2. **لا بيئة اختبار حِمل** — لم يُشغَّل أي Load/Stress/Soak. الأرقام أدناه إمّا
   **مقيسة** على استعلامٍ منفرد أو **مستنتجة من الكود**، لا من حِملٍ متزامن.
3. **ذاكرة خطط SQL فارغة من حِمل المستخدمين** — التطبيق أُعيد تشغيله قبل ~15 دقيقة
   ولم يدخل أحد بعدها. فما ظهر بـ`dm_exec_query_stats` هو **الإقلاع واستعلاماتي**
   لا حِمل الإنتاج. ⟹ `RUNTIME BASELINE NOT AVAILABLE` لمسارات المستخدم.
4. القياسات نُفِّذت على **`SmartAttendance_Test`** (نسخة بنفس الحجم)، والقراءات
   الوصفيّة فقط على الإنتاج (`sys.dm_db_partition_stats` — بلا لمس بيانات).

---

## C. SCALE MODEL — **مقيس، لا مُقدَّر**

`sys.dm_db_partition_stats` على قاعدة الإنتاج (2026-08-11):

| الجدول | الصفوف | التصنيف |
|---|---|---|
| **AttendanceRecords** | **414,226** | 🔥 الأسخن |
| **DayAttendances** | **177,060** | 🔥 الأسخن (نموّ يوميّ) |
| AttendanceRecommendations | 33,566 | |
| EmployeeMonthAttendance | 5,448 | |
| UserNotificationRecipients | 2,913 | |
| Employees · EmployeeFinancialInfos | 2,794 لكلٍّ | |
| AnnouncementRecipients · EmployeeWeekAttendance | ~2,724 | |

```text
حجم القاعدة: 784 ميغابايت · 170 جدولاً · 3 شركات
نموذج النموّ: DayAttendances ≈ الموظفون × الأيام
             2,794 × 365 ≈ 1,020,000 صفّاً/سنة  (اليوم 177,060 = تغطية جزئية)
             AttendanceRecords ≈ الموظفون × البصمات/يوم × الأيام
```

---

## DBPERF-001 🔥

```text
Severity:   HIGH
Confidence: MEASURED
Category:   Missing Index / Scan on Hot Table
Status:     OPEN
```

**Title:** الاستعلام الرئيسيّ لشاشة `/DayAttendance` **لا يستطيع الـseek** — الفهرس
الوحيد يبدأ بعمودٍ لا يذكره الاستعلام، فيمسح الجدول كلّه في كل فتحة شاشة.

**Query (المصدر):** `Infrastructure/Hrms/DayAttendanceStore.cs:1225-1227`
```sql
FROM DayAttendances d
WHERE d.WorkDate >= @From AND d.WorkDate <= @To {companyClause}{searchClause}
```

**الفهارس الموجودة فعلاً على `DayAttendances`:**
```text
PK__DayAtten__…              CLUSTERED (Id)
UX_DayAttendances_Employee_Date  NONCLUSTERED (EmployeeId, WorkDate)   ← يبدأ بـEmployeeId
```
الاستعلام يرشّح بـ`WorkDate` **وحده** ⟹ العمود القائد غير مذكور ⟹ **لا seek**.

**القياس (على `SmartAttendance_Test`، 177,060 صفّاً، `SET STATISTICS IO/TIME`):**

| الاستعلام | logical reads | CPU |
|---|---|---|
| `WHERE WorkDate BETWEEN …` (شكل الشاشة الفعليّ) | **1,290** | 31 ms |
| `WHERE EmployeeId = 1 AND WorkDate BETWEEN …` (يدعمه الفهرس) | **3** | 0 ms |

**الفارق: 430×.**

**Scale projection:** القراءات تنمو **خطّياً** مع الجدول. عند مليون صفّ (سنة واحدة
كاملة) تصير ~7,300 قراءة منطقيّة لكل فتحة شاشة، وعند ثلاث سنوات ~22,000 — لمستخدمٍ
**واحد**. والشاشة تشغيليّة تُفتح وتُحدَّث كثيراً.

**Failure mode:** ليس انهياراً بل **تدهوراً خطّياً صامتاً** — الشاشة تبطؤ شهراً بعد
شهر بلا حدثٍ يفسّر السبب.

**Measurement required:** خطة تنفيذ فعليّة (`SET SHOWPLAN`) لتأكيد `Index Scan`،
وقياس تحت تزامن.

**Recommended direction (لا يُنفَّذ بهذه المرحلة):** فهرس يقود بـ`WorkDate`.
⚠️ **وتقييم التكلفة أولاً**: الجدول يستقبل كتابةً كثيفة بالتحليل، وكل فهرسٍ يُثقل
`INSERT/UPDATE/DELETE`.

---

## DBPERF-002

```text
Severity:   MEDIUM
Confidence: CODE-CONFIRMED + SCHEMA-CONFIRMED
Category:   Tenant Filtering Cost
Status:     OPEN
```

**Title:** جدولا الحضور **لا يحملان `CompanyId`**، فكلّ ترشيحٍ بالشركة يفرض ضمّاً
إلى `Employees`.

**Evidence:** `COL_LENGTH('AttendanceRecords','CompanyId')` = **NULL** ·
`COL_LENGTH('DayAttendances','CompanyId')` = **NULL**.

**Impact:** لا يمكن لأي فهرس على جدول الحضور أن يبدأ بالشركة، فالعزل يُنفَّذ
بالضمّ لا بالـseek — على أسخن جدولين بالنظام (414K و177K صفّاً). يفسّر هذا أيضاً
صعوبة موجات عزل الشركات.

⚠️ **لا يُقترح عمودٌ جديد الآن** — إضافة `CompanyId` لجدولٍ بـ414K صفّاً قرارٌ
معماريّ (هجرة + تعبئة + تزامن مع مصدر الحقيقة) لا تحسينٌ عابر.

---

## PERF-003

```text
Severity:   MEDIUM
Confidence: MEASURED (إقلاع)
Category:   Startup Cost / Runtime DDL
Status:     OPEN
```

**Title:** فحوص المخطط بالإقلاع تكلّف I/O حقيقيّاً — **11,551 قراءة منطقيّة لفحصٍ
واحد**.

**Evidence (من `dm_exec_query_stats` بعد الإقلاع مباشرة):**
```text
198.1 ms · 11,551 logical reads
IF COL_LENGTH('Employees','Position') IS NULL  ALTER TABLE Employees ADD Po…
```
وهذا **فحصٌ واحد** من ضمن **46 هجرة SQL محكومة + 390 موضع `EnsureAsync`**
(جرد Phase 1).

**Scale/Reliability impact:** الوقت بين بدء العملية وجاهزيتها ينمو مع المخطط.
و`/health/ready` يعتمد على القاعدة، فالإقلاع البطيء يعني نافذةً أطول قبل استقبال
الحركة. ⚠️ **وبنسختين متزامنتين** تتسابق فحوص الـDDL هذه على نفس الجداول.

---

## SCALE-004

```text
Severity:   HIGH
Confidence: CODE-CONFIRMED
Category:   Horizontal Scalability — Single-Instance Assumptions
Status:     OPEN
```

**Title:** النظام مصمَّم لعمليةٍ واحدة؛ تشغيل نسخةٍ ثانية يكسر ثلاثة أشياء.

| المكوّن | الحالة | الأثر بنسختين |
|---|---|---|
| مفاتيح Data Protection | ✅ **بالقاعدة** (`PersistKeysToDbContext` + `SetApplicationName`) | سليم — مشترك |
| `IMemoryCache` (نطاق الصلاحيات · `portal:idleMinutes` · حالة الحساب 60 ث) | 🔴 بالذاكرة | كاشان متباعدان ⟹ صلاحية مُلغاة تبقى فعّالة على نسخةٍ حتى انتهاء TTL |
| `NotificationRuleGeneratorService` (HostedService) | 🔴 بلا قفل موزَّع | **كل نسخة تولّد نفس الإشعارات** ⟹ تكرار |
| `RoleSecurityMiddleware.LoginDatabaseIsReady` (`static volatile`) | 🟡 لكل عملية | حميد (مزلاج idempotent) |
| ملفات `App_Data/ProtectedEmployeeFiles` + `wwwroot/uploads` | 🔴 قرص محليّ | نسخةٌ لا ترى ملفات الأخرى |

**Conclusion:** `SCALE-OUT NOT READY`. التوسّع الأفقيّ اليوم يسبّب **تكرار إشعارات**
و**ملفاتٍ مفقودة** — لا مجرّد بطء.

---

## EFPERF-005

```text
Severity:   MEDIUM
Confidence: CODE-CONFIRMED
Category:   Load-All-Then-Filter
Status:     OPEN
```

**Title:** `LeaveRequestService.GetAllAsync` يحمّل **كل** طلبات الإجازة **وكل**
الموظفين للذاكرة، ثم يرشّح ويبحث بالـLINQ.

**Evidence:** `Infrastructure/Services/LeaveRequestService.cs:38-40` —
`_unitOfWork.LeaveRequests.GetAllAsync()` + `_unitOfWork.Employees.GetAllAsync()`،
ثم قاموس بكل الموظفين، ثم الترشيح والبحث بالذاكرة (`Contains` على السلاسل).

**Today:** 2,794 موظفاً وطلبات قليلة ⟹ غير محسوس. **بلا صفحنة** إطلاقاً.

**نزاهةٌ واجبة:** لاحظتُ هذا أثناء إصلاح AUTHZ-003 اليوم، و**أضفتُ الترشيح
بالذاكرة نفسه** لأن العقد يعمل على `IEnumerable` — أي أنّ إصلاح الأمان لم يُدخل
النمط لكنه **لم يُصلحه**. تحسينه يلزمه تغيير عقد المستودع (استعلام مُسنَد للقاعدة)،
وهو خارج نطاق إصلاحٍ أمنيّ.

---

## OBS-006

```text
Severity:   HIGH
Confidence: CONFIRMED
Category:   Observability Gap
Status:     OPEN
```

**Title:** لا يمكن الإجابة عن «ما الذي أبطأ النظام أمس؟» — لا قياس زمنٍ ولا مقاييس.

| القدرة | الحالة |
|---|---|
| `/health/live` · `/health/ready` | ✅ موجودان ويعملان (قِستُ: 117ms · 53ms) |
| زمن الطلب (P50/P95/P99) | 🔴 غير موجود |
| معدّل الأخطاء · CPU/Memory/GC/ThreadPool | 🔴 غير موجود |
| زمن استعلامات القاعدة | 🔴 غير موجود |
| مدّة استيراد الحضور · مدّة المسير · معدّل الفشل | 🔴 غير موجود |
| سجلّ مُهيكل يربط (طلب · مستخدم · شركة · عملية · مدّة) | 🔴 غير موجود |

**Impact:** كل تحقيق أداء لاحق يبدأ **من الصفر** كما بدأتُ اليوم — لا تاريخ ولا
خطّ أساس. وذاكرة خطط SQL تُمحى بكل إعادة تشغيل (وقد حدث اليوم فعلاً).

---

## Z. FAILURE MODES (مراجعة كود، بلا حقن أعطال)

| العطل | السلوك الفعليّ | التقييم |
|---|---|---|
| القاعدة غير متاحة | `/health/ready` يفشل · `/health/live` يبقى ناجحاً (بلا لمس القاعدة) | ✅ تصميم سليم |
| القاعدة غير متاحة أثناء التحقّق من الجلسة | **fail-open** بالويب · **fail-closed** بالموبايل | 🔴 AUTHN-002 |
| SMTP معطّل | مرسِل No-Op يُحقن بالإقلاع · لا خدمة تسليم | ✅ عزل سليم |
| Web-Push معطّل | No-Op + نقطة `vapid-key` تُرجع `enabled=false` | ✅ عزل سليم |
| القرص ممتلئ | **غير معروف** — رفع الملفات يكتب للقرص المحليّ | ⚠️ REQUIRES VERIFICATION |
| فشل المسير في منتصفه | **غير مُدقَّق** (`PayrollRunStore` 1,945 سطراً لم يُقرأ) | ⚠️ غير معروف |
| انقطاع العميل بطلبٍ طويل | تمرير `CancellationToken` غير مُدقَّق شاملاً | ⚠️ غير معروف |

---

## AD. TOP RISKS (بالترتيب)

1. **DBPERF-001** — مسح جدولٍ ساخن بشاشة تشغيليّة · **مقيس 430×** · يسوء خطّياً.
2. **SCALE-004** — التوسّع الأفقيّ يكرّر الإشعارات ويفقد الملفات.
3. **OBS-006** — لا قياس ⟹ كل مشكلة أداء تُكتشف من شكوى مستخدم لا من مقياس.
4. **DBPERF-002** — العزل بالضمّ لا بالفهرس على أسخن جدولين.
5. **PERF-003** — كلفة الإقلاع تنمو مع المخطط.

**ROOT-PERF-01:** الأداء **لم يُقَس قطّ**. لا فهارس مبنيّة على أشكال استعلامٍ
مرصودة، ولا خطّ أساس، ولا مقاييس — فالبنية مبنيّة على الصحّة الوظيفيّة وحدها.

---

## PHASE 4 COVERAGE

```text
PHASE 4 STATUS: INCOMPLETE

Scale model:              ✅ MEASURED (بيانات إنتاج وصفيّة)
Index inventory:          ✅ جداول الحضور الأربعة
Measured queries:         2 (شكل /DayAttendance + نظيره القابل للـseek)
Startup cost:             ✅ مقيس جزئيّاً (فحصٌ واحد من 436)
Load tests:               ❌ لم تُنفَّذ — لا بيئة حِمل
Runtime user baseline:    ❌ غير متاح (ذاكرة الخطط فارغة)

Performance-relevant files reviewed: ~6 من عشرات
غير مُدقَّق: المسير · الاستيراد · التقارير · PDF/Excel · الواجهة · الموبايل ·
            الخدمات الخلفية · 920 موضع SQL خام

STATIC PERFORMANCE AUDIT: PARTIAL
RUNTIME LOAD VALIDATION: NOT STARTED

No source code was modified. No index was created. No commit was made.
DO NOT claim performance or scalability coverage.
```
