# جرد صفحات مودل «الحضور والانصراف» بكيان مقابل نظامنا — 2026-07-31

**المصدر:** استخراج مباشر من قائمة كيان الحيّة (`demo.kayanhr.com/TimeAttendance/Home/Index`).
**نظامنا:** `_Layout.cshtml` (قائمة الحضور) + ملفات `Pages/**/*.cshtml`.

**الرموز:** ✅ موجودة · 🟡 جزئية/بتجزئة مختلفة · ❌ غير موجودة · ➕ عندنا زيادة.

---

## 1) الأرقام

| | العدد |
|---|---|
| شاشات كيان تحت `/TimeAttendance` | **22** |
| ✅ عندنا | **15** |
| 🟡 جزئية | **4** |
| ❌ ناقصة | **3** |
| + مودل مستقل مرتبط (`/TimeSheet` سجل الدوام) | ❌ غائب كاملاً |
| + شاشتا عطل بالإعدادات (`/Setup/DaysOff`) | ✅ عطل · 🟡 نهايات الأسبوع |

> **الخلاصة المبكّرة: تطابق مودل الحضور أقوى بكثير من مودل الأشخاص.**

---

## 2) الجرد التفصيلي

### 2.أ الشاشات التشغيلية

| # | كيان | المسار | عندنا | الحالة |
|---|---|---|---|---|
| 1 | رسومات بيانية | `/TimeAttendance/Home/Index` | `/AttendanceOperations` (مراقبة تشغيلية لا داشبورد تحليلي) | 🟡 |
| 2 | الحضور اليومي | `/EmployeeAttendanceManagement/AttendanceManagement` | `/DayAttendance` | ✅ |
| 3 | الحضور الأسبوعي | `/WeeklyAttendanceManagement/WeeklyAttendance` | `/WeekAttendance` | ✅ |
| 4 | الحضور الشهري | `/MonthlyAttendanceManagement/MonthlyAttendance` | `/MonthAttendance` (+ دورة اعتماد) | ✅ |
| 5 | مستعرض الحضور | `/AttendanceViewer/AttendanceViewer` | `/AttendanceViewer` | ✅ |
| 6 | **إعدادات عارض الحضور المسبقة** | `/AttendanceViewerSetup/Index?PageType=1` | — | ❌ |
| 7 | متابعة الإجراءات المقترحة | `/EmployeeAttendanceManagement/RecommendedActionScreeningIndex` | `/AttendanceRecommendations` | ✅ |
| 8 | طلبات البصمات المفقودة | `/EmployeeMissingPunchRequests/Index` | `/MissingPunchRequests` | ✅ |
| 9 | البصمات عبر الإنترنت | `/EmployeeOnlinePunches/Index` | `/EmployeeOnlinePunches` | ✅ |
| 10 | المواقع الجغرافية للموظفين | `/EmployeesGeoLocations/Index` | `/EmployeeGeoLocations` | ✅ |
| 11 | التقارير | `/TimeAttendance/Reports/TabularReports` | `/AttendanceReports` | ✅ |

### 2.ب المناوبات والجدولة

| # | كيان | المسار | عندنا | الحالة |
|---|---|---|---|---|
| 12 | مناوبات العمل الثابتة | `/EmployeeAttendance/…RegularShifts_Index` | `/ShiftAssignments` | ✅ |
| 13 | جدولة مناوبات العمل | `/EmployeeAttendance/…TimesheetSchedule_Index` | `/Roster` (فرشاة/قطّارة/نسخ شهر) | ✅ ➕ أقوى |
| 14 | تعديل مناوبات مؤقت | `/TemporaryShiftsOverride/…` | `/ShiftOverrides` | ✅ |
| 15 | تهيئة المناوبات | `/TimeAttendanceSetup/Index?PageType=3` | `/ShiftTypes` | ✅ |

### 2.ج التهيئة ومنشئ القواعد

| # | كيان | المسار | عندنا | الحالة |
|---|---|---|---|---|
| 16 | دلالات مخصصة | `/TimeAttendanceSetup/Index?PageType=1` | «البصمات ومصادرها» (`AttendanceProcessing`) | ✅ |
| 17 | مصدر بيانات الحضور | `?PageType=2` | نفس الصفحة | ✅ |
| 18 | المواقع الجغرافية (تعريف) | `?PageType=4` | داخل `/EmployeeGeoLocations` لا شاشة تعريف مستقلة | 🟡 |
| 19 | التهيئة | `?PageType=5` | `/AttendanceSettings` | ✅ |
| 20 | منشئ قواعد المناوبات **اليومية** | `ShiftRulesBuilderIndex?PageType=1` | `/ShiftRules` | ✅ |
| 21 | منشئ قواعد المناوبات **الشهرية** | `?PageType=2` | — | ❌ |
| 22 | منشئ قواعد المناوبات **الأسبوعية** | `?PageType=3` | `/PeriodRules` (قواعد فترية) | 🟡 يحتاج تدقيق تطابق |

### 2.د خارج `/TimeAttendance` لكنه من المودل

| كيان | عندنا | الحالة |
|---|---|---|
| `/TimeSheet/Home/Index` — **سجل الدوام (مودل مستقل)** | — | ❌ |
| `/Setup/DaysOff?PageType=2` — العطل | `/Holidays` | ✅ |
| `/Setup/DaysOff?PageType=1` — نهايات آخر الأسبوع | ضمن أنواع المناوبات/القواعد الفترية | 🟡 لا شاشة مستقلة |
| `/Setup/AutomationCenter/TimeAttendanceNotificationsCenter` | `/HrSettings/NotificationCenter` | ✅ |

---

## 3) ما عندنا وليس عند كيان (➕)

| عندنا | ملاحظة |
|---|---|
| سجلات الحضور (`/AttendanceRecords`) | إدخال/تدقيق سجل خام |
| استيراد البصمات (`/AttendanceImports`) | استيراد ملفات الأجهزة |
| الأجهزة (`/Devices`) | إدارة أجهزة البصمة — كيان يعتمد وسيطاً خارجياً |
| اعتماد مفاتيح البصمة/الوجه (`/BiometricKeys`) | WebAuthn — غير موجود بكيان |
| تصحيحات الحضور (`/AttendanceCorrections`) | |
| القواعد الفترية (`/PeriodRules`) | |

---

## 4) الناقص الحقيقي بمودل الحضور — مرتّباً

1. **منشئ قواعد المناوبات الشهرية** ❌ — عندنا القواعد اليومية فقط؛ الشهرية تعني
   قواعد على تجميع الشهر (سقوف تأخير شهرية، حد أدنى ساعات…).
2. **سجل الدوام `TimeSheet`** ❌ — مودل مستقل كامل (إدخال ساعات على مشاريع/مهام).
3. **إعدادات عارض الحضور المسبقة** ❌ — عروض محفوظة جاهزة للمستعرض.
4. **داشبورد حضور تحليلي** 🟡 — عندنا مراقبة تشغيلية لا رسومات.
5. **شاشة تعريف المواقع الجغرافية** و**نهايات الأسبوع** 🟡 — موجودتان مطويّتين داخل
   شاشات أخرى لا كشاشات مستقلة.

> ⚠️ جرد صفحات لا مطابقة حقول. المطابقة حقلاً بحقل تأتي بالطلب لكل صفحة.
