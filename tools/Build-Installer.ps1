param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $CompilerPath) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compilerCommand) { $CompilerPath = $compilerCommand.Source }
    else {
        foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
            $candidate = Join-Path $base 'Inno Setup 6\ISCC.exe'
            if (Test-Path $candidate) { $CompilerPath = $candidate; break }
        }
    }
}
if (-not $CompilerPath) { throw 'Install Inno Setup 6 or supply -CompilerPath to ISCC.exe.' }
$version = ([xml](Get-Content (Join-Path $projectRoot 'src/FocusFence.App/Properties/Version.props') -Raw)).Project.PropertyGroup.Version
$payload = Join-Path $projectRoot 'artifacts/installer/payload'
$output = Join-Path $projectRoot 'artifacts/installer/output'
& dotnet publish (Join-Path $projectRoot 'src/FocusFence.App') -c Release -r win-x64 --self-contained true -o $payload -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'Installer payload build failed.' }
Copy-Item (Join-Path $projectRoot 'README.md') -Destination $payload
& $CompilerPath "/DAppVersion=$version" "/DPayloadDir=$payload" "/DOutputDir=$output" (Join-Path $projectRoot 'installer/FocusFence.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $output "FocusFence-$version-Setup-x64.exe"
$hash = Get-FileHash $installer -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($installer))" | Set-Content (Join-Path $output 'Installer-SHA256SUMS.txt') -Encoding ascii
Write-Output "Installer: $installer"
