# تقرير التدقيق الشامل لواجهات ZYNORA HR

**التاريخ:** 30 آب 2026

**البيئة:** نسخة الاختبار المحلية على `http://127.0.0.1:5086` وقاعدة بيانات تجريبية مع حساب مدير
**نوع العمل:** مراجعة فقط؛ لم تُعدَّل وظائف النظام أو بياناته ضمن هذا التدقيق.

## الخلاصة التنفيذية

النسخة الحالية **ليست جاهزة للإطلاق**. البناء البرمجي ينجح، لكن التنقل الفعلي كشف أعطال قاعدة بيانات توقف 19 مساراً، ومساراً ظاهراً في القائمة يرفض المدير، ومشاكل استجابة على الجوال، وتغطية ترجمة غير مكتملة في الإنكليزية والكردية.

| المؤشر | النتيجة |
|---|---:|
| مسارات القائمة الإدارية المفحوصة فعلياً | 103 |
| صفحات Razor المشمولة بالبناء والفحص الساكن | 210 |
| مسارات تعمل دون صفحة استثناء | 83 |
| مسارات تعرض Developer Exception | 19 |
| مسارات ظاهرة في القائمة لكنها تنتهي بـ AccessDenied | 1 |
| صفحات سليمة فُحصت على الجوال 390×844 | 83 |
| مسارات ذات تمدد أفقي مكتبي | 4 |
| مسارات ذات تمدد أفقي على الجوال | 13 إدخالاً / 12 صفحة فريدة |
| صفحات سليمة بقي فيها نص عربي عند اختيار English | 83 من 83 |
| صفحات ثبت بقاء نص عربي مطابق فيها عند اختيار کوردی | 80 من 83 |
| نتيجة البناء Release | ناجح، 0 أخطاء و0 تحذيرات |
| الاختبارات غير المعتمدة على ProductionClosureSqlTests | 1961 ناجح، 2 فاشل، 28 متجاوز |

> ملاحظة: فحص اللغة الكردية اعتمد المطابقة الحرفية لنصوص عربية بقيت ظاهرة؛ لذلك الرقم 80 حد أدنى مؤكد، وليس دليلاً على اكتمال الصفحات الثلاث الباقية.

## P0 — أعطال تمنع استخدام الصفحات

الأعراض ليست نقص بيانات تجريبية عادي، بل عدم تطابق بين مخطط قاعدة البيانات والكود. نقطة بدء التطبيق تستدعي مجموعة محدودة من إجراءات المخطط في `Program.cs` (الأسطر 560–567)، بينما صفحات أخرى تعتمد جداول وأعمدة لم تُنشأ أو لم تُرقَّ بعد.

| المسار | العطل الظاهر | التصنيف |
|---|---|---|
| `/EmployeeUpdates` | `EffectiveDate` و`IsRetroactive` غير موجودين | مخطط DB |
| `/EmployeeUpdates?section=financial` | نفس العطل | مخطط DB |
| `/EmployeeUpdates?section=payment` | نفس العطل | مخطط DB |
| `/Contracts` | `HrLookups.DefaultMonths` غير موجود | مخطط DB |
| `/Contracts/Movements` | نفس العطل | مخطط DB |
| `/EmployeeTasks` | إدخال `NULL` في `HrTaskTemplates.IsActive` | seed/schema contract |
| `/Violations` | جدول `DisciplinarySettings` غير موجود | مخطط DB |
| `/AttendanceDashboard` | `RequestDate` و`UpdatedAt` غير موجودين | مخطط DB |
| `/DayAttendance` | نفس العطل | مخطط DB |
| `/MissingPunchRequests` | جدول `PunchSemantics` غير موجود | مخطط DB |
| `/ShiftRules` | القارئ يتوقع `IsDeducted` ولا يجده | مخطط/استعلام |
| `/AttendanceSettings` | نفس العطل | مخطط/استعلام |
| `/PeriodRules` | جدول `PeriodRules` غير موجود | مخطط DB |
| `/Payroll/FinancialRequests` | `CurrentStep` غير موجود | مخطط DB |
| `/PayrollProvisions` | خطأ SQL: `Incorrect syntax near '.'` | صياغة SQL |
| `/Approvals` | أعمدة `UpdatedAt` و`StartTime` و`EndTime` و`CurrentStep` مفقودة | مخطط DB |
| `/Approvals?Source=SelfService` | نفس العطل | مخطط DB |
| `/Approvals?Source=Admin` | نفس العطل | مخطط DB |
| `/Approvals/Reports` | `UpdatedAt` غير موجود | مخطط DB |

### أدلة من الكود

- `EmployeeUpdates` يصرح أن `EffectiveDate` مملوك لهجرة محددة، ثم يستعلم عنه مباشرة؛ قاعدة الاختبار لم تحصل على الهجرة.
- `ContractRegisterStore` يستعلم عن `DefaultMonths`، مع وجود منطق إضافة العمود في `SqlSchemaMigrator`، لكن الحالة الفعلية تثبت أن التسلسل لا يضمن وصول المخطط المطلوب.
- `EmployeeTasksSchema` يعرّف `IsActive NOT NULL`، بينما إدخال البيانات الابتدائية لا يضمن قيمة متوافقة مع المخطط الموجود.
- إنشاء `DisciplinarySettings` و`PunchSemantics` و`PeriodRules` موزع على Stores/Schemas منفصلة، وصفحات القراءة تصل إليها قبل ضمان الهجرة.
- هذا النمط يخالف أيضاً قاعدة المستودع التي تمنع الاعتماد على الشفاء الذاتي وقت الطلب وتطلب هجرات محكومة صريحة.

## P0 — عدم تطابق القائمة والصلاحيات

المسار `/Payroll/TerminationSettlement` ظاهر للمدير في شريط التنقل، لكنه يحوّل إلى `/AccessDenied`. المطلوب توحيد شرط إظهار الرابط مع نفس سياسة التخويل، أو تصحيح seed للصلاحية إذا كانت الصفحة مطلوبة للمدير.

## P1 — مشاكل الاستجابة والتصميم

### تمدد أفقي على شاشة 1280px

| المسار | عرض المستند مقابل 1280px |
|---|---:|
| `/LeaveBalances` | 1369px |
| `/HrSettings/ApprovalTemplates` | 1452px |
| `/HrSettings/ApprovalTemplates#approval-delegations` | 1452px |
| `/AuditLogs` | 1516px |

### تمدد أفقي على الجوال 390px

| المسار | عرض المستند |
|---|---:|
| `/` | 593px |
| `/Employees` | 685px |
| `/Employees/Evaluations` | 598px |
| `/LeaveBalances` | 1089px |
| `/DisciplinaryRules` | 471px |
| `/HrSettings/Lookups` | 445px |
| `/HrSettings/FieldControl` | 556px |
| `/HrSettings/ApprovalTemplates` | 908px |
| `/HrSettings/EntityFields` | 445px |
| `/Payroll/Loans` | 546px |
| `/Payroll/SalaryScale` | 782px |
| `/HrSettings/ApprovalTemplates#approval-delegations` | 908px |
| `/AuditLogs` | 1236px |

### مشكلة مشتركة في هيكل الجوال

الشريط الجانبي لا يتحول إلى زر/درج مدمج على الجوال؛ يبقى ممدداً أعلى الصفحة، وتبقى قائمة الوحدة الحالية مفتوحة، فتدفع المحتوى إلى الأسفل وتستهلك مساحة كبيرة قبل بداية الصفحة. هذه مشكلة مشتركة في Shell وليست محصورة بصفحة واحدة.

### الثيم الفاتح

فُحصت عينات من لوحة التحكم، الموظفين، الحضور الشهري، الرواتب، التقارير، الإعدادات، وتهيئة الشركة. متغير الثيم والخلفية والألوان الأساسية تتبدل بصورة صحيحة ولم يظهر تمدد أفقي جديد في العينات. بقيت مشاكل الاستجابة الخاصة بالصفحات المذكورة أعلاه، ولا تُعد هذه العينة بديلاً عن اختبار تباين WCAG آلي كامل.

## P1 — مشاكل اللغة

### English

- فُحصت كل الصفحات الـ83 التي اجتازت التشغيل.
- كل واحدة منها احتوت نصاً عربياً ظاهراً عند اختيار `en-US`.
- النص العام `لغات بيانات الشركة` بقي في الشريط الجانبي في جميع الصفحات.
- 79 صفحة لديها نصوص عربية خاصة بمحتواها، وليس الشريط فقط.
- مجموع النصوص العربية الفريدة المرصودة لكل صفحة بلغ 1326 ظهوراً تجميعياً.

أكثر الصفحات نقصاً: `/Payroll/Settings` (66)، `/Settings` (64)، `/HrSettings/ApprovalTemplates` (57)، `/HrSettings/Lookups` (52)، `/EmployeeDocuments` (39)، `/HrSettings/NotificationCenter` (34)، `/HrSettings/FieldControl` (34)، `/Organization` (33)، `/HrSettings/Formulas` (32)، `/Settings/Dictionary` (32).

### کوردی – سۆرانی

- `lang=ckb-IQ` و`dir=rtl` يُطبقان بصورة صحيحة.
- مع ذلك بقيت نصوص عربية مطابقة حرفياً في 80 من 83 صفحة سليمة، بإجمالي 834 مطابقة مؤكدة ضمن عينة النصوص الإنكليزية غير المترجمة.
- من أسوأ الصفحات: الإعدادات، المستخدمون، الهوية، مستندات الموظفين، المخالفات، الصيغ، حقول الموظف، إعدادات الخدمة الذاتية، الإشعارات، قوالب الموافقات، إعدادات الرواتب، والقاموس.

الاستنتاج: تبديل الثقافة والاتجاه يعمل، لكن قاموس الواجهة وتغطية النصوص الديناميكية غير مكتملين. المشكلة ليست RTL/LTR بحد ذاته.

## P1 — اختبارات التصميم والعقود

البناء `Release` نجح دون أخطاء أو تحذيرات. لكن مجموعة الاختبارات غير المعتمدة على `ProductionClosureSqlTests` انتهت كالآتي: 1961 ناجح، 2 فاشل، 28 متجاوز.

الفشلان الحقيقيان:

1. `UnifiedPageDesignContractTests.PageCssFamilies_AreRatchetLocked`: عدد ملفات CSS المحلية للصفحات أصبح 123، بينما الحد المقفول 122. هذا يشير إلى إضافة عائلة تصميم محلية بدلاً من توسيع العقد الموحد.
2. `RazorPresentationContractTests.MigratedPageCss_UsesTokensAndLogicalDirectionProperties`: لون صريح `#2a3a52` موجود كقيمة fallback داخل `create-105041754d.css`، مخالفاً لعقد استخدام Design Tokens فقط. نفس النمط موجود أيضاً في `zynora-administration.css` وإن كان الاختبار الحالي يفحص مجلد الصفحات.

اختبارات `ProductionClosureSqlTests` لم يمكن إكمالها في جلسة الفحص لأن LocalDB لم يستطع إنشاء instance آلي منفصل. هذه ملاحظة بيئة تحقق، ولا تُسجل كفشل منتج مؤكد؛ لكنها تعني أن اختبارات SQL الحرجة لم تُثبت خضرتها في هذه الجولة.

## P2 — الوصولية والجودة التقنية

- لم تُرصد أخطاء Console JavaScript على الصفحات السليمة أثناء جولة التنقل.
- لم تُرصد صور مكسورة أو معرّفات HTML مكررة ضمن المسارات المفحوصة.
- وُجدت حقول تحكم كثيرة لا يثبت الفحص الآلي ارتباطها بـ`label`، خصوصاً في Organization، TerminationReasons، ApprovalTemplates، ShiftTypes، Branding وفلاتر الرواتب. يلزم فحص DOM يدوي/axe قبل تصنيف كل حالة كخرق مؤكد، لكنها ديْن وصولية واضح.
- صفحة Developer Exception مكشوفة للمستخدم في بيئة الاختبار وتعرض تفاصيل stack/SQL. يجب ضمان تعطيلها في أي بيئة عامة.

## سجل المسارات صفحة بصفحة

الرموز: `EN` نص عربي في الوضع الإنكليزي، `KU` نص عربي مطابق بقي في الكردي، `M` تمدد جوال، `D` تمدد مكتبي. كل صف أدناه فُتح فعلياً؛ الصفحات الحرجة مذكورة منفصلة في جدول P0.

| المسار السليم | نتيجة التشغيل | الملاحظات |
|---|---|---|
| `/` | سليم | EN, KU, M |
| `/Employees/PeopleDashboard` | سليم | EN |
| `/Employees` | سليم | EN, M |
| `/Engagement` | سليم | EN, KU |
| `/Forms/Submissions` | سليم | EN, KU |
| `/AssetsManagement` | سليم | EN, KU |
| `/Employees/Evaluations` | سليم | EN, KU, M |
| `/Employees/TemporaryHeads` | سليم | EN, KU |
| `/LeaveBalances` | سليم | EN, KU, M, D |
| `/Alerts` | سليم | EN, KU |
| `/Organization` | سليم | EN, KU |
| `/Organization/Index#tab=chart` | سليم | EN, KU |
| `/OrgStructures` | سليم | EN, KU |
| `/EmployeeDocuments` | سليم | EN, KU |
| `/Documents/Generate` | سليم | EN, KU |
| `/Documents/Requests` | سليم | EN, KU |
| `/CompanyDocuments` | سليم | EN, KU |
| `/Documents/Templates?kind=Badge` | سليم | EN, KU |
| `/BadgeCenter` | سليم | EN, KU |
| `/PeopleReports` | سليم | EN, KU |
| `/Forms` | سليم | EN, KU |
| `/HrSettings/ViolationConfiguration` | سليم | EN, KU |
| `/DisciplinaryRules` | سليم | EN, KU, M |
| `/Acknowledgments` | سليم | EN, KU |
| `/Acknowledgments/Tracking` | سليم | EN, KU |
| `/Documents/Templates` | سليم | EN, KU |
| `/HrSettings/Formulas` | سليم | EN, KU |
| `/EmployeeProfileSettings` | سليم | EN, KU |
| `/HrSettings/ProbationPeriod` | سليم | EN, KU |
| `/HrSettings/NoticePeriod` | سليم | EN, KU |
| `/HrSettings/SelfServiceSettings` | سليم | EN, KU |
| `/HrSettings/TerminationReasons` | سليم | EN, KU |
| `/HrSettings/NotificationCenter` | سليم | EN, KU |
| `/HrSettings/Lookups` | سليم | EN, KU, M |
| `/HrSettings/EmployeeCodeSchema` | سليم | EN, KU |
| `/HrSettings/FieldControl` | سليم | EN, KU, M |
| `/HrSettings/ApprovalTemplates` | سليم | EN, KU, M, D |
| `/HrSettings/EntityFields` | سليم | EN, KU, M |
| `/HrSettings/EmployeeGroups` | سليم | EN, KU |
| `/EmployeePermissions` | سليم | EN, KU |
| `/MonthAttendance` | سليم | EN, KU |
| `/AttendanceRecommendations` | سليم | EN, KU |
| `/WorkFromHome` | سليم | EN, KU |
| `/ShiftAssignments` | سليم | EN, KU |
| `/ShiftOverrides` | سليم | EN, KU |
| `/Roster` | سليم | EN, KU |
| `/AttendanceReports` | سليم | EN, KU |
| `/ShiftTypes` | سليم | EN, KU |
| `/Devices` | سليم | EN, KU |
| `/BiometricKeys` | سليم | EN, KU |
| `/Holidays` | سليم | EN, KU |
| `/EmployeeGeoLocations` | سليم | EN, KU |
| `/Payroll/Runs` | سليم | EN |
| `/Payroll/Analytics` | سليم | EN, KU |
| `/Payroll/PayslipInquiry` | سليم | EN, KU |
| `/Payroll/Simulator` | سليم | EN, KU |
| `/Payroll/TransactionsAuditor` | سليم | EN, KU |
| `/PayrollReports` | سليم | EN, KU |
| `/Payroll/SalarySheet` | سليم | EN, KU |
| `/Payroll/Transactions?type=Income` | سليم | EN, KU |
| `/Payroll/Transactions?type=Deduction` | سليم | EN, KU |
| `/Payroll/Overtime` | سليم | EN, KU |
| `/Payroll/SalaryDaysAdjustment` | سليم | EN, KU |
| `/Payroll/LeaveEncashment` | سليم | EN, KU |
| `/Payroll/Loans` | سليم | EN, KU, M |
| `/Payroll/Raises` | سليم | EN, KU |
| `/Payroll/EndOfService` | سليم | EN, KU |
| `/Payroll/SalaryItems` | سليم | EN, KU |
| `/Payroll/Settings` | سليم | EN, KU |
| `/Payroll/ExchangeRates` | سليم | EN, KU |
| `/Payroll/BankTemplates` | سليم | EN, KU |
| `/Payroll/SalaryScale` | سليم | EN, KU, M |
| `/Approvals/Committees` | سليم | EN, KU |
| `/HrSettings/ApprovalTemplates#approval-delegations` | سليم | EN, KU, M, D |
| `/Settings` | سليم | EN, KU |
| `/Setup` | سليم | EN, KU |
| `/UserAccess` | سليم | EN, KU |
| `/AccessRoles` | سليم | EN, KU |
| `/AuditLogs` | سليم | EN, KU, M, D |
| `/Settings/Dictionary` | سليم | EN, KU |
| `/Settings/DataLanguages` | سليم | EN, KU |
| `/Branding` | سليم | EN, KU |
| `/Integrations/Webhooks` | سليم | EN, KU |

## نتيجة جولة الإصلاح الشامل

- عولجت أعطال مخطط قاعدة البيانات التي كانت تسقط صفحات حركات الموظفين، العقود، المهام، المخالفات، الحضور، مستحقات الرواتب والموافقات. أضيفت هجرة مصالحة واحدة قابلة للتكرار، مع تصحيح مخططات الإنشاء الأولي كي تتطابق القواعد الجديدة مع القواعد المرقّاة.
- صُححت صلاحية صفحة تسوية نهاية الخدمة عندما تُفتح كصفحة اختيار من دون `EmployeeId`.
- أعيد فحص جميع مسارات قائمة المدير البالغ عددها **103 مسارات** بعد الإصلاح: **0 استثناءات، 0 منع صلاحية غير متوقع، 0 معرّفات DOM مكررة، 0 عناصر تحكم مرئية بلا اسم وصول، و0 تمدد أفقي للمستند**.
- أضيف درج تنقل حقيقي للجوال مع خلفية إغلاق، زر واضح، دعم `Escape`، وإغلاق تلقائي بعد اختيار الرابط، مع إصلاح الجداول والنماذج والبطاقات في العرض الضيق.
- أُعيد فحص المسارات الـ103 في `Light Mode` بقياس الألوان المحسوبة للمساحات الكبيرة. صُححت بقايا Dark في `/EmployeeProfileSettings` و`/Settings/DataLanguages` (بما فيها الهيرو والبطاقات والأقسام والحقول ورسائل التحقق والنوافذ). كشف فحص متابعة واعٍ بـ`background-image` أن قالب `nxhs` المشترك كان يخفي تدرجات داكنة في `/HrSettings/ApprovalTemplates`؛ عولج العقد المشترك بالتوكنات، ثم فُحصت **31 وجهة تعتمد القالب (30 صفحة فعلية + إعادة توجيه واحدة)** وكانت النتيجة **0 مساحات داكنة شاذة**. جرى أيضاً التحقق من بقاء `Dark Mode` سليماً بعد التحويل إلى Design Tokens.
- صُححت اتجاهات الإنجليزية والكوردية والعربية على مستوى الغلاف ومكوّنات الواجهة؛ الاختبار الفعلي أكد `en-US/LTR` و`ckb-IQ/RTL` من دون تمدد أفقي.
- أضيفت عناوين `h1` الدلالية للصفحات التي كانت تملك عنواناً بصرياً فقط، وأسماء وصول للقوائم المخصصة ومنتقيات الألوان والحقول القديمة.
- نجح بناء Release بلا تحذيرات أو أخطاء، ونجحت حزمة الاختبارات الكاملة مع SQL/LocalDB: **2020 ناجح، 0 فاشل، 0 متخطى**.

### المتبقي خارج إصلاحات الكود

- البنية الكاملة للقاموس والترجمة الآلية والاستيراد/التصدير موجودة، لكن استكمال كل نصوص المحتوى غير المترجمة آلياً يحتاج تفعيل مفتاح مزود الترجمة. لا تُنشأ خدمة مدفوعة ولا تُخزّن مفاتيح سراً داخل المستودع. بيانات الأعمال التي يدخلها المستخدم (أسماء الموظفين والشركات والأقسام وغيرها) تُدار عبر طبقة ترجمات بيانات الشركة، ولا ينبغي اعتبار الاسم العربي الأصلي خطأ واجهة.

## ترتيب الإصلاح المنفذ

1. **بوابة هجرات واحدة ومحكومة:** تجهيز قاعدة جديدة من الصفر، تطبيق كل migrations/SQL scripts بالترتيب، ثم منع الصفحات من الاعتماد على `EnsureAsync` وقت الطلب.
2. **اختبار smoke لكل Route:** يفشل عند أي 500، Developer Exception، AccessDenied غير متوقع، أو redirect إلى Login.
3. **إصلاح 19 مساراً كمجموعات مخطط:** EmployeeUpdates، Contracts، Tasks، Disciplinary، Attendance، Payroll requests/provisions، Approvals.
4. **توحيد الصلاحيات والقائمة:** المصدر نفسه يقرر عرض الرابط والوصول إليه.
5. **إصلاح Shell الجوال أولاً** ثم الجداول/النماذج الاثني عشر ذات التمدد.
6. **إغلاق فجوة القاموس:** استخراج مفاتيح الواجهة، منع النصوص الخام بعقد اختبار، واشتراط عدم وجود حروف عربية في وضع English مع استثناء بيانات المستخدم فقط.
7. **إصلاح عقد التصميم:** إزالة عائلة CSS الزائدة واستبدال الألوان الصريحة بـDesign Tokens.
8. **تشغيل SQL/security suites في بيئة CI مخصصة** قبل أي نشر.

## حدود الجولة

- تم فتح كل المسارات الـ103 المتاحة من قائمة المدير، وليس كل حالة فرعية ممكنة داخل كل زر أو modal أو صفحة تفاصيل تتطلب معرّف سجل محدداً.
- ملفات Razor البالغ عددها 210 دخلت في البناء والفحص الساكن، لكن الصفحات غير القابلة للوصول من القائمة أو التي تتطلب بيانات/ID لم تُعامل كمسار runtime مستقل.
- لم تُنفذ عمليات حذف أو اعتماد أو دفع رواتب أو كتابات مدمرة أثناء التدقيق.
