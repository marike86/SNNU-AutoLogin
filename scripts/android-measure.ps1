param(
    [string]$Adb = 'adb',
    [string]$Serial,
    [ValidateSet('Foreground', 'Background', 'Waiting')][string]$Scenario = 'Background',
    [string]$Package = 'io.github.shixiangliu.snnulogin.benchmark'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $projectRoot 'dist/android/measurements'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$deviceArgs = @()
if ($Serial) { $deviceArgs = @('-s', $Serial) }
function Invoke-Device([string[]]$DeviceCommand) {
    # adb prints routine warnings on stderr (e.g. "Activity not started, ... brought to the front").
    # Those must not abort the measurement while $ErrorActionPreference is Stop.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & $Adb @deviceArgs @DeviceCommand 2>$null } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "adb failed: $($DeviceCommand[0])" }
    return $output
}
$packageInfo = (Invoke-Device @('shell', 'dumpsys', 'package', $Package)) -join "`n"
$versionMatch = [regex]::Match($packageInfo, '(?m)^\s*versionName=(\S+)')
$serviceInfo = (Invoke-Device @('shell', 'dumpsys', 'activity', 'services', $Package)) -join "`n"
$servicePresent = $serviceInfo -match 'ServiceRecord\{[^\r\n]*LoginService'
$device = @{
    model = (Invoke-Device @('shell', 'getprop', 'ro.product.model')) -join ''
    manufacturer = (Invoke-Device @('shell', 'getprop', 'ro.product.manufacturer')) -join ''
    android = (Invoke-Device @('shell', 'getprop', 'ro.build.version.release')) -join ''
    api = (Invoke-Device @('shell', 'getprop', 'ro.build.version.sdk')) -join ''
    scenario = $Scenario
    package = $Package
    version = $versionMatch.Groups[1].Value
    foregroundServicePresent = $servicePresent
    backgroundTargetApplies = $Scenario -ne 'Foreground' -and $servicePresent
    processContext = if ($Scenario -eq 'Foreground') { 'VisibleActivity' } elseif ($servicePresent) { 'LoginService' } else { 'CachedActivityWithoutService' }
    measuredAt = [DateTimeOffset]::Now.ToString('o')
}
if ($Scenario -eq 'Foreground') {
    Invoke-Device @('shell', 'am', 'start', '-n', "$Package/io.github.shixiangliu.snnulogin.MainActivity") | Out-Null
} else {
    # Enable the service in the app first. Waiting additionally requires a non-campus Wi-Fi state.
    Invoke-Device @('shell', 'input', 'keyevent', 'KEYCODE_HOME') | Out-Null
}
Write-Output 'Sampling PSS 30 times over approximately 60 seconds. Keep this connection state unchanged.'
$samples = for ($i = 0; $i -lt 30; $i++) {
    $memory = (Invoke-Device @('shell', 'dumpsys', 'meminfo', $Package)) -join "`n"
    $match = [regex]::Match($memory, '(?m)^\s*TOTAL PSS:\s*(\d+)')
    if (-not $match.Success) { $match = [regex]::Match($memory, '(?m)^\s*TOTAL\s+(\d+)') }
    if (-not $match.Success) { throw 'No PSS value; confirm the selected app process is running' }
    [PSCustomObject]@{ sample = $i + 1; pssKiB = [int]$match.Groups[1].Value }
    Start-Sleep -Seconds 2
}
$values = @($samples | ForEach-Object pssKiB | Sort-Object)
$device.samples = @($samples)
$device.minimumPssMiB = [Math]::Round($values[0] / 1024, 2)
$device.medianPssMiB = [Math]::Round(($values[14] + $values[15]) / 2048, 2)
$device.maximumPssMiB = [Math]::Round($values[-1] / 1024, 2)
$device.backgroundTargetMiB = 30
$path = Join-Path $outputRoot "$Scenario-$([DateTime]::Now.ToString('yyyyMMdd-HHmmss')).json"
[IO.File]::WriteAllText($path, ($device | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output "Median PSS: $($device.medianPssMiB) MiB; maximum: $($device.maximumPssMiB) MiB"
Write-Output "Measurement saved: $path"
