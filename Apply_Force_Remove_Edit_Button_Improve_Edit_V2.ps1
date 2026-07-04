# NEXORA - FORCE remove Edit button from Employees list + improve Employee Edit page
# Run from: C:\Projects\SmartAttendance
# Command:
# powershell -ExecutionPolicy Bypass -File .\Apply_Force_Remove_Edit_Button_Improve_Edit_V2.ps1

$ErrorActionPreference = "Stop"

Write-Host "NEXORA force employee edit patch started..." -ForegroundColor Cyan

$root = (Get-Location).Path

$indexPath = Join-Path -Path $root -ChildPath "SmartAttendance.Web\Pages\Employees\Index.cshtml"
$editPath = Join-Path -Path $root -ChildPath "SmartAttendance.Web\Pages\Employees\Edit.cshtml"
$editCsPath = Join-Path -Path $root -ChildPath "SmartAttendance.Web\Pages\Employees\Edit.cshtml.cs"
$cssPath = Join-Path -Path $root -ChildPath "SmartAttendance.Web\wwwroot\css\employee-edit-nexora.css"

if (-not (Test-Path $indexPath)) {
    Write-Host "Employees Index not found: $indexPath" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $editPath)) {
    Write-Host "Employees Edit page not found: $editPath" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $editCsPath)) {
    Write-Host "Employees Edit code-behind not found: $editCsPath" -ForegroundColor Red
    exit 1
}

Copy-Item $indexPath "$indexPath.bak_force_remove_edit_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Force
Copy-Item $editPath "$editPath.bak_force_improve_edit_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Force
Copy-Item $editCsPath "$editCsPath.bak_force_improve_edit_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Force

# 1) Remove Edit and 360 buttons from Employees list.
$index = Get-Content -Path $indexPath -Raw -Encoding UTF8

$index = [regex]::Replace(
    $index,
    '(?is)\s*<a\b(?=[^>]*(?:asp-page="\./Edit"|asp-page="/Employees/Edit"|href="[^"]*/Employees/Edit))[\s\S]*?(?:تعديل|Edit)[\s\S]*?</a>\s*',
    ''
)

$index = [regex]::Replace(
    $index,
    '(?is)\s*<a\b[^>]*>\s*360\s*</a>\s*',
    ''
)

$index = [regex]::Replace($index, "(\r?\n){3,}", "`r`n`r`n")

Set-Content -Path $indexPath -Value $index -Encoding UTF8
Write-Host "Employees list: Edit and 360 buttons removed." -ForegroundColor Green

# 2) Replace Edit.cshtml with modern NEXORA design.
$editView = @'
@page
@model SmartAttendance.Web.Pages.Employees.EditModel

@{
    ViewData["Title"] = "تعديل بيانات الموظف";
}

<link rel="stylesheet" href="~/css/employee-edit-nexora.css" asp-append-version="true" />

<div class="employee-edit-page" dir="rtl">
    <section class="employee-edit-hero">
        <div>
            <span class="employee-edit-eyebrow">NEXORA Employee Data</span>
            <h1>تعديل بيانات الموظف</h1>
            <p>
                تحديث البيانات الأساسية للموظف بشكل منظم، مع الحفاظ على ملف الموظف الشامل كمركز رئيسي للمتابعة.
            </p>
        </div>

        <div class="employee-edit-hero-actions">
            <a asp-page="./Profile" asp-route-id="@Model.Employee.Id" class="employee-edit-btn secondary">العودة إلى الملف</a>
            <a asp-page="./Index" class="employee-edit-btn ghost">قائمة الموظفين</a>
        </div>
    </section>

    @if (!string.IsNullOrWhiteSpace(Model.ErrorMessage))
    {
        <div class="employee-edit-alert danger">
            @Model.ErrorMessage
        </div>
    }

    <section class="employee-edit-summary">
        <div class="employee-edit-avatar">
            @{
                var initial = string.IsNullOrWhiteSpace(Model.Employee.FullName)
                    ? "N"
                    : Model.Employee.FullName.Trim()[0].ToString();
            }
            @initial
        </div>

        <div class="employee-edit-summary-main">
            <h2>@(string.IsNullOrWhiteSpace(Model.Employee.FullName) ? "موظف" : Model.Employee.FullName)</h2>
            <div class="employee-edit-meta">
                <span>الكود: <strong>@Model.Employee.EmployeeNo</strong></span>
                <span>المنصب: <strong>@(string.IsNullOrWhiteSpace(Model.Employee.Position) ? "-" : Model.Employee.Position)</strong></span>
                <span>الحالة: <strong>@(Model.Employee.IsActive ? "فعال" : "غير فعال")</strong></span>
            </div>
        </div>
    </section>

    <form method="post" class="employee-edit-form">
        <input type="hidden" asp-for="Employee.Id" />

        <section class="employee-edit-card">
            <div class="employee-edit-section-head">
                <div>
                    <h2>البيانات التنظيمية</h2>
                    <p>تحديد القسم والفرع والمنصب الذي يظهر في ملف الموظف والتقارير.</p>
                </div>
                <span>Organization</span>
            </div>

            <div class="employee-edit-grid">
                <div class="employee-edit-field wide">
                    <label asp-for="Employee.DepartmentId">القسم / الفرع</label>
                    <select asp-for="Employee.DepartmentId" class="employee-edit-input">
                        <option value="">اختر القسم</option>
                        @foreach (var department in Model.Departments)
                        {
                            <option value="@department.Id">@department.Name - @department.BranchName</option>
                        }
                    </select>
                    <span asp-validation-for="Employee.DepartmentId" class="employee-edit-validation"></span>
                </div>

                <div class="employee-edit-field">
                    <label asp-for="Employee.Position">المنصب</label>
                    <input asp-for="Employee.Position" class="employee-edit-input" placeholder="مثال: مشرف، محاسب، كاشير..." />
                    <span asp-validation-for="Employee.Position" class="employee-edit-validation"></span>
                </div>

                <div class="employee-edit-field status-field">
                    <label>حالة الموظف</label>
                    <label class="employee-edit-switch">
                        <input asp-for="Employee.IsActive" />
                        <span>فعال داخل النظام</span>
                    </label>
                </div>
            </div>
        </section>

        <section class="employee-edit-card">
            <div class="employee-edit-section-head">
                <div>
                    <h2>البيانات الشخصية</h2>
                    <p>البيانات الأساسية التي تستخدم للتعريف والاتصال وسجلات الموارد البشرية.</p>
                </div>
                <span>Personal</span>
            </div>

            <div class="employee-edit-grid">
                <div class="employee-edit-field">
                    <label asp-for="Employee.EmployeeNo">كود الموظف</label>
                    <input asp-for="Employee.EmployeeNo" class="employee-edit-input" />
                    <span asp-validation-for="Employee.EmployeeNo" class="employee-edit-validation"></span>
                </div>

                <div class="employee-edit-field wide">
                    <label asp-for="Employee.FullName">اسم الموظف</label>
                    <input asp-for="Employee.FullName" class="employee-edit-input" />
                    <span asp-validation-for="Employee.FullName" class="employee-edit-validation"></span>
                </div>

                <div class="employee-edit-field">
                    <label asp-for="Employee.NationalId">الرقم الوطني / البطاقة</label>
                    <input asp-for="Employee.NationalId" class="employee-edit-input" />
                    <span asp-validation-for="Employee.NationalId" class="employee-edit-validation"></span>
                </div>

                <div class="employee-edit-field">
                    <label asp-for="Employee.Phone">الهاتف</label>
                    <input asp-for="Employee.Phone" class="employee-edit-input" />
                    <span asp-validation-for="Employee.Phone" class="employee-edit-validation"></span>
                </div>

                <div class="employee-edit-field">
                    <label asp-for="Employee.Email">الإيميل</label>
                    <input asp-for="Employee.Email" class="employee-edit-input" />
                    <span asp-validation-for="Employee.Email" class="employee-edit-validation"></span>
                </div>
            </div>
        </section>

        <section class="employee-edit-card">
            <div class="employee-edit-section-head">
                <div>
                    <h2>التواريخ</h2>
                    <p>تواريخ مهمة تدخل في ملف الموظف والتقارير ومتابعة الخدمة.</p>
                </div>
                <span>Dates</span>
            </div>

            <div class="employee-edit-grid compact">
                <div class="employee-edit-field">
                    <label asp-for="Employee.HireDate">تاريخ التعيين</label>
                    <input asp-for="Employee.HireDate" type="date" class="employee-edit-input" />
                    <span asp-validation-for="Employee.HireDate" class="employee-edit-validation"></span>
                </div>

                <div class="employee-edit-field">
                    <label asp-for="Employee.BirthDate">تاريخ الميلاد</label>
                    <input asp-for="Employee.BirthDate" type="date" class="employee-edit-input" />
                    <span asp-validation-for="Employee.BirthDate" class="employee-edit-validation"></span>
                </div>
            </div>
        </section>

        <div class="employee-edit-actions">
            <button type="submit" class="employee-edit-btn primary">حفظ التعديل</button>
            <a asp-page="./Profile" asp-route-id="@Model.Employee.Id" class="employee-edit-btn secondary">إلغاء والعودة للملف</a>
        </div>
    </form>
</div>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
}
'@

Set-Content -Path $editPath -Value $editView -Encoding UTF8
Write-Host "Employees Edit page design replaced." -ForegroundColor Green

# 3) Adjust code-behind Arabic messages and redirect after save.
$editCs = Get-Content -Path $editCsPath -Raw -Encoding UTF8

$editCs = $editCs.Replace(
    'ErrorMessage = "Employee not found, employee number already exists, or selected department is invalid.";',
    'ErrorMessage = "تعذر حفظ التعديل. تأكد من كود الموظف والقسم المحدد.";'
)

$editCs = $editCs.Replace(
    'TempData["SuccessMessage"] = "Employee updated successfully.";',
    'TempData["SuccessMessage"] = "تم تحديث بيانات الموظف بنجاح.";'
)

$editCs = $editCs.Replace(
    'return RedirectToPage("./Index");',
    'return RedirectToPage("./Profile", new { id = Employee.Id });'
)

Set-Content -Path $editCsPath -Value $editCs -Encoding UTF8
Write-Host "Employees Edit code-behind adjusted." -ForegroundColor Green

# 4) CSS
$css = @'
/* NEXORA Employee Edit Page */

.employee-edit-page {
    display: grid;
    gap: 18px;
}

.employee-edit-hero {
    position: relative;
    overflow: hidden;
    min-height: 190px;
    display: flex;
    align-items: flex-end;
    justify-content: space-between;
    gap: 20px;
    padding: 26px;
    border: 1px solid rgba(18, 217, 227, .18);
    border-radius: 28px;
    background:
        radial-gradient(circle at 16% 8%, rgba(18,217,227,.20), transparent 32%),
        radial-gradient(circle at 86% 0%, rgba(215,178,109,.12), transparent 34%),
        linear-gradient(135deg, rgba(15,34,51,.98), rgba(7,18,31,.98));
    box-shadow: var(--nx-shadow, 0 22px 60px rgba(0,0,0,.34));
}

.employee-edit-hero::after {
    content: "EDIT";
    position: absolute;
    inset-inline-start: 30px;
    inset-block-end: -46px;
    color: rgba(230,234,238,.045);
    font-family: var(--nx-font-en, "Segoe UI", Arial, sans-serif);
    font-size: 140px;
    line-height: 1;
    font-weight: 1000;
    pointer-events: none;
}

.employee-edit-eyebrow {
    display: inline-flex;
    align-items: center;
    margin-bottom: 10px;
    padding: 7px 11px;
    border-radius: 999px;
    border: 1px solid rgba(18,217,227,.34);
    color: #07121F;
    background: linear-gradient(135deg, #12D9E3, #7CEAF0);
    font-size: 12px;
    font-weight: 1000;
    letter-spacing: 1px;
}

.employee-edit-hero h1 {
    margin: 0;
    color: var(--sa-text, #E6EAEE);
    font-size: clamp(30px, 4vw, 48px);
    font-weight: 1000;
}

.employee-edit-hero p {
    margin: 10px 0 0;
    max-width: 760px;
    color: var(--sa-muted, #9FB0BE);
    font-weight: 850;
    line-height: 1.8;
}

.employee-edit-hero-actions,
.employee-edit-actions {
    position: relative;
    z-index: 1;
    display: flex;
    gap: 10px;
    flex-wrap: wrap;
}

.employee-edit-btn {
    min-height: 42px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    border-radius: 12px;
    padding: 10px 14px;
    border: 1px solid rgba(230,234,238,.14);
    text-decoration: none;
    font-weight: 1000;
    cursor: pointer;
}

.employee-edit-btn.primary {
    color: #07121F;
    background: linear-gradient(135deg, #12D9E3, #7CEAF0);
    box-shadow: 0 12px 30px rgba(18,217,227,.18);
}

.employee-edit-btn.secondary {
    color: #E6EAEE;
    background: rgba(230,234,238,.08);
}

.employee-edit-btn.ghost {
    color: #D7B26D;
    background: rgba(215,178,109,.07);
    border-color: rgba(215,178,109,.22);
}

.employee-edit-alert {
    padding: 13px 15px;
    border-radius: 16px;
    font-weight: 900;
}

.employee-edit-alert.danger {
    color: #ffb4b4;
    border: 1px solid rgba(239,68,68,.25);
    background: rgba(239,68,68,.10);
}

.employee-edit-summary,
.employee-edit-card {
    border: 1px solid var(--sa-border, rgba(230,234,238,.12));
    border-radius: 22px;
    background: var(--sa-surface, #0F2233);
    box-shadow: var(--nx-shadow, 0 22px 60px rgba(0,0,0,.24));
}

.employee-edit-summary {
    display: flex;
    align-items: center;
    gap: 16px;
    padding: 18px;
}

.employee-edit-avatar {
    width: 66px;
    height: 66px;
    border-radius: 20px;
    display: grid;
    place-items: center;
    color: #07121F;
    background: linear-gradient(135deg, #12D9E3, #D7B26D);
    font-size: 28px;
    font-weight: 1000;
}

.employee-edit-summary-main h2 {
    margin: 0;
    color: var(--sa-text, #E6EAEE);
    font-weight: 1000;
}

.employee-edit-meta {
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
    margin-top: 10px;
}

.employee-edit-meta span {
    display: inline-flex;
    gap: 5px;
    padding: 7px 10px;
    border-radius: 999px;
    color: var(--sa-muted, #9FB0BE);
    border: 1px solid rgba(230,234,238,.10);
    background: rgba(7,18,31,.30);
    font-weight: 850;
}

.employee-edit-meta strong {
    color: var(--nx-teal, #12D9E3);
}

.employee-edit-form {
    display: grid;
    gap: 18px;
}

.employee-edit-card {
    padding: 18px;
}

.employee-edit-section-head {
    display: flex;
    justify-content: space-between;
    gap: 14px;
    align-items: flex-start;
    padding-bottom: 14px;
    border-bottom: 1px solid rgba(230,234,238,.08);
    margin-bottom: 16px;
}

.employee-edit-section-head h2 {
    margin: 0;
    color: var(--sa-text, #E6EAEE);
    font-weight: 1000;
}

.employee-edit-section-head p {
    margin: 5px 0 0;
    color: var(--sa-muted, #9FB0BE);
    font-weight: 800;
    line-height: 1.7;
}

.employee-edit-section-head span {
    display: inline-flex;
    padding: 6px 10px;
    border-radius: 999px;
    color: var(--nx-gold, #D7B26D);
    border: 1px solid rgba(215,178,109,.24);
    background: rgba(215,178,109,.08);
    font-weight: 1000;
    white-space: nowrap;
}

.employee-edit-grid {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 14px;
}

.employee-edit-grid.compact {
    grid-template-columns: repeat(2, minmax(0, 1fr));
}

.employee-edit-field.wide {
    grid-column: span 2;
}

.employee-edit-field label {
    display: block;
    margin-bottom: 8px;
    color: var(--nx-gold, #D7B26D);
    font-weight: 1000;
}

.employee-edit-input {
    width: 100%;
    min-height: 44px;
    padding: 10px 12px;
    border: 1px solid rgba(230,234,238,.14);
    border-radius: 12px;
    color: var(--sa-text, #E6EAEE);
    background: rgba(7,18,31,.38);
    outline: none;
    font-weight: 850;
}

.employee-edit-input:focus {
    border-color: rgba(18,217,227,.55);
    box-shadow: 0 0 0 4px rgba(18,217,227,.10);
}

.employee-edit-validation {
    display: block;
    margin-top: 6px;
    color: #ffb4b4;
    font-size: 12px;
    font-weight: 850;
}

.employee-edit-switch {
    min-height: 44px;
    display: flex !important;
    align-items: center;
    gap: 9px;
    padding: 10px 12px;
    border: 1px solid rgba(18,217,227,.18);
    border-radius: 12px;
    color: var(--sa-text, #E6EAEE) !important;
    background: rgba(18,217,227,.06);
}

.employee-edit-switch input {
    width: 18px;
    height: 18px;
    accent-color: #12D9E3;
}

.employee-edit-actions {
    justify-content: flex-start;
    padding-bottom: 16px;
}

@media (max-width: 1100px) {
    .employee-edit-grid,
    .employee-edit-grid.compact {
        grid-template-columns: repeat(2, minmax(0, 1fr));
    }

    .employee-edit-field.wide {
        grid-column: span 2;
    }
}

@media (max-width: 720px) {
    .employee-edit-hero,
    .employee-edit-section-head,
    .employee-edit-summary {
        flex-direction: column;
        align-items: stretch;
    }

    .employee-edit-grid,
    .employee-edit-grid.compact {
        grid-template-columns: 1fr;
    }

    .employee-edit-field.wide {
        grid-column: span 1;
    }

    .employee-edit-hero-actions a,
    .employee-edit-actions a,
    .employee-edit-actions button {
        flex: 1 1 auto;
    }
}
'@

Set-Content -Path $cssPath -Value $css -Encoding UTF8
Write-Host "CSS written." -ForegroundColor Green

Write-Host "Done." -ForegroundColor Cyan
Write-Host "Now run:" -ForegroundColor Yellow
Write-Host "taskkill /F /IM SmartAttendance.Web.exe" -ForegroundColor White
Write-Host "taskkill /F /IM dotnet.exe" -ForegroundColor White
Write-Host "dotnet build" -ForegroundColor White
Write-Host "dotnet run --project SmartAttendance.Web" -ForegroundColor White
