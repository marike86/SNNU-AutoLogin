param([switch]$Release)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$androidRoot = Join-Path $projectRoot 'android'
$outputRoot = Join-Path $projectRoot 'dist/android'
$tasks = @('testDebugUnitTest', 'lintDebug', 'lintBenchmark', 'assembleBenchmark', 'assembleDebugAndroidTest')
# Some IDE runtimes ship javac but omit jlink, which Android's JDK image transform requires.
if (-not $env:JAVA_HOME -or -not (Test-Path (Join-Path $env:JAVA_HOME 'bin/jlink.exe'))) {
    $jdkRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.jdks'
    $fullJdk = Get-ChildItem -LiteralPath $jdkRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { (Test-Path (Join-Path $_.FullName 'bin/jlink.exe')) -and (Test-Path (Join-Path $_.FullName 'bin/javac.exe')) } |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($fullJdk) { $env:JAVA_HOME = $fullJdk.FullName }
    else { throw 'Set JAVA_HOME to a complete JDK 21 (including javac and jlink), rather than an IDE runtime.' }
}
if ($Release) {
    foreach ($name in @('SNNU_KEYSTORE', 'SNNU_STORE_PASSWORD', 'SNNU_KEY_ALIAS', 'SNNU_KEY_PASSWORD')) {
        if (-not [Environment]::GetEnvironmentVariable($name)) { throw "Missing signing variable: $name" }
    }
    $tasks += @('lintRelease', 'assembleRelease')
}
# Gradle and javac write routine warnings to stderr; they must not abort packaging.
$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try { & (Join-Path $androidRoot 'gradlew.bat') -p $androidRoot @tasks --console=plain --no-daemon } finally { $ErrorActionPreference = $previous }
if ($LASTEXITCODE -ne 0) { throw 'Android checks or build failed' }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$testApk = Join-Path $androidRoot 'app/build/outputs/apk/benchmark/app-benchmark.apk'
Copy-Item -LiteralPath $testApk -Destination (Join-Path $outputRoot 'SNNU-AutoLogin-android-test.apk') -Force
if ((Get-Item -LiteralPath $testApk).Length -gt 2MB) { throw 'Optimized APK exceeds the 2 MiB size budget' }
if ($Release) {
    Copy-Item -LiteralPath (Join-Path $androidRoot 'app/build/outputs/apk/release/app-release.apk') `
        -Destination (Join-Path $outputRoot 'SNNU-AutoLogin-android.apk') -Force
}
Copy-Item -LiteralPath (Join-Path $androidRoot 'README.md') -Destination $outputRoot -Force
Copy-Item -LiteralPath (Join-Path $androidRoot 'VALIDATION.md') -Destination $outputRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $outputRoot -Force
$checksums = Get-ChildItem -LiteralPath $outputRoot -Filter '*.apk' | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
}
# LF endings so `sha256sum -c` works on Linux and macOS as well as Windows.
[IO.File]::WriteAllText((Join-Path $outputRoot 'SHA256SUMS.txt'), (($checksums -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Write-Output "Android package ready: $outputRoot"
