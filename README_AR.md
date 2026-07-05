
# NEXORA Force Right Sidebar

هذا الباتش يصلح مشكلة ظهور القائمة الجانبية في جهة اليسار، خصوصاً في صفحة مستندات الموظفين.

## التشغيل

انسخ إلى:

`C:\Projects\SmartAttendance`

- `Apply_NEXORA_Force_Right_Sidebar.ps1`
- `files`

ثم شغل:

```powershell
cd C:\Projects\SmartAttendance
powershell -ExecutionPolicy Bypass -File .\Apply_NEXORA_Force_Right_Sidebar.ps1
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
powershell -ExecutionPolicy Bypass -File .\Apply_NEXORA_Force_Right_Sidebar.ps1 -SkipRun
```
