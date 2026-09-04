# جرد المخطط المُنشأ وقت التشغيل — ZYNORA HR

**تاريخ الجرد:** 2026-07-31 · **الفرع:** `agent/security-ci-governance-fixes`

## المشكلة

`HrmsDatabase.EnsureCreatedAsync` وعشرات المخازن تُنشئ وتُعدّل جداول **أثناء تشغيل
التطبيق** (self-healing schema) بدل هجرات محكومة. النتيجة:

- لا سجل تاريخي لتغيّرات المخطط ولا قدرة على المراجعة قبل التطبيق.
- تغيّر المخطط قد يقع بأول طلب بعد النشر بلا إشعار.
- لا يمكن التراجع بشكل محكوم.
- تكلفة تحقّق متكرر على مسار الطلب.

## النطاق المرصود

**54 ملفاً** بـ`SmartAttendance.Web/Infrastructure/` يحوي `EnsureCreatedAsync` أو
`CREATE TABLE`، و**~96 جدولاً** يُنشأ/يُعدَّل وقت التشغيل:

```
AccessRoleGrants · AccessRoles · ApiTokens · AppLoginUsers · ApprovalHistories
ApprovalRequestFlows · ApprovalRequestSteps · ApprovalTemplateSteps
ApprovalTemplateWatchers · ApprovalTemplates · AttendanceNotificationOutbox
AttendanceNotifications · AttendanceRecommendations · AttendanceRecords
AttendanceSources · AttendanceTransactions · AuditLogs · BankFileTemplates
CompanyBrandingProfiles · DashboardWidgets · DataChangeRequestFields
DayAttendances · DisciplinaryFormTextBlocks · DisciplinaryMessageTemplates
DisciplinaryPenaltyRules · DisciplinarySettings · DisciplinaryTemplateTypes
DisciplinaryViolationCategories · DisciplinaryViolationTypes · EmployeeAllowances
EmployeeCodeSchemas · EmployeeCompensations · EmployeeContracts
EmployeeCustomFields · EmployeeDependents · EmployeeDocuments
EmployeeEndOfService · EmployeeEndServices · EmployeeFeedbackItems
EmployeeFileRecords · EmployeeFinancialInfos · EmployeeGeoLocations
EmployeeLoanInstallments · EmployeeLoans · EmployeeMonthAttendance
EmployeePollOptions · EmployeePollVotes · EmployeePolls
EmployeePortalAnnouncements · EmployeeRehires · EmployeeSalaryRaises
EmployeeShiftTypes · EmployeeTasks · EmployeeUpdateBatches
EmployeeUpdateChanges · EmployeeViolationCases · EmployeeWebAuthnCredentials
EmployeeWeekAttendance · FinancialRequestDetails · GeoLocations
HrEntityFieldDefs · HrEntityFieldValues · HrFieldControls · HrTaskTemplates
LeaveBalances · MissingPunchRequests · ZynoraHrSettings
ZynoraNotificationEvents · ZynoraNotificationRules · ZynoraTerminationReasons
PayrollGosiProfiles · PayrollRunLineComponents · PayrollRunLines · PayrollRuns
PayrollTaxBrackets · PayrollTaxProfiles · PayrollTransactions
PeriodRuleSlices · PeriodRules · PunchSemantics · PushSubscriptions
RequestCategories · RequestTypes · RosterCells · RosterMonths · SalaryItems
SelfServiceRequests · ShiftEligibilityRules · ShiftOverrides · ShiftRules
ShiftTypeDays · ShiftTypePeriods · ShiftTypes · SystemNotifications
ThemeVersions · UserAccessRoles · UserAppearancePreferences
```

جداول حساسة ضمن القائمة تستحق أولوية الترحيل: `AppLoginUsers` · `ApiTokens` ·
`AccessRoles`/`AccessRoleGrants` · `AuditLogs` · `EmployeeDocuments` ·
`EmployeeFileRecords` · `PayrollRuns`/`PayrollRunLines` · `EmployeeLoans`.

## القاعدة المعتمدة الآن

> **ممنوع إضافة أي جدول أو عمود جديد عبر كود الشفاء الذاتي وقت التشغيل.**
> كل مخطط جديد يمرّ بهجرة محكومة:
> - **EF Core migrations** للكيانات المُدارة بـEF.
> - **سكربت SQL مُصدَّر ومُرقَّم** للجداول القديمة الخام، مع جدول تاريخ هجرات
>   مستقل للطبقة القديمة.

مثبّتة بـ`docs/AI-DEVELOPMENT-RULES.md` وقائمة مراجعة الـPR.

## ما لم يُنفَّذ بهذه المهمة (مقصود)

**لم تُعَد كتابة الجداول الـ96 القائمة** — تحويلها دفعة واحدة يهدد الإنتاج ويحتاج
نافذة صيانة واختبارات ترحيل. القائمة أعلاه هي نقطة البداية للترحيل التدريجي.

## المُنفَّذ بمهمة المراحل 5–7 و10 (2026-07-31)

الخطوة (1) من الخطة أدناه صارت واقعاً: **`SqlSchemaMigrator`** بجدول
`__SchemaMigrations` — هجرات مرقّمة بمعرّفات ثابتة تُطبَّق **مرة واحدة عند
الإقلاع صراحةً** (لا مع الطلبات)، وأي فشل يُرفع بوضوح. الهجرات الثلاث الأولى:
ختم الأمان بـ`AppLoginUsers` و`ApiTokens`، و`ProtectedKey` بـ`EmployeeProfileFiles`.

قاعدة ملزمة: **أي عمود/جدول جديد يُكتب كهجرة هنا، لا عبر الشفاء الذاتي.**

## خطة الترحيل المقترحة (لاحقاً)

1. ~~جدول تتبّع لسكربتات SQL القديمة~~ ✅ نُفِّذ (`__SchemaMigrations`).
2. تجميد الشفاء الذاتي: تحويله لفحص «هل المخطط متوافق؟» يفشل بوضوح بدل التعديل.
3. الترحيل بالأولوية: المصادقة والتدقيق ← الرواتب ← الوثائق ← الباقي.
4. تشغيل الهجرات كخطوة نشر صريحة، لا مع الطلبات، ولا تلقائياً بالإنتاج.

## تحديث الإغلاق البرمجي — 2026-08-26

- صار `HrmsDatabase.EnsureCreatedAsync` مملوكاً للإقلاع فقط: لا تستدعيه صفحة أو
  Controller أو خدمة تشغيلية أو مسار بصمة/استيراد. يثبت ذلك اختبار عقدي يفحص
  المصدر كله ويقبل `Program.cs` و`LoginDatabase.cs` فقط.
- أزيلت أوامر المخطط الدائم من Razor Pages/Controllers بالكامل. بقي استعمال
  `CREATE TABLE` الوحيد داخل الصفحات لجدولين مؤقتين يبدأ اسمهما بـ`#` ويزولان
  بانتهاء اتصال SQL.
- نُقلت الحالات الأربع التي كانت تنشئ/تعدل مخططاً من الصفحة إلى هجرات محكومة:
  `EmployeeProfileFiles`، `EmployeeGroups`، مخطط الملف الديناميكي وأقسامه،
  و`EmployeeUpdateBatches.EffectiveDate` (`20260826-18` حتى `-21`).
- لا يعني ذلك أن كل إرث الجداول الـ96 رُحّل. ما زالت مخازن قديمة تنفذ
  `EnsureAsync` idempotent، وتحويلها دفعة واحدة ممنوع بلا نسخة مخطط حقيقية
  واختبار ترقية/تراجع ونافذة صيانة. لم تُضف هذه الدفعة أي حالة شفاء ذاتي جديدة.
