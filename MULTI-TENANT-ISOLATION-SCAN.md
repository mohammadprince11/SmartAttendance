# مسح عزل الشركات — جرد مُثبَت لا قائمة اشتباه

**التاريخ:** 2026-08-07 · **المرجع:** فرع العمل بعد `ab17ac8`
**الغرض:** تحويل «١٣٤ ملف SQL خام بلا `CompanyId`» — وهو رقمُ اشتباهٍ لا عمل — إلى
قائمةٍ مُثبَتة لكل بند: **أين المسار، وما الدليل، وهل هو مخترَق فعلاً**.

**القاعدة المتبعة:** لا يُصنَّف بندٌ ثغرةً إلا بتتبّع المسار كاملاً من الصفحة إلى
الاستعلام. وغيابُ `CompanyId` من ملفٍ **ليس دليلاً**: قد يكون الوصول محكوماً
بمستوى أعلى، أو الجدول غير موظفيّ أصلاً.

---

## 1) الاكتشاف المعماريّ الحاكم

الحارس المركزيّ (`RoleSecurityMiddleware`) يملك مسارين للتخويل:

| المسار | ما يفحصه | تغطيته |
|---|---|---|
| **الديناميكيّ** (`PeopleRoutePermissionResolver` + `PeopleTargetEmployeeResolver`) | **ملكية الكيان**: يستخرج الموظف المستهدَف من الطلب ويسأل `CanAccessEmployeeAsync` | `/employees/*` و`/employeepermissions` **فقط** |
| **التوافقيّ** (`RoleRouteCatalog`) | **المسار بالدور**: «هل يفتح هذا الدور /Documents؟» | كل ما عداه |

⟹ **هذه هي جذر المشكلة كلها.** خارج `/employees/*` لا يوجد فحص ملكية إطلاقاً؛
السؤال المطروح هو «هل تفتح هذه الشاشة؟» لا «هل هذا الصفّ لك؟». فأي صفحة تأخذ
معرّفاً من الطلب خارج ذينك المسارين مكشوفة بنيوياً.

الدليل: `grep -oE '"/[a-z/]+"' PeopleRoutePermissionResolver.cs` يعطي اثني عشر
مساراً كلها تحت `/employees` عدا `/employeepermissions`.

---

## 2) الجرد المُثبَت — صفحات تقبل معرّفاً من الطلب

المسح: كل نموذج صفحة يربط معرّف كيان بـ`[BindProperty(SupportsGet = true)]`، ثم
تتبّع كل واحدة حتى الاستعلام.

| # | الصفحة | المعرّف | محميّ؟ | الدليل |
|---|---|---|---|---|
| 1 | `Employees/Profile` | `Id` | ✅ | الحارس الديناميكيّ — `Employee(ViewProfile)` |
| 2 | `Employees/EndService` | `Id` | ✅ | `Employee(EndService)` |
| 3 | `Employees/Rehire` | `Id` | ✅ | `Employee(Rehire)` |
| 4 | `EmployeePermissions/Index` | `EmployeeId` | ✅ | مسار مسجَّل بالمحلِّل |
| 5 | `Payroll/RunDetail` | `Id` | ✅ | **أُصلح** — `627fe8f` |
| 6 | **`Documents/View`** | `Id` | ❌ | **ثغرة مُثبَتة** — أدناه |
| 7 | **`EmployeeDocuments/Index`** | `EmployeeId` | ❌ | **ثغرة مُثبَتة** — أدناه |
| 8 | **`LeaveBalances/Adjust`** | `EmployeeId` | ❌ | **ثغرة مُثبَتة** — أدناه |
| 9 | **`Payroll/TerminationSettlement`** | `EmployeeId` | ❌ | **ثغرة مُثبَتة** — أدناه |
| 10 | `Documents/Generate` | `TemplateId` | 🟡 | قالب لا بيانات موظف — أدناه |
| 11 | `BadgeCenter/Index` | `TemplateId` | 🟡 | قالب بطاقة — أدناه |

`grep -cE "CanAccessEmployeeAsync|AllowsEmployee|IEffectiveScopeService|CompanyScope"`
على الستّ (6-11) = **صفر** لكلٍّ منها.

---

## 3) الثغرات المُثبَتة — بالتفصيل

### 🔴 ث-1 · `Documents/View?Id=N` — قراءة أي وثيقة مولَّدة بأي شركة

**Evidence** (`Pages/Documents/View.cshtml.cs`, `OnGetAsync`):
```csharp
Document = await DocumentTemplateStore.FindGeneratedAsync(_db, Id);
if (Document is null) return NotFound();
CompanyName = await HrmsDatabase.ScalarAsync<string>(_db, """
    SELECT TOP 1 c.Name FROM Employees e
    INNER JOIN Companies c ON c.Id = e.CompanyId
    WHERE e.Id = @EmployeeId;""", … Document.EmployeeId);
```

**Risk: Critical.** الوثائق المولَّدة شهاداتُ راتب وكتبُ تعريف ورسائلُ تأديب —
أي أنّ الصفحة تعرض **الراتب والحالة التأديبية** نصّاً. وتعدادُ `?Id=` مباشر.

**المفارقة الكاشفة:** الصفحة تستعلم عن **شركة** صاحب الوثيقة لتعرض اسمها — فهي
تملك المعلومة اللازمة للفحص ولا تفحص.

**Status: MULTI-TENANT BLOCKER.**

### 🔴 ث-2 · `EmployeeDocuments/Index` — سرد وثائق كل الموظفين بكل الشركات

**Evidence** (`Pages/EmployeeDocuments/Index.cshtml.cs`, `LoadAsync`):
```sql
SELECT TOP 300 d.Id, d.EmployeeId, e.EmployeeNo, e.FullName, d.DocumentType,
       d.FileName, d.StoredPath, d.ExpiryDate, ISNULL(d.Notes,'') AS Notes, d.UploadedAt
FROM EmployeeDocuments d
INNER JOIN Employees e ON d.EmployeeId = e.Id
WHERE (@EmployeeId <= 0 OR d.EmployeeId = @EmployeeId)
```

`@EmployeeId <= 0` — أي **فتح الصفحة بلا معامل** — يسرد ٣٠٠ وثيقة من **كل**
الموظفين بكل الشركات: النوع والاسم وتاريخ الانتهاء والملاحظات ورقم الموظف واسمه.

**Risk: High.** الملفّ نفسه محميّ (`/files/download` يعيد فحص الصلاحية)، لكن
**الوصف الوصفيّ يتسرّب** — وهو كافٍ لبناء صورة عن موظفي المنافس.

**Status: MULTI-TENANT BLOCKER.**

### 🔴 ث-3 · `LeaveBalances/Adjust?EmployeeId=N` — قراءة **وتعديل** رصيد أي موظف

**Evidence** (`Pages/LeaveBalances/Adjust.cshtml.cs`):
```csharp
// OnGetAsync
var employee = await _dbContext.Employees.AsNoTracking()
    .Where(e => e.Id == EmployeeId && !e.IsDeleted) …
// OnPostAsync
var employeeExists = await _dbContext.Employees.AnyAsync(e => e.Id == EmployeeId && !e.IsDeleted);
if (!employeeExists) return NotFound();
```

الفحص الوحيد **«هل الموظف موجود»** لا «هل هو لك». و`OnPostAsync` **يكتب**.

**Risk: Critical.** كتابةٌ عابرة للشركات على رصيد الإجازات — وهو مُدخَل ماليّ
(بدل الإجازة · نهاية الخدمة).

**Status: MULTI-TENANT BLOCKER.**

### 🔴 ث-4 · `Payroll/TerminationSettlement?EmployeeId=N` — تسوية نهاية خدمة أي موظف

**Evidence** (`Pages/Payroll/TerminationSettlement.cshtml.cs`):
```csharp
[BindProperty(SupportsGet = true)] public int? EmployeeId { get; set; }
public async Task OnGetAsync() { if (EmployeeId is > 0) await LoadEmployeeAsync(EmployeeId.Value); }
public async Task<IActionResult> OnPostAsync() { if (EmployeeId is > 0) { await LoadEmployeeAsync(…); BuildDifferences(); } }
```

بلا أي فحص. `YearWithholding` يكشف **الضريبة والضمان المحتجزين سنوياً**.

**Risk: Critical.** **Status: MULTI-TENANT BLOCKER.**

### 🟡 ث-5 · `Documents/Generate?TemplateId=N` و`BadgeCenter/Index?TemplateId=N`

المعرّف **قالب** لا صفّ موظف. القوالب إعدادٌ إداريّ لا بيانات شخصية، وتسرّبها
يكشف تصميم مستندات الشركة لا رواتب أحد.

**Risk: Low.** **Status: NEEDS IMPROVEMENT** (تُعالَج مع الحاجز العام لا قبله).

---

## 4) المتاجر — القياس لا الانطباع

| المتجر | ضمّ لجدول الموظفين | ذكر `CompanyId` | الحكم |
|---|---|---|---|
| `PayrollRunStore` | نعم | ✅ (بعد `627fe8f`) | مُعالَج |
| `DocumentTemplateStore` | 3 | 1 | ناقص — الذكر الوحيد بمسار غير الاستعلام |
| `LoanStore` | 2 | **0** | غير مُعزَل |
| `FinancialRequestStore` | 2 | **0** | غير مُعزَل |
| `AcknowledgmentStore` | 1 | **0** | غير مُعزَل |
| `ContractRegisterStore` | 2 | **0** | غير مُعزَل |
| `DisciplinarySchema` | 0 | 0 | لا ينطبق (مخطط لا استعلام) |

⚠️ **هذه المتاجر غير مُثبَتة الاختراق بعد** — لم أتتبّع مسار وصولٍ يقبل معرّفاً من
الطلب لكلٍّ منها. تُصنَّف «غير مُعزَلة» لا «مخترقة»، ومحلّها الحاجز العام لا إصلاحٌ
مفرد. إدراجها هنا للجرد لا للعمل الفوريّ.

---

## 5) لماذا لا أُصلح الـ١٣٤ ملفاً

1. **صفر دليل لكلٍّ.** ثبتت أربع ثغرات بتتبّع المسار. البقية «غير مُثبَتة العزل»
   وليست مرادفاً لـ«مخترَقة».
2. **إضافة `WHERE CompanyId = @x` لاستعلامٍ لا يحتاجه تُخفي بياناتٍ مشروعة** —
   عطلٌ صامت بمودل حسّاس، وهو أسوأ من الثغرة لأنه لا يُكتشف.
3. **لا قاعدة بيانات بهذه البيئة** — أحد عشر اختبار تكامل متخطًّى، فالتحقّق من
   تعديلٍ بهذا الحجم يقتصر على التصريف.

---

## 6) العلاج الموصى به — بترتيب التنفيذ

### الآن (ثغرات مُثبَتة، أربع صفحات)
حارس ملكية على الأربع بنفس نمط `PayrollRunStore.CanAccessRunAsync`:
اقرأ شركة الموظف المستهدَف ← قارنها بـ`CompanyScope` ← ارفض مغلق الفشل.
**النطاق: أربع صفحات، لا ١٣٤ ملفاً.**

### يليه (الحلّ البنيويّ — يزيل الفئة كلها)
**توسيع الحارس المركزيّ** ليغطي كل مسار يستهدف موظفاً، لا `/employees/*` وحدها.
`PeopleTargetEmployeeResolver` يستخرج المعرّف من المسار والاستعلام والنموذج
أصلاً — الناقص **تسجيل المسارات** بـ`PeopleRoutePermissionResolver`.

هذا هو الفرق بين سدّ أربع ثغرات وإغلاق **الفئة**: بعده تصير الصفحة الجديدة محميّةً
بحكم موقعها لا بذاكرة كاتبها.

### أخيراً (الاتّساع)
حاجز `CompanyScope` على استعلامات السرد بالمتاجر الستّة بعد إثبات مسار وصولٍ لكلٍّ.

---

## 7) أثر المسح على تصنيف الجاهزية

التصنيف **لم يتغيّر**: ❌ NOT READY FOR MULTI-TENANT.

لكنّ صورته تغيّرت جذرياً: لم تعد «١٣٤ ملفاً مشبوهاً» بل **أربع ثغرات مُثبَتة
وسببٌ معماريّ واحد يفسّرها جميعاً**. والعلاج صار محدوداً وقابلاً للمراجعة بدل أن
يكون مشروع إعادة بناء.
