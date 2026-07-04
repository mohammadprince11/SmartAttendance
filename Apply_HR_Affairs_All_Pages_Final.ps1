# SmartAttendance / NEXORA
# HR Affairs All Pages Final Installer
#
# Run from: C:\Projects\SmartAttendance
# powershell -ExecutionPolicy Bypass -File .\Apply_HR_Affairs_All_Pages_Final.ps1

$ErrorActionPreference = "Stop"

Write-Host "NEXORA HR Affairs All Pages Final installer started..." -ForegroundColor Cyan

$root = (Get-Location).Path

$layoutPath = Join-Path $root "SmartAttendance.Web\Pages\Shared\_Layout.cshtml"
$employeesIndexPath = Join-Path $root "SmartAttendance.Web\Pages\Employees\Index.cshtml"
$profileCsPath = Join-Path $root "SmartAttendance.Web\Pages\Employees\Profile.cshtml.cs"
$profilePath = Join-Path $root "SmartAttendance.Web\Pages\Employees\Profile.cshtml"

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"

foreach ($file in @($layoutPath, $employeesIndexPath, $profileCsPath, $profilePath)) {
    if (Test-Path $file) {
        Copy-Item $file "$file.bak_hr_all_pages_$stamp" -Force
    }
}

# 1) Clean Employees action buttons: keep only ملف, remove 360 and Edit from the list.
if (Test-Path $employeesIndexPath) {
    $idx = Get-Content $employeesIndexPath -Raw -Encoding UTF8

    $idx = [regex]::Replace($idx, '(?is)\s*<a\b[^>]*>\s*360\s*</a>\s*', '')
    $idx = [regex]::Replace($idx, '(?is)\s*<a\b(?=[^>]*(?:Employee360|/Employee360))[\s\S]*?</a>\s*', '')
    $idx = [regex]::Replace(
        $idx,
        '(?is)\s*<a\b(?=[^>]*(?:asp-page="\./Edit"|asp-page="/Employees/Edit"|href="[^"]*/Employees/Edit))[\s\S]*?(?:تعديل|Edit)[\s\S]*?</a>\s*',
        ''
    )
    $idx = [regex]::Replace($idx, "(\r?\n){3,}", "`r`n`r`n")

    Set-Content $employeesIndexPath $idx -Encoding UTF8
    Write-Host "Employees list cleaned." -ForegroundColor Green
}

# 2) Add HR Affairs module links to Layout under HR Affairs.
if (Test-Path $layoutPath) {
    $layout = Get-Content $layoutPath -Raw -Encoding UTF8

    # Remove duplicate HR Affairs final links inserted by this installer if re-run.
    $layout = [regex]::Replace($layout, '(?is)\s*<!-- HR_AFFAIRS_FINAL_LINKS_START -->[\s\S]*?<!-- HR_AFFAIRS_FINAL_LINKS_END -->\s*', "`r`n")

    $links = @'
                            <!-- HR_AFFAIRS_FINAL_LINKS_START -->
                            <a asp-page="/HRAffairs/Index" class="nexora-nav-link nexora-primary-link">
                                <span>شؤون الموظفين</span>
                            </a>

                            <a asp-page="/HRAffairs/PayrollReadiness" class="nexora-nav-link">
                                <span>جاهزية الراتب</span>
                            </a>

                            <a asp-page="/HRAffairs/AttendanceExceptions" class="nexora-nav-link">
                                <span>استثناءات الحضور</span>
                            </a>

                            <a asp-page="/HRAffairs/DocumentsRisk" class="nexora-nav-link">
                                <span>العقود والمستندات</span>
                            </a>

                            <a asp-page="/HRAffairs/RequestsCenter" class="nexora-nav-link">
                                <span>طلبات شؤون الموظفين</span>
                            </a>
                            <!-- HR_AFFAIRS_FINAL_LINKS_END -->
'@

    $employeesLinkPattern = '(?is)(\s*<a\s+asp-page="/Employees/Index"\s+class="nexora-nav-link">\s*<span\s+data-i18n="employees">Employees</span>\s*</a>)'
    if ([regex]::IsMatch($layout, $employeesLinkPattern)) {
        $layout = [regex]::Replace($layout, $employeesLinkPattern, { param($m) $m.Value + "`r`n" + $links }, 1)
        Write-Host "Layout HR Affairs links inserted after Employees." -ForegroundColor Green
    } else {
        Write-Host "Employees menu link not found in layout. New pages still exist, but menu was not patched." -ForegroundColor Yellow
    }

    Set-Content $layoutPath $layout -Encoding UTF8
}

# 3) Ensure ProfileModel has methods/properties needed by final Employee 360 pages.
if (Test-Path $profileCsPath) {
    $cs = Get-Content $profileCsPath -Raw -Encoding UTF8

    if ($cs -notmatch "AttendanceStatusName\s*\(") {
        $method = @'

    public string AttendanceStatusName(int status)
    {
        return status switch
        {
            1 => "حاضر",
            2 => "متأخر",
            3 => "غياب",
            4 => "بصمة ناقصة",
            5 => "عطلة",
            _ => "غير محدد"
        };
    }

'@
        $cs = [regex]::Replace($cs, '(\s*public class EmployeeProfileCard)', "`r`n" + $method + "`r`n`$1", 1)
    }

    if ($cs -notmatch "AttendanceSourceName\s*\(") {
        $method = @'

    public string AttendanceSourceName(int source)
    {
        return source switch
        {
            1 => "جهاز بصمة",
            2 => "إدخال يدوي",
            3 => "استيراد",
            4 => "طلب",
            5 => "نظام",
            _ => "غير محدد"
        };
    }

'@
        $cs = [regex]::Replace($cs, '(\s*public class EmployeeProfileCard)', "`r`n" + $method + "`r`n`$1", 1)
    }

    if ($cs -notmatch "PayrollRiskItems") {
        $props = @'
    public int PayrollRiskItems => AbsentCount + MissingCheckoutCount + PendingRequests + ExpiredDocumentsCount + ExpiringDocumentsCount;

    public int AttendanceRiskItems => AbsentCount + LateCount + MissingCheckoutCount;

    public int ExpiredDocumentsCount => DocumentRows.Count(x => x.ExpiryDate.HasValue && x.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today));

    public int ExpiringDocumentsCount => DocumentRows.Count(x =>
        x.ExpiryDate.HasValue &&
        x.ExpiryDate.Value >= DateOnly.FromDateTime(DateTime.Today) &&
        x.ExpiryDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(30)));

    public string PayrollReadinessClass => PayrollRiskItems == 0 ? "ok" : "warn";

    public string PayrollReadinessText => PayrollRiskItems == 0
        ? "جاهز مبدئياً لإغلاق الراتب"
        : "يحتاج مراجعة قبل إغلاق الراتب";

    public string AttendanceRiskClass => AttendanceRiskItems == 0 ? "ok" : "warn";

'@
        $cs = [regex]::Replace($cs, '(public decimal TotalWorkingHours => Math\.Round\(TotalWorkingMinutes / 60m, 2\);\s*)', "`$1`r`n" + $props, 1)
    }

    if ($cs -notmatch "Employee360HealthScore") {
        $props = @'
    public int Employee360HealthScore
    {
        get
        {
            var score = 100;
            score -= AbsentCount * 8;
            score -= MissingCheckoutCount * 10;
            score -= PendingRequests * 6;
            score -= ExpiredDocumentsCount * 12;
            score -= ExpiringDocumentsCount * 4;
            return score < 0 ? 0 : score;
        }
    }

    public string Employee360HealthClass => Employee360HealthScore >= 85 ? "ok" : Employee360HealthScore >= 60 ? "warn" : "danger";

    public string Employee360HealthText => Employee360HealthScore >= 85 ? "مستقر" : Employee360HealthScore >= 60 ? "يحتاج متابعة" : "خطر تشغيلي";

    public bool HasPayrollBlockingIssues => AbsentCount > 0 || MissingCheckoutCount > 0 || PendingRequests > 0 || ExpiredDocumentsCount > 0;

'@
        $cs = [regex]::Replace($cs, '(\s*public class EmployeeProfileCard)', "`r`n" + $props + "`r`n`$1", 1)
    }

    if ($cs -notmatch "AttendanceExceptionsCount") {
        $props = @'
    public int AttendanceExceptionsCount => AttendanceRows.Count(x =>
        x.Status == 2 ||
        x.Status == 3 ||
        !x.CheckIn.HasValue ||
        !x.CheckOut.HasValue ||
        !string.IsNullOrWhiteSpace(x.Notes));

    public List<AttendanceRow> AttendanceExceptionRows => AttendanceRows
        .Where(x =>
            x.Status == 2 ||
            x.Status == 3 ||
            !x.CheckIn.HasValue ||
            !x.CheckOut.HasValue ||
            !string.IsNullOrWhiteSpace(x.Notes))
        .Take(20)
        .ToList();

    public string AttendanceExceptionClass => AttendanceExceptionsCount == 0 ? "ok" : "warn";

'@
        $cs = [regex]::Replace($cs, '(\s*public class EmployeeProfileCard)', "`r`n" + $props + "`r`n`$1", 1)
    }

    Set-Content $profileCsPath $cs -Encoding UTF8
    Write-Host "Employee profile helpers verified." -ForegroundColor Green
}

# 4) Remove accidental duplicate Employee360 page if any.
$employee360Dir = Join-Path $root "SmartAttendance.Web\Pages\Employee360"
if (Test-Path $employee360Dir) {
    Remove-Item -LiteralPath $employee360Dir -Recurse -Force
    Write-Host "Removed duplicated Employee360 folder." -ForegroundColor Yellow
}

$employee360Css = Join-Path $root "SmartAttendance.Web\wwwroot\css\employee360-v1.css"
if (Test-Path $employee360Css) {
    Remove-Item -LiteralPath $employee360Css -Force
}

Write-Host "NEXORA HR Affairs All Pages Final installer completed." -ForegroundColor Cyan
Write-Host "Next commands:" -ForegroundColor Yellow
Write-Host "taskkill /F /IM SmartAttendance.Web.exe" -ForegroundColor White
Write-Host "taskkill /F /IM dotnet.exe" -ForegroundColor White
Write-Host "dotnet build" -ForegroundColor White
Write-Host "dotnet run --project SmartAttendance.Web" -ForegroundColor White
