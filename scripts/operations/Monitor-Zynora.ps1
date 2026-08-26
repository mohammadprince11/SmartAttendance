<#
.SYNOPSIS
    مسبار مستقل أحادي التشغيل لـTask Scheduler؛ ينبه بعد إخفاقات متتالية ويعلن التعافي.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][uri] $HealthUrl,
    [Parameter(Mandatory)][uri] $AlertWebhookUrl,
    [Parameter(Mandatory)][string] $StatePath,
    [ValidateRange(1, 20)][int] $ConsecutiveFailures = 3,
    [ValidateRange(1, 120)][int] $TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($HealthUrl.Scheme -notin @('http', 'https')) { throw 'HealthUrl يجب أن يكون HTTP/HTTPS.' }
if ($AlertWebhookUrl.Scheme -ne 'https') { throw 'AlertWebhookUrl يجب أن يكون HTTPS.' }

$stateFull = [IO.Path]::GetFullPath($StatePath)
$stateDirectory = Split-Path -Parent $stateFull
if (-not $stateDirectory) { throw 'StatePath يجب أن يحتوي مجلداً.' }
New-Item -ItemType Directory -Force -Path $stateDirectory | Out-Null

$state = [ordered]@{ failures = 0; alerted = $false; lastSuccessUtc = $null; lastError = $null }
if (Test-Path -LiteralPath $stateFull -PathType Leaf) {
    try {
        $saved = Get-Content -LiteralPath $stateFull -Raw -Encoding UTF8 | ConvertFrom-Json
        $state.failures = [int]$saved.failures
        $state.alerted = [bool]$saved.alerted
        $state.lastSuccessUtc = $saved.lastSuccessUtc
        $state.lastError = $saved.lastError
    } catch { Write-Warning 'تعذّرت قراءة حالة المراقب السابقة؛ بدأت حالة جديدة.' }
}

$healthy = $false
$failureMessage = $null
try {
    $response = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec $TimeoutSeconds
    $healthy = $response.StatusCode -eq 200
    if (-not $healthy) { $failureMessage = "HTTP $($response.StatusCode)" }
} catch {
    $failureMessage = $_.Exception.Message
}

function Send-Alert([string] $status, [string] $message) {
    $payload = [ordered]@{
        source = 'ZYNORA'
        status = $status
        message = $message
        healthUrl = $HealthUrl.AbsoluteUri
        occurredAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri $AlertWebhookUrl -ContentType 'application/json' -Body $payload -TimeoutSec $TimeoutSeconds | Out-Null
}

if ($healthy) {
    if ($state.alerted) { Send-Alert 'recovered' 'ZYNORA readiness probe recovered.' }
    $state.failures = 0
    $state.alerted = $false
    $state.lastSuccessUtc = (Get-Date).ToUniversalTime().ToString('o')
    $state.lastError = $null
} else {
    $state.failures++
    $state.lastError = $failureMessage
    if (-not $state.alerted -and $state.failures -ge $ConsecutiveFailures) {
        Send-Alert 'down' "ZYNORA readiness failed $($state.failures) consecutive checks."
        $state.alerted = $true
    }
}

$temp = "$stateFull.tmp"
Set-Content -LiteralPath $temp -Value ($state | ConvertTo-Json) -Encoding UTF8
Move-Item -LiteralPath $temp -Destination $stateFull -Force

if (-not $healthy) { exit 2 }
