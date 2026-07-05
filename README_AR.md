
# NEXORA Create Employee Cleanup + Active Menu Fix

هذا الباتش يعالج ملاحظتين:

1. عند فتح **إضافة موظف** لا يبقى رابط **قائمة الموظفين** مضيئاً.
2. يرجع صفحة **إضافة موظف جديد** إلى تصميم أنظف وبسيط بدل الشكل الجانبي المزعج.

## الملفات

انسخ هذين إلى داخل مجلد المشروع:

`C:\Users\Lenovo\SmartAttendance`

- `Apply_NEXORA_Create_Cleanup_Active_Menu.ps1`
- `files`

## التشغيل

```powershell
cd C:\Users\Lenovo\SmartAttendance
powershell -ExecutionPolicy Bypass -File .\Apply_NEXORA_Create_Cleanup_Active_Menu.ps1
```

## تطبيق + Build فقط بدون تشغيل

```powershell
powershell -ExecutionPolicy Bypass -File .\Apply_NEXORA_Create_Cleanup_Active_Menu.ps1 -SkipRun
```

## الاختبار بعد التشغيل

1. افتح شؤون الموظفين > إضافة موظف.
2. تأكد أن **إضافة موظف** فقط مضيئة وليس **قائمة الموظفين**.
3. تأكد أن صفحة الإضافة أصبحت أبسط بدون لوحة الجاهزية الجانبية.
4. جرّب زر **اختيار المستمسكات والملفات** وتأكد أن المودال يفتح.
