function ConvertFrom-BlockedExecutableRequest {
    param([string]$EncodedPaths)
    if (-not $EncodedPaths) { return }
    if ($EncodedPaths.Length -gt 20000) { throw 'The individual app request is too large.' }
    $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($EncodedPaths))
    if (-not $json.TrimStart().StartsWith('[')) { throw 'The individual app request must contain a JSON array.' }
    # Windows PowerShell 5.1 emits the JSON array as one pipeline object.
    # Assign before enumerating so each item is validated as a path string.
    $decoded = ConvertFrom-Json -InputObject $json
    $paths = @($decoded)
    if ($paths.Count -gt 100) { throw 'Select at most 100 individual apps.' }
    foreach ($path in $paths) {
        if ($path -isnot [string] -or -not [IO.Path]::IsPathRooted($path) -or $path -notmatch '\.exe$') {
            throw 'Each individual app must be an absolute executable path.'
        }
        Write-Output $path
    }
}
