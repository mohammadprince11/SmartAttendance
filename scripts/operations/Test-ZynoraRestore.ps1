<#
.SYNOPSIS
    تمرين استعادة لآخر نسخة ZYNORA داخل قاعدة مؤقتة محروسة الاسم ثم حذفها.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $SqlServer,
    [Parameter(Mandatory)][string] $Database,
    [Parameter(Mandatory)][string] $BackupRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw 'اسم قاعدة المصدر غير صالح.' }
$backupRootFull = [IO.Path]::GetFullPath($BackupRoot)
$backup = Get-ChildItem -LiteralPath $backupRootFull -File -Filter "zynora-$Database-*.bak" |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $backup) { throw "لا توجد نسخة مطابقة تحت $backupRootFull" }

$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$restoreDatabase = "SmartAttendance_RestoreDrill_$suffix"
if ($restoreDatabase -notmatch '^SmartAttendance_RestoreDrill_[a-f0-9]{12}$') {
    throw 'رفض اسم قاعدة تمرين غير محروس.'
}

function Escape-SqlLiteral([string] $value) { $value.Replace("'", "''") }
$backupPath = Escape-SqlLiteral $backup.FullName
$restoreIdentifier = "[$restoreDatabase]"
$created = $false

try {
    $fileRows = & sqlcmd -S $SqlServer -E -C -b -h -1 -W -s '|' -Q `
        "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK=N'$backupPath';"
    if ($LASTEXITCODE -ne 0) { throw 'تعذّر قراءة قائمة ملفات النسخة.' }

    $parsed = $fileRows | Where-Object { $_ -match '\|' } | ForEach-Object { $_ -split '\|' }
    $dataLogical = ($parsed | Where-Object { $_[2].Trim() -eq 'D' } | Select-Object -First 1)[0].Trim()
    $logLogical = ($parsed | Where-Object { $_[2].Trim() -eq 'L' } | Select-Object -First 1)[0].Trim()
    if (-not $dataLogical -or -not $logLogical) { throw 'تعذّر تحديد الأسماء المنطقية للبيانات والسجل.' }

    $defaultData = (& sqlcmd -S $SqlServer -E -C -b -h -1 -W -Q `
        "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000));" | Select-Object -First 1).Trim()
    $defaultLog = (& sqlcmd -S $SqlServer -E -C -b -h -1 -W -Q `
        "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000));" | Select-Object -First 1).Trim()
    if (-not $defaultData -or -not $defaultLog) { throw 'تعذّر تحديد مسارات SQL الافتراضية.' }

    $mdf = Escape-SqlLiteral (Join-Path $defaultData "$restoreDatabase.mdf")
    $ldf = Escape-SqlLiteral (Join-Path $defaultLog "${restoreDatabase}_log.ldf")
    $dataLogicalSql = Escape-SqlLiteral $dataLogical
    $logLogicalSql = Escape-SqlLiteral $logLogical

    & sqlcmd -S $SqlServer -E -C -b -Q @"
RESTORE DATABASE $restoreIdentifier FROM DISK=N'$backupPath'
WITH MOVE N'$dataLogicalSql' TO N'$mdf', MOVE N'$logLogicalSql' TO N'$ldf',
CHECKSUM, RECOVERY, REPLACE, STATS=10;
"@
    if ($LASTEXITCODE -ne 0) { throw 'فشل تمرين الاستعادة.' }
    $created = $true

    $tableCount = (& sqlcmd -S $SqlServer -d $restoreDatabase -E -C -b -h -1 -W -Q `
        "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables;") | Select-Object -First 1
    if ([int]"$tableCount" -le 0) { throw 'القاعدة المستعادة بلا جداول مستخدم.' }

    Write-Host "Restore drill passed for $($backup.Name) ($tableCount tables)." -ForegroundColor Green
}
finally {
    if ($created) {
        & sqlcmd -S $SqlServer -E -C -b -Q `
            "IF DB_ID(N'$restoreDatabase') IS NOT NULL BEGIN ALTER DATABASE $restoreIdentifier SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $restoreIdentifier; END"
        if ($LASTEXITCODE -ne 0) { Write-Warning "تعذّر حذف قاعدة التمرين المحروسة: $restoreDatabase" }
    }
}
