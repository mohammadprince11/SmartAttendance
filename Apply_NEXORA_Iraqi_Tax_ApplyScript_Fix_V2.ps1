$ErrorActionPreference = "Stop"

$root = Get-Location

# This fix completes the step where the previous script stopped:
# adding the Iraqi Income Tax button inside HR Policy Settings.
$hrPolicyPath = Join-Path $root "SmartAttendance.Web\Pages\Settings\HrPolicy.cshtml"

if (!(Test-Path $hrPolicyPath)) {
    Write-Host "HrPolicy.cshtml not found. Apply HR Policy Settings Phase 1 first." -ForegroundColor Red
    exit 1
}

$p = Get-Content $hrPolicyPath -Raw -Encoding UTF8

if ($p -notmatch 'asp-page="/Settings/IraqTax"') {
    # ضريبة الدخل العراقية
    $text = -join ([char[]](0x0636,0x0631,0x064A,0x0628,0x0629,0x0020,0x0627,0x0644,0x062F,0x062E,0x0644,0x0020,0x0627,0x0644,0x0639,0x0631,0x0627,0x0642,0x064A,0x0629))
    $link = '<a asp-page="/Settings/IraqTax" class="hr-policy-btn primary">' + $text + '</a>'

    $needle = '<div class="hr-policy-actions">'
    $index = $p.IndexOf($needle)

    if ($index -ge 0) {
        $insertAt = $index + $needle.Length
        $p = $p.Insert($insertAt, "`r`n            " + $link)
        [System.IO.File]::WriteAllText($hrPolicyPath, $p, [System.Text.UTF8Encoding]::new($true))
        Write-Host "Iraqi Tax button added to HR Policy Settings." -ForegroundColor Green
    }
    else {
        Write-Host "hr-policy-actions block was not found. IraqTax page files should still be available." -ForegroundColor Yellow
    }
}
else {
    Write-Host "Iraqi Tax button already exists." -ForegroundColor Green
}

Write-Host "Fix completed. Now run: dotnet build" -ForegroundColor Cyan
