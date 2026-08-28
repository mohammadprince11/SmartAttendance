param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$pagesRoot = Join-Path $RepositoryRoot 'SmartAttendance.Web\Pages'
$cssRoot = Join-Path $RepositoryRoot 'SmartAttendance.Web\wwwroot\css\pages'
New-Item -ItemType Directory -Force -Path $cssRoot | Out-Null
$resolvedCssRoot = [IO.Path]::GetFullPath($cssRoot)
$resolvedRepository = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not $resolvedCssRoot.StartsWith($resolvedRepository, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean generated CSS outside repository: $resolvedCssRoot"
}
Get-ChildItem -LiteralPath $resolvedCssRoot -Filter '*.css' -File | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Force
}

function Hash-Text([string]$Text, [int]$Length = 12) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return ([Convert]::ToHexString($sha.ComputeHash($bytes))).Substring(0, $Length).ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

$knownColors = @{
    '#12d9e3' = '--zy-primary'; '#0ea5b5' = '--zy-primary-strong'; '#04141d' = '--zy-on-primary'
    '#dceaf4' = '--zy-text'; '#8fa8bc' = '--zy-muted'; '#4ade80' = '--zy-success'
    '#22c55e' = '--zy-success-strong'; '#facc15' = '--zy-warning'; '#eab308' = '--zy-warning-strong'
    '#f87171' = '--zy-danger'; '#ef4444' = '--zy-danger-strong'; '#60a5fa' = '--zy-info'
    '#a78bfa' = '--zy-accent'; '#ffffff' = '--zy-white'; '#fff' = '--zy-white'
    '#000000' = '--zy-black'; '#000' = '--zy-black'
}
$migratedColors = [ordered]@{}
function Replace-Colors([string]$Css) {
    $pattern = '(?i)#[0-9a-f]{3,8}\b|rgba?\([^\)]*\)'
    return [regex]::Replace($Css, $pattern, {
        param($match)
        $literal = $match.Value
        $key = $literal.ToLowerInvariant().Replace(' ', '')
        if ($knownColors.ContainsKey($key)) { return "var($($knownColors[$key]))" }
        $name = '--zy-migrated-color-' + (Hash-Text $key 10)
        if (-not $migratedColors.Contains($name)) { $migratedColors[$name] = $literal }
        return "var($name)"
    })
}

function Normalize-Css([string]$Css) {
    $css = Replace-Colors $Css
    $css = [regex]::Replace($css, '(?i)\b(margin|padding|border)-(left|right)\b', {
        param($match)
        $edge = if ($match.Groups[2].Value.Equals('left', [StringComparison]::OrdinalIgnoreCase)) { 'start' } else { 'end' }
        return $match.Groups[1].Value + '-inline-' + $edge
    })
    $css = [regex]::Replace($css, '(?i)(?<![-\w])(left|right)\s*:', {
        param($match)
        $property = if ($match.Groups[1].Value.Equals('left', [StringComparison]::OrdinalIgnoreCase)) { 'inset-inline-start:' } else { 'inset-inline-end:' }
        return $property
    })
    $css = [regex]::Replace($css, '(?i)text-align\s*:\s*left\b', 'text-align:start')
    $css = [regex]::Replace($css, '(?i)text-align\s*:\s*right\b', 'text-align:end')
    $css = [regex]::Replace($css, '(?i)background-position\s*:\s*left\s+([\d.]+(?:px|rem|em|%))', 'background-position:$1')
    $css = [regex]::Replace($css, '(?i)\btop\s+right\b', '100% 0')
    $css = [regex]::Replace($css, '(?i)\btop\s+left\b', '0 0')
    return $css
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
$styleRegex = [regex]::new('(?s)<style(?<attrs>[^>]*)>(?<body>.*?)</style>')
$tagStyleRegex = [regex]::new('(?s)<(?<inside>[A-Za-z][^<>]*?)\sstyle="(?<style>[^"@]*)"')
$unusedAccentPages = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@(
    'Approvals/Index.cshtml', 'BiometricKeys/Index.cshtml', 'MissingPunchRequests/Index.cshtml',
    'PayrollProvisions/Index.cshtml', 'PeriodRules/Index.cshtml', 'Payroll/BankTemplates.cshtml',
    'Payroll/FinancialRequests.cshtml', 'Payroll/EndOfService.cshtml', 'Payroll/Overtime.cshtml',
    'Payroll/SalaryDaysAdjustment.cshtml', 'Payroll/Raises.cshtml'
) | ForEach-Object { [void]$unusedAccentPages.Add($_) }

foreach ($file in Get-ChildItem -Path $pagesRoot -Filter '*.cshtml' -Recurse) {
    # Always transform the committed source so a failed/interrupted run is safely rerunnable.
    $repoRelative = [IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/')
    $content = (& git show "HEAD:$repoRelative" 2>$null | Out-String)
    if ($LASTEXITCODE -ne 0) {
        $content = [IO.File]::ReadAllText($file.FullName)
    }
    $originalContent = $content
    $relative = [IO.Path]::GetRelativePath($pagesRoot, $file.FullName).Replace('\', '/')
    $slug = ([IO.Path]::GetFileNameWithoutExtension($file.Name) + '-' + (Hash-Text $relative 10)).ToLowerInvariant()
    $cssFileName = "$slug.css"
    $cssWebPath = "~/css/pages/$cssFileName"
    $cssParts = [System.Collections.Generic.List[string]]::new()
    $utilities = [ordered]@{}
    $linkInserted = $false
    $dynamicAccentRoot = $null
    $accMatch = [regex]::Match($content, 'var\s+acc\s*=\s*"(?<value>#[0-9A-Fa-f]{3,8})"')
    $accent = if ($accMatch.Success) { $accMatch.Groups['value'].Value } else { $null }

    $content = $styleRegex.Replace($content, {
        param($match)
        $body = $match.Groups['body'].Value
        if ($accent) { $body = $body.Replace('@acc', $accent) }
        elseif ($body.Contains('@acc')) {
            $rootMatch = [regex]::Match($body, '\.(?<root>[A-Za-z0-9_-]+-page)\s*\{')
            if ($rootMatch.Success) { $dynamicAccentRoot = $rootMatch.Groups['root'].Value }
            $body = $body.Replace('@acc', 'var(--zy-dynamic-accent)')
        }
        # Razor escapes CSS at-rules as @@media/@@keyframes/@@page. A lone Razor
        # expression (tenant theme in _Layout) remains inline and is migrated separately.
        if ([regex]::IsMatch($body, '(?<!@)@(?!@)')) { return $match.Value }
        $body = $body.Replace('@@', '@')
        $cssParts.Add("/* Source: Pages/$relative */`n" + (Normalize-Css $body).Trim())
        if (-not $linkInserted) {
            $linkInserted = $true
            return "<link rel=`"stylesheet`" href=`"$cssWebPath`" asp-append-version=`"true`" />"
        }
        return ''
    })

    $content = $tagStyleRegex.Replace($content, {
        param($match)
        $style = $match.Groups['style'].Value.Trim()
        if ([string]::IsNullOrWhiteSpace($style)) { return $match.Value }
        $className = 'zyu-' + (Hash-Text $style 12)
        $prefix = '<' + $match.Groups['inside'].Value
        if ([regex]::IsMatch($prefix, 'class="[^"]*@')) { return $match.Value }
        if (-not $utilities.Contains($className)) { $utilities[$className] = Normalize-Css $style }
        $classMatch = [regex]::Match($prefix, 'class="(?<classes>[^"]*)"')
        if ($classMatch.Success) {
            $replacement = 'class="' + $classMatch.Groups['classes'].Value + ' ' + $className + '"'
            return $prefix.Remove($classMatch.Index, $classMatch.Length).Insert($classMatch.Index, $replacement)
        }
        return $prefix + " class=`"$className`""
    })

    # Razor-dependent presentation values are data, not embedded CSS. The global,
    # allow-listed dynamic-style bridge applies them after DOM creation.
    $content = $content.Replace(' style=', ' data-zy-style=')
    if ($dynamicAccentRoot) {
        $rootPattern = 'class="(?<classes>[^"]*\b' + [regex]::Escape($dynamicAccentRoot) + '\b[^"]*)"'
        $content = [regex]::Replace($content, $rootPattern, {
            param($match)
            return $match.Value + ' data-zy-style="--zy-dynamic-accent:@acc"'
        }, 1)
    }
    if ($unusedAccentPages.Contains($relative)) {
        $content = [regex]::Replace($content, '(?m)^\s*var\s+acc\s*=.*?;\r?\n', '')
    }
    if ($utilities.Count -gt 0) {
        $cssParts.Add("/* Static inline utilities migrated from Pages/$relative */")
        foreach ($entry in $utilities.GetEnumerator()) {
            $declaration = $entry.Value.Trim().TrimEnd(';')
            $cssParts.Add(".$($entry.Key) { $declaration; }")
        }
    }

    if ($cssParts.Count -gt 0) {
        $pageLink = '<link rel="stylesheet" href="' + $cssWebPath + '" asp-append-version="true" />'
        $content = $content.Replace($pageLink + "`r`n", '').Replace($pageLink + "`n", '').Replace($pageLink, '')
        $firstMarkup = [regex]::Match($content, '(?m)^\s*<(?!!--|%)')
        if (-not $firstMarkup.Success) { throw "Cannot place stylesheet link in $relative" }
        $content = $content.Insert($firstMarkup.Index, $pageLink + "`r`n")
        [IO.File]::WriteAllText((Join-Path $cssRoot $cssFileName), ($cssParts -join "`r`n`r`n") + "`r`n", $utf8)
    }

    if ($relative -eq 'Shared/_Layout.cshtml' -and $cssParts.Count -gt 0) {
        $layoutPageLink = '<link rel="stylesheet" href="' + $cssWebPath + '" asp-append-version="true" />'
        $content = $content.Replace($layoutPageLink + "`r`n", '').Replace($layoutPageLink + "`n", '').Replace($layoutPageLink, '')
        $layoutCssAnchor = '    <link rel="stylesheet" href="~/css/zynora-design-tokens.css" asp-append-version="true" />'
        $content = $content.Replace($layoutCssAnchor, $layoutCssAnchor + "`r`n    " + $layoutPageLink)
    }

    if ($relative -eq 'Shared/_Layout.cshtml') {
        $content = $content.Replace('@inject SmartAttendance.Infrastructure.Persistence.ApplicationDbContext DesignTokenDb' + "`r`n", '')
        $content = [regex]::Replace($content, '(?m)^\s*// مقاسات الواجهة.*\r?\n\s*var designTokenCss = .*?;\r?\n', '')
        $content = [regex]::Replace(
            $content,
            '(?s)\s*@if \(companyTheme\.HasCompanyOverride\)\s*\{\s*<style id="company-theme">@Html\.Raw\(companyTheme\.CompiledCss\)</style>\s*\}',
            "`r`n    <link rel=`"stylesheet`" href=`"~/theme/current.css`" />")
        $content = [regex]::Replace(
            $content,
            '(?s)\s*@if \(!string\.IsNullOrEmpty\(designTokenCss\)\)\s*\{\s*<style id="zy-design-tokens">@Html\.Raw\(designTokenCss\)</style>\s*\}',
            '')
    }

    $content = [regex]::Replace($content, '(?m)[ \t]+(?=\r?$)', '').TrimEnd("`r", "`n") + "`r`n"

    if ($content -ne $originalContent) {
        [IO.File]::WriteAllText($file.FullName, $content, $utf8)
    }
}

$palettePath = Join-Path $RepositoryRoot 'SmartAttendance.Web\wwwroot\css\zynora-migrated-color-tokens.css'
$palette = [System.Collections.Generic.List[string]]::new()
$palette.Add(':root {')
foreach ($entry in $migratedColors.GetEnumerator()) { $palette.Add("  $($entry.Key): $($entry.Value);") }
$palette.Add('}')
[IO.File]::WriteAllText($palettePath, ($palette -join "`r`n") + "`r`n", $utf8)

$layoutPath = Join-Path $pagesRoot 'Shared\_Layout.cshtml'
$layout = [IO.File]::ReadAllText($layoutPath)
$paletteLink = '    <link rel="stylesheet" href="~/css/zynora-migrated-color-tokens.css" asp-append-version="true" />'
if (-not $layout.Contains('zynora-migrated-color-tokens.css')) {
    $anchor = '    <link rel="stylesheet" href="~/css/zynora-design-tokens.css" asp-append-version="true" />'
    $layout = $layout.Replace($anchor, $anchor + "`r`n" + $paletteLink)
    [IO.File]::WriteAllText($layoutPath, $layout, $utf8)
}
$dynamicScript = '    <script src="~/js/zynora-dynamic-style.js" asp-append-version="true" defer></script>'
if (-not $layout.Contains('zynora-dynamic-style.js')) {
    $scriptAnchor = '    <script src="~/js/zynora-theme.js" asp-append-version="true"></script>'
    $layout = $layout.Replace($scriptAnchor, $dynamicScript + "`r`n" + $scriptAnchor)
    [IO.File]::WriteAllText($layoutPath, $layout, $utf8)
}

Write-Output "Migrated Razor CSS: $((Get-ChildItem $cssRoot -Filter '*.css').Count) page stylesheets; $($migratedColors.Count) centralized legacy colors."
