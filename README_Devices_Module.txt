SmartAttendance Devices Module
================================

طريقة التثبيت:

1) فك الضغط داخل جذر المشروع:
   C:\Projects\SmartAttendance

2) تأكد من وجود الرابط داخل:
   SmartAttendance.Web/Pages/Shared/_Layout.cshtml

   أضف هذا السطر داخل قائمة menu إذا لم يكن موجودًا:
   <a href="/Devices">Devices</a>

3) أوقف تشغيل المشروع إذا كان شغالًا:
   Shift + F5
   أو:
   taskkill /F /IM SmartAttendance.Web.exe
   taskkill /F /IM dotnet.exe

4) نفذ:
   dotnet build

5) شغل المشروع وافتح:
   /Devices

المحتويات:
- Device ViewModels
- DeviceProfile
- IDeviceService
- DeviceService
- Pages/Devices: Index / Create / Edit / Delete
- Program.cs محدث لتسجيل DeviceProfile و IDeviceService
