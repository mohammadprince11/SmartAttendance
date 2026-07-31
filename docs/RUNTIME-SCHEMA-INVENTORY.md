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
LeaveBalances · MissingPunchRequests · NexoraHrSettings
NexoraNotificationEvents · NexoraNotificationRules · NexoraTerminationReasons
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

هذه المهمة **لم تُدخل أي تغيير مخطط**، فلا هجرة مطلوبة لها.

## خطة الترحيل المقترحة (لاحقاً)

1. جدول `__LegacySchemaHistory` لتتبع سكربتات SQL القديمة (نسخة/تاريخ/بصمة).
2. تجميد الشفاء الذاتي: تحويله لفحص «هل المخطط متوافق؟» يفشل بوضوح بدل التعديل.
3. الترحيل بالأولوية: المصادقة والتدقيق ← الرواتب ← الوثائق ← الباقي.
4. تشغيل الهجرات كخطوة نشر صريحة، لا مع الطلبات، ولا تلقائياً بالإنتاج.
