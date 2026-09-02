# ZynoraHR Mobile

تطبيق موظفي ZYNORA HR المؤقت، مبني بـ .NET MAUI ويغلف بوابة الموظف المنشورة مع معالجة الاتصال، شاشة تحميل، وحصر التنقل داخل نطاق ZynoraHR الموثوق.

## الهوية

- Display name: `ZYNORA HR`
- Android package / iOS bundle: `com.zynorahr.employee`
- Start URL: `https://zynorahr.com/EmployeePortal`

## البناء

```powershell
dotnet restore ZynoraHR.Mobile/ZynoraHR.Mobile.csproj
dotnet build ZynoraHR.Mobile/ZynoraHR.Mobile.csproj -f net10.0-android -c Debug
dotnet publish ZynoraHR.Mobile/ZynoraHR.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormats=apk
```

ملفا Android SDK وOpenJDK المحليان محفوظان تحت `.mobile-tools` ويُكتشفان تلقائياً من ملف المشروع. لا يدخل هذا المجلد إلى Git.

يبنى هدف iOS من جهاز macOS بعد تثبيت workload الخاص بـ iOS وإضافة شهادات Apple والتوقيع.

## الخطوات التالية

1. جسر Android/iOS أصلي للموقع والبصمة والكاميرا واختيار الملفات.
2. إشعارات FCM وAPNs بدلاً من Web Push داخل الغلاف.
3. تخزين الجلسة وتسجيل الجهاز بصورة مشفرة.
4. استبدال الشاشات ذات الأولوية بواجهات MAUI أصلية تدريجياً.
