param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $flags = @('--locked')
    if ($Offline) { $flags += '--offline' }
    $json = & cargo metadata --format-version 1 --filter-platform x86_64-pc-windows-msvc @flags
    if ($LASTEXITCODE -ne 0) { throw '无法读取依赖许可证信息' }
    $metadata = $json | ConvertFrom-Json
    $nodes = @{}
    foreach ($node in $metadata.resolve.nodes) { $nodes[$node.id] = $node }
    $visited = [Collections.Generic.HashSet[string]]::new()
    function Visit-Dependency([string]$Id) {
        if ($visited.Add($Id)) {
            foreach ($dependency in $nodes[$Id].dependencies) { Visit-Dependency $dependency }
        }
    }
    Visit-Dependency $metadata.resolve.root

    $report = [Text.StringBuilder]::new()
    [void]$report.AppendLine('Third-party notices for SNNU-AutoLogin (Windows x64)')
    [void]$report.AppendLine('Generated from Cargo.lock; dependency licenses are reproduced below.')
    [void]$report.AppendLine()
    $texts = [Collections.Generic.List[string]]::new()
    $known = [Collections.Generic.Dictionary[string, int]]::new()
    foreach ($package in ($metadata.packages | Sort-Object name, version)) {
        if ($package.id -eq $metadata.resolve.root -or -not $visited.Contains($package.id)) { continue }
        [void]$report.AppendLine("$($package.name) $($package.version) | $($package.license)")
        if ($package.repository) { [void]$report.AppendLine("Source: $($package.repository)") }
        $dir = Split-Path -Parent $package.manifest_path
        $files = @(Get-ChildItem -LiteralPath $dir -File | Where-Object {
            $_.Name -match '^(licen[sc]e|copying|copyright|notice)([._-]|$)'
        } | Sort-Object Name)
        if ($files.Count -eq 0) { throw "依赖缺少许可证正文，请检查：$($package.name)" }
        foreach ($file in $files) {
            $text = [IO.File]::ReadAllText($file.FullName).Replace("`r`n", "`n").TrimEnd()
            if (-not $known.ContainsKey($text)) {
                $texts.Add($text)
                $known[$text] = $texts.Count
            }
            [void]$report.AppendLine("  $($file.Name): license text #$($known[$text])")
        }
        [void]$report.AppendLine()
    }
    for ($index = 0; $index -lt $texts.Count; $index++) {
        [void]$report.AppendLine(('=' * 72))
        [void]$report.AppendLine("License text #$($index + 1)")
        [void]$report.AppendLine(('=' * 72))
        [void]$report.AppendLine($texts[$index])
        [void]$report.AppendLine()
    }
    [IO.File]::WriteAllText($Destination, $report.ToString(), [Text.UTF8Encoding]::new($false))
} finally {
    Pop-Location
}
