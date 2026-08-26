<#
.SYNOPSIS
    نشر ZYNORA HR على خادم الويندوز بالإجراء الآمن المعتمد.

.DESCRIPTION
    يُنفّذ الخطوات الستّ بترتيبها الملزم:
      ١ نسخة رجوع للملفات   ٢ نسخة رجوع للقاعدة   ٣ إيقاف المهمة وحلقة run-server
      ٤ نشر كامل            ٥ نسخ يستبعد الإعدادات ٦ تشغيل ثم **قياس** لا افتراض

    السكربت مصمَّم ليفشل مبكراً وبصوتٍ عالٍ:
      • لا يبدأ بلا موافقة صريحة لهذه العملية بالذات (`-Confirm`).
      • لا يلمس شيئاً قبل أن تنجح نسختا الرجوع — الملفات **والقاعدة**.
      • يتحقّق أن الموقع قام فعلاً؛ وإن لم يقم يرجع تلقائياً لنسخة الملفات.

    ⚠️ الرجوع التلقائيّ يستعيد **الملفات وحدها**. هجرات المخطط التي طُبِّقت عند
    الإقلاع تبقى مطبَّقة — ولهذا نسخة القاعدة إلزامية لا اختيارية.

.PARAMETER TaskName
    اسم المهمة المجدولة التي تشغّل الموقع (حلقة run-server).

.PARAMETER Database
    اسم قاعدة البيانات المراد نسخها. يُتجاوَز بـ`-SkipDbBackup` وحده، ومعه تحذير.

.EXAMPLE
    .\Publish-Zynora.ps1 -TaskName "ZynoraPortal" -SqlServer "." -Database "ZynoraHR" -Confirm

.NOTES
    شغّله من جذر المستودع بصلاحيات إدارية، والفرع المطلوب مسحوبٌ ومحدَّث.
#>
[CmdletBinding()]
param(
    # مطلوب للنشر، لا للفحص وحده (-CheckOnly) — يُتحقق منه بعد تحديد الوضع.
    [string] $TaskName,

    [string] $SitePath   = 'C:\ZynoraPortal',
    [string] $RepoPath   = (Get-Location).Path,
    [string] $BackupRoot = 'C:\ZynoraPortal-Backups',
    [string] $PublishDir = 'C:\ZynoraPortal-publish',

    [string] $SqlServer,
    [string] $Database,

    [int]    $Port            = 5080,
    [int]    $StartupTimeoutSeconds = 120,

    # النشر فعلٌ لا رجعة فيه على قاعدةٍ يشترك فيها التطوير والإنتاج — فلا يبدأ بلا نيّة صريحة.
    # الاسم `Approve` لا `Confirm`: الأخير محجوز بـPowerShell لمنظومة ShouldProcess.
    [switch] $Approve,

    # مخرجٌ صريح لبيئةٍ تُنسخ قاعدتها بوسيلة أخرى. يُسجَّل تحذيراً ولا يمرّ صامتاً.
    [switch] $SkipDbBackup,

    # يفحص البيئة والإعدادات ويخرج — بلا نسخ ولا إيقاف ولا نشر.
    # لا يحتاج -Approve ولا نسخة قاعدة: لا يكتب شيئاً.
    [switch] $CheckOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$stamp     = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $BackupRoot "files-$stamp"
$dbBackup  = Join-Path $BackupRoot "db-$stamp.bak"

function Write-Step { param([string] $Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Ok   { param([string] $Text) Write-Host "  [ok] $Text"    -ForegroundColor Green }
function Write-Warn { param([string] $Text) Write-Host "  [!]  $Text"    -ForegroundColor Yellow }

<#
.SYNOPSIS
    يُسكِت كل ما يشغّل الموقع فعلاً — لا العملية وحدها.

.DESCRIPTION
    إيقاف المهمة المجدولة لا يكفي: `run-server.bat` حلقةٌ لا نهائية تعيد إطلاق
    التطبيق كل ٣ ثوانٍ، وهي تعمل بـ`cmd.exe` **من System32** لا من مجلّد الموقع —
    فالفلترة بمسار العملية وحدها تتركها حيّة، فتُقيم الموقع وسط النسخ ويعلق
    `robocopy` على ملفاتٍ مقفولة (أوقف نشرَي 2026-08-07 دقائق بلا تقدّم).

    فيُقتل صنفان: ما يعمل **من** مجلّد الموقع، وما يذكر مجلّد الموقع **بسطر
    أوامره** (cmd/wscript/timeout). والقيد على السطر يمنع إسقاط تطبيقٍ آخر بالخادم.
#>
function Stop-SiteProcesses {
    param([Parameter(Mandatory)][string] $SitePath)

    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($SitePath, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Write-Host "  قتل PID $($_.Id) ($($_.ProcessName))"; Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

    Get-CimInstance Win32_Process -Filter "Name='cmd.exe' OR Name='wscript.exe' OR Name='timeout.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($SitePath, [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
        ForEach-Object { Write-Host "  قتل حلقة PID $($_.ProcessId) ($($_.Name))"; Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

# ─────────────────────────────────────────────────────────────────────────────
# ٠) شروط البدء
# ─────────────────────────────────────────────────────────────────────────────
if (-not $Approve -and -not $CheckOnly) {
    throw @'
النشر يحتاج موافقة صريحة لهذه العملية بالذات — أعد التشغيل بـ-Approve.
موافقةٌ سابقة على نشرٍ سابق لا تُعمَّم على هذا.
(للفحص وحده بلا نشر: -CheckOnly)
'@
}

if (-not $CheckOnly -and -not $TaskName) {
    throw 'النشر يحتاج -TaskName (اسم المهمة المجدولة التي تشغّل الموقع).'
}

if (-not $CheckOnly -and -not $SkipDbBackup -and (-not $Database -or -not $SqlServer)) {
    throw @'
نسخة القاعدة إلزامية: مرّر -SqlServer و-Database.
السبب: هجرات المخطط تُطبَّق عند أول إقلاع، والرجوع بالملفات لا يبطلها.
إن كنت تنسخ القاعدة بوسيلة أخرى فمرّر -SkipDbBackup صراحةً.
'@
}

if (-not (Test-Path $SitePath)) { throw "مجلّد الموقع غير موجود: $SitePath" }
if (-not (Test-Path (Join-Path $RepoPath 'SmartAttendance.slnx'))) {
    throw "لم أجد SmartAttendance.slnx تحت $RepoPath — شغّل السكربت من جذر المستودع أو مرّر -RepoPath."
}

Write-Step 'ما سيُنشر'
Push-Location $RepoPath
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
$head   = (git rev-parse --short HEAD).Trim()
$dirty  = (git status --porcelain)
Pop-Location

Write-Host "  الفرع: $branch @ $head"
if ($dirty) { Write-Warn 'شجرة العمل غير نظيفة — ستُنشر تعديلات غير مُودعة.' }

# تحذير الهجرات: نعرضه من الكود لا من الذاكرة، فلا يتقادم النصّ.
$migrator = Join-Path $RepoPath 'SmartAttendance.Web\Infrastructure\Hrms\SqlSchemaMigrator.cs'
if (Test-Path $migrator) {
    $ids = Select-String -Path $migrator -Pattern '"\d{8}-\d{2}-[a-z0-9-]+"' -AllMatches |
           ForEach-Object { $_.Matches } | ForEach-Object { $_.Value.Trim('"') }
    Write-Warn "هجرات المخطط بالكود: $($ids.Count). ستُطبَّق غير المسجَّلة منها بأول إقلاع."
}

# ─────────────────────────────────────────────────────────────────────────────
# ٠٫٥) فحص ما قبل الإقلاع — أرخص من نشرٍ يرفض الإقلاع ثم رجوع
#
# `CookieSecurityPolicy` يرمي عند الإقلاع إن كانت البيئة Production بلا إثبات
# TLS. المصيدة أن `launchSettings.json` (الذي يضبط Development) **ملف تطوير لا
# يُنسخ لمخرَج publish**، فالنشر المُصدَّر بلا متغيّر بيئة يُقلع افتراضاً على
# Production — أي أن الوضع الافتراضيّ تماماً هو الوضع الذي يُرفض.
#
# نحاكي القرار هنا بنفس منطق CookieSecurityPolicy.Evaluate قبل لمس أي شيء.
# ─────────────────────────────────────────────────────────────────────────────
Write-Step '٠٫٥) فحص ما قبل الإقلاع'

function Get-ConfigFlag {
    param([object] $Json, [string[]] $Path)
    $node = $Json
    foreach ($seg in $Path) {
        if ($null -eq $node) { return $false }
        $prop = $node.PSObject.Properties[$seg]
        if (-not $prop) { return $false }
        $node = $prop.Value
    }
    return [bool] $node
}

# ترتيب ASP.NET Core: متغيّر العملية ← المستخدم ← الجهاز. وغيابها كلها ⟹ Production.
$aspnetEnv = $env:ASPNETCORE_ENVIRONMENT
if (-not $aspnetEnv) { $aspnetEnv = [Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT','Machine') }
if (-not $aspnetEnv) {
    $aspnetEnv = 'Production'
    Write-Warn 'ASPNETCORE_ENVIRONMENT غير مضبوط — ASP.NET Core يفترض Production.'
}
Write-Host "  البيئة: $aspnetEnv"

$forceHttps = $false; $revProxy = $false; $allowInsecure = $false
$loadedConfigs = @()
foreach ($name in @('appsettings.json', "appsettings.$aspnetEnv.json")) {
    $file = Join-Path $SitePath $name
    if (-not (Test-Path $file)) { continue }
    try {
        $cfg = Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        Write-Warn "تعذّرت قراءة $name كـJSON — أتخطّاه بالفحص."
        continue
    }
    $loadedConfigs += $cfg
    # اللاحق يغلب السابق، تماماً كترتيب ASP.NET Core.
    if (Get-ConfigFlag $cfg @('ForceHttps'))                      { $forceHttps    = $true }
    if (Get-ConfigFlag $cfg @('ReverseProxy','Enabled'))           { $revProxy      = $true }
    if (Get-ConfigFlag $cfg @('Security','AllowInsecureCookies'))  { $allowInsecure = $true }
    Write-Host "  قُرئ: $name"
}

Write-Host "  ForceHttps=$forceHttps · ReverseProxy:Enabled=$revProxy · Security:AllowInsecureCookies=$allowInsecure"

$isProd = ($aspnetEnv -eq 'Production')
if ($isProd -and -not $forceHttps -and -not $revProxy -and -not $allowInsecure) {
    throw @"
سيرفض الموقع الإقلاع بعد النشر — أُوقفه الآن قبل أن ألمس شيئاً.

البيئة Production ولا إثبات على TLS، فكوكي الجلسة ستُصدَر بلا راية Secure.
اضبط **واحداً** بـ$SitePath\appsettings.json ثم أعد التشغيل:

  • نشر داخليّ على HTTP (حالتك الأرجح):
      "Security": { "AllowInsecureCookies": true }

  • التطبيق نفسه يستقبل HTTPS:
      "ForceHttps": true

  • خلف وسيط عكسيّ موثوق ينهي TLS (لا تضبطه بلا وسيط فعليّ —
    يجعل التطبيق يثق بترويسات X-Forwarded المُزوَّرة بسهولة):
      "ReverseProxy": { "Enabled": true }
"@
}

# نفس أولوية الإعداد الفعلية: JSON الأساسي ثم JSON البيئة ثم متغيّر البيئة.
# لا نطبع قيمة AlertWebhook أو كلمات المرور كي لا تتسرّب الأسرار في سجل النشر.
function Get-NestedConfigValue {
    param([object] $Json, [string[]] $Path)
    $node = $Json
    foreach ($segment in $Path) {
        if ($null -eq $node) { return $null }
        $property = $node.PSObject.Properties[$segment]
        if (-not $property) { return $null }
        $node = $property.Value
    }
    return $node
}

function Resolve-ZynoraSetting {
    param([string[]] $Path)
    $value = $null
    foreach ($config in $loadedConfigs) {
        $candidate = Get-NestedConfigValue $config $Path
        if ($null -ne $candidate) { $value = $candidate }
    }

    $environmentName = $Path -join '__'
    foreach ($target in @('Machine', 'User', 'Process')) {
        $candidate = [Environment]::GetEnvironmentVariable($environmentName, $target)
        if (-not [string]::IsNullOrWhiteSpace($candidate)) { $value = $candidate }
    }
    return $value
}

function Test-TrueValue([object] $Value) {
    if ($Value -is [bool]) { return $Value }
    return "$Value".Equals('true', [StringComparison]::OrdinalIgnoreCase)
}

if ($isProd) {
    $enforceOperations = Resolve-ZynoraSetting @('Operations', 'EnforceProductionReadiness')
    if ($null -eq $enforceOperations) { $enforceOperations = $true }

    if (Test-TrueValue $enforceOperations) {
        $missing = [Collections.Generic.List[string]]::new()

        foreach ($entry in @(
            @{ Path = @('Operations','OwnerAcceptanceReference'); Label = 'Operations:OwnerAcceptanceReference' },
            @{ Path = @('Operations','OffsiteBackupPath');        Label = 'Operations:OffsiteBackupPath' },
            @{ Path = @('Operations','BackupHeartbeatPath');      Label = 'Operations:BackupHeartbeatPath' },
            @{ Path = @('Operations','HealthMonitorUrl');         Label = 'Operations:HealthMonitorUrl' },
            @{ Path = @('Operations','AlertWebhookUrl');          Label = 'Operations:AlertWebhookUrl' },
            @{ Path = @('Smtp','Host');                           Label = 'Smtp:Host' },
            @{ Path = @('Smtp','FromAddress');                    Label = 'Smtp:FromAddress' },
            @{ Path = @('MalwareScanning','Host');                Label = 'MalwareScanning:Host' }
        )) {
            if ([string]::IsNullOrWhiteSpace("$(Resolve-ZynoraSetting $entry.Path)")) {
                $missing.Add($entry.Label)
            }
        }

        foreach ($entry in @(
            @{ Path = @('Operations','RpoMinutes'); Label = 'Operations:RpoMinutes' },
            @{ Path = @('Operations','RtoMinutes'); Label = 'Operations:RtoMinutes' },
            @{ Path = @('MalwareScanning','Port');  Label = 'MalwareScanning:Port' }
        )) {
            $number = 0
            if (-not [int]::TryParse("$(Resolve-ZynoraSetting $entry.Path)", [ref]$number) -or $number -le 0) {
                $missing.Add($entry.Label)
            }
        }

        foreach ($entry in @(
            @{ Path = @('Smtp','Enabled');               Label = 'Smtp:Enabled=true' },
            @{ Path = @('MalwareScanning','Enabled');    Label = 'MalwareScanning:Enabled=true' },
            @{ Path = @('MalwareScanning','Required');   Label = 'MalwareScanning:Required=true' }
        )) {
            if (-not (Test-TrueValue (Resolve-ZynoraSetting $entry.Path))) {
                $missing.Add($entry.Label)
            }
        }

        $healthUrl = "$(Resolve-ZynoraSetting @('Operations','HealthMonitorUrl'))"
        $alertUrl = "$(Resolve-ZynoraSetting @('Operations','AlertWebhookUrl'))"
        if ($healthUrl -and $healthUrl -notmatch '^https?://') { $missing.Add('Operations:HealthMonitorUrl (HTTP/HTTPS)') }
        if ($alertUrl -and $alertUrl -notmatch '^https://') { $missing.Add('Operations:AlertWebhookUrl (HTTPS)') }

        if ($missing.Count -gt 0) {
            throw "بوابة تشغيل الإنتاج غير مكتملة:`n  - $($missing -join "`n  - ")"
        }

        Write-Ok 'بوابة قبول المالك والنسخ والمراقبة والبريد وفحص الملفات مكتملة إعدادياً'
    } else {
        Write-Warn 'Operations:EnforceProductionReadiness=false — تجاوز طوارئ ظاهر؛ لا يُعدّ قبول إنتاج.'
    }
}
Write-Ok 'الإعدادات تسمح بالإقلاع'

if ($CheckOnly) {
    Write-Host "`nالفحص وحده (-CheckOnly) — لم يُنشر شيء ولم يُلمس $SitePath." -ForegroundColor Green
    return
}

Write-Host "`nاكتب DEPLOY للمتابعة: " -ForegroundColor Yellow -NoNewline
if ((Read-Host) -ne 'DEPLOY') { throw 'أُلغي النشر بناءً على طلبك.' }

# ─────────────────────────────────────────────────────────────────────────────
# ١) نسخة رجوع للملفات — تحفظ appsettings*.json التي ليست بالمستودع
# ─────────────────────────────────────────────────────────────────────────────
Write-Step "١) نسخة رجوع للملفات ← $backupDir"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
robocopy $SitePath $backupDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
# robocopy: ما دون 8 نجاح. أي شيء أعلى إخفاقُ نسخ — ولا نشر بلا نسخة.
if ($LASTEXITCODE -ge 8) { throw "فشلت نسخة الملفات (robocopy=$LASTEXITCODE). أُوقف النشر." }
Write-Ok "نُسخت — الرجوع: robocopy `"$backupDir`" `"$SitePath`" /MIR"

# ─────────────────────────────────────────────────────────────────────────────
# ٢) نسخة رجوع للقاعدة — قبل أي إقلاع يطبّق هجرة
# ─────────────────────────────────────────────────────────────────────────────
Write-Step '٢) نسخة رجوع للقاعدة'
if ($SkipDbBackup) {
    Write-Warn 'تُخُطّيت بطلبك (-SkipDbBackup). الرجوع لن يبطل الهجرات المطبَّقة.'
} else {
    # الضغط غير مدعوم على SQL Server Express (وهي نسخة خادمنا) — وطلبُه يُفشل
    # النسخة كلّها لا يتجاهلها، فتتوقّف عملية النشر عند خطوتها الثانية.
    # EngineEdition = 4 ⟹ Express.
    $engineEdition = (& sqlcmd -S $SqlServer -E -C -h -1 -W -Q `
        "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('EngineEdition') AS int);") | Select-Object -First 1
    $withOptions = if ("$engineEdition".Trim() -eq '4') {
        'INIT, CHECKSUM, STATS=10'
    } else {
        'INIT, COMPRESSION, CHECKSUM, STATS=10'
    }

    $sql = "BACKUP DATABASE [$Database] TO DISK=N'$dbBackup' WITH $withOptions;"
    # `-C` (ثق بشهادة الخادم) إلزاميّ مع ODBC Driver 18: صار يشفّر الاتصال افتراضاً
    # فيرفض شهادة SQL المحليّة الموقَّعة ذاتياً — نفس ما تقوله TrustServerCertificate
    # بسلسلة الاتصال. بدونه تفشل نسخة القاعدة ويتوقّف النشر عند خطوته الثانية.
    & sqlcmd -S $SqlServer -E -C -b -Q $sql
    if ($LASTEXITCODE -ne 0) { throw "فشلت نسخة القاعدة (sqlcmd=$LASTEXITCODE). أُوقف النشر." }
    if (-not (Test-Path $dbBackup)) { throw "sqlcmd نجح ولا ملف نسخة على القرص: $dbBackup" }
    Write-Ok "نُسخت ← $dbBackup"
}

# ─────────────────────────────────────────────────────────────────────────────
# ٣) إيقاف المهمة وقتل حلقة run-server
# ─────────────────────────────────────────────────────────────────────────────
Write-Step '٣) إيقاف الموقع'
Disable-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue | Out-Null
Stop-ScheduledTask    -TaskName $TaskName -ErrorAction SilentlyContinue

Stop-SiteProcesses -SitePath $SitePath

Start-Sleep -Seconds 3
if (Test-NetConnection -ComputerName localhost -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue) {
    Write-Warn "المنفذ $Port ما زال مشغولاً — تحقّق يدوياً قبل المتابعة."
}
Write-Ok 'توقّف الموقع'

# ─────────────────────────────────────────────────────────────────────────────
# ٤) نشر كامل
# ─────────────────────────────────────────────────────────────────────────────
Write-Step '٤) dotnet publish'
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
& dotnet publish (Join-Path $RepoPath 'SmartAttendance.Web\SmartAttendance.Web.csproj') -c Release -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "فشل النشر (dotnet=$LASTEXITCODE). لم يُمسّ $SitePath — الموقع متوقّف، أعِد تشغيل المهمة." }
Write-Ok 'بُني'

# ─────────────────────────────────────────────────────────────────────────────
# ٥) نسخ يستبعد الإعدادات
# ─────────────────────────────────────────────────────────────────────────────
Write-Step '٥) نسخ لمجلّد الموقع'
# ⚠️ `/MIR` يحذف من الموقع كل ما ليس بمخرجات النشر — وعلى الخادم أشياء **يملكها
# الخادم لا المستودع**، فتُستثنى صراحةً وإلا مُسحت بصمت:
#   • appsettings*.json — سلسلة الاتصال والأسرار.
#   • certs\ — شهادة Kestrel (`lan.pfx`). حذفها يعني أن التطبيق **لا يقلع أصلاً**:
#     `DirectoryNotFoundException` عند ربط 5443، فيفشل النشر ويفشل الرجوع معه —
#     وهو ما حدث فعلاً بنشر 2026-08-07 وأسقط الموقع.
#   • run-*.vbs / run-*.bat — مشغّلات المهمة المجدولة. حذفها يترك المهمة تنادي
#     ملفاً غير موجود («Can not find script file»).
#   • App_Data و uploads — بيانات ومرفقات المستخدمين، لا مخرجات بناء.
$preserveDirs  = @('certs', 'App_Data', 'uploads', 'logs', 'keys')
$preserveFiles = @('appsettings*.json', 'run-*.vbs', 'run-*.bat', '*.dev-backup', '*.pfx')

robocopy $PublishDir $SitePath /MIR /XD $preserveDirs /XF $preserveFiles /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "فشل النسخ (robocopy=$LASTEXITCODE). ارجع: robocopy `"$backupDir`" `"$SitePath`" /MIR" }

if (-not (Get-ChildItem -Path $SitePath -Filter 'appsettings*.json' -ErrorAction SilentlyContinue)) {
    throw "لا appsettings*.json بـ$SitePath بعد النسخ — لن يقلع. ارجع: robocopy `"$backupDir`" `"$SitePath`" /MIR"
}

# حارس ما بعد النسخ: أصولٌ يملكها الخادم وغيابها يمنع الإقلاع أو التشغيل. يُفحص
# **هنا** لا بعد الإقلاع، لأن اكتشافها متأخراً يعني موقعاً ساقطاً ورجوعاً عاجزاً
# عن استعادتها (نسخة الرجوع تكون قد أُخذت وهي ناقصة أصلاً).
$mustExist = @(
    @{ Path = Join-Path $SitePath 'certs\lan.pfx';   Why = 'شهادة Kestrel — بدونها يفشل ربط 5443 والتطبيق لا يقلع' },
    @{ Path = Join-Path $SitePath 'run-hidden.vbs';  Why = 'مشغّل المهمة المجدولة — بدونه لا يعمل التشغيل التلقائي' }
)
foreach ($item in $mustExist) {
    if (-not (Test-Path $item.Path)) {
        throw "مفقود بعد النسخ: $($item.Path) — $($item.Why). ارجع: robocopy `"$backupDir`" `"$SitePath`" /MIR"
    }
}

Write-Ok 'نُسخ والإعدادات وأصول الخادم سليمة'

# ─────────────────────────────────────────────────────────────────────────────
# ٦) تشغيل ثم قياس
# ─────────────────────────────────────────────────────────────────────────────
Write-Step '٦) تشغيل وقياس'
Enable-ScheduledTask -TaskName $TaskName | Out-Null
Start-ScheduledTask  -TaskName $TaskName

$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
$ready    = $false
while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest "http://localhost:$Port/health/ready" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch { Start-Sleep -Seconds 3 }
}

if (-not $ready) {
    Write-Warn "لم يستجب /health/ready خلال $StartupTimeoutSeconds ثانية — أرجع الملفات."
    Write-Warn 'سببٌ محتمل: CookieSecurityPolicy يرفض الإقلاع بالإنتاج بلا إثبات TLS.'
    Write-Warn 'المخارج: ForceHttps=true · ReverseProxy:Enabled=true · Security:AllowInsecureCookies=true'

    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Stop-SiteProcesses -SitePath $SitePath
    robocopy $backupDir $SitePath /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    Start-ScheduledTask -TaskName $TaskName

    throw "فشل الإقلاع. أُرجعت الملفات من $backupDir. ⚠️ الهجرات المطبَّقة تحتاج استعادة القاعدة من $dbBackup يدوياً."
}
Write-Ok 'الموقع مستجيب و/health/ready أخضر'

# ٦.٥) إحماء المسار الحقيقيّ قبل فتح الباب للمستخدمين.
# `/health/ready` نقطةٌ خفيفة لا توقظ خطّ Razor/EF/MVC، فأوّل **طلب صفحةٍ حقيقيّ**
# بعد إعادة التشغيل يدفع ~٨ ثوانٍ تهيئة (JIT + بناء نموذج EF + ترجمة العروض). بلا
# هذا الإحماء يبتلعها أوّل مستخدمٍ فيشكو «الموقع ثقيل». نطلب صفحة الدخول بضع مرّات
# هنا فيدفعها السكربت بدلاً منه — الطلب الأوّل بطيءٌ عمداً، والبقيّة تؤكّد الدفء.
Write-Step 'إحماء المسار الحقيقيّ'
foreach ($i in 1..3) {
    try {
        $ms = (Measure-Command {
            Invoke-WebRequest "http://localhost:$Port/Account/Login" -UseBasicParsing -TimeoutSec 90 | Out-Null
        }).TotalMilliseconds
        '  إحماء {0}: {1,7:N0} ms' -f $i, $ms | Write-Host
    } catch {
        Write-Warn "إحماء $i — $($_.Exception.Message)"
    }
}

# القياس لا الافتراض — لكن على ما يصحّ قياسه بلا جلسة.
# `/AttendanceRecords` و`/DayAttendance` خلف المصادقة: نداؤهما هنا يقيس تحويلة
# الدخول لا الصفحة، فرقمٌ جميلٌ كاذب. قياسهما مكانه المتصفّح بجلسة حقيقية.
Write-Step 'قياس'
foreach ($path in '/health/live', '/health/ready') {
    try {
        $ms = (Measure-Command { Invoke-WebRequest "http://localhost:$Port$path" -UseBasicParsing -TimeoutSec 60 | Out-Null }).TotalMilliseconds
        '  {0,-22} {1,7:N0} ms' -f $path, $ms | Write-Host
    } catch {
        Write-Warn "$path — $($_.Exception.Message)"
    }
}

Write-Host "`nتمّ النشر: $branch @ $head" -ForegroundColor Green
Write-Host "الرجوع (ملفات): robocopy `"$backupDir`" `"$SitePath`" /MIR"
if (-not $SkipDbBackup) { Write-Host "الرجوع (قاعدة):  RESTORE DATABASE [$Database] FROM DISK=N'$dbBackup' WITH REPLACE" }
Write-Warn 'تحقّق بالمتصفّح بجلسة حقيقية:'
Write-Warn "  • زمن /AttendanceRecords (كان ~2.4s — فهرس AttendanceDate يُفترض أن يُسقطه)"
Write-Warn '  • تبويبة الحضور بمجموعاتها الثمانية · /Holidays من تحت «إعدادات تسجيل الحضور»'
Write-Warn '  • دخولٌ جديد — الجلسات القائمة تُطرد مرّة واحدة (مفاتيح Data Protection انتقلت للقاعدة).'
