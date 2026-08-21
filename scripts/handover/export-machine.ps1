<#
.SYNOPSIS
    يجمع كل ما لا يحمله git لنقل النظام إلى جهاز آخر.

.DESCRIPTION
    git يحمل الكود فقط. هذا السكربت يجمع الباقي:
      1. نسخة احتياطية للقاعدة (.bak) — **الطريق الوحيد الموثوق**، لأن هجرات
         النظام لا تبني المخطط من الصفر (17/20 تنجح، وبعض «النجاح» كاذب).
      2. ملفات الإعدادات (appsettings*.json) — فيها أسرار حيّة.
      3. مرفوعات المستخدمين (wwwroot/uploads + wwwroot/tenant-assets).
      4. حالة App_Data وشهادة TLS المحليّة.
      5. سكربت التشغيل وتعريف المهمة المجدولة.
      6. نفق Cloudflare (مجلد .cloudflared) — يربط النطاق zynorahr.com بالحاسبة (أُضيف 2026-08-16).

    ⚠️ البند 4 أُضيف بعد اكتشاف ثغرة فقدِ بيانات: `App_Data/ProtectedEmployeeFiles`
    يحوي وثائق الموظفين **مشفَّرةً بـIDataProtector** (`ProtectedFileService`)،
    ولم تكن تُنقل. الإصدار السابق من هذا السكربت كان يترك تلك الوثائق خلفه.

    لا يعدّل شيئاً على الجهاز الحالي — قراءة ونسخ فقط.

.PARAMETER OutputRoot
    مجلد الحزمة. الافتراضي C:\ZynoraHandover.

.PARAMETER LivePath
    مسار النشر الحيّ. الافتراضي C:\ZynoraPortal.

.PARAMETER SqlInstance
    خادم SQL. الافتراضي localhost.

.PARAMETER Database
    اسم القاعدة. الافتراضي SmartAttendance.

.PARAMETER IncludeImportArchives
    يضمّ أرشيف ملفات الاستيراد (App_Data\AttendanceImports وأخواته, >130 ميغابايت).
    مُستثنى افتراضياً: يمكن إعادة توليده من ملفات المصدر.

.EXAMPLE
    .\export-machine.ps1
#>

[CmdletBinding()]
param(
    [string] $OutputRoot = 'C:\ZynoraHandover',
    [string] $LivePath   = 'C:\ZynoraPortal',
    [string] $SqlInstance = 'localhost',
    [string] $Database   = 'SmartAttendance',
    [switch] $IncludeImportArchives
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Ok   { param([string] $Text) Write-Host "  [OK]   $Text" -ForegroundColor Green }
function Write-Warn { param([string] $Text) Write-Host "  [تنبيه] $Text" -ForegroundColor Yellow }

$stamp  = Get-Date -Format 'yyyyMMdd_HHmmss'
$bundle = Join-Path $OutputRoot "handover_$stamp"
$notes  = New-Object System.Collections.Generic.List[string]

Write-Step "تجهيز الحزمة"
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
Write-Ok "المجلد: $bundle"

# ---------------------------------------------------------------- 1. القاعدة
Write-Step "1/6 نسخة القاعدة الاحتياطية"

# ⚠️ يكتب الملفَ **حسابُ خدمة SQL Server** لا حسابك — وهو ليس عضواً في Users،
# فلا يقدر يكتب بمجلد الحزمة افتراضياً. نمنحه الحقّ صراحةً ليكتب مباشرةً هناك،
# وإلا رجعنا لمجلد SQL الافتراضي (له صلاحية مضمونة عليه) ثم نقلنا الملف.
$bakName = "${Database}_handover_$stamp.bak"
$serviceAccount = (& sqlcmd -S $SqlInstance -E -C -h -1 -W -Q `
    "SET NOCOUNT ON; SELECT TOP 1 CAST(service_account AS varchar(200)) FROM sys.dm_server_services WHERE servicename LIKE N'SQL Server (%';" |
    Where-Object { $_.Trim() } | Select-Object -First 1)

$grantedDirectWrite = $false

if ($serviceAccount) {
    $serviceAccount = $serviceAccount.Trim()
    & icacls $bundle /grant "${serviceAccount}:(OI)(CI)M" /Q 2>&1 | Out-Null
    $grantedDirectWrite = ($LASTEXITCODE -eq 0)

    if ($grantedDirectWrite) { Write-Ok "مُنح $serviceAccount حقّ الكتابة بالحزمة" }
    else { Write-Warn "تعذّر منح الصلاحية (شغّل السكربت كمسؤول لتفادي خطوة يدوية)." }
}

if ($grantedDirectWrite) {
    $sqlBak = Join-Path $bundle $bakName
}
else {
    $sqlBackupDir = (& sqlcmd -S $SqlInstance -E -C -h -1 -W -Q `
        "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS varchar(300));" |
        Where-Object { $_.Trim() } | Select-Object -First 1).Trim()

    if (-not $sqlBackupDir) { throw "تعذّر تحديد مجلد النسخ الاحتياطي لـSQL Server." }

    $sqlBak = Join-Path $sqlBackupDir $bakName
}

# COMPRESSION غير مدعوم على Express — نتركه لتعمل النسخة على كل الإصدارات.
& sqlcmd -S $SqlInstance -E -C -b -Q `
    "BACKUP DATABASE [$Database] TO DISK = N'$sqlBak' WITH INIT, NAME = N'machine handover', STATS = 25;" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "فشلت النسخة الاحتياطية." }

& sqlcmd -S $SqlInstance -E -C -b -Q "RESTORE VERIFYONLY FROM DISK = N'$sqlBak';" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "النسخة الاحتياطية غير سليمة — لا تنقلها." }
Write-Ok "النسخة أُخذت وتحقّقت (RESTORE VERIFYONLY)"

if ($grantedDirectWrite) {
    Write-Ok "كُتبت بالحزمة مباشرةً: $bakName"
}
else {
    # النقل من مجلد SQL يحتاج صلاحية إدارية؛ إن فشل نترك الملف مكانه ونخبر المستخدم.
    try {
        Move-Item -LiteralPath $sqlBak -Destination (Join-Path $bundle $bakName) -Force -ErrorAction Stop
        Write-Ok "نُقلت للحزمة: $bakName"
    }
    catch {
        Write-Warn "تعذّر نقل الـ.bak (صلاحيات). انسخه يدوياً من:"
        Write-Warn "  $sqlBak"
        $notes.Add("الـ.bak بقي في: $sqlBak — انسخه للحزمة يدوياً (يحتاج صلاحية مسؤول).")
    }
}

# ------------------------------------------------------- 2. ملفات الإعدادات
Write-Step "2/6 ملفات الإعدادات (تحوي أسراراً)"
$configDir = Join-Path $bundle 'config'
New-Item -ItemType Directory -Path $configDir -Force | Out-Null

$configCount = 0
foreach ($name in @('appsettings.json', 'appsettings.Production.json', 'appsettings.Development.json')) {
    $source = Join-Path $LivePath $name
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $configDir -Force
        Write-Ok $name
        $configCount++
    }
}

if ($configCount -eq 0) { Write-Warn "لم يُعثر على أي appsettings في $LivePath" }

# ------------------------------------------------------------ 3. المرفوعات
Write-Step "3/6 مرفوعات المستخدمين"

# مجلدٌ واحدٌ لكل شجرة: robocopy /E ينسخ الفروع كما هي.
# ⚠️ tenant-assets **شقيق** uploads لا ابنٌ له — نسخ uploads وحده يترك هوية كل
# شركة (الشعارات والألوان المرفوعة) خلفه فيرجع النظام للهوية الافتراضية.
function Copy-Tree {
    param([string] $Source, [string] $Target, [string] $Label)

    if (-not (Test-Path -LiteralPath $Source)) { Write-Warn "غير موجود: $Source"; return }

    robocopy $Source $Target /E /NFL /NDL /NP /R:2 /W:2 | Out-Null
    # robocopy: 0-7 نجاح، 8+ فشل حقيقي.
    if ($LASTEXITCODE -ge 8) { throw "فشل نسخ $Label (robocopy=$LASTEXITCODE)." }

    $files  = @(Get-ChildItem -LiteralPath $Target -Recurse -File -ErrorAction SilentlyContinue)
    $sizeMb = if ($files.Count -gt 0) { [math]::Round((($files | Measure-Object Length -Sum).Sum) / 1MB, 1) } else { 0 }
    Write-Ok "$Label — $($files.Count) ملفاً ($sizeMb ميغابايت)"
}

Copy-Tree (Join-Path $LivePath 'wwwroot\uploads')       (Join-Path $bundle 'uploads')       'المرفوعات'
Copy-Tree (Join-Path $LivePath 'wwwroot\tenant-assets') (Join-Path $bundle 'tenant-assets') 'هوية الشركات'

# ------------------------------------------- 4. App_Data والشهادة المحليّة
Write-Step "4/6 حالة App_Data والشهادة"

# 🔴 ProtectedEmployeeFiles: وثائق الموظفين مشفَّرةً بـIDataProtector. تركُها
# يعني فقداً نهائياً لا يُسترجَع من القاعدة — الصفوف تشير لملفات غير موجودة.
# ومفاتيح DataProtection نفسها تسكن القاعدة (PersistKeysToDbContext) فتأتي مع
# الـ.bak، لكننا ننسخ مجلد المفاتيح أيضاً: 12 كيلوبايت تُلغي فئة خطرٍ كاملة.
$appDataSource = Join-Path $LivePath 'App_Data'
foreach ($folder in @('ProtectedEmployeeFiles', 'DataProtection-Keys', 'ReportTemplates')) {
    Copy-Tree (Join-Path $appDataSource $folder) (Join-Path $bundle "App_Data\$folder") $folder
}

# AttendanceImports/PageImports ملفاتُ استيرادٍ خام (>130 ميغابايت) — أُعيد
# تشغيلها ممكنٌ من المصدر، فلا تُثقَّل الحزمة بها إلا بطلب صريح.
if ($IncludeImportArchives) {
    foreach ($folder in @('AttendanceImports', 'PageImports', 'PositionImports')) {
        Copy-Tree (Join-Path $appDataSource $folder) (Join-Path $bundle "App_Data\$folder") $folder
    }
}
else {
    Write-Warn 'أرشيف الاستيراد مُستثنى (-IncludeImportArchives لتضمينه).'
}

# شهادة TLS المحليّة: بدونها لا يعمل PWA الموبايل على الشبكة (متطلّب HTTPS).
Copy-Tree (Join-Path $LivePath 'certs') (Join-Path $bundle 'certs') 'شهادة TLS'

# ------------------------------------------------- 5. تشغيل الخادم والمهمة
Write-Step "5/6 إعداد التشغيل"
$runtimeDir = Join-Path $bundle 'runtime'
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null

# run-hidden.vbs هو ما تستدعيه المهمة المجدولة فعلياً (يشغّل الـbat بلا نافذة) —
# غيابه اكتُشف ميدانياً 2026-08-21: مهمة مستوردة تفشل بـ«Can not find script file».
foreach ($runFile in @('run-server.bat', 'run-hidden.vbs')) {
    $runPath = Join-Path $LivePath $runFile
    if (Test-Path -LiteralPath $runPath) {
        Copy-Item -LiteralPath $runPath -Destination $runtimeDir -Force
        Write-Ok $runFile
    }
    else { Write-Warn "غير موجود: $runFile" }
}

# تعريف المهمة المجدولة كـXML — يُستورَد على الجهاز الجديد بأمر واحد.
& schtasks /Query /TN 'ZynoraPortalServer' /XML > (Join-Path $runtimeDir 'ZynoraPortalServer.xml') 2>$null
if ($LASTEXITCODE -eq 0) { Write-Ok 'ZynoraPortalServer.xml' }
else { Write-Warn 'تعذّر تصدير المهمة المجدولة (قد تحتاج صلاحية إدارية).' }

# ------------------------------------------------ 6. نفق Cloudflare (النطاق)
Write-Step "6/6 نفق Cloudflare — ما يربط zynorahr.com بهذه الحاسبة"

# 🔴 أُضيف 2026-08-16 بعد جرد فعلي: النطاق يعمل عبر Cloudflare Tunnel بوضع ملف
# تهيئة (لا توكن). بدون هذا المجلد يعمل النظام على الجديدة محلياً فقط ولا يصله
# أحد من الإنترنت. المعرّف نفسه يعمل على أي حاسبة — لا حاجة لتغيير DNS.
# المجلد أسرار حقيقية (cert.pem + credentials json) — يمنح حامله ربط النطاق.
$cfCandidates = @(
    (Join-Path $env:USERPROFILE '.cloudflared'),
    'C:\Windows\System32\config\systemprofile\.cloudflared',
    'C:\ProgramData\cloudflared'
)
$cfCopied = $false
foreach ($cfDir in $cfCandidates) {
    if ((Test-Path -LiteralPath $cfDir) -and (Test-Path -LiteralPath (Join-Path $cfDir 'config.yml'))) {
        Copy-Tree $cfDir (Join-Path $bundle 'cloudflared') "نفق Cloudflare ($cfDir)"
        $cfCopied = $true
        break
    }
}
if (-not $cfCopied) {
    Write-Warn 'لم يُعثر على مجلد .cloudflared بملف config.yml — إن كان النفق بوضع التوكن فسجّل التوكن من لوحة Cloudflare يدوياً.'
    $notes.Add('نفق Cloudflare لم يُنسخ تلقائياً — انسخ مجلد .cloudflared يدوياً أو أعد تسجيل النفق بالتوكن على الجديدة.')
}
$cfService = Get-CimInstance Win32_Service -Filter "Name='Cloudflared'" -ErrorAction SilentlyContinue
if ($cfService) { Write-Ok "خدمة Cloudflared على هذه الحاسبة: $($cfService.State) — على الجديدة: cloudflared service install" }

# ------------------------------------------------------------------ البيان
Write-Step "بيان الحزمة"
$commit = (& git -C (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) rev-parse --short HEAD 2>$null)
$branch = (& git -C (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) rev-parse --abbrev-ref HEAD 2>$null)

$manifest = @"
# بيان نقل ZYNORA HR

التاريخ: $(Get-Date -Format 'yyyy-MM-dd HH:mm')
الجهاز المصدر: $env:COMPUTERNAME
الفرع: $branch
الكوميت: $commit
القاعدة: $Database على $SqlInstance

## المحتوى
- $bakName ......... نسخة القاعدة (متحقَّقة)
- config/ .......... ملفات الإعدادات ($configCount ملف)
- uploads/ ......... مرفوعات المستخدمين
- tenant-assets/ ... هوية الشركات المرفوعة
- App_Data/ ........ وثائق الموظفين المشفَّرة + مفاتيح الحماية + قوالب التقارير
- certs/ ........... شهادة TLS المحليّة (يلزمها PWA الموبايل)
- runtime/ ......... run-server.bat + تعريف المهمة المجدولة
- cloudflared/ ..... نفق Cloudflare (config.yml + cert.pem + credentials) — يربط zynorahr.com بالحاسبة

## ⚠️ أسرار
config/appsettings.json يحوي سلسلة الاتصال ومفاتيح SMTP وVAPID، وcloudflared/ يمنح
حامله ربط النطاق بأي حاسبة. **لا تُرسل هذه الحزمة ببريد أو محادثة أو تخزين سحابي
عام.** انقلها بذاكرة محمولة أو شبكة موثوقة، واحذفها بعد الاستيراد.
(بيانات الموظفين بالقاعدة وهمية للتجربة — الحساسية بأسرار البنية لا بالبيانات.)

## الاستيراد على الجهاز الجديد
1. git clone للمستودع ثم: git checkout $branch
2. نصّب .NET 10 SDK و SQL Server 17+ (الـ.bak لا يُسترجع على أقدم) و cloudflared
3. شغّل: scripts\handover\import-machine.ps1 -BundlePath "<مسار هذه الحزمة>"
4. النفق: انسخ cloudflared\ إلى %USERPROFILE%\.cloudflared\ ثم: cloudflared service install
   ⚠️ أوقف خدمة Cloudflared على الحاسبة القديمة أولاً — لا تشغّل النفق نفسه على حاسبتين.
5. القائمة الكاملة (20 خطوة + التحقق): docs\MACHINE-MIGRATION-CHECKLIST-2026-08-16.md

$(if ($notes.Count -gt 0) { "## ملاحظات`n" + (($notes | ForEach-Object { "- $_" }) -join "`n") })
"@

$manifestPath = Join-Path $bundle 'MANIFEST.md'
Set-Content -Path $manifestPath -Value $manifest -Encoding utf8
Write-Ok 'MANIFEST.md'

Write-Host "`n" -NoNewline
Write-Host "الحزمة جاهزة: $bundle" -ForegroundColor Green
Write-Warn 'تحوي أسراراً — انقلها بوسيط موثوق واحذفها بعد الاستيراد.'
