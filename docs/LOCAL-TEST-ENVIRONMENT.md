# بيئة الاختبار المحلية — ZYNORA HR

هذا الملف يشرح **أسماء** المتغيرات فقط. **ممنوع** وضع أي قيمة حقيقية (كلمة مرور،
توكن، سلسلة اتصال، عنوان إنتاج) داخل المستودع.

## اختبارات الوحدة

لا تحتاج أي متغيّر ولا قاعدة بيانات — منطق نقي:

```bash
dotnet test SmartAttendance.Tests/SmartAttendance.Tests.csproj -c Release
```

## اختبارات E2E (Playwright)

تعمل **فقط** على بيئة اختبار مخصصة. **ممنوع تشغيلها على الإنتاج.**

| المتغيّر | الوصف |
|---|---|
| `ZYNORA_E2E_BASE_URL` | عنوان بيئة الاختبار (مثال شكلي: `https://localhost:5443`) |
| `ZYNORA_E2E_USERNAME` | حساب اختبار مخصص — لا حساب حقيقي ولا أدمن الإنتاج |
| `ZYNORA_E2E_PASSWORD` | كلمة مرور حساب الاختبار |

**السلوك عند غياب المتغيرات:** الاختبارات تُتخطّى برسالة واضحة (`Assert.Ignore`) —
لا تسقط على حساب افتراضي ولا تضرب أي بيئة بالخطأ.

### التشغيل محلياً

```bash
dotnet build SmartAttendance.E2E/SmartAttendance.E2E.csproj -c Release
```

ثم ثبّت متصفحات Playwright مرة واحدة:

```bash
pwsh SmartAttendance.E2E/bin/Release/net10.0/playwright.ps1 install chromium
```

ثم عرّف المتغيرات بجلستك (لا تكتبها بملف داخل المستودع) وشغّل:

```bash
dotnet test SmartAttendance.E2E/SmartAttendance.E2E.csproj -c Release --no-build
```

ولتشغيل بوابة الإصدار بنفس عزل CI، اختر اسماً مؤقتاً يبدأ حصراً بـ
`SmartAttendance_E2E_`، وعرّف كلمة مرور تركيبية في جلسة PowerShell، ثم:

```powershell
$env:ZYNORA_E2E_DATABASE_NAME = 'SmartAttendance_E2E_Local'
$env:ZYNORA_BOOTSTRAP_ADMIN_PASSWORD = '<synthetic-password-for-local-test>'
dotnet run --project tools/SmartAttendance.E2E.Bootstrap -c Release -- setup
# شغّل SmartAttendance.Web واختبارات Playwright بالمتغيرات أعلاه.
dotnet run --project tools/SmartAttendance.E2E.Bootstrap -c Release --no-build -- teardown
```

أداة التهيئة ترفض أي اسم خارج النمط الآمن، ولا تستعمل قاعدة التطوير أو الإنتاج.
يجب تنفيذ `teardown` بعد الاختبار حتى عند الفشل.

إذا فشل التثبيت برسالة تخصّ المتصفحات، **أبلغ عن الفشل كما هو** ولا تُخفِه.

## على CI

تعمل بوابة E2E تلقائياً لكل طلب دمج إلى `main`. تنشئ أثناء التشغيل كلمة مرور
تركيبية وقاعدة LocalDB منفصلة، تشغّل ZYNORA محلياً، تفحص رحلة الموظفين والحضور
والإجازات والموافقات والرواتب وCSS الثيم، ثم توقف الخادم وتحذف القاعدة في خطوة
`always()`. لا تعتمد البوابة على أسرار مستودع أو بيئة مشتركة ولا تنشر شيئاً.

## قواعد ثابتة

- لا تُضِف قيماً حقيقية لـ`.env.example`.
- لا تلتزم بملف `.env` (مستثنى).
- لا تضع بيانات موظفين أو رواتب أو أرقام وطنية بأي ملف اختبار.
- بيانات دخول الإنتاج تُدار خارج المستودع بالكامل.
