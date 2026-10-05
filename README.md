# ZYNORA HR

نسخة النظام والإدارة على الفرع `feature/tenant-code-login`.
يحتوي المستودع كود الويب وواجهة API وأدوات قاعدة البيانات وملفات OCR والمigrations وملفات التصميم والتشغيل.

## تنزيل النسخة

```powershell
git clone --branch feature/tenant-code-login https://github.com/mohammadprince11/SmartAttendance.git
cd SmartAttendance
dotnet restore SmartAttendance.slnx
dotnet build SmartAttendance.slnx -c Release --no-restore
```

المتطلبات: .NET SDK 10 وSQL Server. تثبيت Python ومتطلبات OCR مطلوب عند استخدام قراءة المستندات الذكية.

## قاعدة البيانات والتشغيل المحلي

استخدم قاعدة اختبار مستقلة مهيأة بمخطط النظام؛ ملفات الأساس موجودة في `database/schema.sql`.
راجع المخطط قبل تطبيقه على قاعدة فارغة، أو استعد نسخة قاعدة اختبار متوافقة.
ملفات قاعدة البيانات والمرفقات وحسابات الدخول والأسرار المحلية لا تُنقل بواسطة Git.

في PowerShell، عرّف الإعدادات الخاصة بجهازك:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = '<YOUR-LOCAL-SQL-CONNECTION>'
$env:DatabaseMigrations__ApplyOnStartup = 'true'
$env:PeopleAIWorker__Enabled = 'false'
$env:ZYNORA_PLATFORM_OWNER_USERNAME = '<YOUR-PLATFORM-OWNER>'
$env:ZYNORA_PLATFORM_OWNER_PASSWORD = '<YOUR-STRONG-PASSWORD-AT-LEAST-12-CHARACTERS>'
dotnet run --project SmartAttendance.Web -c Release --no-build --no-launch-profile --urls http://127.0.0.1:5091
```

بعد إقلاع التطبيق:

- النظام: `http://127.0.0.1:5091/`
- إدارة المنصة وإنشاء المنظومات والشركات: `http://127.0.0.1:5091/Platform`

ينشأ مالك المنصة عند خلو جدول المالكين فقط. هذه المتغيرات لا تعيد تعيين كلمة مرور حساب موجود.
أنشئ المنظومة وحساب إدارتها من لوحة المنصة، ثم استخدم حساب المنظومة لدخول نظام الموارد البشرية.
ملف `.env.example` قائمة إعدادات مرجعية؛ التطبيق لا يحمّل ملف `.env` تلقائياً.

## تشغيل OCR

راجع [دليل إعداد OCR](docs/PEOPLE-AI-OCR-PRODUCTION.md) لتثبيت Python/PaddleOCR وLibreOffice والنماذج.
فعّل `PeopleAIWorker__Enabled` وحدد مسار Python بعد اكتمال التثبيت وفحص `--preflight`.
حزم Python والنماذج تُثبّت على الجهاز؛ ملفات العامل ومتطلباته موجودة في `SmartAttendance.Web/PeopleAI`.

## بناء ملفات التشغيل

```powershell
dotnet publish SmartAttendance.Web/SmartAttendance.Web.csproj -c Release --no-restore -o artifacts/publish
```

المجلد الناتج يحتوي ملفات الويب وملفات عامل OCR. إعدادات الاتصال والأسرار تُضبط على جهاز التشغيل.
للاستخدام الإنتاجي اتبع [دليل التشغيل](docs/ZYNORA-PRODUCTION-OPERATIONS-RUNBOOK.md) و[دليل الهجرات المحكومة](docs/PEOPLE-AI-DATABASE-DEPLOYMENT.md).
الإنتاج يتطلب إعدادات الأمان وفحص المرفقات والهجرات الصريحة؛ أمر التشغيل المحلي أعلاه مخصص للتطوير.

## التحقق

```powershell
dotnet test SmartAttendance.Tests/SmartAttendance.Tests.csproj -c Release
```

اختبارات SQL تتطلب SQL Server/LocalDB مخصصاً للاختبار، واختبارات E2E تحتاج إعدادات `ZYNORA_E2E_*` الموضحة في `.env.example`.
