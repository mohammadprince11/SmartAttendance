<#
.SYNOPSIS
    ينصّب حزمة النقل على الجهاز الجديد.

.DESCRIPTION
    يفحص المتطلبات، يسترجع القاعدة، ويعيد ملفات الإعدادات والمرفوعات لمكانها.

    ⚠️ **لا يبني القاعدة من الهجرات عمداً.** جُرّب ذلك وفشل: 17 من 20 هجرة
    تنجح، وبعض «النجاح» كاذب لأن كل هجرة BEGIN TRAN…COMMIT بلا XACT_ABORT
    فالجملة تفشل والمعاملة تُكمَل وتُسجَّل الهجرة كمطبَّقة. النتيجة مخطط ناقص
    يُخفي نقصه. الاسترجاع من .bak هو الطريق الموثوق الوحيد.

.PARAMETER BundlePath
    مسار حزمة النقل (مجلد handover_*).

.PARAMETER LivePath
    مسار النشر على الجهاز الجديد. الافتراضي C:\ZynoraPortal.

.PARAMETER WhatIfOnly
    يفحص ويعرض الخطة بلا تنفيذ.

.EXAMPLE
    .\import-machine.ps1 -BundlePath D:\handover_20260803_190000 -WhatIfOnly
    .\import-machine.ps1 -BundlePath D:\handover_20260803_190000
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BundlePath,

    [string] $LivePath    = 'C:\ZynoraPortal',
    [string] $SqlInstance = 'localhost',
    [string] $Database    = 'SmartAttendance',
    [switch] $WhatIfOnly
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Ok   { param([string] $Text) Write-Host "  [OK]   $Text" -ForegroundColor Green }
function Write-Bad  { param([string] $Text) Write-Host "  [ناقص] $Text" -ForegroundColor Red }
function Write-Warn { param([string] $Text) Write-Host "  [تنبيه] $Text" -ForegroundColor Yellow }

if (-not (Test-Path -LiteralPath $BundlePath)) { throw "الحزمة غير موجودة: $BundlePath" }

# ------------------------------------------------------------ فحص المتطلبات
Write-Step 'فحص المتطلبات'
$blockers = New-Object System.Collections.Generic.List[string]

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $sdks = & dotnet --list-sdks 2>$null
    if ($sdks -match '^10\.') { Write-Ok '.NET 10 SDK' }
    else { Write-Bad '.NET 10 SDK غير مثبّت'; $blockers.Add('.NET 10 SDK') }
}
else { Write-Bad 'dotnet غير موجود'; $blockers.Add('.NET SDK') }

$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
if ($sqlcmd) { Write-Ok 'sqlcmd' } else { Write-Bad 'sqlcmd غير موجود'; $blockers.Add('SQL Server / sqlcmd') }

if ($sqlcmd) {
    & sqlcmd -S $SqlInstance -E -C -b -Q 'SELECT 1;' 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Ok "الاتصال بـ$SqlInstance" }
    else { Write-Bad "تعذّر الاتصال بـ$SqlInstance"; $blockers.Add("اتصال SQL ($SqlInstance)") }
}

$bak = Get-ChildItem -LiteralPath $BundlePath -Filter '*.bak' -File -ErrorAction SilentlyContinue |
       Select-Object -First 1
if ($bak) { Write-Ok "نسخة القاعدة: $($bak.Name)" }
else { Write-Bad 'لا توجد نسخة .bak بالحزمة'; $blockers.Add('ملف .bak') }

$configDir  = Join-Path $BundlePath 'config'
$uploadsDir = Join-Path $BundlePath 'uploads'
$runtimeDir = Join-Path $BundlePath 'runtime'

if (Test-Path -LiteralPath (Join-Path $configDir 'appsettings.json')) { Write-Ok 'appsettings.json' }
else { Write-Warn 'appsettings.json غير موجود — ستحتاج إعداده يدوياً' }

# حزمةٌ أُنشئت بالإصدار القديم من export لا تحمل الوثائق المشفَّرة. الاسترجاع
# ينجح ظاهرياً ثم تكتشف الضياع بعد حذف الحزمة — فالتنبيه هنا لا بعدها.
if (Test-Path -LiteralPath (Join-Path $BundlePath 'App_Data\ProtectedEmployeeFiles')) {
    Write-Ok 'وثائق الموظفين المشفَّرة'
}
else {
    Write-Warn 'الحزمة بلا App_Data\ProtectedEmployeeFiles — وثائق الموظفين لن تُنقل.'
    Write-Warn 'إن كانت الحزمة من إصدارٍ قديم للتصدير: أعِد التصدير قبل إخلاء الجهاز.'
}

# نفق Cloudflare هو ما يربط النطاق العام (zynorahr.com) بالحاسبة. حزمة بلاه
# تعني نظاماً يعمل محلياً فقط بعد الاستيراد — تنبيهٌ الآن لا اكتشافٌ بعد الإخلاء.
if (Test-Path -LiteralPath (Join-Path $BundlePath 'cloudflared\config.yml')) {
    Write-Ok 'نفق Cloudflare (cloudflared/)'
}
else {
    Write-Warn 'الحزمة بلا cloudflared\config.yml — النطاق العام لن يُربط تلقائياً.'
    Write-Warn 'انسخ مجلد .cloudflared من الحاسبة القديمة يدوياً، أو أعد تسجيل النفق بالتوكن.'
}

if ($blockers.Count -gt 0) {
    Write-Host "`nناقص قبل المتابعة:" -ForegroundColor Red
    $blockers | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw 'المتطلبات غير مكتملة.'
}

if ($WhatIfOnly) {
    Write-Host "`nالفحص فقط — لم يُنفَّذ شيء. أعِد التشغيل بلا -WhatIfOnly." -ForegroundColor Yellow
    return
}

# ------------------------------------------------------- 1. استرجاع القاعدة
Write-Step '1/4 استرجاع القاعدة'

$exists = (& sqlcmd -S $SqlInstance -E -C -h -1 -W -Q `
    "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$Database';" |
    Where-Object { $_.Trim() } | Select-Object -First 1).Trim()

if ($exists -ne '0') {
    Write-Warn "القاعدة [$Database] موجودة أصلاً وسيُستبدَل محتواها بالكامل."
    $answer = Read-Host 'اكتب YES للمتابعة'
    if ($answer -cne 'YES') { throw 'أُلغي بطلب المستخدم.' }

    & sqlcmd -S $SqlInstance -E -C -b -Q `
        "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" | Out-Null
}

& sqlcmd -S $SqlInstance -E -C -b -Q "RESTORE VERIFYONLY FROM DISK = N'$($bak.FullName)';" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "النسخة الاحتياطية تالفة — لا تُسترجَع." }

& sqlcmd -S $SqlInstance -E -C -b -Q `
    "RESTORE DATABASE [$Database] FROM DISK = N'$($bak.FullName)' WITH REPLACE, STATS = 25;" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'فشل الاسترجاع.' }

& sqlcmd -S $SqlInstance -E -C -b -Q "ALTER DATABASE [$Database] SET MULTI_USER;" 2>$null | Out-Null

$tables = (& sqlcmd -S $SqlInstance -d $Database -E -C -h -1 -W -Q `
    'SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables;' |
    Where-Object { $_.Trim() } | Select-Object -First 1).Trim()
Write-Ok "استُرجعت — $tables جدولاً"

# --------------------------------------------------- 2. الإعدادات والمرفوعات
Write-Step '2/4 الإعدادات والمرفوعات'
New-Item -ItemType Directory -Path $LivePath -Force | Out-Null

if (Test-Path -LiteralPath $configDir) {
    Get-ChildItem -LiteralPath $configDir -Filter 'appsettings*.json' -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $LivePath -Force
        Write-Ok $_.Name
    }
    Write-Warn 'راجع سلسلة الاتصال في appsettings.json إن اختلف اسم خادم SQL.'
}

function Restore-Tree {
    param([string] $Source, [string] $Target, [string] $Label)

    if (-not (Test-Path -LiteralPath $Source)) { return }

    robocopy $Source $Target /E /NFL /NDL /NP /R:2 /W:2 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "فشل نسخ $Label (robocopy=$LASTEXITCODE)." }
    Write-Ok $Label
}

Restore-Tree $uploadsDir (Join-Path $LivePath 'wwwroot\uploads') 'المرفوعات'
Restore-Tree (Join-Path $BundlePath 'tenant-assets') (Join-Path $LivePath 'wwwroot\tenant-assets') 'هوية الشركات'
Restore-Tree (Join-Path $BundlePath 'certs') (Join-Path $LivePath 'certs') 'شهادة TLS'

# App_Data يُنسخ فرعاً فرعاً كما جُمِع — ProtectedEmployeeFiles أهمّها: بدونه
# تشير صفوف الوثائق بالقاعدة لملفات غير موجودة، ولا سبيل لاسترجاعها.
$bundleAppData = Join-Path $BundlePath 'App_Data'
if (Test-Path -LiteralPath $bundleAppData) {
    Get-ChildItem -LiteralPath $bundleAppData -Directory | ForEach-Object {
        Restore-Tree $_.FullName (Join-Path $LivePath "App_Data\$($_.Name)") "App_Data\$($_.Name)"
    }
}

if (Test-Path -LiteralPath (Join-Path $runtimeDir 'run-server.bat')) {
    Copy-Item -LiteralPath (Join-Path $runtimeDir 'run-server.bat') -Destination $LivePath -Force
    Write-Ok 'run-server.bat'
}

# ------------------------------------------------------- 3. نفق Cloudflare
Write-Step '3/4 نفق Cloudflare (ربط النطاق بهذه الحاسبة)'

# التصدير يحمل مجلد .cloudflared (config.yml + cert.pem + credentials) منذ
# 2026-08-16. نعيده لمجلد المستخدم هنا — أما تثبيت الخدمة فيبقى يدوياً عمداً:
# النفق نفسه على حاسبتين معاً يوزّع الطلبات بينهما بلا قصد، فلا نشغّله قبل أن
# يؤكّد المستخدم إيقافه على القديمة.
$bundleCf = Join-Path $BundlePath 'cloudflared'
if (Test-Path -LiteralPath (Join-Path $bundleCf 'config.yml')) {
    $cfTarget  = Join-Path $env:USERPROFILE '.cloudflared'
    $proceedCf = $true

    if (Test-Path -LiteralPath (Join-Path $cfTarget 'config.yml')) {
        Write-Warn "يوجد $cfTarget بملف config.yml أصلاً وسيُستبدَل محتواه."
        $answer = Read-Host 'اكتب YES للاستبدال'
        if ($answer -cne 'YES') { $proceedCf = $false; Write-Warn 'تُرك النفق الحالي كما هو.' }
    }

    if ($proceedCf) {
        Restore-Tree $bundleCf $cfTarget 'نفق Cloudflare'
        if (-not (Get-Command cloudflared -ErrorAction SilentlyContinue)) {
            Write-Warn 'cloudflared غير مثبَّت على هذه الحاسبة — ثبّته قبل خطوة service install.'
        }
    }
}
else {
    Write-Warn 'الحزمة بلا cloudflared/ — تخطّيتُ ربط النطاق (النظام سيعمل محلياً فقط).'
}

# ------------------------------------------------------------ 4. ما بقي عليك
Write-Step '4/4 الخطوات المتبقية (يدوية عمداً)'

Write-Host @"
  1) انشر التطبيق من مجلد المستودع:
       dotnet publish SmartAttendance.Web\SmartAttendance.Web.csproj -c Release -o "$LivePath"
     ثم أعِد نسخ appsettings.json إن دهسه النشر.

  2) سجّل المهمة المجدولة (يحتاج صلاحية إدارية):
       schtasks /Create /TN "ZynoraPortalServer" /XML "$runtimeDir\ZynoraPortalServer.xml"

  3) شغّل وتحقّق:
       schtasks /Run /TN "ZynoraPortalServer"
       ثم افتح http://localhost:5080

  4) النفق (بعد استرجاع cloudflared/ أعلاه):
       ⚠️ أوقف خدمة Cloudflared على الحاسبة القديمة أولاً — لا تشغّل النفق
       نفسه على حاسبتين معاً. ثم هنا:
       cloudflared service install
       Start-Service Cloudflared
       وتحقّق بفتح https://zynorahr.com من خارج الشبكة.

  5) بعد نجاح الدخول وفتح وثيقة موظف فعليّة: **احذف حزمة النقل** — فيها أسرار حيّة.
"@ -ForegroundColor Gray

Write-Host "`nالاستيراد اكتمل." -ForegroundColor Green
