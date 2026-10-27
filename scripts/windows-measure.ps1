param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][ValidateSet('Foreground', 'Background')][string]$Scenario,
    [ValidateRange(2, 300)][int]$Samples = 30,
    [ValidateRange(1, 60)][int]$IntervalSeconds = 2,
    [ValidateRange(0, 60)][int]$WarmupSeconds = 10,
    [string]$Output
)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 8) { throw 'Run this script in 64-bit PowerShell.' }
if (-not ('SnnuWorkingSetMeasurement' -as [type])) {
    Add-Type @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
public static class SnnuWorkingSetMeasurement {
    delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    public static bool HasVisibleAppWindow(int id) {
        bool found = false;
        EnumWindows((window, parameter) => {
            uint owner; GetWindowThreadProcessId(window, out owner);
            if (owner != id) return true;
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() == "SNNUDesktop" && IsWindowVisible(window) && !IsIconic(window)) found = true;
            return !found;
        }, IntPtr.Zero);
        return found;
    }
    [DllImport("psapi.dll", SetLastError = true)]
    static extern bool QueryWorkingSet(IntPtr process, IntPtr buffer, int bytes);
    public static long[] Read(int id) {
        using (var process = Process.GetProcessById(id)) {
            for (int size = 1048576; size <= 67108864; size *= 2) {
                var buffer = Marshal.AllocHGlobal(size);
                try {
                    if (!QueryWorkingSet(process.Handle, buffer, size)) {
                        int error = Marshal.GetLastWin32Error();
                        if (error == 24) continue;
                        throw new Win32Exception(error);
                    }
                    long pages = Marshal.ReadInt64(buffer), own = 0;
                    for (int i = 0; i < pages; i++) {
                        // PSAPI_WORKING_SET_BLOCK.Shared is bit 8.
                        long block = Marshal.ReadInt64(buffer, 8 * (i + 1));
                        if ((block & 256) == 0) own++;
                    }
                    long pageSize = Environment.SystemPageSize;
                    return new long[] { pages * pageSize, own * pageSize, (pages - own) * pageSize };
                } finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        throw new InvalidOperationException("Working set exceeded the measurement buffer.");
    }
}
'@
}
$exePath = (Resolve-Path -LiteralPath $Exe).Path
if (-not $Output) {
    $Output = Join-Path (Split-Path -Parent $PSScriptRoot) "dist/windows/measurements/$Scenario.json"
}
function Get-AppProcesses {
    $items = @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exePath)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exePath })
    if (-not $items.Count) { throw 'The selected application is not running.' }
    $visible = @($items | Where-Object { [SnnuWorkingSetMeasurement]::HasVisibleAppWindow($_.Id) }).Count -gt 0
    if ($visible -ne ($Scenario -eq 'Foreground')) { throw 'Window state does not match the selected scenario.' }
    return $items
}
function Get-ConnectionStatus {
    # Keep only the status category: --probe can also contain the account and IP.
    $probe = (& $exePath --probe | Out-String).Trim()
    if ($probe -match '^(Online|Offline|Unreachable|OtherAccount)\b') { return $Matches[1] }
    return 'Unknown'
}
function Get-Statistics($Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    return [ordered]@{
        mean_mib = [Math]::Round(($sorted | Measure-Object -Average).Average / 1MB, 2)
        median_mib = [Math]::Round($median / 1MB, 2)
        minimum_mib = [Math]::Round($sorted[0] / 1MB, 2)
        maximum_mib = [Math]::Round($sorted[-1] / 1MB, 2)
    }
}
$version = (& $exePath --version | Out-String).Trim()
$hash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
$statusBefore = Get-ConnectionStatus
$initialProcesses = @(Get-AppProcesses | ForEach-Object {
    [PSCustomObject]@{ process_id = $_.Id; started_at = $_.StartTime.ToString('o') }
})
$externalModules = @(Get-AppProcesses | ForEach-Object {
    $_.Modules | Where-Object {
        $_.FileName -ne $exePath -and -not $_.FileName.StartsWith("$env:WINDIR\", [StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object ModuleName
} | Sort-Object -Unique)
Write-Output "Measuring $Scenario; warmup $WarmupSeconds s, $Samples samples every $IntervalSeconds s."
Start-Sleep -Seconds $WarmupSeconds
$rows = for ($i = 0; $i -lt $Samples; $i++) {
    $items = @(Get-AppProcesses)
    if (Compare-Object @($initialProcesses.process_id | Sort-Object) @($items.Id | Sort-Object)) {
        throw 'The application process set changed during measurement; repeat with a stable state.'
    }
    $workingSet = 0L; $privateWorkingSet = 0L; $sharedWorkingSet = 0L
    foreach ($item in $items) {
        $memory = [SnnuWorkingSetMeasurement]::Read($item.Id)
        $workingSet += $memory[0]
        $privateWorkingSet += $memory[1]
        $sharedWorkingSet += $memory[2]
    }
    [PSCustomObject]@{
        timestamp = [DateTimeOffset]::Now.ToString('o')
        process_count = $items.Count
        working_set_bytes = $workingSet
        private_working_set_bytes = $privateWorkingSet
        shareable_working_set_bytes = $sharedWorkingSet
        private_commit_bytes = [long](($items | Measure-Object PrivateMemorySize64 -Sum).Sum)
    }
    Start-Sleep -Seconds $IntervalSeconds
}
$os = Get-CimInstance Win32_OperatingSystem
$report = [ordered]@{
    measured_at = [DateTimeOffset]::Now.ToString('o')
    application_version = $version
    executable_sha256 = $hash
    executable_bytes = (Get-Item -LiteralPath $exePath).Length
    os = $os.Caption
    os_version = $os.Version
    os_build = $os.BuildNumber
    os_architecture = $os.OSArchitecture
    scenario = $Scenario
    connection_before = $statusBefore
    connection_after = Get-ConnectionStatus
    sample_count = $Samples
    interval_seconds = $IntervalSeconds
    warmup_seconds = $WarmupSeconds
    processes = $initialProcesses
    window_validation = 'SNNUDesktop window class, visible and not minimized; input method windows excluded.'
    external_module_names = $externalModules
    process_scope = 'Sum of all processes running this exact executable (GUI and daemon when present). Shared pages can be counted twice.'
    working_set = Get-Statistics @($rows | ForEach-Object working_set_bytes)
    private_working_set = Get-Statistics @($rows | ForEach-Object private_working_set_bytes)
    shareable_working_set = Get-Statistics @($rows | ForEach-Object shareable_working_set_bytes)
    private_commit = Get-Statistics @($rows | ForEach-Object private_commit_bytes)
    samples = @($rows)
}
$outputPath = [IO.Path]::GetFullPath($Output)
$null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath)
[IO.File]::WriteAllText($outputPath, ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Output "Mean MiB: total working set $($report.working_set.mean_mib), private working set $($report.private_working_set.mean_mib), private commit $($report.private_commit.mean_mib)."
Write-Output "Saved: $outputPath"
