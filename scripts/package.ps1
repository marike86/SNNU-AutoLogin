param([switch]$Offline)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
$stage = $null
$previousEncodedFlags = [Environment]::GetEnvironmentVariable('CARGO_ENCODED_RUSTFLAGS')
try {
    $cargoFlags = @('--locked', '--target', 'x86_64-pc-windows-msvc')
    if ($Offline) { $cargoFlags += '--offline' }
    $metadata = & cargo metadata --no-deps --format-version 1 --locked
    if ($LASTEXITCODE -ne 0) { throw '无法读取 Cargo 项目信息' }
    $package = ($metadata | ConvertFrom-Json).packages | Where-Object { $_.name -eq 'snnulogin' }
    $version = $package.version
    $hostInfo = & rustc -vV
    if ($LASTEXITCODE -ne 0) { throw '无法读取 Rust 工具链信息' }
    $hostTriple = ($hostInfo | Where-Object { $_ -like 'host: *' }) -replace '^host: ', ''
    if ($hostTriple -ne 'x86_64-pc-windows-msvc') {
        throw '发布脚本需要 Windows x64 MSVC 工具链'
    }

    # 路径映射同时作用于依赖，避免在错误位置字符串中嵌入编译者的本机目录。
    $separator = [char]31
    $flags = [Collections.Generic.List[string]]::new()
    if ($previousEncodedFlags) {
        foreach ($flag in $previousEncodedFlags.Split($separator)) { $flags.Add($flag) }
    } elseif ($env:RUSTFLAGS) {
        foreach ($flag in ($env:RUSTFLAGS -split '\s+' | Where-Object { $_ })) { $flags.Add($flag) }
    }
    if ($env:USERPROFILE) { $flags.Add("--remap-path-prefix=$env:USERPROFILE=/build") }
    $flags.Add("--remap-path-prefix=$projectRoot=/src/SNNU-AutoLogin")
    $cargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
    $flags.Add("--remap-path-prefix=$cargoHome=/cargo")
    if ($env:RUSTUP_HOME) { $flags.Add("--remap-path-prefix=$env:RUSTUP_HOME=/toolchain") }
    $flags.Add('-C')
    $flags.Add('target-feature=+crt-static')
    $env:CARGO_ENCODED_RUSTFLAGS = $flags -join $separator

    & cargo fmt --all -- --check
    if ($LASTEXITCODE -ne 0) { throw '源码格式检查未通过' }
    & cargo test --release --target-dir (Join-Path $projectRoot 'target') @cargoFlags
    if ($LASTEXITCODE -ne 0) { throw '回归测试未通过' }
    & cargo build --release --target-dir (Join-Path $projectRoot 'target') @cargoFlags
    if ($LASTEXITCODE -ne 0) { throw '发布构建失败' }

    $exe = Join-Path $projectRoot "target/$hostTriple/release/snnulogin.exe"
    $fileVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    if ($fileVersion -ne "$version.0") { throw "程序版本与 Cargo.toml 不一致：$fileVersion" }

    $targetRoot = Join-Path $projectRoot 'target'
    $stage = Join-Path $targetRoot ('release-package-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -LiteralPath $exe -Destination (Join-Path $stage '校园网自动登录.exe')
    foreach ($name in @('README.md', 'LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $stage
    }
    & (Join-Path $PSScriptRoot 'third-party.ps1') -Destination (Join-Path $stage 'THIRD-PARTY-NOTICES.txt') -Offline:$Offline

    $dist = Join-Path $projectRoot 'dist'
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    Copy-Item -LiteralPath $exe -Destination (Join-Path $dist 'SNNU-AutoLogin.exe') -Force
    $zipName = "SNNU-AutoLogin-v$version-windows-x64.zip"
    $zipPath = Join-Path $dist $zipName
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -Force
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $exeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), "$hash  $zipName`n$exeHash  SNNU-AutoLogin.exe`n", [Text.UTF8Encoding]::new($false))
    Write-Host "发布包：$zipPath"
} finally {
    [Environment]::SetEnvironmentVariable('CARGO_ENCODED_RUSTFLAGS', $previousEncodedFlags, 'Process')
    if ($stage -and (Test-Path -LiteralPath $stage)) {
        $resolvedStage = [IO.Path]::GetFullPath($stage)
        $expectedParent = [IO.Path]::GetFullPath((Join-Path $projectRoot 'target'))
        if ((Split-Path -Parent $resolvedStage) -ne $expectedParent -or
            (Split-Path -Leaf $resolvedStage) -notlike 'release-package-*') {
            throw '临时目录超出预期范围，已停止清理'
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    Pop-Location
}
