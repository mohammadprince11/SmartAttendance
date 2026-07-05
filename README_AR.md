
# NEXORA Profile Remove Links + Tabs

هذا الباتش ينظف ملف الموظف من الروابط الزائدة:

- حذف "فتح سجلات الحضور"
- حذف "فتح الطلبات"
- حذف "+ رفع مستند"
- حذف شريط التبويبات:
  - الملخص
  - البيانات
  - الحضور
  - الطلبات
  - المستندات
  - الشفتات
  - السجل

## التشغيل

انسخ إلى:

`C:\Projects\SmartAttendance`

- `Apply_NEXORA_Profile_Remove_Links_Tabs.ps1`
- `files`

ثم شغل:

```powershell
cd C:\Projects\SmartAttendance
powershell -ExecutionPolicy Bypass -File .\Apply_NEXORA_Profile_Remove_Links_Tabs.ps1
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
powershell -ExecutionPolicy Bypass -File .\Apply_NEXORA_Profile_Remove_Links_Tabs.ps1 -SkipRun
```
