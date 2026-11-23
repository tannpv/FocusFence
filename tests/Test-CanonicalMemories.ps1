param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$memoryPath = Join-Path $Root 'scripts\memory\canonical_memories.json'
if (-not (Test-Path -LiteralPath $memoryPath)) { throw "FAIL: Canonical memory file missing: $memoryPath" }

$raw = Get-Content -LiteralPath $memoryPath -Raw -Encoding UTF8
$memories = $raw | ConvertFrom-Json
if (-not ($memories -is [System.Collections.IEnumerable])) { throw 'FAIL: Canonical memories must be a JSON array.' }

$count = 0
$seenIds = @{}
foreach ($m in $memories) {
    $count++
    $expectedPrefix = "mem_{0:D3}_" -f $count
    if (-not $m.id -or -not $m.id.StartsWith($expectedPrefix)) {
        throw "FAIL: Memory #$count has invalid or non-sequential ID '$($m.id)' (expected prefix '$expectedPrefix')."
    }
    if ($seenIds.ContainsKey($m.id)) { throw "FAIL: Duplicate memory ID: $($m.id)" }
    $seenIds[$m.id] = $true

    foreach ($prop in @('domain', 'category', 'title', 'timestamp', 'summary', 'verification', 'tags', 'files_affected')) {
        if (-not $m.PSObject.Properties[$prop]) { throw "FAIL: Memory '$($m.id)' missing required field '$prop'." }
    }
    if ($m.tags.Count -eq 0) { throw "FAIL: Memory '$($m.id)' has empty tags array." }
    if ($m.files_affected.Count -eq 0) { throw "FAIL: Memory '$($m.id)' has empty files_affected array." }

    foreach ($file in $m.files_affected) {
        $fullPath = Join-Path $Root $file
        if (-not (Test-Path -LiteralPath $fullPath)) { throw "FAIL: Memory '$($m.id)' references missing file '$file'." }
    }
}

$pyOutput = & python (Join-Path $Root 'scripts\memory\milvus_sync.py') status
if ($LASTEXITCODE -ne 0) { throw "FAIL: milvus_sync.py status check failed with exit code $LASTEXITCODE." }

Write-Output "PASS: All $count canonical memories strictly validated (schema, sequential IDs, file references, python runner)."
