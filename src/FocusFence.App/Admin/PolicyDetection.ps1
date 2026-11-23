function Test-SrpConfigurationContent {
    param([System.Collections.IDictionary]$Values, [int]$SubKeyCount)
    # An empty key or the disabled certificate-check setting alone defines no rules.
    # Reject all other settings, including unknown values, conservatively.
    if ($SubKeyCount -gt 0) { return $true }
    if ($null -eq $Values) { return $false }
    foreach ($name in $Values.Keys) {
        if ($name -ine 'AuthenticodeEnabled' -or $Values[$name] -isnot [int] -or $Values[$name] -ne 0) { return $true }
    }
    return $false
}

function Test-ExistingSrpConfiguration {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -ErrorAction SilentlyContinue)) { return $false }
    $key = try { Get-Item -LiteralPath $Path -ErrorAction Stop } catch { return $false }
    try {
        $values = @{}
        foreach ($name in $key.GetValueNames()) { $values[$name] = $key.GetValue($name) }
        return Test-SrpConfigurationContent -Values $values -SubKeyCount $key.SubKeyCount
    } finally { $key.Dispose() }
}
