
param(
    [string]$ProjectRoot = "C:\Users\Lenovo\SmartAttendance",
    [switch]$SkipRun
)

$ErrorActionPreference = "Stop"

function Backup-File {
    param([string]$Path)
    if (Test-Path $Path) {
        $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $backupPath = "$Path.bak_create_cleanup_active_menu_$stamp"
        Copy-Item -LiteralPath $Path -Destination $backupPath -Force
        Write-Host "[NEXORA] Backed up: $Path"
    }
}

function Ensure-Include {
    param(
        [string]$FilePath,
        [string]$Needle,
        [string]$InsertBefore,
        [string]$Snippet
    )

    $content = [System.IO.File]::ReadAllText($FilePath, [System.Text.Encoding]::UTF8)
    if ($content -notlike "*$Needle*") {
        Backup-File -Path $FilePath
        $content = $content.Replace($InsertBefore, "$Snippet`r`n$InsertBefore")
        $utf8Bom = New-Object System.Text.UTF8Encoding($true)
        [System.IO.File]::WriteAllText($FilePath, $content, $utf8Bom)
        Write-Host "[NEXORA] Added include: $Needle"
    }
}

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$FilesRoot = Join-Path $ScriptRoot "files"

if (-not (Test-Path $ProjectRoot)) {
    throw "Project root not found: $ProjectRoot"
}

if (-not (Test-Path $FilesRoot)) {
    throw "files folder not found beside the script."
}

$layoutPath = Join-Path $ProjectRoot "SmartAttendance.Web\Pages\Shared\_Layout.cshtml"
if (-not (Test-Path $layoutPath)) {
    throw "_Layout.cshtml was not found."
}

$copyItems = @(
    @{
        Source = Join-Path $FilesRoot "SmartAttendance.Web\Pages\Employees\Create.cshtml"
        Target = Join-Path $ProjectRoot "SmartAttendance.Web\Pages\Employees\Create.cshtml"
    },
    @{
        Source = Join-Path $FilesRoot "SmartAttendance.Web\wwwroot\css\nexora-employee-create-clean-fix.css"
        Target = Join-Path $ProjectRoot "SmartAttendance.Web\wwwroot\css\nexora-employee-create-clean-fix.css"
    },
    @{
        Source = Join-Path $FilesRoot "SmartAttendance.Web\wwwroot\js\nexora-active-menu-precision-fix.js"
        Target = Join-Path $ProjectRoot "SmartAttendance.Web\wwwroot\js\nexora-active-menu-precision-fix.js"
    }
)

foreach ($item in $copyItems) {
    if (-not (Test-Path $item.Source)) {
        throw "Patch file missing: $($item.Source)"
    }

    $targetDir = Split-Path -Parent $item.Target
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    Backup-File -Path $item.Target
    Copy-Item -LiteralPath $item.Source -Destination $item.Target -Force
    Write-Host "[NEXORA] Copied: $($item.Target)"
}

Ensure-Include -FilePath $layoutPath `
    -Needle "nexora-employee-create-clean-fix.css" `
    -InsertBefore "</head>" `
    -Snippet '    <link rel="stylesheet" href="~/css/nexora-employee-create-clean-fix.css" asp-append-version="true" />'

Ensure-Include -FilePath $layoutPath `
    -Needle "nexora-active-menu-precision-fix.js" `
    -InsertBefore "</body>" `
    -Snippet '    <script src="~/js/nexora-active-menu-precision-fix.js" asp-append-version="true"></script>'

Write-Host "[NEXORA] Patch applied."

Write-Host "[NEXORA] Stopping running application processes if any..."
cmd /c "taskkill /F /IM SmartAttendance.Web.exe >nul 2>nul"
cmd /c "taskkill /F /IM dotnet.exe >nul 2>nul"

Write-Host "[NEXORA] Building project..."
Push-Location $ProjectRoot
try {
    dotnet build

    if (-not $SkipRun) {
        Write-Host "[NEXORA] Starting SmartAttendance.Web..."
        dotnet run --project SmartAttendance.Web
    }
}
finally {
    Pop-Location
}
