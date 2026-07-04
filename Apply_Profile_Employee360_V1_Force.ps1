# SmartAttendance - FORCE Employee 360 V1 upgrade for existing Employees/Profile page
# Run from: C:\Projects\SmartAttendance
# Command:
# powershell -ExecutionPolicy Bypass -File .\Apply_Profile_Employee360_V1_Force.ps1

$ErrorActionPreference = "Stop"

Write-Host "NEXORA Employee 360 V1 FORCE patch started..." -ForegroundColor Cyan

$root = (Get-Location).Path
$profilePath = Join-Path -Path $root -ChildPath "SmartAttendance.Web\Pages\Employees\Profile.cshtml"
$profileCsPath = Join-Path -Path $root -ChildPath "SmartAttendance.Web\Pages\Employees\Profile.cshtml.cs"
$cssPath = Join-Path -Path $root -ChildPath "SmartAttendance.Web\wwwroot\css\employee-360-profile-v2.css"

if (-not (Test-Path $profilePath)) {
    Write-Host "Profile.cshtml not found: $profilePath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $profileCsPath)) {
    Write-Host "Profile.cshtml.cs not found: $profileCsPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $cssPath)) {
    Write-Host "employee-360-profile-v2.css not found: $cssPath" -ForegroundColor Red
    exit 1
}

Copy-Item $profilePath "$profilePath.bak_employee360_force_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Force
Copy-Item $profileCsPath "$profileCsPath.bak_employee360_force_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Force
Copy-Item $cssPath "$cssPath.bak_employee360_force_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Force

# ------------------------------------------------------------
# 1) Add computed properties in Profile.cshtml.cs
# ------------------------------------------------------------
$cs = Get-Content -Path $profileCsPath -Raw -Encoding UTF8

if ($cs -notmatch "PayrollRiskItems") {
    $insertProps = @'
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

    if ($cs -match 'public decimal TotalWorkingHours => Math\.Round\(TotalWorkingMinutes / 60m, 2\);') {
        $cs = [regex]::Replace(
            $cs,
            '(public decimal TotalWorkingHours => Math\.Round\(TotalWorkingMinutes / 60m, 2\);\s*)',
            "`$1`r`n" + $insertProps,
            1
        )
        Write-Host "Profile.cshtml.cs: readiness properties inserted after TotalWorkingHours." -ForegroundColor Green
    }
    else {
        $cs = [regex]::Replace(
            $cs,
            '(public class EmployeeProfileCard)',
            $insertProps + "`r`n    `$1",
            1
        )
        Write-Host "Profile.cshtml.cs: readiness properties inserted before EmployeeProfileCard." -ForegroundColor Yellow
    }
}
else {
    Write-Host "Profile.cshtml.cs: readiness properties already exist." -ForegroundColor Yellow
}

Set-Content -Path $profileCsPath -Value $cs -Encoding UTF8

# ------------------------------------------------------------
# 2) Force insert Employee 360 V1 sections into Profile.cshtml
# ------------------------------------------------------------
$page = Get-Content -Path $profilePath -Raw -Encoding UTF8

# Ensure title/intro.
$page = $page.Replace('ViewData["Title"] = "ملف الموظف";', 'ViewData["Title"] = "Employee 360";')
$page = $page.Replace('<div class="employee360v2-eyebrow">NEXORA Employee 360</div>', '<div class="employee360v2-eyebrow">NEXORA Employee 360 V1</div>')
$page = $page.Replace('مركز موحد لقراءة بيانات الموظف وتاريخه الوظيفي والحضور والطلبات والمستندات.', 'Employee 360 V1: ملف موحد للبيانات، الحضور، الطلبات، المستندات، وجاهزية الراتب قبل الإغلاق.')

# A) Insert Payroll Readiness top card after contract alert strip.
if ($page -notmatch "employee360v2-readiness-card") {
    $readinessTop = @'

        <section class="employee360v2-readiness-card @Model.PayrollReadinessClass">
            <div>
                <span class="employee360v2-mini-label">Payroll Readiness</span>
                <h3>@Model.PayrollReadinessText</h3>
                <p>
                    يتم احتساب المؤشر من الغياب، البصمات الناقصة، الطلبات المعلقة، والمستندات المنتهية أو القريبة من الانتهاء.
                </p>
            </div>
            <div class="employee360v2-readiness-score">
                <strong>@Model.PayrollRiskItems</strong>
                <span>نقاط تحتاج مراجعة</span>
            </div>
        </section>
'@

    $contractPattern = '(?is)(\s*<section\s+class="employee360v2-alert-strip\s+@contractClass">[\s\S]*?</section>)'
    if ([regex]::IsMatch($page, $contractPattern)) {
        $page = [regex]::Replace($page, $contractPattern, { param($m) $m.Value + $readinessTop }, 1)
        Write-Host "Profile.cshtml: top readiness card inserted." -ForegroundColor Green
    }
    else {
        Write-Host "Profile.cshtml: contract strip not found, top readiness card skipped." -ForegroundColor Yellow
    }
}
else {
    Write-Host "Profile.cshtml: top readiness card already exists." -ForegroundColor Yellow
}

# B) Insert KPI cards at end of KPI section before </section>.
if ($page -notmatch "مخاطر الحضور") {
    $kpiCards = @'
            <div class="employee360v2-kpi @Model.PayrollReadinessClass">
                <span>جاهزية الراتب</span>
                <strong>@Model.PayrollRiskItems</strong>
                <small>@Model.PayrollReadinessText</small>
            </div>

            <div class="employee360v2-kpi @Model.AttendanceRiskClass">
                <span>مخاطر الحضور</span>
                <strong>@Model.AttendanceRiskItems</strong>
                <small>غياب + تأخير + بصمة ناقصة</small>
            </div>

            <div class="employee360v2-kpi warn">
                <span>مستندات تحتاج متابعة</span>
                <strong>@(Model.ExpiredDocumentsCount + Model.ExpiringDocumentsCount)</strong>
                <small>منتهية أو خلال 30 يوم</small>
            </div>

'@

    $kpiPattern = '(?is)(<section\s+class="employee360v2-kpis">[\s\S]*?)(\s*</section>)'
    if ([regex]::IsMatch($page, $kpiPattern)) {
        $page = [regex]::Replace($page, $kpiPattern, { param($m) $m.Groups[1].Value + $kpiCards + $m.Groups[2].Value }, 1)
        Write-Host "Profile.cshtml: new KPI cards inserted." -ForegroundColor Green
    }
    else {
        Write-Host "Profile.cshtml: KPI section not found." -ForegroundColor Yellow
    }
}
else {
    Write-Host "Profile.cshtml: KPI cards already exist." -ForegroundColor Yellow
}

# C) Insert tab link.
if ($page -notmatch 'href="#readiness"') {
    if ($page -match '<a href="#documents">المستندات</a>') {
        $page = $page.Replace(
            '<a href="#documents">المستندات</a>',
            '<a href="#documents">المستندات</a>' + "`r`n            " + '<a href="#readiness">جاهزية الراتب</a>'
        )
        Write-Host "Profile.cshtml: readiness tab inserted after documents." -ForegroundColor Green
    }
    elseif ($page -match '<a href="#audit">السجل</a>') {
        $page = $page.Replace(
            '<a href="#audit">السجل</a>',
            '<a href="#readiness">جاهزية الراتب</a>' + "`r`n            " + '<a href="#audit">السجل</a>'
        )
        Write-Host "Profile.cshtml: readiness tab inserted before audit." -ForegroundColor Yellow
    }
}
else {
    Write-Host "Profile.cshtml: readiness tab already exists." -ForegroundColor Yellow
}

# D) Insert detailed readiness panel before audit card.
if ($page -notmatch 'id="readiness"') {
    $readinessPanel = @'

            <div id="readiness" class="employee360v2-card employee360v2-readiness-panel">
                <div class="employee360v2-card-head">
                    <h3>جاهزية الراتب</h3>
                    <span class="employee360v2-status @Model.PayrollReadinessClass">@Model.PayrollReadinessText</span>
                </div>

                <div class="employee360v2-risk-grid">
                    <div class="employee360v2-risk-item">
                        <span>غياب</span>
                        <strong>@Model.AbsentCount</strong>
                        <small>سجلات غياب ضمن الفترة</small>
                    </div>

                    <div class="employee360v2-risk-item">
                        <span>بصمات ناقصة</span>
                        <strong>@Model.MissingCheckoutCount</strong>
                        <small>تحتاج تصحيح قبل الراتب</small>
                    </div>

                    <div class="employee360v2-risk-item">
                        <span>طلبات معلقة</span>
                        <strong>@Model.PendingRequests</strong>
                        <small>بانتظار موافقة</small>
                    </div>

                    <div class="employee360v2-risk-item">
                        <span>مستندات منتهية</span>
                        <strong>@Model.ExpiredDocumentsCount</strong>
                        <small>تحتاج تحديث</small>
                    </div>

                    <div class="employee360v2-risk-item">
                        <span>مستندات خلال 30 يوم</span>
                        <strong>@Model.ExpiringDocumentsCount</strong>
                        <small>تنبيه مبكر</small>
                    </div>
                </div>

                <div class="employee360v2-payroll-note">
                    <strong>ملاحظة تشغيلية:</strong>
                    لا تعتمد هذه البطاقة كراتب نهائي. وظيفتها كشف المخاطر قبل إغلاق الراتب وربط ملف الموظف بالحضور والطلبات.
                </div>
            </div>

'@

    $auditPattern = '(?is)(\s*<div\s+class="employee360v2-card">\s*<div\s+class="employee360v2-card-head">\s*<h3>سجل التعديلات</h3>)'
    if ([regex]::IsMatch($page, $auditPattern)) {
        $page = [regex]::Replace($page, $auditPattern, { param($m) $readinessPanel + $m.Value }, 1)
        Write-Host "Profile.cshtml: detailed readiness panel inserted before audit." -ForegroundColor Green
    }
    else {
        # fallback append before final closing page div
        $page = [regex]::Replace($page, '(?is)(\s*</div>\s*$)', $readinessPanel + "`r`n`$1", 1)
        Write-Host "Profile.cshtml: audit block not found, readiness panel appended near end." -ForegroundColor Yellow
    }
}
else {
    Write-Host "Profile.cshtml: detailed readiness panel already exists." -ForegroundColor Yellow
}

Set-Content -Path $profilePath -Value $page -Encoding UTF8

# ------------------------------------------------------------
# 3) Append CSS
# ------------------------------------------------------------
$css = Get-Content -Path $cssPath -Raw -Encoding UTF8

if ($css -notmatch "Employee 360 V1 force upgrade") {
    $extraCss = @'

/* Employee 360 V1 force upgrade */
.employee360v2-readiness-card {
    display: flex;
    justify-content: space-between;
    gap: 18px;
    align-items: center;
    padding: 18px;
    border: 1px solid rgba(230,234,238,.12);
    border-radius: 22px;
    background:
        radial-gradient(circle at 12% 12%, rgba(18,217,227,.10), transparent 32%),
        rgba(15,34,51,.82);
    box-shadow: var(--nx-shadow, 0 22px 60px rgba(0,0,0,.24));
}

.employee360v2-readiness-card.ok {
    border-color: rgba(34,197,94,.25);
}

.employee360v2-readiness-card.warn {
    border-color: rgba(215,178,109,.28);
}

.employee360v2-mini-label {
    display: inline-flex;
    margin-bottom: 8px;
    padding: 6px 10px;
    border-radius: 999px;
    color: #07121F;
    background: linear-gradient(135deg, #12D9E3, #7CEAF0);
    font-size: 12px;
    font-weight: 1000;
}

.employee360v2-readiness-card h3 {
    margin: 0;
    color: var(--sa-text, #E6EAEE);
    font-size: 22px;
    font-weight: 1000;
}

.employee360v2-readiness-card p {
    margin: 8px 0 0;
    color: var(--sa-muted, #9FB0BE);
    font-weight: 850;
    line-height: 1.8;
}

.employee360v2-readiness-score {
    min-width: 160px;
    display: grid;
    place-items: center;
    text-align: center;
    padding: 16px;
    border-radius: 20px;
    border: 1px solid rgba(215,178,109,.18);
    background: rgba(7,18,31,.32);
}

.employee360v2-readiness-score strong {
    color: var(--nx-gold, #D7B26D);
    font-size: 44px;
    line-height: 1;
    font-weight: 1000;
}

.employee360v2-readiness-score span {
    margin-top: 8px;
    color: var(--sa-muted, #9FB0BE);
    font-weight: 850;
}

.employee360v2-risk-grid {
    display: grid;
    grid-template-columns: repeat(5, minmax(0, 1fr));
    gap: 12px;
}

.employee360v2-risk-item {
    padding: 14px;
    border-radius: 18px;
    border: 1px solid rgba(230,234,238,.10);
    background: rgba(7,18,31,.30);
}

.employee360v2-risk-item span,
.employee360v2-risk-item small {
    display: block;
    color: var(--sa-muted, #9FB0BE);
    font-weight: 850;
}

.employee360v2-risk-item strong {
    display: block;
    margin: 8px 0;
    color: var(--nx-teal, #12D9E3);
    font-size: 28px;
    font-weight: 1000;
}

.employee360v2-payroll-note {
    margin-top: 14px;
    padding: 12px 14px;
    border-radius: 16px;
    color: var(--sa-muted, #9FB0BE);
    border: 1px solid rgba(18,217,227,.16);
    background: rgba(18,217,227,.05);
    font-weight: 850;
    line-height: 1.8;
}

.employee360v2-payroll-note strong {
    color: var(--nx-gold, #D7B26D);
}

@media (max-width: 1100px) {
    .employee360v2-risk-grid {
        grid-template-columns: repeat(2, minmax(0, 1fr));
    }
}

@media (max-width: 760px) {
    .employee360v2-readiness-card {
        flex-direction: column;
        align-items: stretch;
    }

    .employee360v2-risk-grid {
        grid-template-columns: 1fr;
    }
}
'@

    Add-Content -Path $cssPath -Value $extraCss -Encoding UTF8
    Write-Host "CSS: Employee 360 V1 styles appended." -ForegroundColor Green
}
else {
    Write-Host "CSS: Employee 360 V1 styles already exist." -ForegroundColor Yellow
}

Write-Host "Done." -ForegroundColor Cyan
Write-Host "Now run:" -ForegroundColor Yellow
Write-Host "taskkill /F /IM SmartAttendance.Web.exe" -ForegroundColor White
Write-Host "taskkill /F /IM dotnet.exe" -ForegroundColor White
Write-Host "dotnet build" -ForegroundColor White
Write-Host "dotnet run --project SmartAttendance.Web" -ForegroundColor White
