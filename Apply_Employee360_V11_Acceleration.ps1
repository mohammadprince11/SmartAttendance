# SmartAttendance - Employee 360 V1.1 Acceleration Patch
# Run from: C:\Projects\SmartAttendance
# Command:
# powershell -ExecutionPolicy Bypass -File .\Apply_Employee360_V11_Acceleration.ps1

$ErrorActionPreference = "Stop"

Write-Host "NEXORA Employee 360 V1.1 acceleration patch started..." -ForegroundColor Cyan

$root = (Get-Location).Path
$profilePath = Join-Path -Path $root -ChildPath "SmartAttendance.Web\Pages\Employees\Profile.cshtml"
$profileCsPath = Join-Path -Path $root -ChildPath "SmartAttendance.Web\Pages\Employees\Profile.cshtml.cs"
$cssPath = Join-Path -Path $root -ChildPath "SmartAttendance.Web\wwwroot\css\employee-360-profile-v2.css"

if (-not (Test-Path $profilePath)) { Write-Host "Profile.cshtml not found." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $profileCsPath)) { Write-Host "Profile.cshtml.cs not found." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $cssPath)) { Write-Host "CSS file not found." -ForegroundColor Red; exit 1 }

Copy-Item $profilePath "$profilePath.bak_employee360_v11_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Force
Copy-Item $profileCsPath "$profileCsPath.bak_employee360_v11_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Force
Copy-Item $cssPath "$cssPath.bak_employee360_v11_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Force

# ------------------------------------------------------------
# 1) Add helper properties if missing
# ------------------------------------------------------------
$cs = Get-Content -Path $profileCsPath -Raw -Encoding UTF8

if ($cs -notmatch "Employee360HealthScore") {
    $helpers = @'
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

            if (score < 0)
            {
                return 0;
            }

            return score;
        }
    }

    public string Employee360HealthClass => Employee360HealthScore >= 85
        ? "ok"
        : Employee360HealthScore >= 60
            ? "warn"
            : "danger";

    public string Employee360HealthText => Employee360HealthScore >= 85
        ? "مستقر"
        : Employee360HealthScore >= 60
            ? "يحتاج متابعة"
            : "خطر تشغيلي";

    public bool HasPayrollBlockingIssues => AbsentCount > 0 || MissingCheckoutCount > 0 || PendingRequests > 0 || ExpiredDocumentsCount > 0;

'@

    if ($cs -match "public string AttendanceRiskClass") {
        $cs = [regex]::Replace(
            $cs,
            '(public string AttendanceRiskClass => AttendanceRiskItems == 0 \? "ok" : "warn";\s*)',
            "`$1`r`n" + $helpers,
            1
        )
    } else {
        $cs = [regex]::Replace(
            $cs,
            '(public class EmployeeProfileCard)',
            $helpers + "`r`n    `$1",
            1
        )
    }

    Write-Host "Profile.cshtml.cs: V1.1 helper properties added." -ForegroundColor Green
} else {
    Write-Host "Profile.cshtml.cs: V1.1 helper properties already exist." -ForegroundColor Yellow
}

Set-Content -Path $profileCsPath -Value $cs -Encoding UTF8

# ------------------------------------------------------------
# 2) Upgrade page wording and insert V1.1 widgets
# ------------------------------------------------------------
$page = Get-Content -Path $profilePath -Raw -Encoding UTF8

$page = $page.Replace("NEXORA Employee 360 V1", "NEXORA Employee 360 V1.1")
$page = $page.Replace("Employee 360 V1: ملف موحد", "Employee 360 V1.1: ملف موحد")

# Insert Health Score card after readiness card if missing.
if ($page -notmatch "employee360v11-health-card") {
    $healthCard = @'

        <section class="employee360v11-health-card @Model.Employee360HealthClass">
            <div>
                <span class="employee360v2-mini-label">Employee Health Score</span>
                <h3>مؤشر استقرار الموظف: @Model.Employee360HealthText</h3>
                <p>
                    مؤشر تشغيلي سريع يجمع الغياب، البصمات الناقصة، الطلبات المعلقة، والمستندات. الهدف منه مساعدة HR قبل الراتب، وليس اتخاذ قرار نهائي.
                </p>
            </div>

            <div class="employee360v11-score-ring">
                <strong>@Model.Employee360HealthScore</strong>
                <span>من 100</span>
            </div>
        </section>
'@

    $readyPattern = '(?is)(\s*<section\s+class="employee360v2-readiness-card\s+@Model\.PayrollReadinessClass">[\s\S]*?</section>)'
    if ([regex]::IsMatch($page, $readyPattern)) {
        $page = [regex]::Replace($page, $readyPattern, { param($m) $m.Value + $healthCard }, 1)
        Write-Host "Profile.cshtml: Health score card inserted." -ForegroundColor Green
    } else {
        Write-Host "Profile.cshtml: readiness card not found, health score skipped." -ForegroundColor Yellow
    }
} else {
    Write-Host "Profile.cshtml: Health score card already exists." -ForegroundColor Yellow
}

# Insert closure checklist in readiness panel if missing.
if ($page -notmatch "employee360v11-closure-checklist") {
    $checklist = @'

                <div class="employee360v11-closure-checklist">
                    <div class="employee360v11-check @(Model.AbsentCount == 0 ? "ok" : "warn")">
                        <strong>الغياب</strong>
                        <span>@(Model.AbsentCount == 0 ? "لا توجد غيابات ضمن الفترة" : "توجد غيابات تحتاج مراجعة")</span>
                    </div>

                    <div class="employee360v11-check @(Model.MissingCheckoutCount == 0 ? "ok" : "warn")">
                        <strong>البصمات الناقصة</strong>
                        <span>@(Model.MissingCheckoutCount == 0 ? "لا توجد بصمات ناقصة" : "توجد بصمات ناقصة قبل الراتب")</span>
                    </div>

                    <div class="employee360v11-check @(Model.PendingRequests == 0 ? "ok" : "warn")">
                        <strong>الطلبات المعلقة</strong>
                        <span>@(Model.PendingRequests == 0 ? "لا توجد طلبات معلقة" : "توجد طلبات بانتظار الموافقة")</span>
                    </div>

                    <div class="employee360v11-check @(Model.ExpiredDocumentsCount == 0 ? "ok" : "warn")">
                        <strong>المستندات المنتهية</strong>
                        <span>@(Model.ExpiredDocumentsCount == 0 ? "لا توجد مستندات منتهية" : "توجد مستندات منتهية")</span>
                    </div>

                    <div class="employee360v11-check @(Model.HasPayrollBlockingIssues ? "warn" : "ok")">
                        <strong>قرار الإغلاق</strong>
                        <span>@(Model.HasPayrollBlockingIssues ? "لا تغلق قبل المراجعة" : "جاهز للمراجعة النهائية")</span>
                    </div>
                </div>
'@

    $notePattern = '(?is)(\s*<div\s+class="employee360v2-payroll-note">[\s\S]*?</div>)'
    if ([regex]::IsMatch($page, $notePattern)) {
        $page = [regex]::Replace($page, $notePattern, { param($m) $checklist + $m.Value }, 1)
        Write-Host "Profile.cshtml: closure checklist inserted." -ForegroundColor Green
    } else {
        Write-Host "Profile.cshtml: payroll note not found, checklist skipped." -ForegroundColor Yellow
    }
} else {
    Write-Host "Profile.cshtml: closure checklist already exists." -ForegroundColor Yellow
}

# Add quick action anchor to readiness in actions if missing.
if ($page -notmatch 'href="#readiness".*مراجعة الراتب') {
    $page = $page.Replace(
        '<a asp-page="./Index" class="employee360v2-btn ghost">رجوع</a>',
        '<a asp-page="./Index" class="employee360v2-btn ghost">رجوع</a>' + "`r`n            " + '<a href="#readiness" class="employee360v2-btn primary">مراجعة الراتب</a>'
    )
}

Set-Content -Path $profilePath -Value $page -Encoding UTF8

# ------------------------------------------------------------
# 3) CSS
# ------------------------------------------------------------
$css = Get-Content -Path $cssPath -Raw -Encoding UTF8

if ($css -notmatch "Employee 360 V1.1 acceleration") {
    $extraCss = @'

/* Employee 360 V1.1 acceleration */
.employee360v11-health-card {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 18px;
    padding: 18px;
    border-radius: 22px;
    border: 1px solid rgba(230,234,238,.12);
    background:
        radial-gradient(circle at 14% 10%, rgba(18,217,227,.13), transparent 34%),
        linear-gradient(135deg, rgba(15,34,51,.92), rgba(7,18,31,.90));
    box-shadow: var(--nx-shadow, 0 22px 60px rgba(0,0,0,.24));
}

.employee360v11-health-card.ok {
    border-color: rgba(34,197,94,.28);
}

.employee360v11-health-card.warn {
    border-color: rgba(215,178,109,.30);
}

.employee360v11-health-card.danger {
    border-color: rgba(239,68,68,.30);
}

.employee360v11-health-card h3 {
    margin: 0;
    color: var(--sa-text, #E6EAEE);
    font-size: 22px;
    font-weight: 1000;
}

.employee360v11-health-card p {
    margin: 8px 0 0;
    color: var(--sa-muted, #9FB0BE);
    font-weight: 850;
    line-height: 1.8;
}

.employee360v11-score-ring {
    min-width: 142px;
    min-height: 142px;
    display: grid;
    place-items: center;
    text-align: center;
    border-radius: 999px;
    border: 1px solid rgba(18,217,227,.30);
    background:
        radial-gradient(circle, rgba(18,217,227,.16), transparent 62%),
        rgba(7,18,31,.42);
}

.employee360v11-score-ring strong {
    color: var(--nx-teal, #12D9E3);
    font-size: 42px;
    line-height: 1;
    font-weight: 1000;
}

.employee360v11-score-ring span {
    display: block;
    color: var(--sa-muted, #9FB0BE);
    font-weight: 850;
}

.employee360v11-closure-checklist {
    margin-top: 14px;
    display: grid;
    grid-template-columns: repeat(5, minmax(0, 1fr));
    gap: 10px;
}

.employee360v11-check {
    padding: 12px;
    border-radius: 16px;
    border: 1px solid rgba(230,234,238,.10);
    background: rgba(7,18,31,.30);
}

.employee360v11-check.ok {
    border-color: rgba(34,197,94,.20);
    background: rgba(34,197,94,.06);
}

.employee360v11-check.warn {
    border-color: rgba(215,178,109,.24);
    background: rgba(215,178,109,.07);
}

.employee360v11-check strong {
    display: block;
    color: var(--sa-text, #E6EAEE);
    font-weight: 1000;
}

.employee360v11-check span {
    display: block;
    margin-top: 6px;
    color: var(--sa-muted, #9FB0BE);
    font-weight: 850;
    line-height: 1.6;
}

@media (max-width: 1150px) {
    .employee360v11-closure-checklist {
        grid-template-columns: repeat(2, minmax(0, 1fr));
    }
}

@media (max-width: 760px) {
    .employee360v11-health-card {
        flex-direction: column;
        align-items: stretch;
    }

    .employee360v11-score-ring {
        margin: 0 auto;
    }

    .employee360v11-closure-checklist {
        grid-template-columns: 1fr;
    }
}
'@

    Add-Content -Path $cssPath -Value $extraCss -Encoding UTF8
    Write-Host "CSS: V1.1 styles appended." -ForegroundColor Green
} else {
    Write-Host "CSS: V1.1 styles already exist." -ForegroundColor Yellow
}

Write-Host "Done. Employee 360 V1.1 acceleration applied." -ForegroundColor Cyan
Write-Host "Next:" -ForegroundColor Yellow
Write-Host "taskkill /F /IM SmartAttendance.Web.exe" -ForegroundColor White
Write-Host "taskkill /F /IM dotnet.exe" -ForegroundColor White
Write-Host "dotnet build" -ForegroundColor White
Write-Host "dotnet run --project SmartAttendance.Web" -ForegroundColor White
