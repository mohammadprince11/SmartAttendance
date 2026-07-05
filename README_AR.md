
# NEXORA Employees Table Modern Rebuild

هذا الباتش يعيد تصميم جدول قائمة الموظفين بشكل حديث.

## شنو يصلح؟

- إزالة شكل Scrollbar الأبيض القديم.
- Scrollbar داكن بتدرج NEXORA.
- هيدر جدول ثابت وحديث.
- صفوف أنظف.
- زر "ملف" بتصميم حديث.
- تحسين عرض الأعمدة.
- منع الشكل القديم الضيق.

## التشغيل

انسخ إلى:

`C:\Projects\SmartAttendance`

- `Apply_NEXORA_Employees_Table_Modern_Rebuild.ps1`
- `files`

ثم شغل:

```powershell
cd C:\Projects\SmartAttendance
powershell -ExecutionPolicy Bypass -File .\Apply_NEXORA_Employees_Table_Modern_Rebuild.ps1
```

## أوامر منفصلة بعد السكربت

```powershell
taskkill /F /IM SmartAttendance.Web.exe
taskkill /F /IM dotnet.exe

dotnet clean
dotnet build
dotnet run --project SmartAttendance.Web
```

## تطبيق + Build فقط

```powershell
powershell -ExecutionPolicy Bypass -File .\Apply_NEXORA_Employees_Table_Modern_Rebuild.ps1 -SkipRun
```
