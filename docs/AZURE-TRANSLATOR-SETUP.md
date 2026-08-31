# إعداد Azure AI Translator لقاموس ZYNORA

طبقة الترجمة الآلية موجودة داخل صفحة **الإعدادات → قاموس اللغات**، لكنها تبقى
معطلة ما لم تتوفر إعدادات Azure الآمنة. لا تضع المفتاح داخل `appsettings` أو أي
ملف متعقّب في Git.

## إعداد التطوير المحلي

من مجلد `SmartAttendance.Web` خزّن القيم في User Secrets:

```powershell
dotnet user-secrets init
dotnet user-secrets set "LocalizationDictionary:AzureTranslator:Endpoint" "https://api.cognitive.microsofttranslator.com"
dotnet user-secrets set "LocalizationDictionary:AzureTranslator:Region" "<azure-region>"
dotnet user-secrets set "LocalizationDictionary:AzureTranslator:SubscriptionKey" "<secret-key>"
```

إذا أعطى مورد Azure نطاقاً خاصاً فاستخدم قاعدة مسار ترجمة النص بالشكل
`https://<resource>.cognitiveservices.azure.com/translator/text/v3.0` بدلاً من
العنوان العام؛ يضيف التطبيق `/translate` ومعلمات الإصدار تلقائياً.
يمكن ترك `Region` فارغاً فقط عندما تنص إعدادات المورد صراحةً على أنه غير مطلوب.

## إعداد الخادم

استخدم مدير أسرار المنصة أو متغيرات البيئة، مثلاً:

```text
LocalizationDictionary__AzureTranslator__Endpoint
LocalizationDictionary__AzureTranslator__Region
LocalizationDictionary__AzureTranslator__SubscriptionKey
```

لا يُسجل المفتاح أو محتوى العبارات في سجلات التطبيق. تُرسل عبارات واجهة النظام
فقط إلى Azure على دفعات، وتُوسم النتائج **آلي · يحتاج مراجعة** إلى أن يحفظها
المشرف يدوياً أو يستورد نسخة Excel مراجعة.

## التشغيل

1. افتح **الإعدادات → قاموس اللغات**.
2. اختر لغة غير العربية.
3. شغّل **ترجمة الناقص** بدفعات صغيرة أولاً.
4. راجع النتائج الموسومة، ثم احفظ التصحيح لإزالة وسم المراجعة.
5. صدّر Excel للاحتفاظ بمسار مراجعة مستقل قبل استيراد أي نسخة جماعية.
