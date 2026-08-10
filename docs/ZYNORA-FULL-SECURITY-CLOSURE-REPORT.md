# ZYNORA — FULL SECURITY CLOSURE REPORT

**الفرع:** `security/full-production-closure` (من `efee99e`) · **بدأ:** 2026-08-11 ·
**الحالة:** جارٍ — الجلسة الأولى · **بلا دمج/نشر.**

> **قيود الجلسة (بموافقة المالك مسبقاً):** فرع من رأس العمل الحالي · قاعدة اختبار
> منفصلة للاختبارات الحيّة (لم تُنشأ بعد — لم تلزم بعد) · **صفر لمس للإنتاج/النشر/DDL** ·
> أولوية P0 · تقرير صادق. هذا البرومت أكبر من أن يكتمل في جلسة؛ ما دون موثّق كمؤجَّل لا مُخفى.

---

## 1) الملخّص التنفيذيّ

النظام يملك **أساس عزلٍ سليماً ومُغلَق الفشل** (`CompanyScope` · `EffectiveScopeService`
· `EmployeeCompanyGuard`) لكنّ **تطبيقه غير متّسق**: الحارس المركزيّ يفرض ملكية الكيان
على ~12 مساراً فقط؛ الباقي يُفحص بالدور/المسار لا بالملكية. فحصُ 169 صفحة حصر
المخاطر الفعلية في **٦ شاشات مكشوفة** لدورٍ غير أدمن — **أُغلقت كلها هذه الجلسة** بحرّاس
واختبارات انحدار. الثغرات الأربع المُثبَتة سابقاً + عزل الرواتب: **تحقّقنا أنها مُغلَقة**
بالفعل على هذا الفرع.

**القرار الحاليّ:** ❌ **NOT YET `PRODUCTION SECURITY CANDIDATE`** — أُغلقت طبقة P0
من عبور الشركات بالباك-أوفيس، لكن مراحل واسعة (XSS/CSP · Android · Runtime schema ·
E2E matrix · مراجعة كل write handler · لوحة/إعدادات/إجازات) **لم تُدقَّق بعد**. لا يجوز
إعلان الجاهزية قبل إغلاقها أو قبول مخاطرها صراحةً.

---

## 2) ما أُنجز هذه الجلسة (مُثبَت + مُختبَر + مكوميت)

| Phase | العمل | الكوميت |
|---|---|---|
| 0 | Coverage Manifest (`SECURITY-CLOSURE-MANIFEST.md`) | `da85292` |
| 1 | primitives مركزية: `FilterEmployeesInScopeAsync` · `FilterOwnedRowsInScopeAsync` | `f2ff30c` |
| 7 · 8A · 8B · 8C · 9 | إغلاق ٦ ثغرات عبور شركات + `TenantIsolationGuardTests` (7 اختبارات) | `369781a` |

**البناء:** أخضر (0 أخطاء). **الاختبارات:** **1561/1561** (1554 + 7 جديدة، صفر انحدار).

### الثغرات المُغلَقة (كلها: دورٌ غير أدمن يقرأ/يكتب مورد شركةٍ أخرى بمعرّفٍ مباشر)
1. **`BiometricKeys`** — اعتماد/رفض/إلغاء مفاتيح بصمة أي شركة (P0). حُرِس بالملكية + حصر السرد.
2. **`ShiftAssignments`** — تعيين/إلغاء مناوبات جماعيّ لموظفي شركة أخرى. حُرِس + حصر.
3. **`EmployeeGeoLocations`** — تعيين/إلغاء مواقع جماعيّ. حُرِس + حصر.
4. **`ShiftOverrides`** — `Scope=All` = كل القاعدة؛ حذف بلا فحص. «الكل»=نطاقي + حارس حذف.
5. **`AssetsManagement`** — تعليم عهدة أي موظف مُرجَعة. حُرِس + حصر.
6. **`EmployeeTasks`** — إطلاق/إنجاز/حذف مهامّ أي موظف. حُرِس (موظف + صفّ) + حصر.

### تحقّقٌ أن ثغرات سابقة **مُغلَقة** بالفعل (Regression verified)
`Documents/View` · `EmployeeDocuments/Index` · `LeaveBalances/Adjust` ·
`Payroll/TerminationSettlement` · `PayrollRunStore`/`RunDetail` — كلها تحمل حرّاسها الآن.

---

## 3) الملفّات المتغيّرة

- `Infrastructure/Security/EmployeeCompanyGuard.cs` (+primitives، +4 ثوابت جداول)
- `Pages/{BiometricKeys,ShiftAssignments,EmployeeGeoLocations,ShiftOverrides,AssetsManagement,EmployeeTasks}/Index.cshtml.cs`
- `SmartAttendance.Tests/TenantIsolationGuardTests.cs` (جديد)
- `docs/SECURITY-CLOSURE-MANIFEST.md` · هذا الملف

**تغييرات قاعدة بيانات:** لا شيء (لا DDL، لا هجرات — بقصد؛ صفر لمس للإنتاج).

---

## 4) المخاطر المتبقّية والمؤجَّلات (لم تُدقَّق بعد — ليست «آمنة»)

| Phase | البند | لماذا مؤجَّل |
|---|---|---|
| 2 | Dashboard `/` — CompanyOptions/Widgets/POST handlers | يحتاج قرار نموذج اللوحة (per-user/company/global) |
| 3 | Organization/Setup — تتبّع `CompanySelectionContext` | يستخدم منتقياً؛ يلزم إثبات حصره بالنطاق |
| 4·5·6 | Employee create/edit/reassign · Leave/self-service · Permissions/Identity | مراجعة عميقة لكل مسار |
| 8D–8M | ShiftTypes/AttendanceSettings/Rules/Recommendations/MissingPunch/Roster/Devices/Holidays | كثيرٌ منها تهيئة عالمية (§5 بالمانيفست) — قرار متعدّد المُلّاك |
| 10·11 | Stored/DOM XSS (`emp-picker.js`/`mass-scope.js` + مسح شامل) · CSP `unsafe-inline` | Gate 4 — لم يُفحص بعد هذه الجلسة |
| 12·13·14·15·16 | وثائق/Verify PIN/MyProfile/Polls/Notifications | — |
| 17 | Android: `handler.proceed()` TLS · `MIXED_CONTENT` · debug signing | تطبيق `SmartAttendance.MobileApp/` بالمستودع؛ لم يُفحص |
| 18 | Runtime schema (`EnsureCreatedAsync`/`SqlSchemaMigrator` DDL بالطلب) | قرار معماريّ — نقل للـpipeline |
| 19·20·21 | Transactions · EmployeeNo/tenant resolution · مسح كل write handler | — |
| 22·23 | E2E tenant matrix + جعلها إلزامية بالـCI | يحتاج بيئة تشغيل |
| 24·25·26 | تدقيق الحزم · إعدادات الإنتاج · الأداء بعد الأمان | — |

---

## 5) قرار الجاهزية

### ❌ NOT READY (بعد)

**السبب القابل للإثبات:** أُغلقت طبقةٌ حقيقية من P0 (عبور شركات بالباك-أوفيس) بحرّاس
واختبارات، لكن **Gates 4/6/7/8/14/15** (XSS · Android TLS · debug signing · E2E
إلزاميّ · مسح كل write handler · مسح شامل نهائيّ) **لم تُنفَّذ**. القرار النهائي `READY`
مشروطٌ بإغلاقها.

**التالي الموصى به (مرتّب):** Phase 10 (XSS — Gate 4، محدود وعالي القيمة) ← Phase 3/6
(تتبّع Organization/UserAccess) ← Phase 17 (Android) ← Phase 22 (E2E matrix عند توفّر بيئة).
