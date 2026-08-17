# نقل النظام إلى حاسبة جديدة كلياً — قائمة التحقق (2026-08-16)

**تحديث لـ`MACHINE-HANDOVER.md` (2026-08-11)** بعد جرد فعلي لهذه الحاسبة اليوم. ما تغيّر منذ
الوثيقة القديمة: **النطاق `zynorahr.com` يعمل عبر Cloudflare Tunnel** (سكربت التصدير القديم
**لا يحمله**) · مفاتيح Data Protection صارت بالقاعدة · الحيّ يعمل بفرع غير مدموج بـ`main`.

---

## الصورة الحالية (جرد 2026-08-16)

| المكوّن | الواقع على هذه الحاسبة |
|---|---|
| القاعدة | SQL Server **17 Express** · `SmartAttendance` · 172 جدولاً · 784 MB · 2794 موظفاً · 3 شركات · آخر نسخة `.bak` 12:54 اليوم |
| مفاتيح Data Protection | **بالقاعدة** (`DataProtectionKeys` = 1) + نسخة احتياطية بالقرص (2 ملف) |
| وثائق الموظفين المشفَّرة | `App_Data\ProtectedEmployeeFiles` — 2 ملف / 5.3 MB 🔴 |
| مرفوعات | `wwwroot\uploads` 10 ملفات / 3.4 MB · `wwwroot\tenant-assets` 3 ملفات |
| TLS محلي | `certs\lan.pfx` موجود |
| التشغيل | `run-server.bat` + مهمة مجدولة `ZynoraPortalServer` (Running) |
| **النفق** | خدمة `Cloudflared` (Running) · وضع **ملف تهيئة** لا توكن · `C:\Users\Lenovo\.cloudflared\` = `config.yml` + `cert.pem` + `<tunnel-id>.json` · النفق `14e05ccf-…` · 3 أسماء مضيفين → `localhost:5080` |
| .NET | SDK **10.0.302** |
| الحيّ | فرع `feat/payroll-kayan-parity @ b7ab769` (**ليس `main`** — الوثيقة القديمة تقول «انقل main» وهذا خطأ الآن) |

---

## ما تحتاجه — 9 بنود (7 بالحزمة + 2 خارجها)

### أ) ما تحمله حزمة `export-machine.ps1` تلقائياً ✅
1. **`.bak` القاعدة** (متحقَّق بـ`RESTORE VERIFYONLY`) — تحمل معها مفاتيح Data Protection
2. **`appsettings*.json`** — سلسلة الاتصال + SMTP + VAPID (أسرار حية)
3. **`wwwroot/uploads`** — صور/وثائق/شعارات
4. **`wwwroot/tenant-assets`** — هوية الشركات
5. **`App_Data/ProtectedEmployeeFiles`** 🔴 — **الفئة الوحيدة التي لا تُسترجَع إن نُسيت** (القاعدة تحمل أسماءها فقط)
6. **`certs/lan.pfx`** — TLS المحلي للـPWA
7. **`run-server.bat` + `ZynoraPortalServer.xml`** — تعريف المهمة

### ب) ما **لا** تحمله الحزمة — تنقله يدوياً 🔴
8. **Cloudflare Tunnel** — بدونه `zynorahr.com` لا يصل للحاسبة الجديدة:
   - انسخ المجلد كاملاً `C:\Users\Lenovo\.cloudflared\` (الثلاثة: `config.yml` · `cert.pem` · `14e05ccf-….json`) — **أسرار، بذاكرة محمولة**
   - على الجديدة: ثبّت `cloudflared` → ضع المجلد بنفس المسار لمستخدمك (أو `C:\Windows\System32\config\systemprofile\.cloudflared\` إن ستشغّله كخدمة نظام) → `cloudflared service install` → تحقق `cloudflared tunnel info 14e05ccf-358c-44a0-8753-c25b532ada5e`
   - **لا تحتاج تغيير DNS**: النفق نفسه بمعرّفه — Cloudflare يوجّه للحاسبة التي يعمل عليها
   - ⚠️ **لا تشغّل النفق على الحاسبتين معاً** بنفس المعرّف إلا وأنت تقصد التوزيع
9. **الفرع الصحيح**: الحيّ = `feat/payroll-kayan-parity` (فوق `feat/people-kayan-parity` فوق `feat/access-roles-phase1`). إمّا **تدمج PR #36 و#38 بـ`main` أولاً** (الأنظف — يجعل «انقل main» صحيحاً)، أو تسحب هذا الفرع بعينه على الجديدة.

### ج) ما يعتمد على البيئة (تحقق لا نقل)
- **اسم خادم SQL**: الحزمة تعمل مع أي اسم؛ عدّل `DefaultConnection` فقط إن اختلف عن `localhost`
- **إصدار SQL**: `.bak` من 17 لا يُسترجع على أقدم — ثبّت **SQL Server 2025/17 Express** أو أحدث
- **.NET 10 SDK** — نفس الإصدار الرئيسي
- **جدار الحماية**: المنفذ 5080 محلي فقط (النفق يخرج لا يدخل — لا حاجة لفتح منافذ)
- **زمن الجهاز/المنطقة الزمنية**: نفس المنطقة الزمنية كي لا تنحرف مواقيت الحضور

---

## التسلسل الآمن (بلا انقطاع طويل)

```
[القديمة]  1. (اختياري لكن مستحسن) ادمج #36 و#38 بـmain
           2. .\scripts\handover\export-machine.ps1      ← كمسؤول
           3. انسخ C:\Users\Lenovo\.cloudflared\ يدوياً إلى الحزمة (مجلد cloudflared/)
           4. انقل الحزمة بذاكرة محمولة (لا بريد/سحابة)

[الجديدة]  5. ثبّت: SQL Server 17 Express · .NET 10 SDK · git · cloudflared
           6. git clone … && git checkout <الفرع الحيّ أو main بعد الدمج>
           7. .\scripts\handover\import-machine.ps1 -BundlePath … -WhatIfOnly   ← فحص
           8. .\scripts\handover\import-machine.ps1 -BundlePath …               ← تنفيذ
           9. .\scripts\deploy\Publish-Zynora.ps1 -CheckOnly  ثم النشر الكامل بـ-Approve
          10. أعد نسخ config\appsettings.json بعد النشر (publish يدهسه)
          11. سجّل المهمة: schtasks /Create /XML runtime\ZynoraPortalServer.xml /TN ZynoraPortalServer
          12. ضع .cloudflared\ بمكانه → cloudflared service install → ابدأ الخدمة

[التحقق]  13. http://localhost:5080/health/ready = 200
          14. عدد الجداول = 172 (يطبعه import)
          15. سجّل الدخول → /Employees → افتح وثيقة موظف فعلية 🔴 (يثبت الملفات + مفاتيحها)
          16. شاشة بشعار شركة (يثبت tenant-assets)
          17. https://zynorahr.com من هاتفك خارج الشبكة (يثبت النفق)
          18. شغّل مسير تجريبي على موظف واحد ثم احذفه (يثبت المحرك والهجرات)

[الإغلاق] 19. أوقف النفق والمهمة على القديمة (لا تترك نفقين)
          20. احذف الحزمة من الذاكرة المحمولة والحاسبتين
```

## المزالق التي أثبتتها هذه الجلسة تحديداً
- **`Publish-Zynora.ps1` يسأل `DEPLOY` تفاعلياً** — بجلسة غير تفاعلية مرّره: `cmd /c 'echo DEPLOY| powershell -File …'`
- **`sqlcmd` يفشل بخطأ شهادة** على ODBC 18 — أضف `-C`
- **`schtasks /End` لا يوقف الخادم** — حلقة `goto loop` تعيده؛ أوقف `cmd.exe` صاحب `run-server.bat`
- **الجلسات تُطرد مرة** بعد أي نشر (مفاتيح Data Protection) — طبيعي
- 🔴 **لا تبنِ القاعدة من الهجرات** — الاسترجاع من `.bak` هو الطريق الوحيد (17/20 هجرة تنجح كذباً)

## ما يمكنني تنفيذه لك الآن (بأمرك)
1. **تحديث `export-machine.ps1`** ليحمل مجلد `.cloudflared` تلقائياً (إغلاق الثغرة الوحيدة بالحزمة)
2. **تشغيل التصدير** وإنتاج حزمة اليوم (`C:\ZynoraHandover\handover_20260816_*`) بلا أي تغيير على الحاسبة
3. **دمج #36/#38 بـmain** إن أردت جعل «انقل main» صحيحاً — **قرارك أنت** (القاعدة الحمراء)
