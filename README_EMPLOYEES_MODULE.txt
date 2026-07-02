Employees Module - SmartAttendance

طريقة الاستخدام:
1) فك ضغط الملف داخل مسار المشروع:
   C:\Projects\SmartAttendance

2) وافق على استبدال Program.cs فقط إذا كنت أكملت Companies + Branches + Departments بنفس النظام السابق.

3) أضف رابط Employees داخل القائمة الجانبية في:
   SmartAttendance.Web/Pages/Shared/_Layout.cshtml

داخل menu أضف السطر التالي بعد Departments:
   <a href="/Employees">Employees</a>

4) نفذ:
   dotnet build

5) شغل المشروع وافتح:
   /Employees

ملاحظة:
إذا قائمة Departments فارغة، أضف Company ثم Branch ثم Department قبل إضافة Employee.
