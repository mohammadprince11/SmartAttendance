# PHASE2_FINDINGS — سجلّ نتائج التدقيق العميق

```text
Repository: github.com/mohammadprince11/SmartAttendance
Branch:     main
Commit:     5c9de593ee73b09b1b8206849a603fc09f875e84
Module:     1 — Composition Root / Runtime  (الوحيد المكتمل)
```

---

## CONFIG-001

```text
Severity:   HIGH
Confidence: CONFIRMED
Category:   Configuration / Session Security
Status:     OPEN
```

**Title:** كوكي الجلسة `ZYNORA.Auth` تُصدَر بالإنتاج **بلا راية `Secure`** — لأن
إعدادات الجهاز الحيّ تسلك المخرج الصريح المخصّص للشبكة الداخلية.

**Affected Files:**
- `SmartAttendance.Web/Program.cs:76-85, 166-168`
- `SmartAttendance.Web/Infrastructure/Security/CookieSecurityPolicy.cs:38-57`
- `C:\ZynoraPortal\appsettings.json` (**خارج المستودع**) · `C:\ZynoraPortal\run-server.bat`

**Evidence (قِيَم الرايات فقط — بلا أي سرّ):**
```text
run-server.bat        ASPNETCORE_ENVIRONMENT = Production
                      ASPNETCORE_URLS = https://0.0.0.0:5443 ; http://0.0.0.0:5080
appsettings.json      ForceHttps                    = false
                      ReverseProxy:Enabled          = false
                      Security:AllowInsecureCookies = true
```

**Execution Path:**
`Program.cs:76` → `CookieSecurityPolicy.Evaluate(forceHttps:false, reverseProxyEnabled:false,
isProduction:true, allowInsecureCookies:true)` → `:45` لا → `:47` لا → `:51` لا →
`:54` **`FollowRequest`** → `Program.cs:166-168` → `CookieSecurePolicy.SameAsRequest`.

**Observed Behavior:** أي طلب يصل عبر المستمع `http://0.0.0.0:5080` (وهو ما يستعمله نفق
Cloudflare داخلياً، وأي عميل على الشبكة المحلية) يتلقّى كوكي المصادقة **بلا `Secure`**،
فيعيد المتصفّح إرسالها على أي اتصال غير مشفَّر لنفس المضيف.

**Expected Behavior:** بيئة إنتاج ⟹ `CookieSecurePolicy.Always`.

**Root Cause:** ليست ثغرة كود — **الكود يعمل كما صُمِّم**. السياسة تعرض ثلاثة مخارج،
والنشر الحيّ اختار الأضعف (`AllowInsecureCookies`) وهو المخصَّص صراحةً بتوثيق الملفّ
نفسه (`:29-32`) لـ«شبكة داخلية على HTTP» — بينما النظام يُخدَم فعلياً على الإنترنت
عبر `portal.zynorahr.com`. **عدم تطابق إعداد/واقع نشر.**

**Why existing controls do not prevent it:** الحارس `RefuseToStart` مُعطَّل تحديداً
بهذه الراية — وهي وظيفتها المقصودة.

**Recommended direction:** ضبط `ReverseProxy:Enabled=true` مع `KnownProxies` الفعلية
لموصّل النفق، وإزالة `AllowInsecureCookies`. عندها `:51` يعطي `AlwaysSecure`.
**يُصلح مع CONFIG-002 معاً — سببهما الجذريّ واحد.**

---

## CONFIG-002

```text
Severity:   HIGH
Confidence: CONFIRMED
Category:   Rate Limiting / Availability
Status:     OPEN
```

**Title:** حدّ معدّل الدخول ينهار من «لكل عنوان» إلى **دلوٍ عالميّ واحد** — عشر
محاولات فاشلة من أيّ مهاجم تُغلق الدخول على **كل** مستخدمي الإنترنت خمس دقائق.

**Affected Files:**
- `SmartAttendance.Web/Program.cs:296-317` (بناء المحدِّد) · `:477-480` (`UseForwardedHeaders` مشروط)
- `SmartAttendance.Web/Infrastructure/Security/LoginRateLimitPolicy.cs:20-23, 42-53`

**Evidence:** `Program.cs:477` — `UseForwardedHeaders()` لا يُستدعى إلا إن كان
`reverseProxyOptions.Enabled`، وهو **`false`** بالإعداد الحيّ. ومفتاح التقسيم
(`Program.cs:309`) هو `context.Connection.RemoteIpAddress` — أي عنوان **موصّل النفق
المحليّ** لكل حركة الإنترنت، لا عنوان الطالب.

**التوثيق نفسه يتنبّأ بالعطل ويؤكّد عكسه:** `LoginRateLimitPolicy.cs:48-50` يحذّر
حرفياً أن العنوان خلف وسيط يجب أن يكون مُطبَّعاً «**وهو مضبوط بهذا النظام**» —
هذا التأكيد **غير صحيح** بالإعداد الحيّ.

**Impact:**
- *Availability:* تعطيل دخولٍ كاملٍ بعشر محاولات (`PermitLimit=10` / `WindowMinutes=5`)، ويشمل `/Account/Login` و`/api/auth/login` ومسارَي WebAuthn ⟹ **الويب والموبايل معاً**.
- *Security:* الخنق المقصود لرشّ كلمات المرور يفقد تمييز المهاجم عن المستخدم الشرعيّ.

**Trigger:** أي عميل إنترنت واحد · بلا مصادقة · بلا معرفة أي اسم مستخدم.

**Related:** نفس السبب الجذريّ يهدّد أصل WebAuthn
(`WebAuthnController.cs:56` → `ForwardedOriginResolver.Resolve`) — **POTENTIAL ISSUE
— REQUIRES VERIFICATION** (لم يُقرأ `ForwardedOriginResolver` بعد).

**Recommended direction:** نفس إصلاح CONFIG-001.

---

## RT-003

```text
Severity:   MEDIUM
Confidence: CONFIRMED
Category:   Session Revocation / Fail-Open
Status:     OPEN
```

**Title:** إبطال الجلسة عند تغيير كلمة المرور أو تعطيل الحساب **يفشل مفتوحاً** عند أي
خطأ بقراءة القاعدة.

**Affected:** `SmartAttendance.Web/Program.cs:189-201` (`catch { accountState = null; }`)
و`:240-253` (`catch { return; }`).

**Evidence:** `accountState = null` يتخطّى `SessionSecurityValidator.Evaluate` بالكامل
(`:203`)، فلا `Reject` ولا `Refresh`. تعليق الكود يعلن الاختيار صراحةً:
«تعذّر الوصول للقاعدة لا يطرد الجلسات القائمة (توفّر قبل تشدّد)».

**Impact:** خلال عُطلٍ عابر بالقاعدة تبقى جلسة موظّفٍ مفصول/معطَّل حيّةً حتى انتهاء
الكوكي (8 ساعات، متجدّدة). والاستثناء **يُبتلع بلا تسجيل** فلا أثر يُراجَع.

**تصنيفٌ عادل:** مقايضة توفّر/أمان مقصودة وموثّقة، لا سهو. تُسجَّل لأنها قرار يستحق
مراجعة صاحب النظام، وتستحقّ على الأقلّ **تسجيلاً** للاستثناء المبتلع.

---

## RT-004 (INFO)

`Security:StrictCsp` مطفأة ⟹ `Program.cs:516-522` لا يُنفَّذ إطلاقاً: لا nonce ولا
ترويسة CSP صارمة. سياسة المحتوى الفعليّة تبقى `'unsafe-inline'` (مطابق لجرد
`docs/CSP-INLINE-INVENTORY.md`). لا جديد — يُثبَت هنا بالكود لا بالوثيقة.

---

## عناصر تحقّقتُ منها ولم تكن مشاكل (تُسجَّل لمنع إعادة الفحص)

| الفحص | النتيجة |
|---|---|
| `IProtectedFileService` مسجَّل **Singleton** | ✅ سليم — تبعيّاته (`IDataProtectionProvider`, `IWebHostEnvironment`) كلتاهما Singleton · لا captive dependency |
| `IEmailSender` / `IWebPushSender` Singleton | ✅ سليم — `IOptions<T>` + `ILogger<T>` فقط |
| `NotificationRuleGeneratorService` (HostedService) | ✅ سليم — `IServiceScopeFactory` + `CreateScope()` قبل `DbContext` (`:60-61`) |
| `PersistKeysToDbContext<ApplicationDbContext>` | ✅ سليم — `ApplicationDbContext : IDataProtectionKeyContext` مع `DbSet<DataProtectionKey>` |
| `/Error` و`/AccessDenied` المُشار إليهما بالإعداد | ✅ موجودتان فعلاً |
| `FallbackPolicy` يتطلّب مصادقة على كل نقطة | ✅ موجود (`:281-288`) مع إعفاءات صريحة `AllowAnonymous` |
| ترتيب الوسائط | ✅ سليم: ForwardedHeaders → ExceptionHandler → HSTS → SecurityHeaders → Routing → RateLimiter → Authentication → RoleSecurity → Authorization → Endpoints |
| DDL وقت الإقلاع | ✅ `SqlSchemaMigrator` و`ApiTokenStore.EnsureAsync` نُقلا للإقلاع (`:456-470`) |

---

# PHASE 3 — SECURITY FINDINGS (الوحدات 2–3: المصادقة والتوكنات)

## AUTHN-001

```text
Severity:   LOW
Confidence: CONFIRMED
Category:   Legacy Cryptography
Status:     OPEN
```

**Title:** التحقّق من كلمة المرور يقبل صيغةً قديمة بـ**SHA-256 بدورة واحدة**.

**Evidence:** `SimplePasswordHasher.cs:114-127` — `VerifyLegacySha256` تحسب
`SHA256(salt + ":" + password)` بدورة واحدة، وتُستدعى بـ`:76` كلما فشل تحليل صيغة
PBKDF2. أي صفٍّ ما زال بالصيغة القديمة يُقبل بتجزئةٍ **سريعة** ⟹ تكسيرٌ خارجيّ
زهيد لو تسرّبت القاعدة.

**لماذا LOW لا HIGH — تحقّقتُ من المخفّف:** `NeedsRehash` (`:93`) تُستدعى فعلاً على
مسارَي الدخول (`Login.cshtml.cs:132` و`AuthController.cs:62`)، فتُرقّى كل كلمة مرور
قديمة **عند أول دخول ناجح**. النافذة = حسابات لم تُستعمل منذ الترقية فقط.

**Recommended direction:** بعد التأكّد من ترقية الجميع، احذف المسار القديم فيصير
الرفض قاطعاً.

---

## AUTHN-002

```text
Severity:   MEDIUM
Confidence: CONFIRMED
Category:   Inconsistent Fail Behavior
Status:     OPEN
```

**Title:** نفس ضابط الأمان (إبطال الجلسة بختم الحساب) **يفشل مغلقاً بمسار الموبايل
ومفتوحاً بمسار الويب**.

**Evidence:**
- الموبايل: `ApiTokenAuthHandler.cs:50` يستدعي `AccountSecurityStore.GetStateAsync`
  **بلا `try/catch`** ⟹ عطل القاعدة يرمي ⟹ الطلب يُرفض (**fail-closed**).
- الويب: `Program.cs:189-201` يلفّ نفس الاستدعاء بـ`catch { accountState = null; }`
  ⟹ يتخطّى `SessionSecurityValidator` بالكامل ⟹ الجلسة تستمرّ (**fail-open**).

**Impact:** خلال عطل قاعدة عابر، موظّف عُطِّل حسابه يُطرد من التطبيق ويبقى داخلاً من
المتصفّح. سلوكٌ أمنيّ غير متّسق لنفس الضابط — وهو ما يجعل التدقيق والتوقّع صعبين.
(هذه هي RT-003 نفسها من الوحدة 1، مضافاً إليها إثبات عدم الاتساق.)

---

## عناصر أمنية تحقّقتُ منها وكانت **سليمة** (بالدليل — لئلا يُعاد فحصها)

| الضابط | الإثبات |
|---|---|
| تخزين كلمات المرور | `PBKDF2-SHA256` · **210,000 دورة** · ملح 32 بايت من `RandomNumberGenerator` · مقارنة `CryptographicOperations.FixedTimeEquals` — **قويّ** رغم اسم الصنف المضلِّل `SimplePasswordHasher` |
| مقاومة عدّ الحسابات (enumeration) | `PerformDummyVerification` تُستدعى على **الفروع الثلاثة** للفشل (`Login.cshtml.cs:76, 89, 103`) وعلى مسار API (`AuthController.cs:48`) — فزمن الردّ لا يفرّق بين «مستخدم غير موجود» و«كلمة خاطئة» |
| ترقية التجزئة | `NeedsRehash` مُستهلَكة فعلاً بمسارَي الدخول |
| عشوائية توكن الـAPI | `RandomNumberGenerator.GetBytes(32)` = **256 بت** · base64url |
| تخزين التوكن | **مجزّأً فقط** (SHA-256 hex) بفهرس فريد · النصّ العلني لا يُخزَّن — والتجزئة السريعة مقبولة هنا لأن المدخل عالي العشوائية |
| انتهاء/إلغاء التوكن | `RevokedAt IS NULL AND ExpiresAt > SYSUTCDATETIME()` **داخل استعلام التحقّق نفسه** لا بعده |
| إبطال توكن الموبايل عند تغيير الدور/كلمة المرور | **مؤكَّد بالكود** لا بالتعليق: `ApiTokenAuthHandler.cs:50-59` يقارن ختم الأمان بكل طلب ويعيد بناء الدور من الحالة الحالية |
| حقن SQL بمسار التوكن | كل الجُمَل بمعاملات (`AddParameter`) — بلا أي تركيب نصّي |

---

# PHASE 3 — MODULE 4: AUTHORIZATION

## AUTHZ-003  🔴

```text
Severity:   CRITICAL
Confidence: CONFIRMED (static trace across all three layers — لم يُنفَّذ أي استغلال)
Category:   Broken Object-Level Authorization (IDOR) + Cross-Tenant Data Destruction
Status:     OPEN
```

**Title:** أيّ موظّف مصادَق يستطيع **قراءة وحذف أي طلب إجازة بأي شركة** بمعرفة رقمه
فقط — بلا فحص ملكية ولا نطاق شركة على أيّ من الطبقات الثلاث.

**Asset at Risk:** جدول `LeaveRequests` لكل الشركات (ثلاث شركات مأهولة بالإنتاج).
**Required Attacker Access:** أدنى دور بالنظام — `Employee` مصادَق. بلا أي صلاحية إضافية.
**Security Boundary Crossed:** ملكية المورد **و** عزل الشركات معاً.

### Evidence — الطبقات الثلاث كلها مفتوحة

**١) الحارس المركزي لا يطالب بصلاحية ديناميكية لهذا المسار**
`PeopleRoutePermissionResolver.Resolve` يغطّي `/employees*` و`/employeedocuments`
و`/employeepermissions` و`/leavebalances/adjust` و`/payroll/terminationsettlement`
فقط — **`/leaverequests` غير مذكور**. فيرجع `null`، ويقع القرار على
`compatibilityAllowed` وحده (`RoleSecurityMiddleware.cs:164-167`).

**٢) فحص الملكية التوافقي يُفلت الطلب**
دور `Employee` على `/leaverequests` يمرّ إلى `IsOwnRequestAsync`
(`RoleSecurityMiddleware.cs:276-279`)، وهناك:
- `:362` — `if (!HttpMethods.IsPost(...)) return true;` ⟹ **كل GET مسموح بلا فحص**.
- `:367` — `if (!HasFormContentType) return true;` ⟹ POST بلا نموذج يمرّ.
- `:374-397` — الحلقة تفحص **فقط** مفاتيح النموذج التي يحوي اسمها `EmployeeId`.
  ونموذج الحذف يرسل حقلاً واحداً: `<input type="hidden" name="id">`
  (`Delete.cshtml:72`) ⟹ **الحلقة لا تدور ولا مرّة** ⟹ `return true`.

**٣) الصفحة والخدمة بلا أي حارس**
```text
Delete.cshtml.cs:22  OnGetAsync(int id)  → _leaveRequestService.GetByIdAsync(id)
Delete.cshtml.cs:33  OnPostAsync(int id) → _leaveRequestService.DeleteAsync(id)
LeaveRequestService.cs:64 / :141          → بحثٌ وحذفٌ بالمعرّف وحده
```
لا `CompanyScope` · لا `EmployeeCompanyGuard` · لا مقارنة بـ`EmployeeId` للمستخدم.

### Execution Path
```text
GET/POST /LeaveRequests/Delete?id=N
→ FallbackPolicy: مصادَق ✔ (الموظّف مصادَق فعلاً)
→ PublicPathPolicy.Classify: ليس Public/BackOfficeOnly
→ IsCompatibilityAllowedAsync → Employee → IsOwnRequestAsync → true
→ PeopleRoutePermissionResolver.Resolve = null → return compatibilityAllowed = true
→ DeleteModel.OnPostAsync(id) → LeaveRequestService.DeleteAsync(id)
→ DELETE FROM LeaveRequests WHERE Id = N        ← بلا CompanyId وبلا EmployeeId
```

### Why existing controls are insufficient
`FallbackPolicy` يثبت **الهوية** لا **الملكية**. وفحص الملكية الوحيد الموجود
مبنيّ على **اسم حقل النموذج** — فهو يصمد صدفةً حيث يُرسل `EmployeeId` (مثل
`Edit` الذي يربط `LeaveRequest.EmployeeId`) وينهار حيث لا يُرسل (`Delete`).
حمايةٌ عرضيّة لا مُصمَّمة.

### Occurrences (نفس السبب الجذريّ — لا تُفرَّق لنتائج منفصلة)
- `Pages/LeaveRequests/Delete.cshtml.cs` — قراءة **وحذف** (الأخطر)
- `Pages/LeaveRequests/Edit.cshtml.cs:37` — `OnGetAsync(int id)` بلا حارس (القراءة مكشوفة؛ الـPOST يصمد عرضاً)
- `Pages/LeaveRequests/Index.cshtml.cs:22` — `OnGetAsync()` بلا نطاق شركة — **يحتاج تحقّقاً إضافياً**
- `Infrastructure/Services/LeaveRequestService.cs:64,141` — الأصل: خدمة بلا نطاق

### Impact
- *Data destruction:* حذف طلبات إجازة لأي موظّف بأي شركة (لا يبدو أنه حذف ناعم).
- *Cross-tenant confidentiality:* قراءة سبب الإجازة وتواريخها وبيانات صاحبها.
- *Downstream:* الإجازات تتفاعل مع الرصيد والحضور والمسير — الحذف يغيّر مخرجات لاحقة.

### Safe Verification Method (بلا لمس الإنتاج)
اختبار تكامل على `SmartAttendance_Test`: أنشئ طلبَي إجازة لشركتين، سجّل دخول
حساب موظّف من الشركة A، واطلب `Delete` بمعرّف طلب الشركة B — التوقّع الصحيح
`404/403`، والسلوك المتوقَّع حالياً نجاح الحذف.

### Root Cause
**ROOT-SEC-01 — إنفاذ الملكية والنطاق ليس مركزياً.** الحارس يعرف الدور والمسار،
ولا يعرف **المورد**. كل صفحة مسؤولة عن حارسها بنفسها، فأي صفحة تُنسى تصبح مكشوفة —
وهذا نفس النمط الذي أنتج ثغرات العبور الستّ المُغلقة سابقاً.

### Recommended direction (لا تُنفَّذ الآن)
حارس ملكية على مستوى **الخدمة** لا الصفحة: `LeaveRequestService` يتلقّى
`CompanyScope` + هوية الطالب ويفرضهما داخل الاستعلام — فلا تعتمد الحماية على
تذكُّر كاتب الصفحة. ثم إضافة `/leaverequests` لمُحلِّل الصلاحيات الديناميكي.

---

## AUTHZ-004

```text
Severity:   HIGH
Confidence: CONFIRMED
Category:   Weak Ownership Check Design
Status:     OPEN
```

**Title:** فحص ملكية الخدمة الذاتية **يفشل مفتوحاً بثلاث طرق** ويعتمد على تسمية
حقول النموذج.

**Evidence:** `RoleSecurityMiddleware.IsOwnRequestAsync` (`:351-400`) —
(أ) كل GET مسموح · (ب) POST بلا `form content-type` مسموح · (ج) POST بنموذج لا
يحوي مفتاحاً اسمه يتضمّن `EmployeeId` مسموح. ثلاثتها `return true` صريحة.

**Impact:** هو المُمكِّن لـAUTHZ-003، وينطبق على `/selfservices` أيضاً — أي صفحة
تُضاف تحت هذين المسارين ترث الثغرة تلقائياً.

**Recommended direction:** قلب الافتراض — `return false` ما لم تُثبَت الملكية.

---

## عناصر تحقّقتُ منها وكانت سليمة (الوحدة 4)

| الفحص | النتيجة |
|---|---|
| `RoleRouteCatalog.IsAdmin` | ✅ مقارنة **مساواة** كاملة (`Equals(Admin, OrdinalIgnoreCase)`) لا `Contains` — لا انتحال بدور اسمه «Administrator2» |
| `RoleRouteCatalog.Matches` | ✅ `path == allowed \|\| StartsWith(allowed + "/")` — البادئة مُنتهية بشرطة، فـ`/employees` **لا** يطابق `/employeesalaries` |
| مسار الصلاحيات الديناميكي بلا هوية | ✅ **fail-closed**: `systemUserId` غائب + متطلَّب ديناميكي ⟹ `return false` (`:172-175`) |
| صنف المسار `BackOfficeOnly` | ✅ يردّ **404** لدور Employee لا 403 — لا يكشف وجود الصفحة |
| مزلاج `EnsureLoginDatabaseCreatedAsync` | ✅ فحص مزدوج + `SemaphoreSlim` — بلا DDL بكل طلب |

---

# PHASE 3 — MODULE 5 (جزئيّ): هل AUTHZ-003 حالةٌ فردية أم نمط؟

## ROOT-SEC-01 (نتيجة نظاميّة)

```text
Severity:   MEDIUM (هشاشة تصميم — لا ثغرة حيّة مؤكَّدة خارج AUTHZ-003)
Confidence: CONFIRMED
Category:   Systemic — Authorization Placement
Status:     OPEN
```

**Title:** إنفاذ النطاق يعيش **بالصفحة** لا بالخدمة، فالحماية تعتمد على تذكُّر كاتب
كل صفحة — وكل صفحة جديدة تبدأ مكشوفة افتراضاً.

**Evidence — مسحٌ لكل خدمات `Infrastructure/Services` بمنهج AUTHZ-003:**

| الخدمة | دوالّ بالمعرّف وحده | ذكرٌ للنطاق بالخدمة | حال صفحاتها |
|---|---|---|---|
| `AttendanceRecordService` | 3 | **0** | ✅ 4/4 صفحات تحرس — **مُعوَّض** |
| `EmployeeService` | 3 | 43 | ✅ محروس بالخدمة |
| `DepartmentService` | 3 | 15 | أدمن فقط بالمسارات |
| `CompanyService` | 3 | 1 | أدمن فقط |
| `HolidayService` | 3 | **0** | ⚠️ 0/5 صفحات — يبلغه HR Manager/Officer |
| `DeviceService` | 3 | 1 | ⚠️ 0/5 صفحات — يبلغه HR Manager |
| `ShiftService` · `PermissionService` · `EmployeeShiftService` | 3 لكلٍّ | 0 | لم يُتحقَّق |

**الاستنتاج المُثبَت:** النمط عامّ، لكنه **ليس ثغرةً عامّة**. `AttendanceRecords`
مكشوفة بالخدمة و**محميّة بالصفحة** — ولهذا لم أُعلنها ثغرة (تحقّق متقاطع، §59).
الفارق أن الحماية هناك موجودة **بالصدفة التنظيمية** لا بالبنية.

**POTENTIAL ISSUE — REQUIRES VERIFICATION:** `Holidays` و`Devices` — خدمةٌ بلا نطاق
وصفحاتٌ بلا حارس، ويبلغهما دورٌ **مقيَّد بشركة** (HR Manager/Officer). لم أُثبت
بعدُ هل الكيانان مرتبطان بشركة أصلاً (قد يكونان تهيئةً مشتركة كـ`ShiftTypes`).
**لا يُعلَن ثغرةً قبل قراءة الخدمتين.**

**Recommended direction:** تعميم نمط AUTHZ-003 — النطاق معطىً إلزاميّاً بعقد كل
خدمة تلمس بيانات شركة، فيصير المُصرِّف هو الحارس بدل الذاكرة البشرية.

---

# حالة النتائج بعد النشر — 2026-08-11 23:00

| ID | الحالة |
|---|---|
| **AUTHZ-003** (CRITICAL) | ✅ **مُغلقة ومنشورة** — PR #28 ⟶ `d003f3f` · التجميعة المنشورة مطابقة بالبايت للمبنيّة المُختبَرة |
| **AUTHZ-004** (HIGH) | 🟡 **مُحيَّدة عملياً** لطلبات الإجازة (الحارس صار بالخدمة) · النمط باقٍ بـ`IsOwnRequestAsync` لأي مسار تحته |
| **CONFIG-001** (HIGH) | ✅ **مُغلقة** — `ReverseProxy:Enabled=true` ⟹ `Evaluate` يعطي `AlwaysSecure` ⟹ كوكي الجلسة `Secure` دائماً · `AllowInsecureCookies=false` |
| **CONFIG-002** (HIGH) | 🟡 **مُغلقة بالكود، غير مُتحقَّقة حيّاً** — `UseForwardedHeaders()` صار يُستدعى (الراية مؤكَّدة بالإعداد المقروء عند الإقلاع)، لكن لم أُثبت أن cloudflared يرسل `X-Forwarded-For` فعلاً |
| RT-003 / AUTHN-002 | 🔴 مفتوحة (fail-open بمسار الويب) |
| AUTHN-001 | 🔴 مفتوحة (LOW) |
| ROOT-SEC-01 | 🔴 مفتوحة — `Holidays`/`Devices` بلا تحقّق |

## طريقة التحقّق المتبقّية لـCONFIG-002 (تحتاج جلسة دخول)

افتح `/AuditLogs` بعد دخول مستخدمين من شبكتين مختلفتين: ظهور **عناوين مختلفة**
يثبت أن الترويسة تصل وتُطبَّع. ظهور عنوان واحد متكرّر (`127.0.0.1`) يعني أن
cloudflared لا يمرّرها، وعندها يبقى حدّ الدخول دلواً عالميّاً.

## ملاحظة صغيرة رُصدت أثناء التحقّق (INFO)

كوكي مقاومة التزوير `.AspNetCore.Antiforgery.*` تُصدَر **بلا `Secure`** حتى على
HTTPS حقيقيّ (سياستها الافتراضية `None`، لا `SameAsRequest`). لا أثر أمنيّ مباشر
(المحتوى ليس سرّاً والكوكي `httponly`+`samesite=strict`)، لكنه يستحقّ الضبط
صراحةً. **ولا تستعملها مؤشّراً على عمل الترويسات المُمرَّرة — استعملتُها فأعطت
نتيجةً كاذبة.**
