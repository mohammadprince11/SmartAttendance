# ATTENDANCE CONSOLIDATION REPORT — مودل الحضور والانصراف
> تاريخ: 2026-08-08 · تقرير **تحليلي فقط** (Discover → Map → Compare → Trace → Detect → Design → Challenge). **لا تعديل كود · لا حذف Route · لا تغيير مخطط.** التنفيذ لاحقاً بموجات بعد إقرار هذا التقرير.

## 0) الخلاصة التنفيذية — اقرأها أولاً
النظام **ليس** «شاشات متناثرة بلا ترتيب». القائمة الحيّة **مُنظَّمة أصلاً** في ثماني مجموعات بنمط كيان (المصدر: `Pages/Shared/_Layout.cshtml:575-767`)، وهي **قريبة جداً** من الشكل المستهدَف بالبرومبت. أي كلام عن «تضخم صفحات فوضوي» بائتٌ جزئياً.

ثلاثة أشياء **مُنجَزة فعلاً** ويظنّها البرومبت ناقصة:
1. **`AttendanceImports` / `AttendanceProcessing` / `AttendanceCorrections`** — **بالفعل** مجرّد Redirect stubs إلى `AttendanceOperations` بتبويبات، و**بالفعل** مُخرَجة من القائمة (`_Layout.cshtml:754-757`).
2. **القائمة مجمّعة** في 8 دُرَج (نظرة عامة · حضور الموظفين · مستعرض · إدارة الحضور · قواعد المناوبات · طلبات البصمات · تقارير · إعدادات) — لا 20 رابطاً مسطّحاً.
3. **مصدر الحقيقة الجديد موجود** (`DayAttendanceStore` ⟵ `DayAttendances`) وتستهلكه اليومي/العارض/الشهري/الأسبوعي/اللوحة.

**الدَّين الحقيقي الوحيد المُثبَت** (وهو ما يستحق الموجات):
> **شاشة «مراقبة الحضور» (`AttendanceOperations`) ما تزال تقرأ من المحرّك القديم `IAttendanceProcessingService` كـfallback للصفوف غير المحلَّلة** (`AttendanceOperations/Index.cshtml.cs:25,78`)، بينما بقيّة الشاشات تقرأ اليومية المحلَّلة (`DayAttendanceStore`). **محرّكان يحسبان الحضور** = خرق مبدأ «حقيقة واحدة». هذا رأس المشكلة، لا عدد الصفحات.

توصية عليا: **لا Big-Bang.** الموجة الأولى **تصميم UX فقط** (دمج تبويبات بلا مسّ منطق)، والموجة الأخيرة **إعدام المحرّك القديم بعد Dependency Scan واختبارات انحدار**.

---

## 1) Existing Pages — جرد كامل (المصدر: `Pages/**` + `_Layout.cshtml`)

| # | Route | الاسم بالقائمة | الحالة | Code-behind |
|---|---|---|---|---|
| 1 | `/AttendanceDashboard` | ① نظرة عامة | حيّة | `AttendanceDashboard/Index.cshtml.cs` |
| 2 | `/ShiftAssignments` | ② مناوبات العمل الثابتة | حيّة | `ShiftAssignments/Index.cshtml.cs` |
| 3 | `/ShiftOverrides` | ② تعديل مناوبات مؤقت | حيّة | `ShiftOverrides/Index.cshtml.cs` |
| 4 | `/Roster` | ② جدولة مناوبات العمل | حيّة | `Roster/Index.cshtml.cs` |
| 5 | `/AttendanceViewer` | ③ مستعرض الحضور (مصفوفة) | حيّة | `AttendanceViewer/Index.cshtml.cs` |
| 6 | `/AttendanceOperations` | ④ مراقبة الحضور | حيّة **(محرّك قديم)** | `AttendanceOperations/Index.cshtml.cs` |
| 7 | `/DayAttendance` | ④ الحضور اليومي | حيّة **(المحرّك الرسمي)** | `DayAttendance/Index.cshtml.cs` |
| 8 | `/AttendanceRecommendations` | ④ متابعة الإجراءات المقترحة | حيّة | `AttendanceRecommendations/Index.cshtml.cs` |
| 9 | `/MonthAttendance` | ④ الحضور الشهري (اعتماد) | حيّة | `MonthAttendance/Index.cshtml.cs` |
| 10 | `/WeekAttendance` | ④ الحضور الأسبوعي (اعتماد) | حيّة | `WeekAttendance/Index.cshtml.cs` |
| 11 | `/EmployeeOnlinePunches` | ④ البصمات عبر الإنترنت | حيّة | `EmployeeOnlinePunches/Index.cshtml.cs` |
| 12 | `/WorkFromHome` | ④ العمل من المنزل | حيّة (قراءة فقط) | `WorkFromHome/Index.cshtml.cs` |
| 13 | `/AttendanceRecords` | ④ سجلات الحضور (بصمات خام) | حيّة | `AttendanceRecords/Index.cshtml.cs` (+Create/Edit/Delete) |
| 14 | `/ShiftRules` | ⑤ قواعد المناوبات | حيّة | `ShiftRules/Index.cshtml.cs` |
| 15 | `/PeriodRules` | ⑤ القواعد الفترية | حيّة | `PeriodRules/Index.cshtml.cs` |
| 16 | `/MissingPunchRequests` | ⑥ طلبات البصمات المفقودة | حيّة | `MissingPunchRequests/Index.cshtml.cs` |
| 17 | `/AttendanceReports` | ⑦ التقارير | حيّة (AddPageRoute) | — |
| 18 | `/AttendanceSettings` | ⑧ التهيئة | حيّة | `AttendanceSettings/Index.cshtml.cs` |
| 19 | `/ShiftTypes` | ⑧ تهيئة المناوبات | حيّة | `ShiftTypes/Index.cshtml.cs` |
| 20 | `/Devices` | ⑧ الأجهزة | حيّة | `Devices/Index.cshtml.cs` |
| 21 | `/BiometricKeys` | ⑧ اعتماد مفاتيح البصمة/الوجه | حيّة | `BiometricKeys/Index.cshtml.cs` |
| 22 | `/Holidays` | ⑧ أيام العطل | حيّة | `Holidays/Index.cshtml.cs` |
| — | `/AttendanceProcessing` | **مخفيّة** | **Redirect → `/AttendanceOperations?Tab=process`** | `AttendanceProcessing/Index.cshtml:5` |
| — | `/AttendanceCorrections` | **مخفيّة** | **Redirect → `/AttendanceOperations?Tab=corrections`** | `AttendanceCorrections/Index.cshtml:5` |
| — | `/AttendanceImports` | **مخفيّة** | **Redirect → `/AttendanceOperations?Tab=import`** | `AttendanceImports/Index.cshtml:5` |
| — | `/EmployeeGeoLocations` | ② (ضمن الدرج) | حيّة (خارج نطاق هذا التقرير) | — |

**ملاحظة**: لا يوجد `/Shifts` كصفحة في القائمة — «الشفتات القديمة» موجودة على مستوى **الدومين/القاعدة** فقط (`ShiftService.cs` · `EmployeeShiftService.cs` · `ShiftConfiguration.cs` · جداول `Shifts`/`EmployeeShifts`)، لا كشاشة. راجع القسم «Shift Legacy».

---

## 2) Purpose of Each Page (مختصر من تعليقات الكلاس)
- **AttendanceDashboard** — رسومات وKPIs فقط (قراءة محضة، `DashboardAggregateAsync`).
- **ShiftAssignments** — تعيين المناوبة الثابتة الافتراضية لموظف (المحلّل يقرؤها).
- **ShiftOverrides** — استثناء مؤقّت لمناوبة موظف لفترة (أولوية على الثابت).
- **Roster** — شبكة موظف×يوم لشهر: مناوبة/عطلة لكل خلية + نشر.
- **AttendanceViewer** — **مصفوفة** موظف×أيام الشهر بمفتاح حالات ملوّن (يقرأ `DayAttendances`).
- **AttendanceOperations** — لوحة تشغيلية: أزواج بصمات + تصحيح سريع + استيراد + بصمات أخرى (**تقرأ المحرّك القديم للصفوف غير المحلَّلة**).
- **DayAttendance** — يوميات موظف×يوم بالحقول المشتقّة + «تحديث الحضور» (يعيد بناء الشهر من الخام) — **المحرّك الرسمي**.
- **AttendanceRecommendations** — فرز مخرجات محرّك القواعد (اعتماد ← مخالفة / تجاهل).
- **MonthAttendance** — دورة اعتماد شهر الموظف (تحت المراجعة ← معتمد ← مقفل).
- **WeekAttendance** — نفس الدورة بتجميع أسبوعي (أسابيع ISO).
- **EmployeeOnlinePunches** — بصمات المتصفّح/الجوال (`Source=موبايل`) بفلاتر + حذف مختار.
- **WorkFromHome** — عرض أيام «العمل من المنزل» المُنتَجة من طلبٍ معتمد (قراءة فقط عمداً).
- **AttendanceRecords** — البصمات الخام (كل المصادر) بـCRUD كامل.
- **ShiftRules / PeriodRules** — محرّكا قواعد الاقتراحات (مناوبة / فترة).
- **MissingPunchRequests** — طلبات البصمة المفقودة + البتّ.
- **AttendanceSettings / ShiftTypes / Devices / BiometricKeys / Holidays** — إعدادات ومصادر البصمة.

---

## 3) Duplicate & Partial Duplicates

| النوع | الطرفان | نسبة التداخل | القرار |
|---|---|---|---|
| **Legacy Redirect** | Processing/Corrections/Imports → Operations | 100% (stubs) | **مُنجَز** — أبقِها Redirect، لا تحذف Route |
| **Partial (خطير)** | **AttendanceOperations ↔ DayAttendance** | ~70% عرض (نفس اليوميات: موظف/تاريخ/دخول/خروج/ساعات/تأخير/حالة/ملاحظات) لكن **بمحرّكين** | **دمج في تبويب واحد + توحيد المحرّك** |
| **Partial** | **AttendanceViewer ↔ DayAttendance** | نفس مصدر الحقيقة (`DayAttendances`)، الفرق: **مصفوفة** مقابل **قائمة** | **View داخل نفس Workspace** |
| **Partial** | **MonthAttendance ↔ WeekAttendance** | نفس الدورة (بناء/مراجعة/اعتماد/قفل)، الفرق: **Period فقط** | **Workspace «اعتماد الحضور» بمبدّل شهري/أسبوعي** |
| **Partial** | **EmployeeOnlinePunches ⊂ AttendanceRecords** | Online = Subset بـ`Source=موبايل` | **View/فلتر داخل «سجلات البصمات»** |

---

## 4) Current Attendance Source of Truth & Legacy Engine Usage
```
AttendanceRecords (البصمات الخام)
        ↓  «تحديث الحضور» (المحلّل الرسمي)
DayAttendances (اليوميات المحلَّلة)  ←── DayAttendanceStore ── المصدر الرسمي
        ↓
DayAttendance · AttendanceViewer · MonthAttendance · WeekAttendance · AttendanceDashboard · Recommendations
```
**الاستثناء (الدَّين):** `AttendanceOperations` تحقن `IAttendanceProcessingService` (المحرّك القديم) وتعرض `IsLegacyEngineFallback=true` حين تجد صفوفاً في الفترة **بلا يومية محلَّلة** — فتحسب النتيجة بالمحرّك القديم بدل أن تطلب «تحديث الحضور». هذا يخالف قاعدة البرومبت (المرحلة 4): _«لا أريد شاشة تحسب النتيجة بمحرّك قديم إذا كانت اليومية غير محللة؛ بدلاً منه أظهر Not analyzed + زر تحديث»._

---

## 5) Proposed Final Navigation (فرق بسيط عن الحيّ — لا ثورة)
```
الحضور والانصراف
├── ① نظرة عامة            → AttendanceDashboard (كما هو)
├── ② إدارة الحضور          [Workspace موحّد جديد /Attendance]
│     ├── اليوميات          ← DayAttendance ⊕ AttendanceOperations (محرّك واحد)
│     ├── المصفوفة          ← AttendanceViewer (View)
│     ├── سجلات البصمات     ← AttendanceRecords
│     └── البصمات الأخرى/الأونلاين ← OtherPunches ⊕ EmployeeOnlinePunches
│        (Actions: استيراد · تحليل · تعديل · إشعار)
├── ③ اعتماد الحضور         [Workspace] شهري ⊕ أسبوعي (مبدّل Period)
├── ④ الجداول والمناوبات     المناوبات(ShiftTypes) · الثابتة · الجدولة(Roster) · المؤقتة
├── ⑤ الإجراءات والطلبات     Recommendations · MissingPunch · WorkFromHome (Workflow مختلف ⟹ لا دمج داخلي)
├── ⑥ التقارير              AttendanceReports
└── ⑦ الإعدادات            Settings · ShiftTypes · Devices · BiometricKeys · Holidays · PeriodRules · ShiftRules
```

---

## 6) Feature Migration Matrix (Old Feature → New Location) — لا فقدان وظيفة
| الوظيفة | من | إلى |
|---|---|---|
| تصحيح يدوي / أزواج بصمات / بصمات أخرى / ملاحظات / استيراد | AttendanceOperations | ② اليوميات (Drawer التعديل + Action الاستيراد) |
| تحليل + حالات + Stale + إشعار | DayAttendance | ② اليوميات |
| مصفوفة موظف×أيام + فلاتر متقدّمة + إشعار | AttendanceViewer | ② المصفوفة (View) |
| CRUD بصمات خام + فلاتر | AttendanceRecords | ② سجلات البصمات |
| بصمات موبايل + GPS/جهاز/حذف | EmployeeOnlinePunches | ② سجلات البصمات (فلتر Source + Details Drawer) |
| بناء/مراجعة/اعتماد/قفل شهري | MonthAttendance | ③ اعتماد (شهري) |
| نفسها أسبوعياً | WeekAttendance | ③ اعتماد (أسبوعي) |
| فرز الاقتراحات | Recommendations | ⑤ الإجراءات |
| طلبات البصمة المفقودة | MissingPunchRequests | ⑤ الإجراءات |
| العمل من المنزل (قراءة) | WorkFromHome | ⑤ الإجراءات |

> **قاعدة الأمان:** أي وظيفة بلا خانة «إلى» مؤكَّدة ⟹ **تبقى صفحتها**.

---

## 7) Routes: Keep / Redirect / Safe-to-remove-later
- **Keep (مسارات حيّة):** كل الـ22 أعلاه.
- **Redirect (مُنجَز — أبقِها):** `/AttendanceProcessing` · `/AttendanceCorrections` · `/AttendanceImports`. **تحذير:** `AttendanceSourceStore` يبذر مصدراً باسم استيراد — لا تُعطّل المسار (`_Layout.cshtml:757`).
- **Safe-to-remove (فقط بعد Wave 9 + Dependency Scan + اختبارات):** لا شيء الآن. المرشّحون المستقبليون: `AttendanceOperations` (يُطوى في ② بعد توحيد المحرّك)، ثم المحرّك القديم `AttendanceProcessingService` + كيانات `Shift`/`EmployeeShift` القديمة **إن ثبت أنها بلا مستهلك**.

---

## 8) Impact Analysis
- **Permission Impact:** القائمة كلها خلف `CanPage("Attendance.Operations")` (بوّابة مجموعة واحدة) + بوّابات فرعية موجودة (مثل `Attendance.MissingPunch`). **خطر الدمج:** توحيد شاشات ذات صلاحيات مختلفة قد يوسّع رؤية مستخدمٍ كان يرى المصفوفة فقط. **مطلوب** تصميم Sub-Permissions (`Attendance.View/Edit/Import/Analyze/Approve/Lock`) **قبل** أي دمج فعلي.
- **Company Scope:** مُحكَم أصلاً بعد الموجات 2–6 (نطاق إلزامي بتوقيع المتاجر). أي Query جديد في الـWorkspace **يجب** أن يبدأ من `ICompanyScopeProvider` — الدمج **ينقل** الاستعلامات، فيجب ألّا يلتفّ على النطاق. راجع [[tenant-isolation-waves]].
- **Payroll Impact:** المسير يقرأ اليوميات المحلَّلة/المقفلة (Month/WeekAttendance). **ممنوع** لمس صيغ الحضور أو مخرجاته. توحيد المحرّك يجب أن يُثبَت بانحدار: التأخير/الخروج المبكر/نقص البصمة/الإضافي/أثر الإجازة **بلا تغيّر**.
- **Performance Impact:** الـTabs **لا** تُحمَّل معاً — كل View يحمّل عند الطلب (Server-side paging موجود: `DayAttendance PageSize=50`, `PageRangeAsync` بالموجة 4). المصفوفة: فلترة موظفين ثم TOP N ثم جلب حضورهم فقط. اللوحة: `DashboardAggregateAsync` (SQL). **لا تُدخل الدمج انحداراً في الأداء.**

---

## 9) Risks
1. **توحيد المحرّكين** أخطر خطوة — أي فرق سلوكي بين القديم/الرسمي على صفوف غير محلَّلة قد يقلب أرقاماً يعتمدها المسير. **التخفيف:** استبدل fallback القديم بـ«Not analyzed + زر تحديث» أولاً (سلوك، لا حساب)، ثم أعدِم المحرّك.
2. **Shift Legacy:** `AttendanceProcessingService` قد يستهلك كيان `Shift`/`EmployeeShift` القديم لا `ShiftType`. **مطلوب Shift Legacy Dependency Report** يمسح Attendance/Payroll/Reports/APIs/Portal/Imports/Tests/Jobs **قبل** أي إعدام.
3. **صلاحيات أوسع** بالدمج (القسم 8) — لا تدمج قبل Sub-Permissions.
4. **كسر روابط/إشعارات/Bookmarks** — لا تحذف Route، Redirect فقط.

---

## 10) Implementation Waves (لاحقاً — بعد إقرار التقرير)
- **W1 — UX/Navigation فقط:** لا شيء تقريباً (القائمة مجمّعة أصلاً). تأكيد إخفاء الـstubs. **بلا كود منطقي.**
- **W2 — إطار Workspace `/Attendance`** بتبويبات (Shell فقط، كل تبويب يستضيف الصفحة الحالية Partial).
- **W3 — دمج اليوميات:** طيّ DayAttendance ⊕ Operations في تبويب واحد؛ **استبدال fallback المحرّك القديم بـ«Not analyzed»** (أهم تغيير سلوكي).
- **W4 — المصفوفة** كـView.
- **W5 — سجلات البصمات** (Records ⊕ Online بفلتر Source).
- **W6 — اعتماد الحضور** (Month ⊕ Week بمبدّل، Stores منفصلة تبقى مبدئياً).
- **W7 — Sub-Permissions** للحضور.
- **W8 — Shift Legacy Dependency Report** ثم أرشفة القديم.
- **W9 — إعدام المحرّك القديم + كود ميت** بعد Dependency Scan + انحدار كامل.

---

## 11) Challenge the Design (المرحلة 22)
- **هل فقدنا وظيفة؟** لا — كل وظيفة لها خانة «إلى» بالمصفوفة (القسم 6). WorkFromHome/MissingPunch/Recommendations **لم** تُدمج داخلياً (Workflow مختلف) — أُبقيت مستقلّة داخل ⑤.
- **هل صار Workspace ضخماً؟** لا — Tabs بتحميل عند الطلب، لا Request واحد يجمع الكل.
- **هل الصلاحيات أوسع؟** خطر حقيقي ⟹ عولج بـW7 قبل الدمج.
- **هل المصفوفة ما تزال سريعة؟** نعم إن حافظنا على TOP-N ثم جلب حضورهم فقط.
- **هل Import مخفيّ؟** لا — Action صريح في ② + المسار القديم Redirect.
- **هل يفهم المستخدم أين يعدّل البصمة؟** نعم — Drawer داخل ② اليوميات، لا صفحة منفصلة.
- **هل الرواتب على نفس الحقيقة؟** نعم بعد W3 (محرّك واحد) — وهو **شرط** إتمام W9.
- **هل بقي محرّك قديم يحسب؟** نعم اليوم (`AttendanceOperations`) — إزالته **هدف W3/W9**، وهي الغاية الفعلية من كل هذا التقرير.

---
## القرار المطلوب من محمد قبل أي كود
التقرير جاهز. **لم يُعدَّل أي ملف كود ولا Route ولا قاعدة.** أطلب إقرار:
1. اعتماد الشكل النهائي للقائمة (القسم 5) — أم تعديله؟
2. البدء بـ**W1 فقط** (تأكيد الإخفاء، بلا منطق)، أم الانتظار؟
3. **W3 (توحيد المحرّك)** هو أخطر بند ويمسّ مسار الرواتب — يحتاج موافقة صريحة مستقلّة قبل تنفيذه.
