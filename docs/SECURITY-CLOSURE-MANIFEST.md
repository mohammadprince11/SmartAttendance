# SECURITY CLOSURE — Coverage Manifest (Phase 0)

**التاريخ:** 2026-08-11 · **الفرع:** `security/full-production-closure` (من رأس
`efee99e`، وهو 29 كوميت فوق `origin/main`) · **الطريقة:** فحص الكود مباشرةً — لا
اعتماد أعمى على تقرير سابق. كل بند مُثبَت بملف/سطر أو بمسح مُعاد.

> **قاعدة التصنيف:** غياب فحصٍ *داخل الصفحة* **ليس** دليل ثغرة — الحارس المركزيّ قد
> يحمي بالمسار/الدور أو المحلِّل الديناميكيّ. كل بند «مكشوف» أدناه تُتبِّع مساره
> كاملاً حتى الاستعلام والتُّحقِّق من وصول دورٍ غير أدمن إليه.

---

## 1) نموذج التخويل الفعليّ (كما هو بالكود)

`RoleSecurityMiddleware` هو البوّابة المركزية لكل طلب. له مساران:

| المسار | ما يفرضه | مصدره | تغطيته |
|---|---|---|---|
| **الديناميكيّ** | **ملكية الكيان** (الموظف المستهدَف ← شركته ← تخويل) | `PeopleRoutePermissionResolver` + `PeopleTargetEmployeeResolver` + `CanAccessEmployeeAsync` | مسارات مسجَّلة فقط (أدناه) |
| **التوافقيّ** | **الدور بالمسار** («هل يفتح هذا الدور هذه الشاشة؟») — **لا ملكية** | `RoleRouteCatalog` | كل ما عداه |

**المسارات ذات الحارس الديناميكيّ (ملكية مفروضة مركزياً):** `/leavebalances/adjust` ·
`/payroll/terminationsettlement` · `/employeedocuments` (بـ`EmployeeId`) · كل
`/employees/*` · `/employeepermissions`.

**الأدوار غير الأدمن وخريطة وصولها** (`RoleRouteCatalog`): `HR Manager` (≈45 مساراً) ·
`HR Officer` (≈21) · `Branch Manager` (≈9) · `Finance Viewer` (`/organization` فقط) ·
`Employee` (البوابة + طلباته). الأدمن غير مقيَّد بالتصميم.

⟹ **جذر فئة المخاطر:** أي شاشة يصلها دورٌ غير أدمن، وتقبل معرّف كيان من الطلب، وليست
بالحارس الديناميكيّ، **تعتمد كليّاً على فحصٍ داخل الصفحة**. غيابه = عبور شركات.

**أساس العزل (سليم ومُغلَق الفشل):** `CompanyScope` (`Unrestricted`/`DeniedAll`/
`ForCompanies` + `ToSqlPredicate`) · `CompanyScopeProvider` (من `EffectiveScopeService`) ·
`EmployeeCompanyGuard` (`CanAccessEmployeeAsync` · `CanAccessOwnedRowAsync` · `ListFilter`
· **جديد:** `FilterEmployeesInScopeAsync` · `FilterOwnedRowsInScopeAsync`).

---

## 2) سطح النظام (جرد كميّ)

- **169** نموذج صفحة (`*.cshtml.cs`) · **5** كنترولرات (`AuthController`,
  `MeController`, `WebAuthnController`, `EmployeeFilesController`, `PushController`) ·
  **189** ملف `Infrastructure/` (منها ~105 Store) · **58** ملف JS (خارج المكتبات) ·
  ميدل وير أمنيّ واحد (`RoleSecurityMiddleware`).

**مسح آليّ (`scripts`-less grep):** من 169 صفحة، **33** تربط معرّفاً من الطلب ولها
مسار كتابة وصفر إشارة عزل داخل الصفحة. بعد التصنيف ضد نموذج §1:

| الفئة | العدد | الحكم |
|---|---|---|
| عامّة (Login, Verify) | 2 | آمنة (بلا هدف موظفيّ؛ Verify ← Phase 13) |
| محروسة ديناميكياً (EmployeePermissions, Employees/EndService+Rehire) | 3 | آمنة مركزياً |
| **أدمن فقط** (AccessRoles, Setup, HrSettings×4, Branches/Departments/Positions, CompanyDocuments, DisciplinaryRules, Documents×3, Forms×2, Violations) | ~16 | آمنة بالتصميم (الأدمن عابرٌ عمداً)؛ تُعالَج تهيئتها العالمية بـ§5 |
| لوحة/ذاتيّ (Index, MyProfile) | 2 | Phase 2 / Phase 14 |
| **مكشوفة فعلاً لدور غير أدمن** | **6** | **أُصلحت — §3** |
| بحاجة تتبّع أعمق (Organization, UserAccess) | 2 | §4 |

---

## 3) الثغرات المُثبَتة المكشوفة — وإصلاحها (P0/P1)

كلها يصلها `HR Manager`/`HR Officer`، تقبل معرّفاً من الطلب، وتكتب بلا أي فحص ملكية.
أُصلحت بحرّاس `EmployeeCompanyGuard` + اختبارات انحدار (`TenantIsolationGuardTests`).

| # | الصفحة | الثغرة | الإصلاح |
|---|---|---|---|
| 1 | `BiometricKeys/Index` | Approve/Reject/Revoke بمعرّف مفتاح مباشر ⟹ اعتماد/إلغاء مفاتيح بصمة موظفي شركة أخرى؛ والسرد يكشف أسماء/أرقام كل الشركات | `CanAccessOwnedRowAsync` قبل كل إجراء + حصر السرد بالنطاق (Phase 7) |
| 2 | `ShiftAssignments/Index` | Assign/Unassign لقائمة `SelectedIds` بلا فحص ⟹ تعيين مناوبات لموظفي شركة أخرى | `FilterEmployeesInScopeAsync` (رفض الدفعة عند أي معرّف خارج النطاق) + حصر السرد (Phase 8B) |
| 3 | `EmployeeGeoLocations/Index` | Assign/Unassign جماعيّ بلا فحص | كنظيره أعلاه (Phase 8A) |
| 4 | `ShiftOverrides/Index` | `Scope=All` = كل قاعدة البيانات؛ التحديد والحذف بلا فحص | «الكل» = نطاقي · تحديد محروس · حذف بـ`FilterOwnedRowsInScopeAsync` (Phase 8C) |
| 5 | `AssetsManagement/Index` | MarkReturned بمعرّف سجلّ ⟹ تعديل عهدة موظف شركة أخرى؛ سرد شامل | `CanAccessOwnedRowAsync` + حصر السرد (Phase 9) |
| 6 | `EmployeeTasks/Index` | Launch لمعرّف موظف؛ Complete/Reopen/Delete بمعرّف مهمة؛ سرد وعدّادات شاملة | حارس موظف على Launch + حارس ملكية على المهمة + حصر عبر `Employee.CompanyId` (Phase 9) |

---

## 4) الثغرات المُثبَتة سابقاً — تحقّقنا أنها **مُغلَقة** بالشجرة الحالية

مسحُ `MULTI-TENANT-ISOLATION-SCAN.md` (2026-08-07 @ `main`) أثبت أربع ثغرات + عزل
الرواتب. **جميعها مُصلَحة الآن** (موجات العزل بين `main` والفرع الحالي):

| البند | الحالة الحالية (مُتحقَّقة) |
|---|---|
| `Documents/View` | `CanAccessEmployeeAsync` حاضر ✅ |
| `EmployeeDocuments/Index` | `ListFilter` بالنطاق ✅ + حارس ديناميكيّ |
| `LeaveBalances/Adjust` | `CanAccessEmployeeAsync` بـGet وPost ✅ + حارس ديناميكيّ |
| `Payroll/TerminationSettlement` | `CanAccessEmployeeAsync` ✅ + حارس ديناميكيّ |
| `PayrollRunStore` / `RunDetail` | `CompanyId` (45 موضعاً) + `CanAccessRunAsync` ✅ |

**بحاجة تتبّع أعمق (لم يُبتّ):** `Organization/Index` (يستخدم
`CompanySelectionContext.Resolve` — يلزم إثبات أن المنتقي محصورٌ بالنطاق) ·
`UserAccess/Index` (كتاباته `IsAdministrator()→Forbid`؛ يبقى تحقّق تسريب سرد الـGET).

---

## 5) بنودٌ عالمية بالتصميم — مقبولة أحاديّ المالك، تحتاج قراراً متعدّد المُلّاك

جداول تهيئة عالمية يحرّرها الأدمن فقط (أو HR للقوالب) بلا `CompanyId`:
`HrTaskTemplates` · `ShiftTypes` · قوالب المستندات/البطاقات · قواعد التأديب · النماذج ·
`Holidays` · `GeoLocations` (تعريفات المواقع). لا تسرّب بيانات موظفٍ، لكن في نشرٍ
**متعدّد المُلّاك** تحتاج `CompanyId` وحصراً. **قرار مؤجَّل لمالك المنتج** (Phases
8D/8E/8F/16).

---

## 6) الحالة مقابل مراحل البرومت

**مُنجَز هذه الجلسة:** Phase 0 (هذا الملف) · Phase 1 (primitives مركزية للتحديد
الجماعي) · Phase 7 (BiometricKeys) · Phase 8A/8B/8C (Geo/ShiftAssign/ShiftOverrides) ·
Phase 9 جزئياً (Assets/Tasks) · تحقّق Phases الرواتب/الوثائق/الإجازات (مُغلَقة سابقاً).

**مؤجَّل بوضوح (يتجاوز ليلةً واحدة):** Phase 2 (Dashboard) · 3 (Organization deep) ·
4/5/6 · 8D–8M بقيّتها · 10/11 (XSS/CSP) · 12/13/14/15/16 · 17 (Android) · 18 (runtime
schema) · 19–21 · 22/23 (E2E matrix + CI gate) · 24/25/26. راجع
`ZYNORA-FULL-SECURITY-CLOSURE-REPORT.md`.

**القرار:** ❌ ليست `PRODUCTION SECURITY CANDIDATE` بعد — أُغلقت 6 ثغرات P0/P1
مُثبَتة، والبقية موثّقة ومرتّبة. **بلا دمج/نشر.**
