# فحص عميق: ربط الإجازات بالمسير + محرك الصيغ — 2026-07-27

**الخلاصة: كلا «الفجوتين» مبنيّتان ومدموجتان بالكامل. لا عمل بناء مطلوب.** (تأكيد
لدرس «افحص الكود قبل البناء» — [[loans-and-stale-gaps]].)

---

## 1) محرك الصيغ — ✅ مبنيّ بالكامل

- **المحرّك:** `SmartAttendance.Web/Infrastructure/Hrms/SalaryFormulaEvaluator.cs`
  (`TryEvaluate(formula, vars, out value, out error)`).
- **الدمج بالمسير:** `PayrollRunStore.cs:289` يحمّل عناصر الراتب من نوع `ValueKind == "Formula"`،
  و`:419` يبني قاموس المتغيّرات، و`:431` يقيّمها لكل موظف، ويضيفها كمكوّنات (`Kind="Formula"`)
  مع احترام `Taxable` (`:437`) والإضافة/الاقتطاع (`:436/:442`).
- **الواجهة:** عناصر الراتب بـ`/Payroll/SalaryItems` (حقل `Formula`).

**لا فجوة.** أي عمل مستقبلي = توسيع الدوال/المتغيّرات المدعومة فقط (تحسين لا بناء).

---

## 2) ربط الإجازات بالمسير — ✅ مبنيّ ومربوط طرفاً لطرف

السلسلة الكاملة مؤتمتة:

```
DayAttendanceStore.Derive  →  حالة اليوم (Absent | LeaveUnpaid | Leave | Holiday)
      ↓  (MonthAttendanceStore.cs:99/101 — تجميع MERGE)
EmployeeMonthAttendance.AbsentDays / UnpaidLeaveDays
      ↓  (اعتماد الشهر يقفله ثم يقرأه المسير)
PayrollRunStore:
   • :317-320  AbsentDays  → تنسيب الأساسي: factor = (workDays - absentDays)/workDays
   • :487-496  UnpaidLeaveDays → اقتطاع صريح (× dailyRate)، Kind="Leave"
   • :399-409  LeaveEncashment (بدل إجازة) → دخل، Kind="LeaveEncashment"
   • :379-393  SalaryDays (تعديل أيام) → إضافة/اقتطاع
```

**سلوك صحيح مؤكَّد:** الإجازة المدفوعة (`Status='Leave'`) **لا** تُحتسب ضمن `AbsentDays` ولا
`UnpaidLeaveDays` ⟹ لا تُنقص الراتب (= أجر كامل، صحيح). الإجازة بلا راتب (`LeaveUnpaid`) تُقتطع.
العطلة الرسمية (`Holiday`) لا تُخصم. راجع [[attendance-payroll-bridge]] (الجسر مؤتمت أصلاً).

**لا فجوة بنيوية.** نقاط تحسين محتملة (اختيارية، ليست فجوات):
- تحقّق E2E شامل بأرقام حقيقية (مسير كامل بموظف له إجازة بلا راتب) — مؤجَّل لمراجعة محمد.
- سياسات إجازات خاصة (نصف يوم/بالساعة) إن طُلبت مستقبلاً.

---

## التوصية
شطب «ربط الإجازات بالمسير» و«محرك الصيغ» من قائمة الفجوات مقابل كيان
([[gap-analysis-kayan]]) — كلاهما منجز. الأولويات الحقيقية المتبقّية = الحقول الصمّاء
البنيوية (تيار البصمات الخام: TimeLimit/MidShift/StripSemantics/تبويب التعارض) —
ميزة مستقلة كبيرة موصوفة بـ[[shift-type-dead-settings]].
