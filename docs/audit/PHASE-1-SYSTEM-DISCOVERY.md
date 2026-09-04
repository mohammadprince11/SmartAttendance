# PHASE 1 — SYSTEM DISCOVERY & COMPLETE INVENTORY

> **DISCOVERED ≠ REVIEWED.** لا سطر كود واحد قُرئ للحكم عليه بهذه المرحلة.
> الملفّ المرافق `AUDIT_COVERAGE.csv` هو السجلّ الملزِم للمراحل التالية.

---

## A. Repository Snapshot

```text
Repository:      https://github.com/mohammadprince11/SmartAttendance.git
Default Branch:  main
Reviewed Branch: main
Commit SHA:      5c9de593ee73b09b1b8206849a603fc09f875e84
Working Tree:    clean (0 modifications)
Discovery Date:  2026-08-11
```

**ملاحظة فرع:** الفرع `chore/machine-handover-completeness` (`2cecf43`) = `main` + كوميت
واحد يضيف ملفّين (`scripts/handover/*.ps1`) ويعدّل ملفّين. الجرد أُخذ على `main`
لأنّه ما يعمل حيّاً.

---

## B. System Inventory — المشاريع

| Project | Type | TFM | Entry Point | Refs | Deployment Role |
|---|---|---|---|---|---|
| `SmartAttendance.Domain` | Class Library | net10.0 | — | (لا شيء) | كيانات وenums وسياسات |
| `SmartAttendance.Application` | Class Library | net10.0 | — | Domain | واجهات وViewModels وAutoMapper |
| `SmartAttendance.Infrastructure` | Class Library | net10.0 | — | Domain, Application | EF Core + هجرات + خدمات |
| `SmartAttendance.Web` | ASP.NET Core Web | net10.0 | `Program.cs` (574 سطراً) | Infrastructure | **التطبيق الحيّ الوحيد** |
| `SmartAttendance.API` | ASP.NET Core Web | net10.0 | `Program.cs` (23 سطراً) | Application | ⚠️ منفصل — لا يُشير إليه Web |
| `SmartAttendance.Tests` | xUnit | net10.0 | — | Web | 140 ملف اختبار |
| `SmartAttendance.E2E` | NUnit + Playwright | net10.0 | — | **لا شيء** | دخان خارجيّ عبر HTTP |
| `SmartAttendance.MobileApp` | Android (Java خام) | — | `MainActivity.java` | — | غلاف WebView · **خارج الحلّ** |

**حزم خارجية:** AutoMapper 16.2.0 · EF Core 10.0.9 · Microsoft.Data.SqlClient 6.1.1 ·
Fido2 4.0.1 · WebPush 1.0.13 · HealthChecks.EFCore 10.0.10 · xunit 2.9.3 ·
Playwright.NUnit 1.61.0.

---

## C. File Inventory Summary

### حسب الامتداد (أعلى 15)

| Ext | Count | Ext | Count |
|---|---|---|---|
| `.json` | 1483 | `.png` | 58 |
| `.cs` | 799 | `.svg` | 25 |
| `.cshtml` | 189 | `.map` | 24 |
| `.md` | 105 | `.csproj` | 7 |
| `.css` | 94 | `.html` | 5 |
| `.js` | 78 | `.xml` | 4 |

بقيّة الامتدادات (`.sql` 2 · `.yml` 1 · `.slnx` 1 · `.sh` 1 · `.ps1` 1 · `.java` 1 ·
`.apk` 1 · `.crt` 1 · `.pdf` 1 · `.xlsx` 1 · `.http` 1 · `.webmanifest` 1 …) مسجَّلة
كاملةً بالسجلّ.

### حسب المجلد الجذري

| Directory | Files |
|---|---|
| `graphify-out/` | 1483 |
| `SmartAttendance.Web/` | 867 |
| `SmartAttendance.Tests/` | 141 |
| `SmartAttendance.Application/` | 114 |
| `SmartAttendance.Infrastructure/` | 108 |
| `docs/` | 91 |
| `SmartAttendance.Domain/` | 60 |
| `SmartAttendance.MobileApp/` | 11 |
| `<root>` | 7 |
| `SmartAttendance.API/` | 5 |
| `scripts/` | 4 |
| `database/` · `SmartAttendance.E2E/` · `.github/` | 2 · 2 · 2 |
| `.claude/` | 1 |

---

## D. AUDIT_COVERAGE Status

```text
Total repository files (git ls-files @ 5c9de59): 2898
Registered in AUDIT_COVERAGE:                    2898
Reconciliation:                                  100%  (diff = 0 files)

NOT REVIEWED ............ 1257
GENERATED — VERIFIED .... 1503
VENDOR — VERIFIED .......   75
BINARY / NON-REVIEWABLE .   63
BLOCKED .................    0
REQUIRES REVISIT ........    0
```

### تصنيف الملفات المولَّدة — بالدليل لا بالظنّ

| المجموعة | العدد | الدليل |
|---|---|---|
| `graphify-out/**` | 1483 | ملفّ علامة `.graphify_root` + `manifest.json` + `cache/ast/v0.9.18` (كاش مُصدَّر) + `CLAUDE.md` ينصّ على إعادة توليده بـ`graphify update .` |
| `**/*.Designer.cs` + `ApplicationDbContextModelSnapshot.cs` | 20 + 1 | سقالة EF Core يكتبها `dotnet ef` |
| `**/*.map` | 24 | خرائط مصدر يصدرها المُحزِّم |
| `wwwroot/lib/**` | 75 | مكتبات طرف ثالث (ag-grid · bootstrap · jquery · jquery-validation · leaflet) |

**«ملفّ SOURCE» = 1158 ملفاً** هي المادّة الفعليّة للمراجعة العميقة، منها 732 بـWeb.

---

## E. Project Dependency Map — الواقع لا المثال

```text
Domain
  ↑
Application ──────────────┐
  ↑                       │
Infrastructure            │
  ↑                       │
Web ← Tests               API (منفصل تماماً)

E2E (بلا أي ProjectReference — يختبر عبر HTTP)
MobileApp (خارج الحلّ — Java + build.sh)
```

**ما يُسجَّل كما هو بلا حكم:**
1. `Web` **لا** يشير لـ`Application` مباشرةً — بل عبر `Infrastructure` فقط.
2. `Infrastructure` يشير لـ`Domain` **و**`Application` معاً.
3. `API` يشير لـ`Application` فقط، ولا يشير إليه أحد ولا يُبنى ضمن مسار النشر.
4. `Tests` يشير لـ`Web` (أعلى الطبقات) — فيصل عبره لكلّ شيء.

---

## F. Functional Module Map

**74 مجموعة صفحات** تحت `Pages/`. الوحدات الوظيفية المكتشَفة فعلياً:

| Module | Pages | ملاحظة |
|---|---|---|
| Payroll | 17 | أكبر وحدة · `PayrollRunStore.cs` 1945 سطراً |
| HrSettings | 16 | إعدادات الموارد البشرية |
| Employees (People) | 14 | `Profile.cshtml` 1715 · `Create.cshtml` 1516 |
| EmployeePortal | 13 | بوابة الموظف · `Index.cshtml.cs` 1729 |
| Engagement | 6 | استطلاعات وتفاعل |
| LeaveRequests · Holidays · Devices · Departments · Companies · Branches | 5 لكلٍّ | CRUD كلاسيكي |
| Violations · Documents · AttendanceRecords | 4 لكلٍّ | |
| Organization | 3 | |
| Positions · OrgStructures · LeaveBalances · Forms · DisciplinaryRules · Contracts · Acknowledgments · Account | 2 لكلٍّ | |
| **48 وحدة بصفحة واحدة** | 1 لكلٍّ | Attendance* (11 شاشة) · Shift* (4) · Roster · Approvals · AuditLogs · AccessRoles · UserAccess · Branding · BadgeCenter · Alerts · Notifications · MyProfile · Setup · … |

`Pages/Shared/` = 9 ملفات (Layouts + Partials).
**189 `.cshtml` مقابل 169 `.cshtml.cs`** ⟹ 20 عرضاً بلا PageModel (تخطيطات وجزئيّات).

---

## G. Domain Map

| Group | Count | أمثلة |
|---|---|---|
| Entities | 40 | Employee · AttendanceRecord · LeaveRequest · Shift · Company · SystemUser · Announcement* (12 كياناً) |
| Enums | 15 | AttendanceStatus · LeaveType · PayrollFrequency · SystemUserRole · PeopleDataScopeType |
| Common | 3 | `BaseEntity` · `AuditableEntity` · `IEntity` |
| Domain Policies | 2 | `Attendance/MovementConflictPolicy.cs` · `Leave/IraqiLeavePolicy.cs` |

**Business Domains:** Employee · Attendance · Leave · Payroll · Organization ·
Security/Permissions · Announcements · Tasks · Violations.

⚠️ **الدومين لا يغطّي إلا جزءاً من النظام**: أغلب منطق الأعمال يسكن
`Web/Infrastructure/Hrms/` (118 ملفاً) لا `Domain/`. يُسجَّل بلا حكم.

---

## H. Application Map

114 ملفاً بنمط ثابت `<Module>/{Services,ViewModels,Mappings}`:
20 وحدة · ~24 واجهة خدمة (`I*Service`) · ~60 ViewModel · 12 AutoMapper Profile ·
3 واجهات مستودعات (`IGenericRepository` · `ICompanyRepository` · `IUnitOfWork`) ·
`Common/Security/` (7 ملفات: `PeopleDataScope` · `EmployeeCompanyGuard` ·
`IPermissionAuthorizationService` · `PeoplePermissionCodes` …).

**تنفيذ الخدمات ليس هنا** — الواجهات بـApplication والتنفيذ بـInfrastructure (22 خدمة).

---

## I. Infrastructure Map

- **Persistence:** `ApplicationDbContext` (41 `DbSet`) + 39 ملفّ `IEntityTypeConfiguration`.
- **Migrations:** 21 هجرة EF + `ApplicationDbContextModelSnapshot`.
- **Repositories:** `GenericRepository` · `CompanyRepository` · `UnitOfWork`.
- **Security:** `PeopleDataScopeQueryExtensions`.
- **Seeding:** `DefaultShiftSeeder` · `PeoplePermissionSeeder`.
- **Services (22):** Attendance{Import,Processing,Record,Report,AdvancedReport,PunchAggregator} ·
  Employee · EmployeeShift · EmployeePermission · Company · Department · Device ·
  Holiday · LeaveRequest · Permission · PermissionAuthorization · Setup · Shift ·
  MasterDataImport · Announcement · LoginIdentity.
- **Web/Infrastructure (خارج مشروع Infrastructure):** `Hrms/` 118 · `Security/` 39 ·
  `Notifications/` 16 · `Theming/` 10 · `Imports/` 4.

---

## J. Database Surface Map

**النظام يغيّر المخطط بأربع طرق مختلفة — هذه أخطر نتيجة بنيوية بالمرحلة:**

| # | الطريق | القياس |
|---|---|---|
| 1 | هجرات EF Core | 21 هجرة |
| 2 | هجرات SQL محكومة (`SqlSchemaMigrator.cs`, 1523 سطراً) | **46 مفتاح هجرة** |
| 3 | شفاء ذاتي وقت الطلب (`XStore.EnsureAsync`) | **390 موضع استدعاء** · **64 ملفّاً فيه `CREATE TABLE`** |
| 4 | SQL خام (`HrmsDatabase.Query/Execute/Scalar`) | **920 موضعاً** |

`DbContext`: 41 `DbSet` · 39 تهيئة · `database/schema.sql` مرجعٌ ثابت ·
`scripts/sql/company-isolation-diagnostics.sql` تشخيص.

**عدد الجداول بالإنتاج فعلياً: 170** — مقابل 41 `DbSet` فقط. الفارق يعيش
بالمسارين 2 و3.

---

## K. Authentication / Authorization Surface

- **مخططا مصادقة:** Cookie (الويب) + `ApiTokenAuthHandler` (الموبايل، Bearer).
- **صفحات:** `Pages/Account/Login` · `Logout` (4 ملفات).
- **الطبقة المركزية (39 ملفاً بـ`Infrastructure/Security/`):**
  `RoleSecurityMiddleware` · `PublicPathPolicy` · `RoleRouteCatalog` ·
  `PeopleRoutePermissionResolver` · `PeopleTargetEmployeeResolver`.
- **العزل متعدّد الشركات:** `CompanyScope` · `ConfigTenantScope` ·
  `EmployeeCompanyGuard` · `EffectiveScopeService` · `EmployeeScopeEvaluator` ·
  `AccessRoleScopeTranslator` · `DataScopeCatalog`.
- **الجلسة:** `SessionSecurityValidator` · `SessionClaimsRefresher` ·
  `PortalSessionPolicy` · `CookieSecurityPolicy` · `LoginRateLimitPolicy`.
- **البصمة/الوجه:** `WebAuthnController` + `WebAuthnCredentialStore` + `WebAuthnProofStore` (Fido2).
- **كلمات المرور:** `SimplePasswordHasher` · `AccountSecurityStore` · `LoginDatabase`.
- **الملفّات المحميّة:** `ProtectedFileService` (`IDataProtector`) · `ProtectedFileStore` ·
  `UploadSignatureValidator`.
- **الرؤوس:** `SecurityHeaderPolicy` · `CspNonceExtensions` · `CspViolationSink` ·
  `CspReportEndpoint`.

---

## L. API Surface

| Route | Controller | Methods | Auth |
|---|---|---|---|
| `api/auth` | `AuthController` | `POST login` · `POST logout` | login = **AllowAnonymous** · logout = ApiToken |
| `api/me` | `MeController` | **11** (attendance · leave-balance · missing-punch ×2 · online-punch · punches · requests ×2 · data-change ×2 · profile) | ApiToken |
| `api/webauthn` | `WebAuthnController` | **6** (register/options·complete · punch/options·verify · login/options·verify) | Cookie · **login/* = AllowAnonymous** |
| `files` | `EmployeeFilesController` | `GET download` · `GET employee-profile/{fileId:int}` | Cookie |
| `push` | `PushController` | `GET vapid-key` · `POST subscribe·unsubscribe·test` | Authorize |

**Minimal APIs / نقاط مباشرة:** `/health/live` · `/health/ready` · `/app.apk` ·
`/csp-report` (+`/summary` بالتطوير فقط) — **جميعها AllowAnonymous**.
`MapStaticAssets().AllowAnonymous()` + `MapRazorPages().WithStaticAssets()`.

---

## M. Frontend Surface

189 `.cshtml` · 169 PageModel · 9 مشتركة · **57 ملفّ JS** (بلا أطر — JS خام) ·
**76 ملفّ CSS** · 78 أصل هوية (`wwwroot/brand/`) · 25 SVG ·
PWA (`sw.js` + `manifest.webmanifest` + `offline.html`) ·
5 مكتبات طرف ثالث (75 ملفاً).

---

## N. Mobile Surface

غلاف WebView أندرويد خام (بلا Gradle — `build.sh` مباشر):

```text
package            com.zynora.portal   (versionCode 1 / versionName 1.0)
Entry              MainActivity.java (WebView, launchMode singleTask)
Permissions        INTERNET · ACCESS_NETWORK_STATE
usesCleartextTraffic  false
networkSecurityConfig  base = cleartext ممنوع، system anchors فقط
                       domain-config = 192.168.1.53 (cleartext مسموح + user anchors)
Distribution       /app.apk ⟵ wwwroot/downloads/ZynoraPortal.apk
```

---

## O. CI/CD & Deployment Surface

**CI** (`.github/workflows/ci.yml`) — 4 وظائف على `windows-latest`:

```text
build-and-test      restore → build Release → xunit (يستثني ProductionClosureSqlTests) → رفع النتائج
sql-acceptance      git diff --check → LocalDB → ProductionClosureSqlTests فقط
security-audit      dotnet list package --vulnerable (يفشل عند وجود إصابة)
e2e                 Playwright — workflow_dispatch + ZYNORA_E2E_ENABLED فقط (يتخطّى بلا أسرار)
```

**النشر يدويّ بالكامل** — لا وظيفة نشر بالـCI:

```text
Source → CI (build+tests+audit) → [يدويّ] scripts/deploy/Publish-Zynora.ps1
       → C:\ZynoraPortal → Scheduled Task ZynoraPortalServer → run-server.bat
       → Cloudflare Tunnel → portal.zynorahr.com
```

الهجرات تُطبَّق **عند بدء التشغيل** لا بالـCI.

---

## P. Test Inventory

141 ملفاً (140 `.cs`) بمشروع واحد مسطّح — بلا مجلدات وحدات. xUnit + SkippableFact.
`SmartAttendance.E2E` = ملفّ واحد (`SmokeTests.cs`) بـNUnit/Playwright، **بلا مرجع
مشروع** (يختبر عبر HTTP فقط).
تصنيف الاختبارات لكل وحدة **مؤجَّل للمرحلة 2** (يلزمه فتح الملفّات).

---

## Q. Cross-Module Flows — للتتبّع بالمراحل التالية

| Flow | Entry | UI/API | App/Infra | DB |
|---|---|---|---|---|
| Login (ويب) | `/Account/Login` | `Login.cshtml.cs` | `LoginIdentityService` · `LoginDatabase` | SystemUsers · Employees |
| Login (موبايل) | `POST api/auth/login` | `AuthController` | `ApiTokenAuthHandler` | ApiTokens |
| WebAuthn punch | `api/webauthn/punch/*` | `WebAuthnController` | `WebAuthnCredentialStore` | WebAuthn* |
| Employee Creation | `/Employees/Create` | PageModel (1516 سطراً بالعرض) | `EmployeeService` | Employees + ~20 جدولاً |
| Attendance Import | `/AttendanceImports` | PageModel | `AttendanceImportService` | AttendanceRecords |
| Attendance Processing | `/AttendanceProcessing` · `/DayAttendance` | PageModel | `DayAttendanceStore` (1725) | Day* |
| Shift Assignment | `/ShiftAssignments` · `/Roster` | PageModel | `EmployeeShiftTypeStore` | ShiftTypes · EmployeeShiftTypes |
| Leave Request | `/LeaveRequests` | 5 صفحات | `LeaveRequestService` | LeaveRequests · LeaveBalances |
| Payroll Calculation | `/Payroll/*` | 17 صفحة | `PayrollRunStore` (1945) | Payroll* |
| File Upload/Download | `/files/*` | `EmployeeFilesController` | `ProtectedFileService` (مشفَّر) | EmployeeFileRecords |
| Permission Evaluation | كل طلب | `RoleSecurityMiddleware` | `EffectiveScopeService` | AccessRoles · Permissions |
| Announcement Publishing | `/Engagement` | 6 صفحات | `AnnouncementService` | 12 جدولاً |

---

## R. Inventory Discrepancies

**لا فرق بين شجرة المستودع والسجلّ: 2898 = 2898 (diff = 0).**

لكن **ملفّات يعتمد عليها النظام الحيّ وليست بالمستودع** (مستثناة بـ`.gitignore`
عمداً — تُسجَّل هنا لئلا تُحسَب «مُراجَعة» لاحقاً):

| Path | لماذا مهمّ |
|---|---|
| `SmartAttendance.Web/appsettings.json` + `.Development.json` | سلسلة الاتصال ومفاتيح SMTP/VAPID — **إعداد الإنتاج خارج التحكّم بالإصدار** |
| `SmartAttendance.Web/certs/` | شهادة TLS المحليّة |
| `wwwroot/uploads/{employee-photos,company-logos}` · `wwwroot/tenant-assets/` | مرفوعات وهوية |
| `App_Data/{DataProtection-Keys,PageImports,PositionImports}` | مفاتيح وحالة |
| `scripts/handover/*.ps1` | موجودان بالقرص · **مستثنيان بقاعدة `*.ps1`** (مُصلَح بفرع منفصل غير مدموج) |

---

## S. Blocked Areas

**لا منطقة محجوبة.** كلّ الملفّات قابلة للقراءة، والمستودع كامل محلياً.
قيدان يُسجَّلان للمراحل التالية:
1. **لا تشغيل بيانات إنتاج** — أيّ فحص حيّ يجري على `SmartAttendance_Test`.
2. **الاختبار الحيّ بالمتصفّح يحتاج جلسة دخول** لا أستطيع إنشاءها (ممنوع إدخال كلمات مرور).

---

## T. DISCOVERY OBSERVATIONS (رصدٌ فقط — بلا إصلاح)

| # | الرصد | الدليل |
|---|---|---|
| 1 | **ملفّ استيراد حضور حقيقيّ داخل المستودع** | `SmartAttendance.Web/App_Data/AttendanceImports/…Attendance_11230_With_Exceptions_20260101_20260717.xlsx` — و`CLAUDE.md` يمنع بيانات موظفين حقيقية بالمستودع |
| 2 | `graphify-out/` مولَّد ومكوميت — **1483 ملفاً / 168 ميغابايت** بشجرة العمل | أحدث لقطة مؤرَّخة `2026-07-25` بينما `HEAD` من `2026-08-11` ⟹ **بائت** بمعيار `CLAUDE.md` نفسه |
| 3 | `SmartAttendance.API` مشروع ويب ثانٍ لا يشير إليه أحد ولا يُنشر | `Program.cs` 23 سطراً · مجلد `Controllers/` فارغ |
| 4 | أربع طرق متوازية لتغيير المخطط (21 هجرة EF + 46 هجرة SQL + 390 شفاء ذاتي + 920 SQL خام) | يفسّر 170 جدولاً مقابل 41 `DbSet` |
| 5 | `ZynoraPortal.apk` المنشور بـ`/app.apk` حجمه **24 كيلوبايت** | حجمٌ لا يطابق تطبيقاً فعلياً — يستحق تحقّقاً |
| 6 | ملفّان `.css.disabled_20260706_*` مكوميتان | `wwwroot/css/zynora-sidebar-menu-cleanup.css.disabled_*` |
| 7 | `.graphify_root` شارد داخل مكتبة طرف ثالث | `wwwroot/lib/ag-grid/graphify-out/.graphify_root` |
| 8 | مرفوع مستخدم مكوميت | `wwwroot/uploads/disciplinary-forms/a4-form_20260706202516835.pdf` |
| 9 | `E2E` بلا مرجع مشروع و`Tests` يشير لـ`Web` (أعلى طبقة) | `*.csproj` |
| 10 | ملفّات ضخمة مرشَّحة لمراجعة عميقة | `EmployeeBootstrapImportEngine.cs` 4186 · `UserAccess/Index.cshtml.cs` 2037 · `PayrollRunStore.cs` 1945 · `DisciplinaryRules/Index.cshtml.cs` 1850 · `EmployeePortal/Index.cshtml.cs` 1729 · `DayAttendanceStore.cs` 1725 · `SqlSchemaMigrator.cs` 1523 |

---

## PHASE 1 STATUS

```text
PHASE 1 STATUS: COMPLETE
SYSTEM DISCOVERY COMPLETE

Repository files:              2898
Registered in AUDIT_COVERAGE:  2898
Inventory reconciliation:      100%   (diff = 0)

Deep code review has NOT started yet.
No source code was modified. No commit was created.
Ready for Phase 2.
```
