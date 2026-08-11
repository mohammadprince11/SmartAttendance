# جرد السطح المضمّن لقلب CSP (المرحلة 11 / البند #8)

> **الخطوة صفر: نقيس قبل أن نرحّل.** رفع `Security:StrictCsp` يجعل المتصفّح يتجاهل
> `'unsafe-inline'` فور وجود nonce — أي أنّ **كل** معالِج حدث مضمّن يتوقّف دفعةً
> واحدة. الترحيل على 181 صفحة بالتخمين ينتج أزراراً ميتة بالإنتاج، فهذا الجرد هو
> قائمة العمل المرتّبة، والمجمّع الحيّ (`Security:CspReportCollector`) هو بوّابة
> التحقّق قبل القلب.

## الأرقام (مقيسة من المصدر — `2026-08-11`)

| المؤشّر | العدد |
|---|---|
| صفحات `.cshtml` | 181 |
| صفحات فيها سطح مضمّن | **100** |
| معالِجات أحداث مضمّنة (`on*=`) | **490** في **54** صفحة |
| كتل `<script>` مضمّنة (بلا `src`) | **84** |
| كتل `<style>` | **77** |
| روابط `javascript:` | **0** ✅ |
| `<script src>` خارجيّة (لا تتأثّر) | 99 |

**التركّز يحكم الترتيب:** 16 صفحة تحمل ≥10 معالِجات لكنها تختصر أكثر من نصف الـ490،
وصفحات الرواتب السبع وحدها ~220 معالِجاً.

## الموجات المقترحة

1. **موجة nonce (ميكانيكيّة، بلا منطق):** إضافة `nonce="@Context.GetCspNonce()"`
   لكل `<script>`/`<style>` مضمّن — 161 وسماً. لا تغيّر سلوكاً ولا تكسر شيئاً وهي
   نافذة الراية مطفأة (السمة تُتجاهَل بلا CSP صارمة).
2. **موجة الرواتب (7 صفحات، ~220 معالِجاً):** `on*=` ⟸ `addEventListener` بتفويض
   حدث على الجدول حيث أمكن — الأنماط متكرّرة فتُختصر كثيراً.
3. **موجة الحضور والمناوبات (9 صفحات، ~130).**
4. **الذيل الطويل (38 صفحة، ≤5 معالِجات لكل صفحة).**
5. **القلب:** رفع الراية ببيئة الاختبار + صفر مخالفات بالمجمّع + تحقّق بصريّ.

## كيف تُجمَع المخالفات الحيّة

```
dotnet run --project SmartAttendance.Web -c Release --no-build --urls http://localhost:5093 -- \
  --environment=Development --Security:CspReportCollector=true \
  "--ConnectionStrings:DefaultConnection=Server=localhost;Database=SmartAttendance_Test;Trusted_Connection=True;TrustServerCertificate=True"
```
ثم تصفّح الشاشات واقرأ `GET /csp-report/summary` (بيئة التطوير وحدها).

⚠️ **مقيس عمليّاً:** المتصفّح المضمّن بأداة الوكيل **لا يُرسل** تقارير CSP (يسجّلها
بالكونسول فقط)، فالجمع يحتاج Chrome حقيقيّاً. المسار نفسه مُثبَت طرفاً لطرف
(POST ⟸ 204 ⟸ يظهر بالملخّص، وسلسلة الاستعلام تُقصّ).

## الجرد صفحةً صفحة

| الصفحة (تحت `Pages/`) | معالِجات | `<script>` | `<style>` | المجموع |
|---|---|---|---|---|
| `Payroll/Transactions.cshtml` | 39 | 1 | 1 | 41 |
| `Payroll/SalaryItems.cshtml` | 37 | 1 | 1 | 39 |
| `Payroll/SalaryDaysAdjustment.cshtml` | 32 | 1 | 1 | 34 |
| `ShiftTypes/Index.cshtml` | 30 | 1 | 1 | 32 |
| `Payroll/LeaveEncashment.cshtml` | 30 | 1 | 1 | 32 |
| `Payroll/Overtime.cshtml` | 29 | 1 | 1 | 31 |
| `Payroll/Raises.cshtml` | 26 | 1 | 1 | 28 |
| `AttendanceViewer/Index.cshtml` | 22 | 1 | 1 | 24 |
| `Payroll/Runs.cshtml` | 19 | 1 | 1 | 21 |
| `DayAttendance/Index.cshtml` | 16 | 1 | 1 | 18 |
| `EmployeeGeoLocations/Index.cshtml` | 14 | 1 | 1 | 16 |
| `Roster/Index.cshtml` | 12 | 1 | 1 | 14 |
| `ShiftOverrides/Index.cshtml` | 11 | 1 | 1 | 13 |
| `Payroll/Settings.cshtml` | 10 | 1 | 2 | 13 |
| `Index.cshtml` | 11 | 1 | 0 | 12 |
| `Payroll/EndOfService.cshtml` | 10 | 1 | 1 | 12 |
| `Payroll/Loans.cshtml` | 9 | 1 | 1 | 11 |
| `HrSettings/ApprovalTemplates.cshtml` | 9 | 1 | 1 | 11 |
| `Employees/Profile.cshtml` | 6 | 5 | 0 | 11 |
| `ShiftRules/Index.cshtml` | 8 | 1 | 1 | 10 |
| `PeriodRules/Index.cshtml` | 8 | 1 | 1 | 10 |
| `AttendanceSettings/Index.cshtml` | 8 | 1 | 1 | 10 |
| `PeopleReports/Index.cshtml` | 6 | 3 | 1 | 10 |
| `Payroll/FinancialRequests.cshtml` | 7 | 1 | 1 | 9 |
| `AttendanceRecommendations/Index.cshtml` | 5 | 2 | 2 | 9 |
| `Payroll/RunDetail.cshtml` | 6 | 1 | 1 | 8 |
| `Payroll/BankTemplates.cshtml` | 6 | 1 | 1 | 8 |
| `Employees/Index.cshtml` | 6 | 1 | 0 | 7 |
| `Shared/_Layout.cshtml` | 0 | 4 | 3 | 7 |
| `LeaveBalances/Index.cshtml` | 5 | 1 | 0 | 6 |
| `ShiftAssignments/Index.cshtml` | 4 | 1 | 1 | 6 |
| `OrgStructures/Index.cshtml` | 4 | 1 | 1 | 6 |
| `MissingPunchRequests/Index.cshtml` | 4 | 1 | 1 | 6 |
| `Employees/Edit.cshtml` | 0 | 5 | 1 | 6 |
| `AccessRoles/Index.cshtml` | 5 | 0 | 0 | 5 |
| `UserAccess/Index.cshtml` | 4 | 1 | 0 | 5 |
| `WeekAttendance/Index.cshtml` | 3 | 1 | 1 | 5 |
| `EmployeePortal/FinancialRequest.cshtml` | 3 | 1 | 1 | 5 |
| `Approvals/Index.cshtml` | 3 | 1 | 1 | 5 |
| `Payroll/PayslipInquiry.cshtml` | 2 | 1 | 2 | 5 |
| `Employees/Create.cshtml` | 0 | 4 | 1 | 5 |
| `MonthAttendance/Index.cshtml` | 2 | 1 | 1 | 4 |
| `EmployeePortal/Index.cshtml` | 2 | 1 | 0 | 3 |
| `Payroll/SalarySheet.cshtml` | 2 | 0 | 1 | 3 |
| `HrSettings/FieldControl.cshtml` | 1 | 1 | 1 | 3 |
| `Positions/Index.cshtml` | 0 | 3 | 0 | 3 |
| `Shared/_EmployeePortalLayout.cshtml` | 0 | 2 | 1 | 3 |
| `EmployeeTasks/Index.cshtml` | 2 | 0 | 0 | 2 |
| `DesignLab/Index.cshtml` | 2 | 0 | 0 | 2 |
| `Organization/Index.cshtml` | 1 | 1 | 0 | 2 |
| `Violations/PrintForm.cshtml` | 1 | 0 | 1 | 2 |
| `Payroll/Analytics.cshtml` | 1 | 0 | 1 | 2 |
| `Documents/View.cshtml` | 1 | 0 | 1 | 2 |
| `BadgeCenter/Index.cshtml` | 1 | 0 | 1 | 2 |
| `Shared/Components/NotificationBell/Default.cshtml` | 0 | 1 | 1 | 2 |
| `Shared/Components/EmployeeNotificationBell/Default.cshtml` | 0 | 1 | 1 | 2 |
| `HrSettings/NotificationCenter.cshtml` | 0 | 1 | 1 | 2 |
| `HrSettings/EntityFields.cshtml` | 0 | 1 | 1 | 2 |
| `Employees/FinancialInfo.cshtml` | 0 | 1 | 1 | 2 |
| `Documents/Templates.cshtml` | 0 | 1 | 1 | 2 |
| `Violations/Letter.cshtml` | 1 | 0 | 0 | 1 |
| `Organization/Chart.cshtml` | 1 | 0 | 0 | 1 |
| `EmployeePortal/DataChange.cshtml` | 1 | 0 | 0 | 1 |
| `AssetsManagement/Index.cshtml` | 1 | 0 | 0 | 1 |
| `Alerts/Index.cshtml` | 1 | 0 | 0 | 1 |
| `Violations/Index.cshtml` | 0 | 1 | 0 | 1 |
| `PositionLevels/Index.cshtml` | 0 | 1 | 0 | 1 |
| `PositionCategories/Index.cshtml` | 0 | 1 | 0 | 1 |
| `Engagement/Index.cshtml` | 0 | 1 | 0 | 1 |
| `Engagement/Announcements.cshtml` | 0 | 1 | 0 | 1 |
| `EmployeeUpdates/Index.cshtml` | 0 | 1 | 0 | 1 |
| `EmployeeProfileSettings/Index.cshtml` | 0 | 1 | 0 | 1 |
| `EmployeePortal/Biometric.cshtml` | 0 | 1 | 0 | 1 |
| `DisciplinaryRules/Index.cshtml` | 0 | 1 | 0 | 1 |
| `AttendanceRecords/Index.cshtml` | 0 | 1 | 0 | 1 |
| `AttendanceOperations/Index.cshtml` | 0 | 1 | 0 | 1 |
| `Account/Login.cshtml` | 0 | 1 | 0 | 1 |
| `WorkFromHome/Index.cshtml` | 0 | 0 | 1 | 1 |
| `Verify.cshtml` | 0 | 0 | 1 | 1 |
| `Shared/_AttendanceViews.cshtml` | 0 | 0 | 1 | 1 |
| `Shared/_AttendanceApprovalViews.cshtml` | 0 | 0 | 1 | 1 |
| `PayrollProvisions/Index.cshtml` | 0 | 0 | 1 | 1 |
| `LeaveRequests/Import.cshtml` | 0 | 0 | 1 | 1 |
| `HrSettings/RequestTypes.cshtml` | 0 | 0 | 1 | 1 |
| `Holidays/Import.cshtml` | 0 | 0 | 1 | 1 |
| `Forms/Index.cshtml` | 0 | 0 | 1 | 1 |
| `EmployeePortal/ShiftRequest.cshtml` | 0 | 0 | 1 | 1 |
| `EmployeePortal/Reports.cshtml` | 0 | 0 | 1 | 1 |
| `EmployeePortal/FormFill.cshtml` | 0 | 0 | 1 | 1 |
| `EmployeePortal/DocumentRequest.cshtml` | 0 | 0 | 1 | 1 |
| `EmployeePortal/Acknowledgments.cshtml` | 0 | 0 | 1 | 1 |
| `EmployeePermissions/Index.cshtml` | 0 | 0 | 1 | 1 |
| `Documents/Generate.cshtml` | 0 | 0 | 1 | 1 |
| `Devices/Import.cshtml` | 0 | 0 | 1 | 1 |
| `DesignPreview/Index.cshtml` | 0 | 0 | 1 | 1 |
| `Companies/Index.cshtml` | 0 | 0 | 1 | 1 |
| `Companies/Import.cshtml` | 0 | 0 | 1 | 1 |
| `Companies/Delete.cshtml` | 0 | 0 | 1 | 1 |
| `BiometricKeys/Index.cshtml` | 0 | 0 | 1 | 1 |
| `AttendanceDashboard/Index.cshtml` | 0 | 0 | 1 | 1 |
