# ZYNORA HR / SmartAttendance — قرار التجهيز للإنتاج السحابي

**تاريخ التدقيق:** 2026-08-07
**المرجع المُدقَّق:** `main` @ `86f8bbb` (2026-08-02)
**الطريقة:** فحص الكود والهجرات والاختبارات والإعدادات والـCI مباشرةً. **لم يُعتمد أي
تقرير أو وثيقة سابقة** — كل ملاحظة أدناه مثبتة بملف وسطر من الشجرة الحالية.
**البناء وقت التدقيق:** `dotnet build -c Release` أخضر · `dotnet test` أخضر ·
`dotnet list package --vulnerable --include-transitive` صفر.

---

## 1) المعمارية الحالية (كما هي فعلاً)

سبعة مشاريع: `Domain` (59 ملفاً) · `Application` (111) · `Infrastructure` (106) ·
`Web` (356 cs + 189 cshtml) · `API` (غلاف من ملف واحد) · `Tests` (68) · `E2E` (1).

- **العرض:** Razor Pages (172 نموذج صفحة) + ثلاثة كنترولرات REST تحت `/api/*`
  لتطبيق الموبايل + `/files` للتنزيل المحمي + `/push` للإشعارات.
- **الوصول للبيانات:** مزدوج. EF Core (`ApplicationDbContext`) للكيانات القديمة،
  و**SQL خام عبر `HrmsDatabase`** لكل ما بُني لاحقاً — **155 ملفاً** يستعمله،
  منها 105 «Store» تحت `Infrastructure/Hrms/`.
- **المخطط:** يُدار بمسارين: هجرات EF (متوقفة عند 2026-07-17) و
  **`SqlSchemaMigrator`** (SQL خام بمعرّفات ثابتة وجدول `__SchemaMigrations`)
  الذي يعمل عند الإقلاع وهو المسار الحيّ فعلياً.
- **التخويل:** `RoleSecurityMiddleware` مركزيّ، يجمع بين كتالوج مسارات ثابت لكل
  دور (`RoleRouteCatalog`) ومحرك صلاحيات ديناميكي (`PeopleRoutePermissionResolver`
  + `PermissionAuthorizationService`)، مع محرك نطاق بيانات نقيّ (`PeopleDataScope`).

---

## 2) نقاط قوة مثبتة (وكثير منها يناقض وثائق أقدم)

| البند | الدليل |
|---|---|
| **تجزئة كلمات المرور** | `SimplePasswordHasher.cs`: PBKDF2-SHA256 · **210,000** تكرار · ملح 32 بايت عشوائي · `CryptographicOperations.FixedTimeEquals` · `PerformDummyVerification` ضد التوقيت · `NeedsRehash` للترقية |
| **قفل الحساب** | `LoginDatabase.MaximumFailedLoginAttempts = 5` + `LockoutDuration`، ويُفحص بالثلاثة مداخل (صفحة الدخول · `/api/auth` · WebAuthn) |
| **لا اعتماد مبثوث** | `LoginDatabase.cs:79` — أدمن الإقلاع من `ZYNORA_BOOTSTRAP_ADMIN_PASSWORD` حصراً، ويفشل بوضوح إن غاب |
| **إبطال الجلسة فوراً** | `SecurityStamp` مختوم بالكوكي **وبتوكن الـAPI**؛ `OnValidatePrincipal` + `ApiTokenAuthHandler` يقارنانه بكل طلب (كاش 60ث) — تعطيل حساب أو تغيير دور يطرد الجلسات حالاً |
| **الحارس مُغلَق الفشل** | `RoleSecurityMiddleware.IsAllowedAsync`: مسار غير معروف ⟹ `false`؛ وغياب هوية النظام مع متطلب ديناميكي ⟹ `false` صراحةً |
| **ترويسات الوسيط** | معطّلة افتراضياً؛ وعند التفعيل تُمسح `KnownProxies`/`KnownIPNetworks` الافتراضية ويُعتمد المصرَّح به فقط، `ForwardLimit = 1` — لا انتحال بروتوكول أو مضيف |
| **ترويسات الأمان وCSP** | `SecurityHeaderPolicy`: `default-src 'self'` · `object-src 'none'` · `base-uri` · `form-action` · `frame-ancestors` · `nosniff` · `Referrer-Policy` · `Permissions-Policy` |
| **`/uploads/` لم تعد عامة** | `PublicPathPolicy.Classify`: الشعارات عامة · صور الموظفين تحتاج مصادقة · الباقي باك-أوفيس فقط |
| **التنزيل المحمي نموذجيّ** | `EmployeeFilesController.Download`: رمز موقّع بـData Protection **ثم إعادة فحص الصلاحية على الخادم** ثم تدقيق — «الرمز يمنع التبديل، والتخويل يُفحص بعده لا به» |
| **تحقق الرفع** | `ProtectedFileService.SaveAsync`: قائمة امتدادات مسموحة + **فحص البصمة السحرية** (`UploadSignatureValidator`) |
| **لا IDOR بالـAPI** | `MeController` يشتقّ `EmployeeId` من مطالبة التوكن حصراً — كل استعلاماته (13 نقطة) تستعمله؛ لا يقبل معرّفاً من الطلب |
| **CSRF** | Razor Pages تتحقق تلقائياً؛ والكنترولرات ذات الكوكي تستقبل `[FromBody]` JSON **ولا CORS مُعرَّفة** ⟹ الطلب عبر الأصول محجوب بالـpreflight |
| **CI حقيقي** | `.github/workflows/ci.yml`: بناء Release · اختبارات بمسار المشروع صراحةً · **فحص حزم مصابة يُسقط الوركفلو** · `permissions: contents: read` · لا نشر آلي |
| **الأسرار خارج Git** | `.gitignore` يستثني `appsettings.json` و`appsettings.*.json` و`secrets.json` و`.env` ومفتاح TLS |
| **محرك النطاق صحيح** | `CompanyIsolationScopeTests` يثبت أن `PeopleDataScope.AllowsEmployee` يمنع عبور الشركات — بما فيه تصادم الفروع |

---

## 3) ملاحظات بطلت أو غير قابلة للتطبيق

- **«`/uploads/` عامة»** — بطلت. `PublicPathPolicy` يصنّفها.
- **«كلمات المرور بـSHA256»** — بطلت. المسار الحالي PBKDF2؛ ودالة SHA256 باقية
  **للتحقق من القديم فقط** مع `NeedsRehash`.
- **«`Employee` بلا `CompanyId`»** — بطلت. الهجرات `20260731-04/05/06` أضافت العمود،
  وعبّأته من الفرع، وشدّدته `NOT NULL` بمفتاح أجنبي خلف حاجز أمان.
  ⚠️ تعليق `CompanyIsolationScopeTests.cs:13` ما زال يقول العكس — **تعليق بائت**.
- **«لا تدقيق لحزم NuGet»** — بطلت. وظيفة `security-audit` بالـCI.

---

## 4) عوائق الإنتاج المثبتة

### 🔴 P0-1 · الرواتب بلا أي عزل شركات — **MULTI-TENANT BLOCKER · Critical**

**Finding:** تشغيل مسير الرواتب يشمل موظفي **كل الشركات**، وتفاصيل أي دفعة تُفتح
بمعرّفها بلا فحص ملكية.

**Evidence:**
- `Infrastructure/Hrms/PayrollRunStore.cs:287`:
  `SELECT Id, EmployeeNo, FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1 ORDER BY EmployeeNo;`
  — **بلا `CompanyId`**.
- `grep -c CompanyId` على `PayrollRunStore.cs` و`PayrollTransactionStore.cs` = **صفر**.
- `Pages/Payroll/RunDetail.cshtml.cs:34-44`: `Id` من `[BindProperty(SupportsGet)]`
  ⟹ `GetRunAsync(_db, Id)` + `ListLinesAsync(_db, Id)` **بلا أي فحص ملكية**.
- نفس الصفحة `OnGetBankFileAsync` تُصدِّر **ملف البنك (آيبان/بطاقات)** بنفس المعرّف.
- `CompanyName` = `SELECT TOP 1 ... FROM Companies ORDER BY Id` — افتراض شركة واحدة مثبَّت بالكود.
- `PayrollRunScope` أوضاعه (All/Manual/Paste/File/Criteria) **اختيار مستخدم لا حدّ أمنيّ**،
  وافتراضه «كل النشطين».

**Why:** مستخدم شركة A يعدّل `?Id=` فيقرأ رواتب شركة B ويُنزّل حساباتها البنكية.
وحتى بلا نيّة، تشغيل مسير شركة A يولّد قسائم لموظفي B. هذا ليس تسريباً محتملاً بل
سلوك افتراضي.

### 🔴 P0-2 · تبنّي نطاق البيانات 6 من 172 — **MULTI-TENANT BLOCKER · Critical**

**Evidence:** محرك النطاق صحيح ومُختبَر، لكن مستهلكيه بكل مشروع الويب **11 ملفاً**،
ومن نماذج الصفحات **6 فقط** — كلها تحت `Pages/Employees/`. الباقي (166 نموذجاً:
الحضور · الإجازات · المستندات · التأديب · العهد · التقارير · لوحات المعلومات ·
الاستيراد · الإشعارات) لا يستشير النطاق إطلاقاً. و**134 من 155** ملف SQL خام لا
يذكر `CompanyId`.

**Why:** العزل مطبَّق بمودل واحد. القاعدة التي طلبتَها — «إثبات العزل end-to-end» —
غير قابلة للإثبات خارج مودل الأشخاص، وقد أثبتُّ نقضها بالرواتب.

### 🔴 P0-3 · مفاتيح Data Protection على قرص محلّي — **SCALE-OUT BLOCKER · High**

**Evidence:** `Program.cs:114-117` — `PersistKeysToFileSystem(App_Data/DataProtection-Keys)`
بلا `SetApplicationName` وبلا تخزين مشترك وبلا تشفير عند الراحة.

**Why:** بنسختين، كلٌّ تولّد حلقة مفاتيحها ⟹ كوكي صادر من A يُرفض على B (طرد عشوائي)،
و**رموز تنزيل الملفات الموقّعة** (`/files/download?t=`) تصير غير قابلة لفكّ التشفير
عبر النسخ. وبكل إعادة نشر على حاوية جديدة يُطرد الجميع.

### 🔴 P0-4 · هجرات الإقلاع بقفل داخل العملية فقط — **SCALE-OUT BLOCKER · High**

**Evidence:** `SqlSchemaMigrator`: `private static readonly SemaphoreSlim Gate = new(1,1)`
و`volatile bool _applied`. و`Program.cs:359-363` يشغّلها عند الإقلاع.

**Why:** `SemaphoreSlim` يحمي داخل عملية واحدة. نسختان تقلعان معاً تدخلان الحلقة
معاً؛ فحص `__SchemaMigrations` ثم التنفيذ **ليس ذرّياً** ⟹ `ALTER TABLE` مزدوج،
وسباق حقيقي على `20260731-06` (إسقاط فهرس ← تعديل عمود ← إعادة بناء). أثناء تدوير
النشر بـApp Service (نسخة قديمة + جديدة معاً) هذا سيناريو عاديّ لا استثنائي.

### 🟠 P1-1 · الملفات على نظام الملفات المحلّي — **SCALE-OUT BLOCKER · High**

**Evidence:** 13 موقع رفع يكتب تحت `wwwroot`/`WebRootPath` مباشرةً
(`Payroll/Loans` · `EmployeeDocuments` · `Employees/{Create,Edit,Profile,FinancialInfo}` ·
`EmployeePortal/{Index,DataChange}` · `DisciplinaryRules` · `CompanyDocuments` · `Setup` …)،
مقابل 13 موقعاً فقط يمرّ بـ`IProtectedFileService`.

**Why:** بـApp Service الملفات ليست مشتركة بين النسخ ولا تنجو من إعادة النشر.
ملفٌ رُفع على النسخة A يعطي 404 من B. وهذه المواقع أيضاً **خارج فحص البصمة السحرية**.

### 🟠 P1-2 · لا `EnableRetryOnFailure` — **NEEDS IMPROVEMENT · High (سحابياً)**

**Evidence:** `Program.cs:242-244` — `UseSqlServer(connectionString)` مجرّدة.

**Why:** Azure SQL يقطع الاتصالات روتينياً (تجاوز موارد، تحويل، صيانة). بلا إعادة
محاولة، كل قطع عابر يصير استثناءً للمستخدم. الأثر أخطر على `AnalyzeMonthAsync`
و`PayrollRunStore` — عمليات طويلة بمعاملات.

### 🟠 P1-3 · لا فحوص صحّة — **NEEDS IMPROVEMENT · Medium**

**Evidence:** لا `AddHealthChecks` ولا `MapHealthChecks` بكل الحل.

**Why:** App Service/Container Apps تحتاج مسبار جاهزية لتوجيه الحركة. بدونه تتلقى
النسخة طلبات قبل اكتمال الهجرات والبذور — أي أثناء أخطر لحظة بدورة حياتها.

### 🟠 P1-4 · خدمتان خلفيتان بلا قفل موزَّع — **SCALE-OUT BLOCKER · Medium**

**Evidence:** `Program.cs:301` `NotificationDispatcherService` و`:325`
`NotificationRuleGeneratorService` (كرون 08:00). لا فحص «هل نسخة أخرى تشتغل».

**Why:** ثلاث نسخ = ثلاثة إشعارات لكل موظف. (المولّد يمنع التكرار بجدول أحداث —
لكن الموزِّع لا يفعل، وسباق القراءة-ثم-الكتابة قائم بينهما.)

### 🟠 P1-5 · لا تحديد معدّل على الدخول — **NEEDS IMPROVEMENT · Medium**

**Evidence:** لا `AddRateLimiter`. الحماية الوحيدة قفل الحساب (5 محاولات) بـ
`RecordFailedLoginAsync` — **لكل حساب**.

**Why:** رشُّ كلمة مرور واحدة على ألف حساب لا يلمس أي عدّاد قفل. وبالعكس: القفل
نفسه سلاح تعطيل — معرفة اسم المستخدم تكفي لإقفال المدير متى شئت.

### 🟡 P1-6 · `/api/me/attendance` يحمّل الجدول كله — **NEEDS IMPROVEMENT · High عند الحجم**

**Evidence:** `MeController.cs:75` — `DayAttendanceStore.ListRangeAsync(_db, from, to, null)`
(كل الموظفين) ثم `.Where(r => r.EmployeeId == EmployeeId)` **بالذاكرة** (سطر 76).

**Why:** 10,000 موظف × 90 يوماً ≈ 900,000 صفّ لكل فتحة تطبيق موبايل. عند 50,000
موظفاً تصير كل نقرة استعلاماً بملايين الصفوف. ليس تسريباً (يُرشَّح قبل الإرجاع)
لكنه سبب انهيار مؤكّد تحت الحمل، ومتّجه إساءة استخدام صريح.

### 🟡 P2-1 · راية `Secure` للكوكي تتبع الإعداد — **NEEDS IMPROVEMENT · Medium**

**Evidence:** `Program.cs:130-132` — `SecurePolicy = forceHttps ? Always : SameAsRequest`
و`forceHttps` افتراضه `false`.

**Why:** خلف منهٍ للـTLS عند الحافة يمرّر HTTP داخلياً، وبلا تفعيل قسم الوسيط
العكسي، تُصدَر كوكي الجلسة **بلا `Secure`**. سحابياً يجب تثبيت
`ForceHttps=true` أو `ReverseProxy.Enabled=true` — وإلا فالإعداد الافتراضي غير آمن.

### 🟡 P2-2 · لا مراقبة ولا تتبّع — **NEEDS IMPROVEMENT · Medium**

**Evidence:** لا Serilog ولا Application Insights ولا OpenTelemetry. `AuditLogs`
جدول تطبيقيّ (ممتاز للتدقيق) لكنه ليس مراقبة تشغيلية.

### 🟢 P3-1 · `'unsafe-inline'` بـCSP — **ACCEPTABLE**

موثّقة بالكود بسببها (مئات الصفحات بسكربتات مضمّنة). إزالتها تحتاج ترحيل nonce شاملاً.

---

## 5) جدول القرار

| Area | Status | Risk | Production Impact | Required Action |
|---|---|---|---|---|
| المصادقة وكلمات المرور | SAFE | Low | لا شيء | — |
| قفل الحساب / ختم الأمان | SAFE | Low | لا شيء | — |
| تحديد معدّل الدخول | NEEDS IMPROVEMENT | Medium | رشّ كلمات مرور · قفل تعطيليّ | `AddRateLimiter` على `/Account/Login` و`/api/auth/login` |
| كوكيز الجلسة | NEEDS IMPROVEMENT | Medium | كوكي بلا `Secure` بإعداد خاطئ | تثبيت `ForceHttps=true` سحابياً + حارس إقلاع |
| تخويل الـAPI / IDOR | SAFE | Low | لا شيء | — |
| CSRF | ACCEPTABLE | Low | محجوب بالـpreflight | إضافة الفحص صراحةً لاحقاً |
| رفع الملفات (المسار المحمي) | SAFE | Low | لا شيء | — |
| رفع الملفات (13 موقعاً قديماً) | NEEDS IMPROVEMENT | Medium | بلا فحص بصمة · قرص محلي | توحيدها على `IProtectedFileService` |
| تنزيل الملفات | SAFE | Low | لا شيء | — |
| **عزل الشركات — الرواتب** | **MULTI-TENANT BLOCKER** | **Critical** | **تسريب رواتب وحسابات بنكية عبر الشركات** | **نطاق شركة إلزاميّ + فحص ملكية** |
| **عزل الشركات — بقية المودلات** | **MULTI-TENANT BLOCKER** | **Critical** | العزل غير مُثبَت لـ166 صفحة | حاجز نطاق مركزيّ |
| **مفاتيح Data Protection** | **SCALE-OUT BLOCKER** | **High** | طرد جلسات · كسر روابط التنزيل | تخزين مشترك + `SetApplicationName` |
| **هجرات الإقلاع** | **SCALE-OUT BLOCKER** | **High** | سباق DDL بين النسخ | قفل تطبيقي موزَّع أو نقلها للـpipeline |
| الملفات على القرص المحلي | SCALE-OUT BLOCKER | High | 404 عشوائي بعد التوسّع | Blob Storage |
| الخدمات الخلفية | SCALE-OUT BLOCKER | Medium | إشعارات مكرّرة | قفل موزَّع أو نسخة واحدة |
| مرونة SQL | NEEDS IMPROVEMENT | High | فشل عابر يظهر للمستخدم | `EnableRetryOnFailure` |
| فحوص الصحّة | NEEDS IMPROVEMENT | Medium | توجيه حركة لنسخة غير جاهزة | `/health/live` + `/health/ready` |
| الأداء (`/api/me/attendance`) | NEEDS IMPROVEMENT | High | انهيار تحت الحمل | ترشيح بالـSQL |
| المراقبة | NEEDS IMPROVEMENT | Medium | لا رؤية بالإنتاج | مزوّد تتبّع |
| CI/CD | ACCEPTABLE | Low | لا نشر آلي (مقصود) | — |
| الأسرار | SAFE | Low | لا شيء | — |

---

## 6) التصنيف النهائي

### ❌ NOT READY FOR MULTI-TENANT PRODUCTION
### ⚠️ READY FOR SINGLE-TENANT PRODUCTION AFTER MINOR HARDENING

**لماذا:**

النظام **آمن بشكل ملحوظ** على مستوى المصادقة والجلسات والملفات والترويسات — أقوى
مما تتوقعه من نظام بهذا الحجم، وكثير من الضوابط (إعادة فحص التخويل بعد فكّ الرمز
الموقّع، ختم الأمان على التوكن، الحارس مغلق الفشل) مطبَّق بمستوى مهنيّ.

لكنّ **العزل متعدد الشركات غير موجود عملياً خارج مودل الأشخاص**. وأخطر من غيابه
أنه **يبدو موجوداً**: هناك `Employee.CompanyId` بمفتاح أجنبي، ومحرك نطاق مُختبَر،
واختبارات عزل خضراء — وكلها تخصّ مودلاً واحداً. الرواتب، وهي أحسّ ما بالنظام،
تشمل كل الشركات بالتصميم لا بالخطأ.

⟹ **لا يجوز تشغيله بشركتين مختلفتَي المالك على نفس القاعدة.**

بشركة واحدة (أو عدة شركات لنفس المالك حيث لا يضرّ اطّلاع الإدارة على الكل)، النظام
قريب جداً من الجاهزية: العوائق المتبقية **تشغيلية لا أمنية**، وكلها قابلة للحلّ
بتعديلات محدودة لا بإعادة بناء.

---

## 7) الأولويات

### P0 — قبل الإنتاج
1. **نطاق شركة إلزاميّ على الرواتب** + فحص ملكية على `RunDetail`/`BankFile`.
   *(أو — إن كان الإطلاق أحادي الشركة — حاجزٌ صريح يمنع إنشاء شركة ثانية، مع توثيق
   القيد. هذا قرارك.)*
2. **مفاتيح Data Protection على تخزين مشترك** + `SetApplicationName` ثابت.
3. **قفل هجرات موزَّع** (`sp_getapplock`) أو نقلها لخطوة نشر مستقلة.
4. **`ForceHttps=true`** سحابياً + حارس إقلاع يرفض التشغيل بكوكي غير آمن.

### P1 — موصى به بشدّة قبل الإطلاق
5. `EnableRetryOnFailure` على `UseSqlServer`.
6. `/health/live` و`/health/ready`.
7. تحديد معدّل على مسارَي الدخول.
8. إصلاح `/api/me/attendance` (ترشيح بالـSQL).
9. الملفات إلى Blob Storage + توحيد الرفع على `IProtectedFileService`.
10. قفل موزَّع للخدمتين الخلفيتين.

### P2 — بعد الإطلاق
11. حاجز نطاق شركة مركزيّ يعمّ الـ155 ملف SQL خام.
12. مراقبة/تتبّع.
13. `[ValidateAntiForgeryToken]` صريح على كنترولرات الكوكي.
14. سياسة تفويض احتياطية عامة (`FallbackPolicy`) — موجودة بفرع `feature/attendance-wave1`
    غير المدموج (`1f79ab0`)، فيكفي دمجه.

### P3 — تحسين
15. إزالة `'unsafe-inline'` بترحيل nonce.
16. إحياء هجرات EF أو حسم الاعتماد على `SqlSchemaMigrator` وحده.

---

## 8) البنية التحتية الموصى بها (بلا إفراط)

**كافٍ:** App Service (Linux) + Azure SQL + **Blob Storage** (الملفات + حلقة مفاتيح
Data Protection) + Key Vault للأسرار + Application Insights.

**غير مطلوب ولا أوصي به الآن:** Redis · Kubernetes · Service Bus · Microservices.
لا يوجد بالكود ما يبرّرها: لا كاش موزَّع مطلوب (الكاش الحالي 60 ثانية لبيانات
صغيرة)، ولا طوابير، ولا حدود سياقية تستدعي التفكيك.

---

## 9) الاختبارات المطلوبة قبل الإطلاق

1. **عزل الشركات** — اختبار تكامل: مستخدم شركة A ⟵ `RunDetail?Id=<دفعة B>` ⟹ رفض.
2. **شمول مسير الرواتب** — دفعة شركة A لا تحوي موظف شركة B.
3. **نسختان معاً** — كوكي من A مقبول على B؛ ورمز تنزيل من A يعمل على B.
4. **سباق الهجرات** — إقلاع نسختين متزامن على قاعدة بكر بلا خطأ DDL.
5. **حدّ المعدّل** — 100 محاولة دخول من IP واحد تُخنق.
6. **الحمل** — `/api/me/attendance` بـ10,000 موظف تحت زمن مقبول.

---

## 10) التوصية النهائية

**GO** لإطلاق **أحادي الشركة** بعد P0 (بنودها الأربعة محدودة ومعزولة).

**NO-GO** لإطلاق **متعدد الشركات** حتى يُغلق عزل الرواتب ويُبنى الحاجز المركزيّ
ويُثبَت بالاختبارات المذكورة.

**لا أوصي بأي إعادة بناء معماريّ.** المشكلة ليست بالمعمارية — محرك النطاق موجود
وصحيح — بل **بتغطيته**. والحلّ توسيع تبنّيه، لا استبداله.
