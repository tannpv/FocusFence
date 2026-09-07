function Test-SrpConfigurationContent {
    param([System.Collections.IDictionary]$Values, [int]$SubKeyCount)
    # An empty key or the disabled certificate-check setting alone defines no rules.
    # Reject all other settings, including unknown values, conservatively.
    if ($SubKeyCount -gt 0) { return $true }
    foreach ($name in $Values.Keys) {
        if ($name -ine 'AuthenticodeEnabled' -or $Values[$name] -isnot [int] -or $Values[$name] -ne 0) { return $true }
    }
    return $false
}

function Test-ExistingSrpConfiguration {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -ErrorAction Stop)) { return $false }
    $key = Get-Item -LiteralPath $Path -ErrorAction Stop
    try {
        $values = @{}
        foreach ($name in $key.GetValueNames()) { $values[$name] = $key.GetValue($name) }
        return Test-SrpConfigurationContent -Values $values -SubKeyCount $key.SubKeyCount
    } finally { $key.Dispose() }
}
