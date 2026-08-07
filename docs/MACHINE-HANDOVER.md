# نقل النظام إلى جهاز آخر

**آخر تحديث:** 2026-08-03 · **مُجرَّب فعلياً على جهاز الإنتاج الحالي**

---

## لماذا git وحده لا يكفي

`git clone` يجيب **الكود فقط**. أربعة أشياء مستثناة عمداً من المستودع (وهذا
صحيح أمنياً) ولا يعمل النظام بدونها:

| # | الشيء | أين هو | لماذا مستثنى |
|---|-------|--------|---------------|
| 1 | قاعدة البيانات | SQL Server | ليست ملفاً بالمستودع أصلاً |
| 2 | `appsettings.json` | جذر النشر | يحوي سلسلة الاتصال ومفاتيح SMTP وVAPID (`.gitignore:52`) |
| 3 | `wwwroot/uploads/*` | جذر النشر | صور الموظفين والوثائق والشعارات |
| 4 | `run-server.bat` + المهمة المجدولة | جذر النشر / Task Scheduler | إعداد تشغيل خاص بالجهاز |

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

يجمع الأربعة في `C:\ZynoraHandover\handover_<تاريخ>\`:

```
MANIFEST.md                         بيان الحزمة (الفرع · الكوميت · المحتوى)
SmartAttendance_handover_*.bak      نسخة القاعدة — متحقَّقة بـRESTORE VERIFYONLY
config/                             appsettings*.json
uploads/                            مرفوعات المستخدمين
runtime/                            run-server.bat + ZynoraPortalServer.xml
```

**شغّله كمسؤول** ليكتب SQL نسخته داخل الحزمة مباشرةً. بدون ذلك تنجح النسخة لكن
تبقى في مجلد SQL الافتراضي وينبّهك السكربت لنقلها يدوياً.

### النقل

الحزمة **تحوي أسراراً حيّة**. انقلها بذاكرة محمولة أو شبكة موثوقة — **لا ببريد
ولا محادثة ولا تخزين سحابي عام** — واحذفها بعد نجاح الاستيراد.

### على الجهاز الجديد

المتطلبات: **.NET 10 SDK** · **SQL Server** (أي إصدار، Express يكفي) · **git**

```powershell
git clone https://github.com/mohammadprince11/SmartAttendance.git
cd SmartAttendance
git checkout feature/people-parity
```

افحص الجاهزية أولاً بلا أي تغيير:

```powershell
.\scripts\handover\import-machine.ps1 -BundlePath "D:\handover_20260803_183652" -WhatIfOnly
```

ثم نفّذ:

```powershell
.\scripts\handover\import-machine.ps1 -BundlePath "D:\handover_20260803_183652"
```

يسترجع القاعدة (**يسأل `YES` صراحةً** إن كانت موجودة) ويعيد الإعدادات
والمرفوعات، ثم يطبع الخطوات الثلاث المتبقية: النشر، تسجيل المهمة، التشغيل.

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
- عدد الجداول = **165** (يطبعه سكربت الاستيراد)
- عملية واحدة وحلقة واحدة فقط
- سجّل الدخول وافتح `/Employees` و`/Setup`
- **احذف حزمة النقل**
