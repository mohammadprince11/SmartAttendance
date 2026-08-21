# نقل النظام إلى جهاز آخر

**آخر تحديث:** 2026-08-21 · **مُجرَّب فعلياً على جهاز الإنتاج الحالي** ·
جرد الحالة الفعلية: [`MACHINE-MIGRATION-CHECKLIST-2026-08-16.md`](MACHINE-MIGRATION-CHECKLIST-2026-08-16.md)

---

## لماذا git وحده لا يكفي

`git clone` يجيب **الكود فقط**. ما يلي مستثنى عمداً من المستودع (وهذا صحيح
أمنياً) ولا يعمل النظام بدونه:

| # | الشيء | أين هو | لماذا مستثنى |
|---|-------|--------|---------------|
| 1 | قاعدة البيانات | SQL Server | ليست ملفاً بالمستودع أصلاً |
| 2 | `appsettings.json` | جذر النشر | يحوي سلسلة الاتصال ومفاتيح SMTP وVAPID |
| 3 | `wwwroot/uploads/*` | جذر النشر | صور الموظفين والوثائق والشعارات |
| 4 | `wwwroot/tenant-assets/*` | جذر النشر | هوية كل شركة المرفوعة — **شقيق `uploads` لا ابنه** |
| 5 | `App_Data/ProtectedEmployeeFiles` | جذر النشر | وثائق الموظفين **مشفَّرة بـIDataProtector** |
| 6 | `certs/lan.pfx` | جذر النشر | شهادة TLS المحليّة — يلزمها PWA الموبايل |
| 7 | `run-server.bat` + `run-hidden.vbs` + المهمة المجدولة | جذر النشر / Task Scheduler | إعداد تشغيل خاص بالجهاز — المهمة تستدعي الـ`.vbs` لا الـ`.bat` مباشرة |
| 8 | مجلد `.cloudflared` | ملف مستخدم Windows | نفق Cloudflare — يربط `zynorahr.com` بالحاسبة؛ أسرار حيّة (cert + credentials) |

### 🔴 البند 5 — فئة الخطر الوحيدة التي لا تُسترجَع

الصفوف بالقاعدة تشير للوثائق باسمٍ مولَّد، والملفّ نفسه بالقرص **مشفَّراً**
(`ProtectedFileService`). ترك المجلد خلفك = ضياعٌ نهائيّ: الاسترجاع ينجح
ظاهرياً، والقاعدة سليمة، والوثائق مفقودة. **مفاتيح الحماية** نفسها تسكن
القاعدة (`PersistKeysToDbContext`) فتأتي مع الـ`.bak`. النسخة الحالية من
سكربت التصدير تحمل الاثنين — وسكربت الاستيراد **يرفض الصمت** إن كانت الحزمة
من إصدارٍ أقدم لا يحملهما.

## ⛔ لا تبنِ القاعدة من الهجرات

**`dotnet ef database update` على قاعدة فارغة يعطي مخططاً ناقصاً يُخفي نقصه.**
جُرِّب فعلياً 2026-08-03: 17 من 20 هجرة طُبِّقت و3 فشلت، **وبعض «النجاح» كاذب**
لأن كل هجرة `BEGIN TRAN…COMMIT` بلا `XACT_ABORT` — فالجملة تفشل، المعاملة
تُكمَل، وتُسجَّل الهجرة كمطبَّقة.

السبب الجذري: هجرات EF تعتمد على جداول ينشئها SQL خام والشفاء الذاتي (396 موضع
DDL)، ومنها `EmployeePortalAnnouncements` التي تقرأها هجرة الإعلانات.

**الاسترجاع من `.bak` هو الطريق الموثوق الوحيد** حتى يُصلَح هذا بندٌ مستقلّ.

---

## الخطوات

### على الجهاز الحالي

```powershell
.\scripts\handover\export-machine.ps1
```

يجمع كل ما سبق في `C:\ZynoraHandover\handover_<تاريخ>\`:

```
MANIFEST.md                         بيان الحزمة (الفرع · الكوميت · المحتوى)
SmartAttendance_handover_*.bak      نسخة القاعدة — متحقَّقة بـRESTORE VERIFYONLY
config/                             appsettings*.json
uploads/                            مرفوعات المستخدمين
tenant-assets/                      هوية الشركات المرفوعة
App_Data/ProtectedEmployeeFiles/    وثائق الموظفين المشفَّرة
App_Data/DataProtection-Keys/       مفاتيح الحماية (احتياطاً — الأصل بالقاعدة)
App_Data/ReportTemplates/           قوالب التقارير
certs/                              شهادة TLS المحليّة
runtime/                            run-server.bat + run-hidden.vbs + ZynoraPortalServer.xml
cloudflared/                        نفق Cloudflare (config.yml + cert.pem + credentials)
```

أرشيف الاستيراد (`AttendanceImports` وأخواته، ~140 ميغابايت) **مُستثنى
افتراضياً** — قابلٌ لإعادة التوليد. أضِف `-IncludeImportArchives` إن أردته.

**شغّله كمسؤول** ليكتب SQL نسخته داخل الحزمة مباشرةً. بدون ذلك تنجح النسخة لكن
تبقى في مجلد SQL الافتراضي وينبّهك السكربت لنقلها يدوياً.

### النقل

الحزمة **تحوي أسراراً حيّة**. انقلها بذاكرة محمولة أو شبكة موثوقة — **لا ببريد
ولا محادثة ولا تخزين سحابي عام** — واحذفها بعد نجاح الاستيراد.

### على الجهاز الجديد

المتطلبات: **.NET 10 SDK** · **SQL Server 17/2025 Express أو أحدث** (الـ`.bak`
المأخوذ من 17 **لا يُسترجَع** على إصدار أقدم) · **git** · **cloudflared** (للنطاق العام)

```powershell
git clone https://github.com/mohammadprince11/SmartAttendance.git
cd SmartAttendance
git checkout <الفرع المذكور في MANIFEST.md>
```

**الفرع الصحيح هو ما يذكره `MANIFEST.md` داخل الحزمة** (بيان الحزمة يسجّل فرع
وكوميت الجهاز المصدر وقت التصدير). لا تفترض أنه `main`: الجهاز الحيّ عمل
فترةً بفرعٍ متقدمٍ على `main` (جرد 2026-08-16) — انقل ما يعمل فعلاً.

افحص الجاهزية أولاً بلا أي تغيير:

```powershell
.\scripts\handover\import-machine.ps1 -BundlePath "D:\handover_20260803_183652" -WhatIfOnly
```

ثم نفّذ:

```powershell
.\scripts\handover\import-machine.ps1 -BundlePath "D:\handover_20260803_183652"
```

يسترجع القاعدة (**يسأل `YES` صراحةً** إن كانت موجودة)، يعيد الإعدادات
والمرفوعات، يعيد مجلد النفق إلى `%USERPROFILE%\.cloudflared` (يسأل `YES`
إن وُجد نفقٌ قائم)، ثم يطبع الخطوات المتبقية: النشر، تسجيل المهمة، التشغيل،
وتثبيت خدمة النفق (`cloudflared service install` — **بعد** إيقافها على القديمة).

---

## مزالق تُوفّر عليك ساعة

| المزلق | ما يحدث | العلاج |
|--------|---------|--------|
| **ترميز سكربتات PowerShell** | PowerShell 5.1 يقرأ `.ps1` بـANSI ما لم يبدأ بـBOM ⟹ العربية تتحوّل رموزاً و**يفشل التحليل قبل أي تنفيذ** | السكربتان محفوظان بـUTF-8 **مع BOM**. لا تحفظهما بمحرّر يُسقطه |
| **حساب خدمة SQL** | `NT Service\MSSQLSERVER` ليس عضواً في Users فلا يكتب بمجلد الحزمة | السكربت يمنحه الحقّ بـ`icacls` (يحتاج تشغيلاً كمسؤول) |
| **`dotnet publish` يدهس appsettings** | تخسر الأسرار بعد الاستيراد | أعِد نسخ `config\appsettings.json` **بعد** النشر |
| **اسم خادم SQL مختلف** | التطبيق لا يتصل | عدّل `DefaultConnection` في `appsettings.json` |
| **`run-server.bat` فيه حلقة `goto loop`** | `schtasks /End` وحده لا يوقف الخادم — الحلقة تعيد إطلاقه بعد 3 ثوانٍ | أوقف الـ`cmd.exe` صاحب الحلقة لا الـ`.exe`. وكل `schtasks /Run` يفتح حلقة جديدة فتتراكم |
| **`sqlcmd` وQUOTED_IDENTIFIER** | جُمَل الفهارس المفلترة تفشل | استعمل الراية `-I` |
| **XML المهمة يحمل حساب الجهاز القديم** (اسماً وSID) | `schtasks /Create /XML` يفشل بـ`No mapping between account names and security IDs` | استبدل كل `<UserId>…</UserId>` بحسابك المحلي: `(Get-Content $xml -Raw) -replace '<UserId>[^<]*</UserId>', "<UserId>$env:COMPUTERNAME\<حسابك></UserId>"` إلى ملف جديد بترميز Unicode ثم سجّله |
| **المهمة تستدعي `run-hidden.vbs` لا الـ`.bat`** | حزمة قديمة بلاه ⟹ «Can not find script file» بعد التسجيل | سكربت الاستيراد الحالي ينشئه تلقائياً إن غاب؛ التصدير الحالي يحمله |

## إيقاف الخادم إيقافاً صحيحاً

```powershell
schtasks /Change /TN "ZynoraPortalServer" /DISABLE
schtasks /End /TN "ZynoraPortalServer"
Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" |
  Where-Object { $_.CommandLine -like '*run-server.bat*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Get-Process -Name "SmartAttendance*" -ErrorAction SilentlyContinue | Stop-Process -Force
```

## بعد الاستيراد — تحقّق

- `http://localhost:5080/Account/Login` يرد **200**
- عدد الجداول = **170** (يطبعه سكربت الاستيراد · كان 165 بتاريخ 2026-08-03)
- عملية واحدة وحلقة واحدة فقط
- سجّل الدخول وافتح `/Employees` و`/Setup`
- **افتح وثيقة موظف فعليّة** — هذا وحده يثبت أن الملفات المشفَّرة وصلت
  ومفاتيحها تفكّها. لا تحذف الحزمة قبل هذا الاختبار بالذات
- افتح شاشةً فيها شعار الشركة — يثبت وصول `tenant-assets`
- افتح `https://zynorahr.com` من هاتفك **خارج الشبكة** — يثبت وصول النفق
  (وتأكد أن خدمة Cloudflared على القديمة **متوقفة** — نفقٌ واحد لا اثنان)
- **احذف حزمة النقل**
