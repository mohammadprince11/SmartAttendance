# جرد صفحات مودل «أشخاص» بكيان مقابل نظامنا — 2026-07-31

**المصدر:** استخراج مباشر من قائمة تنقّل كيان الحيّة (`demo.kayanhr.com/Employees/Dashboard/Index`،
جلسة محمد المصادَقة) — 116 رابطاً بالقائمة كلها، منها **41 شاشة تحت `/Employees`**.
**نظامنا:** جرد ملفات `SmartAttendance.Web/Pages/**/*.cshtml` + قائمة `_Layout.cshtml`.

**الرموز:** ✅ موجودة · 🟡 موجودة جزئياً أو بمسمّى/تجزئة مختلفة · ❌ غير موجودة.

---

## 1) الأرقام

| | كيان | عندنا |
|---|---|---|
| شاشات تحت `/Employees` | **41** (34 مساراً، بعضها بمعاملات) | — |
| منها ✅ عندنا | **19** | |
| منها 🟡 جزئية | **10** | |
| منها ❌ ناقصة | **12** | |

> إضافةً لها: مودل «أشخاص» بكيان يستدعي **~35 شاشة إعدادات تحت `/Setup/*`**
> (قسم «إعدادات الموارد البشرية» بالقائمة) — مجرودة بالقسم 3 لأنها جزء من المودل عملياً.

---

## 2) الجرد التفصيلي — `/Employees/*`

### 2.أ العرض والتشغيل

| # | كيان | المسار | عندنا | الحالة |
|---|---|---|---|---|
| 1 | رسومات بيانية | `/Employees/Dashboard/Index` | `/Employees/PeopleDashboard` | ✅ |
| 2 | عرض الموظفين + الملف | `/Employees/Home/Index` | `/Employees/Index` + `/Employees/Profile` | ✅ |
| 3 | الهياكل التنظيمية | `/Employees/OrganizationalStructures/Index` | `/OrgStructures/Index` + `/Organization/Chart` | ✅ |
| 4 | التقارير | `/Employees/Reports/TabularReports` | `/PeopleReports/Index` | ✅ |
| 5 | إدارة العهد | `/Employees/AssetsManagement/Index` | `/AssetsManagement/Index` | ✅ |
| 6 | إنهاء الموظف | `/Employees/EmployeeTermination/…FromPeople` | `/Employees/EndService` + `EndServiceList` + `Rehire` | ✅ |
| 7 | متابعة الطلبات المخصصة | `/Employees/CustomRequestsScreening/Index` | `/Approvals/Index` + `/HrSettings/RequestTypes` | 🟡 لا منشئ نماذج طلبات |
| 8 | **تخصيص رئيس وحدة مؤقت** | `/Employees/TemporaryHeadofUnitAllocation/Index` | — | ❌ |

### 2.ب تحديثات الموظف (تعديل جماعي مصنّف)

كيان يقسمها **سبع شاشات** بنفس المسار ومعامل `Type`؛ عندنا **شاشة واحدة**
`/EmployeeUpdates/Index`.

| # | تصنيف كيان | عندنا | الحالة |
|---|---|---|---|
| 9 | معلومات الموظف الأساسية | `/EmployeeUpdates/Index` | 🟡 |
| 10 | معلومات التوظيف والتخصيص | ↑ | 🟡 |
| 11 | معلومات الاتصال | ↑ | 🟡 |
| 12 | معلومات الحضور | ↑ | 🟡 |
| 13 | المعلومات المالية | ↑ | 🟡 |
| 14 | معلومات الدفع | ↑ | 🟡 |
| 15 | حقول إضافية | ↑ | 🟡 |

### 2.ج المخالفات والانضباط

| # | كيان | المسار | عندنا | الحالة |
|---|---|---|---|---|
| 16 | حالات المخالفات | `/Employees/ViolationCases/TabIndex` | `/Violations/Index` | ✅ |
| 17 | الإجراءات التأديبية | `/Employees/ViolationActions/Index` | `/DisciplinaryRules/Index` | 🟡 قواعد لا سجلّ إجراءات |
| 18 | فئات المخالفات | `/Employees/Violations/Index?PageType=1` | داخل `/DisciplinaryRules` | 🟡 |
| 19 | قائمة المخالفات | `/Employees/Violations/Index?PageType=2` | داخل `/DisciplinaryRules` | 🟡 |
| 20 | تهيئة المخالفات | `/Employees/ViolationConfiguration/Index` | — | ❌ |

### 2.د العقود

| # | كيان | المسار | عندنا | الحالة |
|---|---|---|---|---|
| 21 | **عقود الموظفين** | `/Employees/ContractsManagement/ViewContracts` | حقول عقد داخل الملف فقط | ❌ لا شاشة عقود |
| 22 | **تحديثات العقود** | `/Employees/ContractsManagement/Index` | — | ❌ |

### 2.هـ الوثائق والإقرارات والبطاقات

| # | كيان | المسار | عندنا | الحالة |
|---|---|---|---|---|
| 23 | وثائق الموظفين (منشئ) | `/Employees/DocumentBuilderCreation/Index` | `/EmployeeDocuments/Index` (مركز الوثائق) | 🟡 رفع لا توليد |
| 24 | **وثائق الشركة** | `/Employees/DocumentCenter/Index` | — | ❌ |
| 25 | **فئات وثائق الشركة** | `/Employees/DocumentCategory/Index` | — | ❌ |
| 26 | **إعدادات الملفات الإلكترونية** | `/Employees/EFiles/Index` | — | ❌ |
| 27 | **قالب الإقرارات** | `/Employees/AcknowledgmentTemplates/…` | — | ❌ |
| 28 | **متابعة الإقرارات** | `/Employees/EmployeeAcknowledge/…` | — | ❌ |
| 29 | بطاقات الموظفين | `/Employees/BadgeCenter/GeneratorIndex` | `/EmployeePermissions/Index` («بطاقات الموظفين») | 🟡 يحتاج تدقيق تطابق |
| 30 | **قوالب البطاقات** | `/Employees/BadgeCenter/TemplateIndex` | — | ❌ |
| 31 | **تصدير البطاقة** | `/Employees/BadgeCenter/ExportBadgeGenerator_Index` | — | ❌ |

### 2.و التفاعل (Engagement)

| # | كيان | المسار | عندنا | الحالة |
|---|---|---|---|---|
| 32 | الإعلانات | `/Employees/Announcement/Index` | `/Engagement/Announcements` | ✅ |
| 33 | الانتخابات واستطلاعات الرأي | `/Employees/VotesAndPolls/Index` | `/Engagement/Polls` | ✅ |
| 34 | اقتراحات وشكاوى | `/Employees/SuggestionsComplaintsBox/…` | `/Engagement/Feedback` | ✅ |
| 35 | **متابعة منشورات الحائط** | `/Employees/WallPostsSetup/ViewPostsIndex` | `/Engagement/Index` (حائط بلا شاشة متابعة/إشراف) | 🟡 |
| — | (عندنا زيادة) التقدير | — | `/Engagement/Recognition` | ➕ |

### 2.ز إعدادات المودل

| # | كيان | المسار | عندنا | الحالة |
|---|---|---|---|---|
| 36 | التهيئة | `/Employees/Configuration/Index` | `/HrSettings/Index` | 🟡 مفاتيح أقل |
| 37 | فترة التجربة | `/Employees/ProbationPeriodSetup/Index` | `/HrSettings/ProbationPeriod` | ✅ |
| 38 | فترة الإنذار | `/Employees/NoticePeriodSetup/Index` | `/HrSettings/NoticePeriod` | ✅ |
| 39 | إعدادات الخدمة الذاتية | `/Employees/SelfServiceSetup/Index` | `/HrSettings/SelfServiceSettings` | ✅ |
| 40 | **تهيئة إنهاء الخدمات** | `/Employees/OffboardingConfiguration/Index` | `/HrSettings/TerminationReasons` | 🟡 أسباب فقط بلا قوالب |
| 41 | **متابعة تقييمات** | `/Employees/EvaluationsScreening/Index` | — | ❌ (مودل أداء غائب أصلاً) |

---

## 3) شاشات `/Setup/*` التي يستدعيها مودل الأشخاص (خارج `/Employees`)

**عندنا ✅:** أدوار الوصول (5 تبويبات `PageType`) · قوالب الموافقات · مركز الإشعارات ·
التحكم بالحقول · حقول إضافية · مجموعات الموظفين · المسمّى الوظيفي · مخطط المرجع
التلقائي · التصنيفات (شخصية/عمل/منظمة) · مهام الموظفين · أسباب الإيقاف.

**❌ ناقصة عندنا** (14 شاشة):

1. `/Setup/CompanySurveys/*` — الاستبيانات (قوالب · مرتبطة بحدث · نتائج · مقابلات
   نهاية الخدمة · استبيانات المدرب/المتدرب/عامة) = **7 شاشات**
2. `/Setup/DocumentBuilder/*` — منشئ الوثائق وقوالب الرسائل والعناوين/التذييلات
   المخصصة = **6 شاشات** (+ `DocumentsComputedFields` الحقول المحسوبة)
3. `/Setup/Competencies/Index` — الكفاءات
4. `/Setup/CommitteeGroup` + `/Setup/ExternalCommittees` — اللجان
5. `/Setup/DelegationRulesScreening` — تفضيلات التفويض
6. `/Setup/CurrenciesExchange` — محوّل العملات
7. `/Setup/Dictionary` — قاموس
8. `/Setup/Cliche` — تذييلات التوقيع
9. `/Setup/FormBuilder/EmployeesCustomRequests` — **منشئ طلبات الموظفين المخصصة**
10. `/Setup/CustomRequesterInformation` — معلومات مقدّم الطلب المخصصة
11. `/Setup/Educational` — التصنيفات التعليمية
12. `/Setup/HierarchyTypes` — أنواع الهيكلية
13. `/Setup/CompanyProcesses/OffBoardingTemplates` + `EmployeeTasksTemplates` — القوالب
14. `/Setup/SecurityConfiguration` — الأمن (عندنا تحصين بلا شاشة)

---

## 4) خلاصة الناقص الحقيقي بمودل الأشخاص

مرتّباً بالأثر التقديري:

1. **العقود** (شاشة عقود + تحديثات العقود) — غياب تام، وهو مودل تشغيلي يومي.
2. **الإقرارات** (قالب + متابعة) — غياب تام.
3. **الوثائق**: وثائق الشركة + فئاتها + الملفات الإلكترونية + **منشئ الوثائق**
   (توليد كتب/خطابات من قوالب) — عندنا رفع وأرشفة فقط.
4. **الاستبيانات** (7 شاشات) — غياب تام.
5. **بطاقات الموظفين**: قوالب + تصدير.
6. **تقييمات/كفاءات** — مودل أداء غائب (قرار منتج لا فجوة تنفيذ).
7. **تخصيص رئيس وحدة مؤقت** — شاشة صغيرة وأثرها إداري بالموافقات.
8. **تهيئة المخالفات** و**تهيئة إنهاء الخدمات** — عندنا أجزاء لا شاشات تهيئة كاملة.
9. **تجزئة «تحديثات الموظف»** لسبعة تصنيفات — عندنا شاشة واحدة (فرق تجربة لا قدرة).

> ⚠️ هذا **جرد صفحات** لا مطابقة حقول. الخطوة التالية بالطريقة المتفق عليها:
> نأخذ صفحة صفحة ونطابق **حقلاً بحقل وزراً بزر**.
