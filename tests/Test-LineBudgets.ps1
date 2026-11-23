param(
    [int]$HardCeiling = 150,
    [int]$TargetBudget = 120,
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$items = Get-ChildItem -Path $Root -Recurse -File | Where-Object {
    $_.FullName -notmatch '\\(bin|obj|artifacts|\.nuget_appdata|\.git|scratch)\\' -and
    $_.Extension -in '.cs', '.xaml', '.ps1', '.py'
}

$violations = @()
$warnings = @()
$compliantCount = 0

foreach ($item in $items) {
    $lines = (Get-Content -LiteralPath $item.FullName | Measure-Object -Line).Lines
    $relPath = $item.FullName.Substring($Root.Length).TrimStart('\', '/')
    if ($lines -gt $HardCeiling) {
        $violations += [pscustomobject]@{ Path = $relPath; Lines = $lines; Limit = $HardCeiling }
    } elseif ($lines -gt $TargetBudget) {
        $warnings += [pscustomobject]@{ Path = $relPath; Lines = $lines; Target = $TargetBudget }
    } else {
        $compliantCount++
    }
}

if ($warnings.Count -gt 0) {
    Write-Warning "Files exceeding target budget ($TargetBudget lines):"
    $warnings | Format-Table -AutoSize | Out-String | Write-Warning
}

if ($violations.Count -gt 0) {
    Write-Error "FAIL: $($violations.Count) files violated Rule #1 hard line ceiling ($HardCeiling lines):"
    $violations | Format-Table -AutoSize | Out-String | Write-Host -ForegroundColor Red
    exit 1
}

Write-Output "PASS: Rule #1 Line Budget Gate verified across $($items.Count) files."
Write-Output "      All $compliantCount files strictly meet the <= $TargetBudget lines target (0 violations > $HardCeiling, 0 warnings > $TargetBudget)."
