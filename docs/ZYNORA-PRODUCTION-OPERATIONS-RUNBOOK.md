# دليل تشغيل ZYNORA للإنتاج

هذا الدليل يُفعّل ما لا يستطيع فرع Git تنفيذه وحده: قبول المالك، النسخ خارج
الموقع، تمرين الاستعادة، المراقبة الخارجية، البريد، وفحص malware. التطبيق
يرفض الإقلاع في بيئة `Production` إذا كانت هذه الإعدادات ناقصة، ما لم يُسجَّل
تجاوز طوارئ صريح `Operations__EnforceProductionReadiness=false`؛ والتجاوز ليس
اعتماد إنتاج.

## 1. القيم التي يقررها المالك

يُسجَّل القرار في تذكرة/محضر ويُستخدم معرّفها في
`Operations__OwnerAcceptanceReference`:

- `RPO`: أقصى عمر مقبول لآخر نسخة سليمة، بالدقائق.
- `RTO`: أقصى زمن مقبول لاستعادة الخدمة، بالدقائق.
- مسار النسخة الخارجية: مشاركة UNC أو قرص/مزود مختلف فعلياً عن قرص الخادم.
- وجهة تنبيه HTTPS مستقلة عن التطبيق.
- محرك ClamAV داخلي؛ المنفذ الافتراضي `3310` ولا يُكشف للإنترنت.

## 2. إعداد البيئة

الأسماء الكاملة موجودة في `.env.example`. القيم الحقيقية والأسرار لا تُكتب في
Git. الحد الأدنى للإنتاج:

```text
Operations__EnforceProductionReadiness=true
Operations__OwnerAcceptanceReference=<ticket-or-signed-record>
Operations__RpoMinutes=<approved-minutes>
Operations__RtoMinutes=<approved-minutes>
Operations__OffsiteBackupPath=<offsite-or-UNC-path>
Operations__BackupHeartbeatPath=<absolute-heartbeat-json-path>
Operations__HealthMonitorUrl=https://<public-host>/health/ready
Operations__AlertWebhookUrl=https://<alert-provider-secret-endpoint>

Smtp__Enabled=true
Smtp__Host=<smtp-host>
Smtp__FromAddress=<sender>

MalwareScanning__Enabled=true
MalwareScanning__Required=true
MalwareScanning__Host=<internal-clamav-host>
MalwareScanning__Port=3310
MalwareScanning__TimeoutSeconds=30
```

نفّذ فحصاً بلا كتابة قبل أي نشر:

```powershell
.\scripts\deploy\Publish-Zynora.ps1 `
  -SitePath C:\ZynoraPortal `
  -RepoPath <repository-root> `
  -CheckOnly
```

## 3. النسخ الدوري خارج الموقع

`scripts/operations/Backup-Zynora.ps1` ينفذ بالتسلسل:

1. `BACKUP DATABASE ... WITH CHECKSUM`؛
2. `RESTORE VERIFYONLY WITH CHECKSUM`؛
3. نسخ إلى مسار خارجي؛
4. مطابقة SHA-256؛
5. كتابة نبضة نجاح atomically؛
6. حذف ملفات انتهت مدة احتفاظها ضمن جذور مقيدة فقط.

مثال يدوي أولي، بعد استبدال القيم:

```powershell
.\scripts\operations\Backup-Zynora.ps1 `
  -SqlServer <server> `
  -Database <database> `
  -LocalBackupRoot D:\ZynoraBackups `
  -OffsiteBackupRoot \\backup-host\zynora `
  -HeartbeatPath C:\ZynoraPortal\App_Data\operations\last-backup.json `
  -RpoMinutes 60 `
  -RetentionDays 30
```

بعد نجاح التشغيل اليدوي تُنشأ مهمة Windows Task Scheduler بفاصل لا يتجاوز
`RPO`. حساب المهمة يحتاج صلاحية `BACKUP DATABASE` والكتابة للمسارين فقط. نقطة
`/health/ready` تصبح غير سليمة تلقائياً إذا تجاوز عمر النبضة قيمة RPO.

## 4. تمرين الاستعادة

يشغّل `Test-ZynoraRestore.ps1` استعادة فعلية لآخر نسخة داخل قاعدة اسمها محروس
`SmartAttendance_RestoreDrill_*`، يتحقق من جداولها، ثم يحذف قاعدة التمرين في
`finally`. لا يقبل قاعدة هدف يحددها المستخدم.

```powershell
.\scripts\operations\Test-ZynoraRestore.ps1 `
  -SqlServer <server> `
  -Database <database> `
  -BackupRoot \\backup-host\zynora
```

يُجرى شهرياً وبعد أي تغيير جوهري بالتخزين. يُقارن الزمن الفعلي بـRTO ويُرفق
السجل بتذكرة القبول.

## 5. المراقبة والتنبيه

`Monitor-Zynora.ps1` تشغيل أحادي مناسب لمهمة مجدولة كل دقيقة. يحتفظ بعداد
الإخفاقات ويرسل إنذاراً بعد ثلاث نتائج متتالية، ثم رسالة تعافٍ عند عودة الصحة.

```powershell
.\scripts\operations\Monitor-Zynora.ps1 `
  -HealthUrl https://<public-host>/health/ready `
  -AlertWebhookUrl https://<alert-provider-secret-endpoint> `
  -StatePath C:\ZynoraOperations\monitor-state.json `
  -ConsecutiveFailures 3
```

يجب أن تعمل مهمة المراقبة من جهاز/عامل مستقل عن خادم ZYNORA؛ تشغيلها على نفس
الخادم لا يكشف سقوط الخادم نفسه أو انقطاع الشبكة عنه.

## 6. فحص الملفات

كل الكتابات إلى مخزن الملفات المحمي تمر عبر فحص التوقيع ثم ClamAV قبل الحفظ.
في الإنتاج تكون السياسة `Required=true`: التهديد، الخطأ، المهلة أو غياب المحرك
كلها تمنع الحفظ. تُحفظ الملفات النظيفة فقط خارج `wwwroot` وتُقرأ عبر روابط
موقعة وصلاحيات الموظف/الشركة.

## 7. دليل القبول

لا يتحول Draft PR إلى Ready ولا يتم الدمج أو النشر حتى تتوفر الأدلة التالية:

- مرجع قبول المالك وقواعد الأعمال؛
- تشغيل نسخة يدوية سليمة ووجودها خارج الموقع؛
- تمرين استعادة ضمن RTO؛
- إنذار تجريبي ورسالة تعافٍ من المراقب المستقل؛
- اختبار EICAR محجوب من ClamAV وملف نظيف مقبول؛
- فحص النشر `-CheckOnly` أخضر؛
- بوابات CI الأربع خضراء.
