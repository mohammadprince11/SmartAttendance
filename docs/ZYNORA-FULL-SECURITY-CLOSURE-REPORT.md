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
| 10 | إصلاح DOM/Stored XSS بمنتقيي الموظفين + معاينة الاستطلاع + `DomXssGuardTests` (3) | `d648a89` |
| 17 | إزالة تجاوز TLS بأندرويد (MITM) + توقيع إصدار من سرٍّ خارجيّ | `eb9b8da` |

**البناء:** أخضر (0 أخطاء). **الاختبارات:** **1564/1564** (1554 + 10 جديدة، صفر انحدار).

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
| ~~10~~ · 11 | ✅ XSS أُغلق (منتقيان + استطلاع) · **باقٍ:** CSP `unsafe-inline` + مسح `Html.Raw` بالخادم | Gate 4 (XSS) مُغلق؛ CSP لم يُبدأ |
| 12·13·14·15·16 | وثائق/Verify PIN/MyProfile/Polls/Notifications | — |
| ~~17~~ | ✅ TLS bypass أُزيل + توقيع خارجيّ · **باقٍ (مالك):** إعادة بناء APK بالرابط العامّ + release keystore، وSTART_URL لا يزال IP محلّيّ | كودياً مُغلق؛ إعادة البناء/الرابط قرار المالك |
| 18 | Runtime schema (`EnsureCreatedAsync`/`SqlSchemaMigrator` DDL بالطلب) | قرار معماريّ — نقل للـpipeline |
| 19·20·21 | Transactions · EmployeeNo/tenant resolution · مسح كل write handler | — |
| 22·23 | E2E tenant matrix + جعلها إلزامية بالـCI | يحتاج بيئة تشغيل |
| 24·25·26 | تدقيق الحزم · إعدادات الإنتاج · الأداء بعد الأمان | — |

---

## 5) قرار الجاهزية

### ❌ NOT READY (بعد)

**السبب القابل للإثبات:** أُغلقت طبقةٌ حقيقية من P0 (عبور شركات بالباك-أوفيس — Gate
13) + **Gate 4** (XSS) + **Gate 6/7** كودياً (TLS bypass أُزيل، توقيع خارجيّ). لكن
**Gates 8/14/15** (E2E إلزاميّ · مسح كل write handler · مسح شامل نهائيّ) و**Phases
2–6, 8D–8M, 11–16, 18–26** **لم تُنفَّذ**. القرار النهائي `READY` مشروطٌ بإغلاقها،
وبقرارات المالك (تهيئة عالمية متعدّدة المُلّاك · إعادة بناء APK · نموذج اللوحة).

**التالي الموصى به (مرتّب):** Phase 3/6 (تتبّع Organization/UserAccess — محدود) ←
Phase 18 (نقل DDL وقت الطلب للـpipeline) ← Phase 21 (مسح كل write handler بالمنهج
نفسه) ← Phase 22/23 (E2E matrix + بوابة CI عند توفّر بيئة تشغيل وقاعدة اختبار).
