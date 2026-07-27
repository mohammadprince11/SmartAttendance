# جرد استهلاك إعدادات أنواع المناوبات (ShiftTypes) — 2026-07-26

**السؤال:** تبويبات نموذج «أنواع المناوبات» (`/ShiftTypes`) — قواعد المناوبة، التعارض مع الحركة،
معايير الاستحقاق — بمنو مرتبطة، وهل تؤثّر فعلاً في محرك الحضور أم مخزّنة فقط؟

**المنهج:** تتبّع كل حقل من الواجهة (`ShiftTypes/Index.cshtml.cs`) ← التخزين
(`ShiftTypeStore.cs`: DDL/INSERT/UPDATE/قراءة) ← المستهلك الفعلي (محرك الاشتقاق الرسمي
`DayAttendanceStore` وما حوله). أي حقل يظهر **فقط** في مكانَي الحفظ/الاسترجاع دون مستهلك = صمّاء.

---

## مسار الحساب (أين تؤثّر الإعدادات فعلاً)

`MonthAttendanceStore.BuildMonthAsync` لكل موظف:
1. **تحديد مناوبة الأساس:** إسناد صريح (`ShiftAssignments`) ← وإلا **مطابقة الاستحقاق**
   (`EmployeeMatchesEligibility`) ← وإلا المناوبة الافتراضية.
2. **لكل يوم:** أولوية الإسناد اليومي = تجاوز مؤقت (`ShiftOverrides`) ← روستر (`Roster`) ← الأساس.
3. **الاشتقاق:** `DayAttendanceStore.Derive(shift, day, dayKind, checkIn, checkOut)` يحسب
   التأخير/الخروج المبكر/الساعات/الحالة. **هذا هو المكان الوحيد الذي تؤثّر فيه إعدادات المناوبة على الحضور.**

`Derive` (السطور ~380–429) يقرأ فعلياً فقط:
- `dayKind` + `day.StartTime`/`day.EndTime` (مصفوفة الأسبوع، تبويب 1).
- `shift.IsFlexible` + `shift.FlexDailyHours` (تبويب 1).
- `shift.LatenessGraceMinutes` + `shift.GraceExceededPolicy` (تبويب 2) عبر `ApplyGrace` — سطر 416.
- `shift.EarlyLeaveGraceMinutes` + `shift.GraceExceededPolicy` (تبويب 2) عبر `ApplyGrace` — سطر 424.

لا شيء آخر من تبويبات 2/3/4 يُقرأ داخل `Derive`.

---

## جدول الاستهلاك

| التبويب | الحقل | المستهلك | الحالة |
|---|---|---|---|
| 1) معلومات | مصفوفة الأيام/الأوقات، ثابتة/مرنة، الفترات، اللون | `Derive`، الروستر | ✅ مؤثّر بالكامل |
| 2) قواعد | `LatenessGraceMinutes`, `EarlyLeaveGraceMinutes`, `GraceExceededPolicy` | `Derive`→`ApplyGrace` | ✅ مؤثّر |
| 2) قواعد | `FillMissingCheckIn/Out` | — | ❌ صمّاء |
| 2) قواعد | `StripSemantics` | — | ❌ صمّاء |
| 2) قواعد | `ConsiderPermissionsOutsideShift` | — | ❌ صمّاء |
| 2) قواعد | `ExcludePermsOutsideStartFromLate` | — | ❌ صمّاء |
| 2) قواعد | `TotalDurationMode` | — | ❌ صمّاء |
| 2) قواعد | `TimeLimitFrom/To` (+ مرساة اليوم) | — | ❌ صمّاء |
| 2) قواعد | `MidShiftTime` | — | ❌ صمّاء (رغم أن `Derive` يعالج العبور لمنتصف الليل عبر `end<start` وليس عبر هذا الحقل) |
| 3) التعارض مع الحركة | `ConflictLateReturn{Enabled,Action,Value}` | — | ❌ صمّاء (التبويب كامل) |
| 3) التعارض مع الحركة | `ConflictEarlyLeave{Enabled,Action,Value}` | — | ❌ صمّاء (التبويب كامل) |
| 4) معايير الاستحقاق | مجموعات القواعد (Field/Value، OR/AND) | `EmployeeMatchesEligibility` ← `DayAttendanceStore:243`, `ShiftAssignments:73` | ✅ مؤثّر (اختيار المناوبة، لا حساب الحضور) |
| — (تبويب 2) | `AvailableInRoster` | الروستر (فلترة العرض) | ✅ مؤثّر (خارج الحضور) |
| — (تبويب 2) | `RequestableFromEss` | — | ❌ صمّاء (لم يظهر مستهلك) |

الحقول الصمّاء تظهر حصراً في: `ShiftTypes/Index.cshtml.cs` (قراءة الفورم) و`ShiftTypeStore.cs`
(الأعمدة، INSERT/UPDATE، القراءة للـViewModel). لا مستهلك في أي محرك.

---

## منطق «معايير الاستحقاق» (تبويب 4)

`ShiftTypeStore.EmployeeMatchesEligibility` (سطر 517):
- لا قواعد ⇒ `true` (تنطبق على الكل).
- وإلا: **OR بين المجموعات، AND داخل المجموعة**. المطابقة على السمات:
  `Department, Branch, Position, ContractType, Nationality, MaritalStatus, Employee`.
- تُستعمل **كاحتياطي فقط** حين لا يوجد إسناد صريح في `ShiftAssignments`.

---

## الخلاصة الحرجة (فخّ توقّعات)

- **تبويب «التعارض مع الحركة» (3) بالكامل + معظم تبويب «القواعد» (2) = واجهة صمّاء:** الأدمن
  يضبطها ويحفظها بنجاح، لكن محرك الحضور يتجاهلها تماماً. المستخدم قد يظنّ أنه فعّل قاعدة
  تعارض/اقتطاع وهي بلا مفعول.
- **الفعّال حقاً:** مصفوفة الأيام (تبويب 1) + السماحيات Grace (تبويب 2) + الاستحقاق كمُختار
  مناوبة (تبويب 4).
- **تنبيه تسمية:** صفحة مستقلة **«قواعد المناوبات» `/ShiftRules`** (`ShiftRuleStore`، محرك المخالفات
  AV) غير تبويب «قواعد المناوبة» داخل ShiftTypes — لا خلط بينهما.

## خيارات المعالجة (لم تُنفَّذ)
1. توصيل تبويب «التعارض» بالمحرك (اقتطاع/إذن عند تعارض المغادرة مع الحضور الفعلي).
2. توصيل `StripSemantics`/`ConsiderPermissionsOutsideShift`/`TotalDurationMode` بحساب الساعات.
3. توصيل `TimeLimitFrom/To`/`MidShiftTime` بتصفية/تقسيم البصمات الصالحة.
4. بديل محافظ: إخفاء الحقول الصمّاء من الواجهة حتى تُنفَّذ، لمنع فخّ التوقّعات.

---

## تحديث 2026-07-27 — المعالجة (المجموعة أ + الخطوة صفر) ✅

- **`FillMissingCheckIn/Out`**: فُعِّلا بجلسة سابقة داخل `Derive` (محروسان، الافتراضي false).
- **`TimeLimitFrom/To` + المرساتان + `MidShiftTime`**: فُعِّلت — نافذة التقاط زمنية بمحرك
  `AnalyzeMonthAsync` (`HasCaptureWindow`/`TrySelectWindowPunches` نقيّتان): البصمات تُلتقط
  من النافذة بدل تاريخ الجهاز (فيلتقط خروجُ ما-بعد-منتصف-الليل ليومه الصحيح)، وMidShift
  يقسم الدخول/الخروج. استعلام البصمات وُسِّع ±1 يوم. مناوبة بلا حدود = السلوك القائم حرفياً.
  15 اختباراً جديداً (`PunchCaptureWindowTests`)، 294 أخضر.
- **`TotalDurationMode`**: فُعِّل بتجميع `MonthAttendanceStore.BuildMonthAsync` (LEFT JOIN
  ShiftTypes): `WorkOnly` = ساعات أيام العمل فقط تدخل إجمالي الشهر؛ `IncludeOff`/`Both` تضمّ
  ساعات العطل/الراحة. صف اليوم يحتفظ بساعاته الفعلية دوماً (مادة الأوفرتايم).
  ⚠️ **تغيير سلوك مقصود**: الافتراضي `WorkOnly` صار يستثني ساعات أيام العطل من إجمالي الشهر
  (سابقاً كانت تُجمَع دائماً رغم أن الواجهة تعرض «أيام العمل فقط») — الواجهة صارت صادقة.
- **الخطوة صفر (الواجهة)**: شارة «غير مفعّل بعد» على `ConsiderPermissionsOutsideShift`
  و`ExcludePermsOutsideStartFromLate` وتبويب «التعارض» كاملاً؛ و`StripSemantics`/`RequestableFromEss`
  أُخفيا (يبقيان بالنموذج حفاظاً على القيم المخزّنة).
- **المجموعة ب ✅ (2026-07-27 لاحقاً بنفس الجلسة، 305 اختبارات خضراء):**
  - `ExcludePermsOutsideStartFromLate`: المغادرات المعتمدة (`SelfServiceRequests` نوع
    `ExitPermission` حالة Approved) تُحمَّل بـ`AnalyzeMonthAsync` وتولّد قسيمة تُطرح من مدة
    التأخير قبل السماحية (`PermissionLatenessCredit` نقية + معامل `lateCredit` بـ`Derive`).
  - `ConsiderPermissionsOutsideShift`: يحكم قصّ نافذة المغادرة على ساعات الدوام قبل
    احتساب القسيمة (false = تُقص، true = النافذة كاملة).
  - **تبويب «التعارض» كاملاً**: ممر رصد بـ`RecommendationStore.AnalyzeMonthAsync` يقارن
    الحركة الفعلية (بصمات اليوم المصنّفة تناوباً؛ `SelectMovementAround` نقية: خروجٌ أقرب
    لبداية النافذة + أول دخول بعده) بنافذة المغادرة عبر `MovementConflictPolicy`، ويولّد
    **اقتراحاً Pending دائماً** (لا تنفيذ تلقائي) بمعرّفَي قاعدة حارسَين سالبَين (-1 خروج
    مبكّر، -2 عودة متأخّرة) يعبران مفتاح منع التكرار، والاعتماد اليدوي ينفّذ الأثر المالي
    (مغادرة=ساعات/اقتطاع=مبلغ) عبر مسار `AttendanceTransactionStore` القائم.
  - لم يبقَ من الجرد صامّاً إلا المخفيّان (`StripSemantics`، `RequestableFromEss`) لحين بناء ميزتيهما.
