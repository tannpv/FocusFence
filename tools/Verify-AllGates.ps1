$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Write-Host "`n=== Gate 1: Rule #1 Line Budget Verification ===" -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'tests\Test-LineBudgets.ps1')
if ($LASTEXITCODE -ne 0) { throw "Gate 1 (Line Budgets) failed." }

Write-Host "`n=== Gate 2: Release Solution Build ===" -ForegroundColor Cyan
& dotnet build (Join-Path $Root 'FocusFence.sln') -c Release -m:1 --nologo
if ($LASTEXITCODE -ne 0) { throw "Gate 2 (Build) failed." }

Write-Host "`n=== Gate 3: Core & Process Integration Tests ===" -ForegroundColor Cyan
$testsExe = Join-Path $Root 'tests\FocusFence.Tests\bin\Release\net8.0-windows\FocusFence.Tests.exe'
$probeExe = Join-Path $Root 'tests\FocusFence.Probe\bin\Release\net8.0\FocusFence.Probe.exe'
& $testsExe $probeExe
if ($LASTEXITCODE -ne 0) { throw "Gate 3 (Core Tests) failed." }

Write-Host "`n=== Gate 4: WPF UI Smoke Suite ===" -ForegroundColor Cyan
$uiSmokeExe = Join-Path $Root 'tests\FocusFence.UiSmoke\bin\Release\net8.0-windows\FocusFence.UiSmoke.exe'
$artifactDir = Join-Path $Root 'artifacts'
[void][IO.Directory]::CreateDirectory($artifactDir)
& $uiSmokeExe (Join-Path $artifactDir 'ui-smoke.png')
if ($LASTEXITCODE -ne 0) { throw "Gate 4 (UI Smoke) failed." }

Write-Host "`n=== Gate 5: PowerShell AppLocker Policy Tests ===" -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'tests\Test-ManagedPolicy.ps1')
if ($LASTEXITCODE -ne 0) { throw "Gate 5 (Managed Policy) failed." }

Write-Host "`n=======================================================" -ForegroundColor Green
Write-Host " ALL 5 HARNESS QUALITY GATES PASSED (100% GREEN)       " -ForegroundColor Green
Write-Host "=======================================================`n" -ForegroundColor Green
