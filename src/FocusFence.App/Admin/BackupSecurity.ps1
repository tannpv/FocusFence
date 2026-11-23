function Save-Journal($journal, [string]$path) {
    $temporary = $path + '.tmp'
    $journal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $path -Force
}

function Set-IdentityStartup([string]$mode) {
    $value = switch ($mode) { 'Auto' { 'auto' }; 'Automatic' { 'auto' }; 'Manual' { 'demand' }; 'Disabled' { 'disabled' }; default { throw "Unknown service mode: $mode" } }
    & (Join-Path $env:SystemRoot 'System32\sc.exe') config AppIDSvc start= $value | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not change Application Identity service startup.' }
}

function Write-Policy([string]$xml, [string]$path) {
    $xml | Set-Content -LiteralPath $path -Encoding UTF8
    Set-AppLockerPolicy -XmlPolicy $path
}

function Assert-ProtectedBackupPath([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Policy backup paths must not be redirected.' }
    $security = Get-Acl -LiteralPath $path
    foreach ($entry in $security.Access) {
        $entrySid = $entry.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        if ($entry.AccessControlType -eq 'Allow' -and $entrySid -notin @('S-1-5-32-544', 'S-1-5-18')) {
            throw 'Policy backup permissions are not restricted to Administrators and SYSTEM. Refusing to trust or overwrite these files.'
        }
    }
}
