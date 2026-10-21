param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Run installer smoke tests only on a disposable GitHub Actions runner.' }
if (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'SNNUAutoLogin')) { throw 'Existing user data found; refusing installer smoke test.' }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -LiteralPath $runKey -Name 'SNNUAutoLogin' -ErrorAction SilentlyContinue) { throw 'Existing startup entry found; refusing installer smoke test.' }
$testDir = Join-Path $env:TEMP ('SNNU-InstallerTest-' + [Guid]::NewGuid().ToString('N'))
$installLog = "$testDir-install.log"
$proc = Start-Process -FilePath $Installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS', "/DIR=`"$testDir`"", "/LOG=`"$installLog`"") -PassThru -WindowStyle Hidden
if (!$proc.WaitForExit(120000)) { throw 'Installer timed out' }
if ($proc.ExitCode -ne 0) { throw "Installer failed ($($proc.ExitCode)): $installLog" }
$exe = Join-Path $testDir 'SNNUAutoLogin.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Installed executable missing' }
if ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne '1.2.1.0') { throw 'Installed version mismatch' }
# No campus credentials are configured on the disposable CI runner.
$app = Start-Process -FilePath $exe -ArgumentList '--test-exit' -WorkingDirectory $env:TEMP -PassThru -WindowStyle Hidden
if (!$app.WaitForExit(30000)) { throw 'Installed application exit self-test timed out' }
if ($app.ExitCode -ne 0) { throw "Installed application failed ($($app.ExitCode))" }
$exitReport = Join-Path $env:TEMP 'snnu_exit.txt'
if (!(Test-Path -LiteralPath $exitReport)) { throw 'Installed application self-test did not run' }
$uninstall = Start-Process -FilePath (Join-Path $testDir 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -PassThru -WindowStyle Hidden
if (!$uninstall.WaitForExit(60000) -or $uninstall.ExitCode -ne 0) { throw 'Uninstall failed' }
if (Test-Path -LiteralPath $exe) { throw 'Uninstall left application files' }
Write-Host 'Installer install / application launch / uninstall smoke test passed.'
