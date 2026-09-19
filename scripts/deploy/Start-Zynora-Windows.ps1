$ErrorActionPreference = "Continue"

$root = "C:\ZynoraPortal"
$exe  = Join-Path $root "SmartAttendance.Web.exe"

$logDir = Join-Path $root "logs"
$log    = Join-Path $logDir "startup-watchdog.log"
$webStdOut = Join-Path $logDir "web-stdout.log"
$webStdErr = Join-Path $logDir "web-stderr.log"

New-Item -ItemType Directory -Path $logDir -Force | Out-Null

function Write-ZLog {
    param([string]$Message)

    $line =
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $Message"

    Add-Content `
        -Path $log `
        -Value $line `
        -Encoding UTF8
}

Write-ZLog "============================================"
Write-ZLog "ZYNORA watchdog started."
Write-ZLog "Windows user: $env:USERNAME"

Set-Location $root

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS =
    "https://0.0.0.0:5443;http://0.0.0.0:5080"
$env:PADDLE_PDX_CACHE_HOME =
    "C:\ZynoraRuntime\PeopleAI\paddlex-cache"


# Windows/SQL startup settling time
Write-ZLog "Waiting 20 seconds for Windows services..."
Start-Sleep -Seconds 20


while ($true) {

    try {

        # إذا ZYNORA شغال أصلاً، لا نشغل نسخة ثانية
        $listener =
            Get-NetTCPConnection `
                -LocalPort 5080 `
                -State Listen `
                -ErrorAction SilentlyContinue

        if ($listener) {

            Start-Sleep -Seconds 10
            continue
        }


        if (-not (Test-Path $exe)) {

            Write-ZLog "ERROR: executable not found: $exe"

            Start-Sleep -Seconds 30
            continue
        }


        Write-ZLog "Starting SmartAttendance.Web.exe..."

        $process =
            Start-Process `
                -FilePath $exe `
                -WorkingDirectory $root `
                -WindowStyle Hidden `
                -RedirectStandardOutput $webStdOut `
                -RedirectStandardError $webStdErr `
                -PassThru


        Write-ZLog "Process started. PID=$($process.Id)"


        # انتظر لفترة حتى يصعد التطبيق
        $ready = $false

        for ($i = 1; $i -le 30; $i++) {

            Start-Sleep -Seconds 2

            if ($process.HasExited) {
                break
            }

            try {

                $response =
                    Invoke-WebRequest `
                        "http://127.0.0.1:5080/health/ready" `
                        -UseBasicParsing `
                        -TimeoutSec 3

                if ($response.StatusCode -eq 200) {

                    $ready = $true

                    Write-ZLog "Health READY = 200"

                    break
                }

            }
            catch {
                # التطبيق بعده يصعد أو SQL بعده مو جاهز
            }
        }


        if (-not $process.HasExited) {

            if (-not $ready) {
                Write-ZLog "Process running; health not ready yet. Continuing supervision."
            }

            Wait-Process `
                -Id $process.Id `
                -ErrorAction SilentlyContinue
        }


        $process.Refresh()

        if ($process.HasExited) {
            Write-ZLog "Application exited. ExitCode=$($process.ExitCode)"
        }
        else {
            Write-ZLog "Application listener disappeared."
        }

    }
    catch {

        Write-ZLog "WATCHDOG ERROR: $($_.Exception.Message)"
    }


    Write-ZLog "Restarting application in 5 seconds..."

    Start-Sleep -Seconds 5
}
