<#
.SYNOPSIS
    نسخة SQL دورية متحققة، تنسخ إلى موقع خارجي وتكتب نبضة نجاح قابلة للمراقبة.

.DESCRIPTION
    السكربت مخصص لمهمة مجدولة بحساب خدمة يملك BACKUP DATABASE والوصول إلى
    مساري النسخ. لا يقبل اسماً عاماً لقاعدة أو مساراً خارجياً على نفس القرص؛
    ولا يحدّث نبضة النجاح إلا بعد RESTORE VERIFYONLY ومطابقة SHA-256.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $SqlServer,
    [Parameter(Mandatory)][string] $Database,
    [Parameter(Mandatory)][string] $LocalBackupRoot,
    [Parameter(Mandatory)][string] $OffsiteBackupRoot,
    [Parameter(Mandatory)][string] $HeartbeatPath,
    [ValidateRange(1, 525600)][int] $RpoMinutes,
    [ValidateRange(1, 3650)][int] $RetentionDays = 30
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Database -notmatch '^[A-Za-z0-9_]+$') {
    throw 'اسم القاعدة يجب أن يتكوّن من حروف/أرقام/شرطة سفلية فقط.'
}

foreach ($root in @($LocalBackupRoot, $OffsiteBackupRoot)) {
    if (-not [IO.Path]::IsPathRooted($root)) { throw "المسار يجب أن يكون مطلقاً: $root" }
    New-Item -ItemType Directory -Force -Path $root | Out-Null
}

$localRoot = [IO.Path]::GetFullPath($LocalBackupRoot).TrimEnd('\')
$offsiteRoot = [IO.Path]::GetFullPath($OffsiteBackupRoot).TrimEnd('\')
if ($localRoot.Equals($offsiteRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'مسار النسخة الخارجية يطابق المسار المحلي.'
}

$localVolume = [IO.Path]::GetPathRoot($localRoot)
$offsiteVolume = [IO.Path]::GetPathRoot($offsiteRoot)
if (-not $offsiteRoot.StartsWith('\\') -and
    $localVolume.Equals($offsiteVolume, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'النسخة الخارجية يجب أن تكون UNC أو على قرص مختلف عن النسخة المحلية.'
}

$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$fileName = "zynora-$Database-$stamp.bak"
$localFile = Join-Path $localRoot $fileName
$offsiteFile = Join-Path $offsiteRoot $fileName

function Escape-SqlLiteral([string] $value) { $value.Replace("'", "''") }

$databaseIdentifier = "[$($Database.Replace(']', ']]'))]"
$localSqlPath = Escape-SqlLiteral $localFile
$backupSql = "BACKUP DATABASE $databaseIdentifier TO DISK=N'$localSqlPath' WITH COPY_ONLY, INIT, CHECKSUM, STATS=10;"

& sqlcmd -S $SqlServer -E -C -b -Q $backupSql
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $localFile -PathType Leaf)) {
    throw "فشلت نسخة SQL المحلية: $localFile"
}

& sqlcmd -S $SqlServer -E -C -b -Q "RESTORE VERIFYONLY FROM DISK=N'$localSqlPath' WITH CHECKSUM;"
if ($LASTEXITCODE -ne 0) { throw 'فشل RESTORE VERIFYONLY؛ لن تُنسخ نتيجة غير سليمة خارجياً.' }

Copy-Item -LiteralPath $localFile -Destination $offsiteFile -Force
$localHash = (Get-FileHash -LiteralPath $localFile -Algorithm SHA256).Hash
$offsiteHash = (Get-FileHash -LiteralPath $offsiteFile -Algorithm SHA256).Hash
if (-not $localHash.Equals($offsiteHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'فشلت مطابقة SHA-256 بين النسخة المحلية والخارجية.'
}

$heartbeatFull = [IO.Path]::GetFullPath($HeartbeatPath)
$heartbeatDirectory = Split-Path -Parent $heartbeatFull
if (-not $heartbeatDirectory) { throw 'HeartbeatPath يجب أن يحتوي مجلداً.' }
New-Item -ItemType Directory -Force -Path $heartbeatDirectory | Out-Null

$heartbeat = [ordered]@{
    schemaVersion = 1
    completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    database = $Database
    rpoMinutes = $RpoMinutes
    backupFile = $fileName
    bytes = (Get-Item -LiteralPath $localFile).Length
    sha256 = $localHash
    verified = $true
    offsiteCopied = $true
}
$heartbeatJson = $heartbeat | ConvertTo-Json
$heartbeatTemp = "$heartbeatFull.tmp"
Set-Content -LiteralPath $heartbeatTemp -Value $heartbeatJson -Encoding UTF8
Move-Item -LiteralPath $heartbeatTemp -Destination $heartbeatFull -Force

$cutoff = (Get-Date).ToUniversalTime().AddDays(-$RetentionDays)
foreach ($root in @($localRoot, $offsiteRoot)) {
    $boundary = $root + [IO.Path]::DirectorySeparatorChar
    Get-ChildItem -LiteralPath $root -File -Filter "zynora-$Database-*.bak" |
        Where-Object { $_.LastWriteTimeUtc -lt $cutoff } |
        ForEach-Object {
            $candidate = [IO.Path]::GetFullPath($_.FullName)
            if (-not $candidate.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
                throw "رفض حذف ملف خارج جذر النسخ: $candidate"
            }
            Remove-Item -LiteralPath $candidate -Force
        }
}

Write-Host "Backup verified and copied offsite: $fileName" -ForegroundColor Green
